using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>List entry: first line of Display is the title, the rest (if any) the body.</summary>
public sealed record Entry(VerseRef Ref, string Display, long? Id = null, string? Color = null)
{
    public bool HasColor => Color is not null;
    public IBrush? ColorBrush => Color is not null && Avalonia.Media.Color.TryParse(Color, out var c) ? new SolidColorBrush(c) : null;
    public string Title => Display.Split('\n', 2)[0];
    public string Body => Display.Contains('\n') ? Display[(Display.IndexOf('\n') + 1)..] : "";
    public bool HasBody => Body.Length > 0;
}

/// <summary>An additional translation shown next to the main one.</summary>
public sealed record ExtraColumn(ComboBox Lang, ComboBox Trans, Control Root, Button PassageBtn)
{
    public TranslationInfo? Translation => Trans.SelectedItem as TranslationInfo;

    /// <summary>A passage of its own (for example Genesis next to Daniel); null = the column shows the same passage as the main text.</summary>
    public (string Book, int Chapter)? Own { get; set; }
}

public partial class MainWindow : Window
{
    const int MaxExtras = 3;
    const int ConcordanceLimit = 3000;
    readonly HelloAoProvider _hello;
    readonly LocalBibleStore _local;
    readonly HttpClient _http;
    bool _building;
    readonly StrongsDictionary _strongs;
    readonly StepLexicon _step;
    readonly SyntaxProvider _syntax;
    readonly MeaningTranslator _meanings;
    int _fontSize = 22;
    bool _singleColumn = true;
    readonly string _root;
    readonly string _settingsPath;
    readonly AppSettings _settings;
    HashSet<(string Book, int Chapter)> _read = [];
    readonly List<Place> _history = [];
    string? _pendingTranslation;
    bool _firstBooks = true;
    bool _startupHome;
    readonly List<ExtraColumn> _extras = [];
    IReadOnlyList<string> _langs = [];

    IBrush? Res(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) ? v as IBrush : null;

    readonly IBibleProvider _provider;
    readonly UserStore _store;
    IReadOnlyList<TranslationInfo> _all = [];
    IReadOnlyList<BookInfo> _books = [];
    readonly Dictionary<int, Border> _verseBoxes = [];
    Chapter? _curChapterData;
    List<CrossRef> _xrefs = [];
    string _curBook = "";
    int _curChapter;
    int? _selected;
    bool _loading;

    public MainWindow()
    {
        SplashWindow.Report(0.2, L("Baue die Oberfläche …"));
        InitializeComponent();
        LocXaml.Apply(this);
        _root = BibleStudy.Core.AppPaths.Root;
        var http = _http = new HttpClient();
        _hello = new HelloAoProvider(http, Path.Combine(_root, "cache"));
        _local = new LocalBibleStore(Path.Combine(_root, "local.db"));
        CreatePlugins();
        _overlay = new StrongsOverlay(Path.Combine(_root, "strongs-overlay"));
        _provider = new CompositeProvider(_hello, _local, _pluginProvider) { Overlay = _overlay };
        Closed += (_, _) => { try { EndQueueRun(); DeleteGuestVaults(_root); RemoveGuestBibles(); } catch (Exception ex) { ErrorLog.Write(ex); } };
        Closed += (_, _) => { try { _overlay.CommitAll(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); } };
        _strongs = new StrongsDictionary(http, Path.Combine(_root, "cache"));
        _step = new StepLexicon(http, Path.Combine(_root, "cache"));
        _syntax = new SyntaxProvider(http, Path.Combine(_root, "cache"));
        _meanings = new MeaningTranslator(Path.Combine(_root, "cache"));
        _store = new UserStore(Path.Combine(_root, "user.db"));
        _settingsPath = Path.Combine(_root, "settings.json");
        _settings = AppSettings.Load(_settingsPath);
        TakeResume();
        CommentariesOn = _settings.ShowCommentaries; CompactLook = _settings.CompactLook; NavGroups = MakeNavGroups();
        DeleteGuestVaults(_root);   // a guest import left over from a crash
        RemoveGuestBibles();
        _readStore = new ReadStore(Path.Combine(_root, "read.db"));
        if (_readStore.IsNew)   // first start with the shared list: what was read in any vault so far counts for all of them
        {
            var vaultsDir = Path.Combine(_root, "vaults");
            if (Directory.Exists(vaultsDir))
                foreach (var dir in Directory.GetDirectories(vaultsDir).Where(d => !IsGuestDir(d)))
                    try { _readStore.MergeFrom(Path.Combine(dir, "vault.db")); } catch (Exception ex) { ErrorLog.Write(ex); }
        }
        _vault = OpenVault(_settings.Vault);
        _read = _vault.GetReadChapters();
        _fontSize = _settings.FontSize;
        _startupHome = _resume is { } resume ? resume.Home : _settings.StartOnHome;
        FontSlider.Value = _fontSize;
        FontLabel.Text = L("Schriftgröße: {0}", _fontSize);
        StartHomeCheck.IsChecked = _settings.StartOnHome;
        AutoReadCheck.IsChecked = _settings.AutoMarkRead;
        ThemeBox.SelectedIndex = _settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        ApplyTheme(ThemeBox.SelectedIndex);

        ThemeBox.SelectionChanged += (_, _) => { ApplyTheme(ThemeBox.SelectedIndex); _settings.Theme = new[] { "Default", "Light", "Dark" }[Math.Max(0, ThemeBox.SelectedIndex)]; SaveSettings(); };
        ThemeBtn.Click += (_, _) => ThemeBox.SelectedIndex = (ThemeBox.SelectedIndex + 1) % 3;
        FontSlider.ValueChanged += async (_, e) =>
        {
            _fontSize = (int)e.NewValue; _settings.FontSize = _fontSize; FontLabel.Text = L("Schriftgröße: {0}", _fontSize);
            await RedrawChapterAsync();
        };
        FontMinusBtn.Click += (_, _) => FontSlider.Value = Math.Max(FontSlider.Minimum, FontSlider.Value - 1);
        FontPlusBtn.Click += (_, _) => FontSlider.Value = Math.Min(FontSlider.Maximum, FontSlider.Value + 1);
        StartHomeCheck.IsCheckedChanged += (_, _) => { _settings.StartOnHome = StartHomeCheck.IsChecked == true; SaveSettings(); };
        AutoReadCheck.IsCheckedChanged += (_, _) => { _settings.AutoMarkRead = AutoReadCheck.IsChecked == true; SaveSettings(); };
        OfflineBtn.Click += async (_, _) => await OfflineAsync();
        DownloadAllBtn.Click += (_, _) => ShowDownloadAll();
        QuickReportBtn.Click += async (_, _) => await ShowChapterReportAsync();
        QuickPericopeBtn.Click += (_, _) => RevealSideTab(PericopeTab);
        QuickSentenceBtn.Click += (_, _) => RevealSideTab(SentenceTab);
        QuickStatsBtn.Click += (_, _) => OpenWork(NavGroups.First(g => g.Name == "Planen"), StatsTab);
        QuickMemoryBtn.Click += (_, _) => OpenWork(NavGroups.First(g => g.Name == "Planen"), MemoryTab);
        // the extra shortcuts only where there is room (the buttons on the right keep their place)
        SizeChanged += (_, _) => UpdateReadQuick();
        SideTabs.SelectionChanged += (_, _) => { if (SideTabs.SelectedIndex == SettingsTab) UpdateOfflineInfo(); };

        HomeBtn.Click += (_, _) => ShowHome();
        BackBtn.Click += async (_, _) => await GoBackAsync();
        ForwardBtn.Click += async (_, _) => await GoForwardAsync();
        AddHandler(PointerPressedEvent, OnMouseSideButtons, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ReadBtn.Click += (_, _) => SetRead(_curBook, [_curChapter], ReadBtn.IsChecked == true);
        Home.ChapterChosen += async (book, chapter) => await GoToAsync(book, chapter);
        Home.ReadChanged += SetRead;

        ActualThemeVariantChanged += async (_, _) => await RedrawChapterAsync(); // highlight opacity depends on the theme
        ReaderScroll.AddHandler(PointerPressedEvent, OnReaderBackgroundPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ReaderScroll.SizeChanged += (_, _) => FitReader();
        StudyBtn.IsCheckedChanged += async (_, _) => await OnStudyToggledAsync();

        LanguageBox.SelectionChanged += (_, _) => { if (!_quietPickers) FillTranslations(); };
        TranslationBox.SelectionChanged += async (_, _) => { if (!_quietPickers) await LoadBooksAsync(); };
        BookBox.SelectionChanged += async (_, _) =>
        {
            if (BookBox.SelectedItem is not BookInfo b) return;
            ChapterBox.Maximum = b.NumberOfChapters;
            if (_loading) return;
            _loading = true; ChapterBox.Value = 1; _loading = false;
            ShowReader();
            await ShowChapterAsync();
        };
        ChapterBox.ValueChanged += async (_, _) => { if (!_loading) ShowReader(); await ShowChapterAsync(); };
        PrevBtn.Click += async (_, _) => await StepChapterAsync(-1);
        NextBtn.Click += async (_, _) =>
        {
            if (_settings.AutoMarkRead && _curBook != "") SetRead(_curBook, [_curChapter], true);
            await StepChapterAsync(1);
        };
        BookmarkBtn.Click += (_, _) => ToggleBookmark();
        ParallelBtn.Click += (_, _) => AddColumn();
        NoteSaveBtn.Click += async (_, _) => await SaveNoteAsync(NoteEditor.Text ?? "");
        NoteDeleteBtn.Click += async (_, _) =>
        {
            // a saved note goes into the trash and can come back; a draft that was never saved is just emptied
            var at = _noteRef ?? (_selected is int v ? new VerseRef(_curBook, _curChapter, v) : null);
            if (at is { } r && _vault.GetNotes().Any(x => x.Ref == r)) TrashNoteWithUndo(r);
            else { NoteEditor.Text = ""; await SaveNoteAsync(""); }
        };
        InitSearch();
        InitKeyboards();
        InitSavedSearches();
        foreach (var list in new[] { SearchList, XrefList, ConcList }) list.SelectionChanged += OnEntryPicked;
        InitVault();
        InitSelection();
        InitBookNames();
        InitAi();
        InitGraph();
        InitPanel();
        InitStudy();
        InitAudio();
        InitSpeak();
        InitStrongSearch();
        InitImport();
        InitKnowledge();
        InitQol();
        InitNav();
        InitPickers();
        InitKeys();
        InitWindows();
        InitShell();
        InitXrefDirection();
        InitLicenses();
        InitTopics();
        InitTimeline();
        InitFamily();
        InitUpdate();
        InitLogo();
        InitLanguage();
        InitLibrary();
        ObsidianBtn.Click += async (_, _) => await ExportObsidianAsync();
        SideTabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == SideTabs && SideTabs.SelectedIndex == GuideTab) _ = ShowGuideAsync(_selected is int v ? new VerseRef(_curBook, _curChapter, v) : default);
        };
        StartControl();
        var initialised = false;
        SplashWindow.Report(0.5, L("Bereite Werkzeuge vor …"));
        Opened += async (_, _) => { if (initialised) return; initialised = true; await InitAsync(); };
    }

    string BookName(string id) => _books.FirstOrDefault(b => b.Id == id)?.CommonName ?? id;
    string Label(VerseRef r) => $"{BookName(r.Book)} {r.Chapter}:{r.Verse}";

    /// <summary>The Bible the very first start opens: one in the language the program speaks (a public-domain classic of it).</summary>
    static string FirstBible() => Loc.Code switch { "en" => "eng_kjv", "es" => "spa_r09", "fr" => "fra_lsg", "ru" => "rus_syn", _ => "deu_elbbk" };

    async Task InitAsync()
    {
        try
        {
            SplashWindow.Report(0.6, L("Lade Übersetzungen …"));
            StatusText.Text = L("Lade Übersetzungen …");
            _all = Named(await _provider.GetTranslationsAsync());
            _langs = _all.Select(t => t.LanguageName).Where(l => l != "").Distinct().Order().ToList();
            LanguageBox.ItemsSource = _langs;
            var wanted = _all.FirstOrDefault(t => t.Id == (_settings.TranslationId ?? FirstBible())) ?? _all.FirstOrDefault(t => t.Id == "eng_kjv");
            _pendingTranslation = wanted?.Id;
            SplashWindow.Report(0.75, L("Lade Bücher …"));
            LanguageBox.SelectedItem = wanted?.LanguageName ?? _langs.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusText.Text = L("Fehler: ") + ex.Message;
            // the very first start without a connection: nothing to read yet, so say so where it is seen and offer to try again
            if (_all.Count == 0) ShowUndoToast(L("Die Bibelübersetzungen konnten nicht geladen werden (keine Verbindung?)."), InitAsync, L("Erneut versuchen"));
        }
        // after "Restart program" the user is taken back to the settings page
        if (Environment.GetCommandLineArgs().Contains("--open-settings")) OpenWork(NavGroups.First(g => g.Name == "Mehr"), SettingsTab);
    }

    void FillTranslations()
    {
        var list = _all.Where(t => t.LanguageName == (string?)LanguageBox.SelectedItem).OrderBy(t => t.Name).ToList();
        TranslationBox.ItemsSource = list;
        TranslationBox.SelectedItem = list.FirstOrDefault(t => t.Id == _pendingTranslation) ?? list.FirstOrDefault(t => t.Id == "eng_kjv") ?? list.FirstOrDefault();
        _pendingTranslation = null;
    }

    void ApplyTheme(int index) =>
        Avalonia.Application.Current!.RequestedThemeVariant = index switch { 1 => Avalonia.Styling.ThemeVariant.Light, 2 => Avalonia.Styling.ThemeVariant.Dark, _ => Avalonia.Styling.ThemeVariant.Default };

    void SaveSettings()
    {
        KeepAliasesOfOtherInstances();
        try { _settings.Save(_settingsPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* settings are a convenience; never crash on save */ }
    }

    void ShowHome() => ShowHome(true);

    void ShowHome(bool leaveWork)
    {
        if (leaveWork && _activeWork is not null) ShowWork(null);   // a tab such as the settings is left first, else the start page stays hidden behind it
        // each vault remembers its own place; a new or empty vault has none
        Home.Resume = _vault.GetLastPosition() is { } last && _books.Any(b => b.Id == last.Book) ? (last.Book, last.Chapter, $"{BookName(last.Book)} {last.Chapter}") : null;
        var recent = new List<(string, string, string, Action)>();
        var votd = VerseOfTheDay();
        if (PlanTodayCard() is { } planCard) recent.Add((L("Heute dran"), planCard.Label, planCard.Hint, planCard.Go));   // the running reading plan comes first
        recent.Add((L("Vers des Tages"), Label(votd), L("Klicken zum Lesen"), () => _ = GoToAsync(votd.Book, votd.Chapter, votd.Verse)));
        var dueCards = Memory.Ids.Count(i => Memory.IsDue(i, DateTime.Now));
        if (dueCards > 0) recent.Add((L("Auswendig lernen"), L("{0} Verse fällig", dueCards), L("Klicken zum Üben"), () => OpenWork(NavGroups.First(g => g.Name == "Planen"), MemoryTab)));
        foreach (var d in _vault.GetDocuments().OrderByDescending(d => d.Updated).Take(4))
        {
            var doc = d;
            recent.Add((L("Dokument"), d.Title, Shorten(string.Join(" ", DocumentLinks.WithLinkTexts(d.Body).Split((char)10).Where(l => !l.TrimStart().StartsWith("#") && !l.StartsWith("**Quelle")).Take(6)).Trim(), 80), () => { SetPanelOpen(true); SideTabs.SelectedIndex = KnowledgeTab; OpenDocument(doc); }));
        }
        foreach (var m in _vault.GetMarks().OrderByDescending(m => m.Id).Take(4))
        {
            var mark = m;
            recent.Add((L("Markierung"), Label(m.Ref), m.Note.Length > 0 ? Shorten(m.Note.ReplaceLineEndings(" "), 80) : Shorten(m.Text, 80), () => _ = GoToAsync(mark.Ref.Book, mark.Ref.Chapter, mark.Ref.Verse)));
        }
        Home.Recent = recent;
        Home.Show(_books, _read, Home.OpenBook);
        Home.IsVisible = true; ReaderHost.IsVisible = false; GraphPane.IsVisible = false; MapPane.IsVisible = false; TimelinePane.IsVisible = false; FamilyPane.IsVisible = false; KnowledgePane.IsVisible = false;
    }

    void ShowReader() { if (_activeWork is not null) ShowWork(null); Home.IsVisible = false; GraphPane.IsVisible = false; MapPane.IsVisible = false; TimelinePane.IsVisible = false; FamilyPane.IsVisible = false; KnowledgePane.IsVisible = false; ReaderHost.IsVisible = true; OnReaderShown(); }

    async Task GoToAsync(string bookId, int chapter, int? verse = null)
    {
        var book = _books.FirstOrDefault(b => b.Id == bookId);
        if (book is null) { StatusText.Text = L("Buch in dieser Übersetzung nicht vorhanden."); return; }
        _loading = true;
        BookBox.SelectedItem = book;
        ChapterBox.Value = chapter;
        _loading = false;
        ShowReader();
        await ShowChapterAsync(verse);
    }

    /// <summary>One chapter forward or back; at the end of a book it goes on to the next (or the last chapter of the previous) book and stops at the first and last book.</summary>
    async Task StepChapterAsync(int delta)
    {
        if (BookBox.SelectedItem is not BookInfo book) return;
        var chapter = (int)(ChapterBox.Value ?? 1) + delta;
        var i = _books.ToList().IndexOf(book);
        if (chapter < 1)
        {
            if (i <= 0) return;
            var prev = _books[i - 1];
            await GoToAsync(prev.Id, prev.NumberOfChapters);
        }
        else if (chapter > book.NumberOfChapters)
        {
            if (i < 0 || i >= _books.Count - 1) return;
            await GoToAsync(_books[i + 1].Id, 1);
        }
        else ChapterBox.Value = chapter;
    }

    async Task GoBackAsync()
    {
        if (Home.IsVisible || GraphPane.IsVisible || FamilyPane.IsVisible || TimelinePane.IsVisible || MapPane.IsVisible || KnowledgePane.IsVisible) { ShowReader(); return; }
        RememberPlace();
        if (_history.Count < 2) return;
        _forward.Add(_history[^1]);
        _history.RemoveAt(_history.Count - 1);
        var target = _history[^1];
        _history.RemoveAt(_history.Count - 1); // GoToAsync records it again
        await ShowPlaceAsync(target);
    }

    /// <summary>The way back again, after <see cref="GoBackAsync"/>; a new jump somewhere else forgets it (as in a browser).</summary>
    async Task GoForwardAsync()
    {
        if (Home.IsVisible || GraphPane.IsVisible || FamilyPane.IsVisible || TimelinePane.IsVisible || MapPane.IsVisible || KnowledgePane.IsVisible) { ShowReader(); return; }
        if (_forward.Count == 0) return;
        RememberPlace();
        var target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        await ShowPlaceAsync(target);
    }

    /// <summary>A place in the history: the chapter, and the verse and the scroll position it was left at.</summary>
    sealed class Place(string book, int chapter, int? verse)
    {
        public string Book { get; } = book;
        public int Chapter { get; } = chapter;
        public int? Verse { get; set; } = verse;
        public Avalonia.Vector? Offset { get; set; }
    }

    readonly List<Place> _forward = [];
    bool _byHistory;

    /// <summary>Notes where the reader is right now in the current place of the history: going back returns to that very verse, not just to the chapter.</summary>
    void RememberPlace()
    {
        if (_byHistory || _history.Count == 0 || _history[^1] is not { } p || p.Book != _curBook || p.Chapter != _curChapter) return;
        p.Verse = _selected; p.Offset = ReaderScroll.Offset;
    }

    async Task ShowPlaceAsync(Place target)
    {
        _byHistory = true;
        try { await GoToAsync(target.Book, target.Chapter, target.Verse); } finally { _byHistory = false; }
        if (target.Offset is { } offset) Avalonia.Threading.Dispatcher.UIThread.Post(() => ReaderScroll.Offset = offset, Avalonia.Threading.DispatcherPriority.Background);
        UpdateHistoryButtons();
    }

    void UpdateHistoryButtons()
    {
        ForwardBtn.IsEnabled = _forward.Count > 0;
    }

    /// <summary>The side buttons of the mouse (back / forward), anywhere in the window.</summary>
    void OnMouseSideButtons(object? sender, PointerPressedEventArgs e)
    {
        var p = e.GetCurrentPoint(this).Properties;
        if (p.IsXButton1Pressed) { e.Handled = true; _ = GoBackAsync(); }
        else if (p.IsXButton2Pressed) { e.Handled = true; _ = GoForwardAsync(); }
    }

    void SetRead(string book, IReadOnlyList<int> chapters, bool read)
    {
        if (book == "") return;
        _vault.SetRead(book, chapters, read);
        _read = _vault.GetReadChapters();
        if (Home.IsVisible) Home.Show(_books, _read, Home.OpenBook);
        ReadBtn.IsChecked = _read.Contains((_curBook, _curChapter));
    }

    void UpdateOfflineInfo()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return;
        var dir = Path.Combine(_root, "cache", "api", t.Id);
        var count = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Count(f => !f.EndsWith(".words.json") && Path.GetFileName(f) != "books.json") : 0;
        var total = _books.Sum(b => b.NumberOfChapters);
        OfflineInfo.Text = L("{0}: {1} von {2} Kapiteln offline verfügbar. Lizenz: {3}.", t.Name, count, total, (t.License.PublicDomain ? L("gemeinfrei") : t.LicenseUrl));
    }

    async Task OfflineAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t || _building) return;
        _building = true; OfflineBtn.IsEnabled = false;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => OfflineInfo.Text = L("{0}: {1}/{2} Kapitel geladen …", t.ShortName, p.Done, p.Total));
            await OfflineDownloader.DownloadAsync(_provider, t.Id, progress);
            UpdateOfflineInfo();
        }
        catch (Exception ex) { OfflineInfo.Text = L("Fehler: ") + ex.Message; }
        finally { _building = false; OfflineBtn.IsEnabled = true; }
    }

    int _booksGeneration;

    async Task LoadBooksAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return;
        var keepVerse = _selected;   // switching the translation must not throw you out of the verse you were reading
        var generation = ++_booksGeneration;   // only the newest load may fill the lists
        try
        {
            _loading = true;
            var prev = (BookBox.SelectedItem as BookInfo)?.Id ?? _settings.BookId;
            var loaded = ApplyBookNames(await _provider.GetBooksAsync(t.Id));
            if (generation != _booksGeneration) return;
            _books = loaded;
            BookBox.ItemsSource = _books;
            BookBox.SelectedItem = _books.FirstOrDefault(b => b.Id == prev) ?? _books.FirstOrDefault(b => b.Id == "JHN") ?? _books.FirstOrDefault();
            if (_firstBooks) { ChapterBox.Value = Math.Max(1, _settings.Chapter); _firstBooks = false; }
        }
        catch (Exception ex) { StatusText.Text = L("Fehler: ") + ex.Message; }
        finally { _loading = false; }
        if (Home.IsVisible) Home.Show(_books, _read, Home.OpenBook);
        if (SideTabs.SelectedIndex == SettingsTab) UpdateOfflineInfo();
        RefreshLists();
        foreach (var tab in _contextTabs.Values.ToList()) _ = DrawContextTabAsync(tab);   // they read in this translation and name its books
        await RedrawChapterAsync(keepVerse);
    }

    /// <summary>Draws the chapter on the screen again as it is (the verse chosen stays, the page stays where it is).</summary>
    Task RedrawChapterAsync() => ShowChapterAsync(_selected, true);
    Task RedrawChapterAsync(int? verse) => ShowChapterAsync(verse, true);

    async Task ShowChapterAsync(int? focusVerse = null, bool keepScroll = false)
    {
        if (_loading || TranslationBox.SelectedItem is not TranslationInfo t || BookBox.SelectedItem is not BookInfo b) return;
        SplashWindow.Report(0.88, L("Öffne Kapitel …"));
        // fast paging starts several loads; only the newest may draw (a slow older answer must not replace the chapter shown)
        var generation = ++_showGeneration;
        try
        {
            var number = (int)(ChapterBox.Value ?? 1);
            var extraCols = _extras.Where(e => e.Translation is not null).ToList();
            var extraTs = extraCols.Select(e => e.Translation!).ToList();
            var primaryTask = _provider.GetChapterAsync(t.Id, b.Id, number);
            var extraTasks = extraCols.Select(e => TryAsync(_provider.GetChapterAsync(e.Translation!.Id, e.Own?.Book ?? b.Id, e.Own?.Chapter ?? number))).ToList();
            var ch = await primaryTask;
            var cols = new List<(TranslationInfo T, Chapter? C)> { (t, ch) };
            cols.AddRange(extraTs.Zip(await Task.WhenAll(extraTasks), (c, x) => (c, x)));
            if (generation != _showGeneration) return;

            var sameChapter = keepScroll && _curBook == b.Id && _curChapter == ch.Number;
            var keepOffset = ReaderScroll.Offset; var keepCols = _colScrolls.Select(s => s.Offset).ToList();
            // a text selection belongs to the chapter it was made in (kept, its offsets would mark the same verse number of the next chapter)
            if (!sameChapter) RememberPlace();
            if (_curBook != b.Id || _curChapter != ch.Number) ClearTextSelection(false);
            _curBook = b.Id; _curChapter = ch.Number; _selected = null; _selVerses.Clear(); _anchorVerse = null;
            if (ReaderHost.IsVisible && !_switchingVault) { try { _vault.SetLastPosition(_curBook, _curChapter); } catch (Exception) { /* a convenience; never block reading */ } }
            ThreadPane.SelectedVerse = null;
            FollowSentence(true);
            FollowPericope(true);
            CollabSendLocation(focusVerse);
            _curChapterData = ch;
            LoadChapterMarks();
            await LoadMarkSourcesAsync(cols.Select(c => (c.T.Id, c.C)));
            if (generation != _showGeneration) return;
            var noted = _vault.GetNotes().Where(n => n.Ref.Book == _curBook && n.Ref.Chapter == _curChapter).Select(n => n.Ref.Verse).ToHashSet();
            var size = cols.Count > 2 ? _fontSize - 3 : _fontSize;

            ReaderPanel.Children.Clear(); _verseBoxes.Clear(); _wordCells.Clear(); _cells.Clear(); _headingControls.Clear(); _hoverSpan = null;
            if (_animKey != $"{_curBook}{_curChapter}") { _animKey = $"{_curBook}{_curChapter}"; if (_resume is null) AnimateIn(ReaderPanel); }
            _singleColumn = cols.Count == 1;
            // a column with a passage of its own cannot run verse by verse next to the others: it gets its own scroller
            _colPassage = [null, .. extraCols.Select(e => e.Own is { } o ? $"{BookName(o.Book)} {o.Chapter}" : null)];
            _independent = cols.Count > 1 && (!_settings.ParallelSync || extraCols.Any(e => e.Own is not null));
            _colBoxes.Clear(); _colScrolls.Clear(); _colIds.Clear(); _colForeign.Clear(); _mirrorExpect.Clear(); _indepGrid = null;
            ParallelHeader.IsVisible = false;
            FitReader();
            var kicker = new TextBlock { Text = b.CommonName.ToUpperInvariant(), FontSize = 13, FontWeight = FontWeight.SemiBold, LetterSpacing = 3, Margin = new(8, 0, 0, 0) };
            kicker.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
            var numeral = new TextBlock { Text = _settings.ChapterHeading && !_independent ? ch.Number.ToString() : $"{b.CommonName} {ch.Number}", FontSize = _settings.ChapterHeading && !_independent ? 64 : 26, FontFamily = UiSerif, FontWeight = FontWeight.SemiBold, Margin = new(8, 0, 0, 14) };
            numeral.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
            if (_settings.ChapterHeading && !_independent) ReaderPanel.Children.Add(kicker);
            ReaderPanel.Children.Add(numeral);
            if (cols.Count > 1 && !_independent) BuildStickyHeader(cols);
            var notes = cols.Select(c => c.C?.Footnotes?.OrderBy(f => f.NoteId).Select((f, i) => (f.NoteId, Letter: NoteLetter(i), f.Text))
                .ToDictionary(x => x.NoteId, x => (x.Letter, x.Text))).ToList();
            await LoadChapterRefsAsync(b.Id, number);
            if (generation != _showGeneration) return;
            if (_independent) BuildIndependent(cols, size, noted, notes!, t);
            else foreach (var num in cols.SelectMany(c => c.C?.Verses.Select(v => v.Number) ?? []).Distinct().Order())
            {
                AddTranslationHeadings(ReaderPanel, cols[0].C, num);
                AddHeadings(ReaderPanel, num);
                var row = NewRow(cols.Count);
                for (var i = 0; i < cols.Count; i++)
                    AddCell(row, i, ColumnCell(VerseCell(cols[i].C?.Verses.FirstOrDefault(v => v.Number == num), num, cols[i].T, size, i == 0 && noted.Contains(num), notes[i], i), i, cols.Count));
                var box = MakeVerseBox(XrefChips(num, t.Id) is { } chips ? new StackPanel { Children = { row, chips } } : row, num);
                _verseBoxes[num] = box;
                ReaderPanel.Children.Add(box);
            }
            Paint();
            ApplyWordMarks();
            ShowThread(_threadOn && cols.Count == 1);
            void KeepPlace() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ReaderScroll.Offset = keepOffset;
                for (var k = 0; k < Math.Min(keepCols.Count, _colScrolls.Count); k++) _colScrolls[k].Offset = keepCols[k];
            }, Avalonia.Threading.DispatcherPriority.Background);
            if (focusVerse is int fv && _verseBoxes.ContainsKey(fv)) { SelectVerse(fv); if (sameChapter) KeepPlace(); else ScrollToVerse(fv); }
            else if (sameChapter) KeepPlace();
            else ReaderScroll.Offset = default;

            ApplyResume();
            ReadBtn.IsChecked = _read.Contains((b.Id, number));
            // a jump inside the chapter to another verse is a step too (a cross reference a few verses on); clicking a verse is not
            var last = _history.Count > 0 ? _history[^1] : null;
            if (_byHistory || last is null || last.Book != b.Id || last.Chapter != number || (!keepScroll && focusVerse is int && last.Verse != focusVerse)) { _history.Add(new Place(b.Id, number, focusVerse)); if (_history.Count > 100) _history.RemoveAt(0); if (!_byHistory) _forward.Clear(); }
            UpdateHistoryButtons();
            _settings.TranslationId = t.Id; _settings.BookId = b.Id; _settings.Chapter = number;
            UpdateTitle(b.CommonName, number, t.ShortName);
            LibraryFollowReader();
            if (_startupHome) { _startupHome = false; ShowHome(); }

            StatusText.Text = string.Join("  |  ", cols.Select(c =>
                L("{0}: {1}, Namensnennung: {2}", c.T.Name, (c.T.License.PublicDomain ? L("gemeinfrei") : L("Lizenz ") + c.T.LicenseUrl), AttributionText(c.T.License.Attribution)))) + L(" · Quelle: {0}", _provider.Name);
        }
        catch (Exception ex) when (generation == _showGeneration) { ChapterFailed(ex, focusVerse); }
        catch (Exception) { }
        finally { SplashWindow.Done(); }
    }

    int _showGeneration;

    /// <summary>The server says "required" or "not-required"; the status line says it in the language of the program.</summary>
    static string AttributionText(string a) => a switch { "required" => L("erforderlich"), "not-required" => L("nicht nötig"), "-" or "" => "–", _ => a };

    static async Task<Chapter?> TryAsync(Task<Chapter> task)
    {
        try { return await task; } catch { return null; }
    }

    static Grid NewRow(int columns)
    {
        var g = new Grid();
        for (var i = 0; i < columns; i++) g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        return g;
    }

    static void AddCell(Grid row, int column, Control cell)
    {
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    // Crimson Text (SIL OFL) is bundled; system serif fonts cover Greek/Hebrew glyphs it lacks.
    static readonly FontFamily UiSerif = new("avares://BibleStudy.App/Assets/Fonts#Crimson Text, Cambria, Georgia, serif");
    FontFamily ScriptureFont => _settings.ReadableText ? new FontFamily("Verdana, Tahoma, Segoe UI, sans-serif") : _settings.ReaderFont switch
    {
        "Crimson Text" => UiSerif,
        var name => new FontFamily(name + ", Cambria, Georgia, serif"),
    };

    /// <summary>Clickable word range inside a verse cell (for the word study).</summary>
    sealed record WordSpan(int Start, int End, string[] Strongs, string Word, string? Note = null, Run? Run = null, IBrush? Base = null);

    static string NoteLetter(int i) => i < 26 ? ((char)('a' + i)).ToString() : NoteLetter(i / 26 - 1) + NoteLetter(i % 26);

    /// <summary>Maps a stretch of the cell's text (Tb) to its offset in the plain verse text (Raw).</summary>
    sealed record RunMap(int Tb, int Raw, int Len);

    /// <summary>Everything the pointer handlers need to know about one verse cell.</summary>
    sealed record CellInfo(List<WordSpan> Spans, List<RunMap> Runs, int Verse, string TranslationId, string RawText);

    /// <summary>What a verse cell is made of, kept so it can be drawn again on its own (new marks, a selection, a note) without building the chapter anew.</summary>
    sealed record CellData(Verse V, int Number, TranslationInfo T, int Size, bool HasNote, IReadOnlyDictionary<int, (string Letter, string Text)>? Notes, int Col, bool Foreign);

    /// <summary>A cell on the page: the text block and what it shows.</summary>
    sealed class CellRef(SelectableTextBlock tb, CellData data) { public SelectableTextBlock Tb { get; } = tb; public CellData Data { get; set; } = data; }

    /// <summary>The part of a verse that a mark covers in a cell; Core = a mark inside a verse that is marked as a whole (the most important part of it); Shared = made in another translation.</summary>
    sealed record CellMark(Mark Mark, int Start, int End, bool Core, bool Shared, bool Estimated = false);

    readonly List<CellRef> _cells = [];

    SelectableTextBlock VerseCell(Verse? v, int number, TranslationInfo t, int size, bool hasNote, IReadOnlyDictionary<int, (string Letter, string Text)>? notes, int col = 0, bool foreign = false)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = size, LineHeight = size * _settings.LineSpacing, Margin = new(0, 0, 12, 0), LetterSpacing = _settings.ReadableText ? size * 0.05 : 0,
            TextAlignment = _settings.Justify ? TextAlignment.Justify : TextAlignment.Left,
            FontFamily = ScriptureFont,
            FlowDirection = t.TextDirection == "rtl" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
        if (!foreign)
        {
            tb.Cursor = new Cursor(StandardCursorType.Ibeam);
            tb.SelectionBrush = Brushes.Transparent;   // what is selected is shown by our own selection (words only, never the verse number)
        }
        // the verse menu is ours; the built-in one (a lone "Copy Ctrl+C") would sit on top of it
        tb.AddHandler(ContextRequestedEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
        if (v is null) return tb;
        var data = new CellData(v, number, t, size, hasNote, notes, col, foreign);
        FillCell(tb, data, null);
        _cells.Add(new CellRef(tb, data));
        if (StudyBtn.IsChecked == true && ((CellInfo)tb.Tag!).Spans.Any(x => x.Run is not null))
        {
            _wordCells.Add(tb);
            tb.PointerMoved += (_, e) => OnWordHover(tb, e);
            tb.PointerExited += (_, _) => OnWordHover(tb, null);
        }
        return tb;
    }

    /// <summary>The marks that show in a cell, as the part of the verse each covers (and from other translations, when that is switched on).</summary>
    List<CellMark> CellMarks(CellData d, string rawText)
    {
        var list = new List<CellMark>();
        if (d.Foreign) return list;
        var core = _wholeMarks.ContainsKey(d.Number) || _otherWhole.ContainsKey(d.Number);
        foreach (var m in _chapterMarks.Concat(_otherChapterMarks))
        {
            if (m.WholeVerse || !m.Covers(d.Number)) continue;
            if (m.TranslationId == d.T.Id)
            {
                if (MarkLogic.PartIn(m, d.Number, rawText.Length) is { } p) list.Add(new CellMark(m, p.Start, p.End, core, false));
            }
            else if (_settings.ShareMarksAcrossTranslations && m.TranslationId != Mark.AnyTranslation && SharedPart(m, d, rawText) is { } q)
                list.Add(new CellMark(m, q.Start, q.End, core, true, q.Estimated));
        }
        return [.. list.OrderBy(x => x.Shared ? 0 : 1)];   // the marks made in this very translation lie on top
    }

    /// <summary>The marks of other translations on this verse that cannot be placed in this one: they are not lost, a small sign says so.</summary>
    List<Mark> UnplacedMarks(CellData d, string rawText)
    {
        if (d.Foreign || !_settings.ShareMarksAcrossTranslations) return [];
        return _chapterMarks.Where(m => !m.WholeVerse && m.Covers(d.Number) && m.TranslationId != d.T.Id && m.TranslationId != Mark.AnyTranslation && SharedPart(m, d, rawText) is null).ToList();
    }

    /// <summary>Where a mark made in another translation lies in this verse (null if it cannot be told for sure).</summary>
    (int Start, int End, bool Estimated)? SharedPart(Mark m, CellData d, string rawText)
    {
        if (_chapterMarks.Contains(m) && _markPlaces.TryGetValue((m.Id, d.T.Id, d.Number), out var byHand) && byHand.End <= rawText.Length) return (byHand.Start, byHand.End, false);   // set by hand
        if (!_markSources.TryGetValue(m.TranslationId, out var ch) || ch.Verses.FirstOrDefault(x => x.Number == d.Number) is not { } sourceVerse)
            return MarkLogic.ProjectByStoredText(m, rawText, d.Number) is { } stored ? (stored.Start, stored.End, false) : null;   // its translation is not here: the words the mark stored
        var sourceLen = MarkLogic.RawText(sourceVerse.Segments).Length;
        if (MarkLogic.PartIn(m, d.Number, sourceLen) is not { } part) return null;
        if (part.Start <= 0 && part.End >= sourceLen) return (0, rawText.Length, false);   // the whole verse lies inside the mark
        return MarkLogic.ProjectChecked(sourceVerse.Segments, part.Start, part.End, d.V.Segments);
    }

    /// <summary>Draws the text of a verse cell (number, words, footnote markers, marks and the selection).</summary>
    void FillCell(SelectableTextBlock tb, CellData d, (int Start, int End)? sel)
    {
        tb.Inlines!.Clear();
        var v = d.V; var number = d.Number; var t = d.T; var size = d.Size;
        var rawText = MarkLogic.RawText(v.Segments);
        var marks = CellMarks(d, rawText);
        var study = StudyBtn.IsChecked == true;
        var spans = new List<WordSpan>();
        var runs = new List<RunMap>();
        var pos = 0;
        var raw = 0; // offset in the plain verse text (without number and footnote markers)
        void Add(Run run)
        {
            tb.Inlines!.Add(run);
            pos += run.Text!.Length;
        }
        var numSize = _settings.VerseNumbers == "Normal" ? size * 0.8 : 14;
        var star = _chapterBookmarks.Contains(number) ? "★" : "";   // on the Lesezeichen
        var num = new Run(_settings.VerseNumbers == "Hidden" ? (d.HasNote ? "✎ " : "") + (star.Length > 0 ? star + " " : "") : number + (d.HasNote ? "✎" : "") + star + " ") { FontSize = numSize, FontWeight = FontWeight.Bold, BaselineAlignment = _settings.VerseNumbers == "Normal" ? BaselineAlignment.Baseline : BaselineAlignment.Top };
        num.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsVerseNumber"));
        Add(num);
        if (UnplacedMarks(d, rawText) is { Count: > 0 } unplaced)
        {
            var sign = new Run("° ") { FontSize = size * 0.62, FontWeight = FontWeight.Bold, BaselineAlignment = BaselineAlignment.Superscript };
            sign.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsAccent"));
            spans.Add(new WordSpan(pos, pos + sign.Text!.Length, [], "°", L("Hier nicht darstellbar: {0}", string.Join("; ", unplaced.Select(m => "„" + Shorten(m.Text, 60) + "“")))));
            Add(sign);
        }
        (Run Run, object? Mark, bool Selected, bool Jesus)? mergeable = null;
        foreach (var s in v.Segments)
        {
            if (s.NoteId is int nid)
            {
                mergeable = null;
                if (d.Notes is null || !d.Notes.TryGetValue(nid, out var note)) { Add(new Run(" ")); continue; }
                var marker = new Run(note.Letter + " ") { FontSize = size * 0.62, FontWeight = FontWeight.Bold, BaselineAlignment = BaselineAlignment.Superscript };
                marker.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsAccent"));
                spans.Add(new WordSpan(pos, pos + marker.Text!.Length, [], note.Letter, note.Text));
                Add(marker);
                continue;
            }
            // Cut the segment where highlights or the selection start or end, so each piece has one background color.
            var cuts = new SortedSet<int> { 0, s.Text.Length };
            foreach (var m in marks) { cuts.Add(Math.Clamp(m.Start - raw, 0, s.Text.Length)); cuts.Add(Math.Clamp(m.End - raw, 0, s.Text.Length)); }
            if (sel is { } sl) { cuts.Add(Math.Clamp(sl.Start - raw, 0, s.Text.Length)); cuts.Add(Math.Clamp(sl.End - raw, 0, s.Text.Length)); }
            var points = cuts.ToList();
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var piece = s.Text[points[i]..points[i + 1]];
                var from = raw + points[i];
                // Neighbouring pieces with the same background are drawn as one run: two backgrounds that touch leave a thin dark seam
                // between them (their edges are anti-aliased). Not while the word study is on: there every word is painted on its own.
                var selected = sel is { } sq && piece.Length > 0 && sq.Start <= from && sq.End >= from + piece.Length;
                var markHere = selected ? null : marks.LastOrDefault(m => m.Start <= from && m.End >= from + piece.Length);
                if (!study && (selected || markHere is not null) && mergeable is { } mg && mg.Selected == selected && ReferenceEquals(mg.Mark, markHere) && mg.Jesus == s.WordsOfJesus && piece.Length > 0)
                {
                    mg.Run.Text += piece;
                    pos += piece.Length;
                    if (s.Strongs is { Count: > 0 }) spans.Add(new WordSpan(pos - piece.Length, pos, [.. s.Strongs], s.Text, null, mg.Run, mg.Run.Background));
                    runs[^1] = runs[^1] with { Len = runs[^1].Len + piece.Length };
                    continue;
                }
                var run = new Run(piece);
                mergeable = (selected || markHere is not null) ? (run, markHere, selected, s.WordsOfJesus) : null;
                if (s.WordsOfJesus && _settings.JesusRed && Color.TryParse(_settings.JesusColor, out var jesusColor)) run.Foreground = new SolidColorBrush(jesusColor);
                else if (s.WordsOfJesus && _settings.JesusRed) run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsJesus"));
                if (sel is { } sp && piece.Length > 0 && sp.Start <= from && sp.End >= from + piece.Length) run.Background = SelectionBrush();
                else if (marks.LastOrDefault(m => m.Start <= from && m.End >= from + piece.Length) is { } mark)
                {
                    run.Background = HighlightBrush(mark.Mark.Color, mark.Core);
                    if (mark.Core) run.FontWeight = FontWeight.SemiBold;
                    if (mark.Estimated && !study) run.TextDecorations = EstimatedUnderline;   // only a guess at the place: a click corrects it
                }
                if (s.Strongs is { Count: > 0 })
                {
                    spans.Add(new WordSpan(pos, pos + piece.Length, [.. s.Strongs], s.Text, null, run, run.Background));
                    if (study) run.TextDecorations = s.Estimated ? EstimatedUnderline : TextDecorations.Underline;
                }
                runs.Add(new RunMap(pos, from, piece.Length));
                Add(run);
            }
            raw += s.Text.Length;
        }
        Add(new Run(" "));
        tb.Tag = new CellInfo(spans, runs, number, t.Id, rawText);
    }

    /// <summary>Translucent so the text stays readable on both themes. A mark inside a marked verse (<paramref name="core"/>) is stronger: it is the most important part.</summary>
    IBrush HighlightBrush(string hex, bool core = false)
    {
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var alpha = core ? (dark ? 0.8 : 0.88) : dark ? 0.45 : 0.55;
        var c = MarkColor(hex);
        // Every word is its own Run; where two see-through backgrounds touch they overlap by a sliver and drew a dark line.
        // So the colour is mixed with the page colour once and drawn solid.
        if (Res("BsBackground") is not SolidColorBrush { Color: var bg }) return new SolidColorBrush(c, alpha);
        byte Mix(byte f, byte b) => (byte)Math.Round(f * alpha + b * (1 - alpha));
        return new SolidColorBrush(Color.FromRgb(Mix(c.R, bg.R), Mix(c.G, bg.G), Mix(c.B, bg.B)));
    }

    /// <summary>The color of the text you have selected (before it is marked): the accent color, see-through.</summary>
    IBrush SelectionBrush() => new SolidColorBrush(SelectionInk(),ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? 0.5 : 0.38);

    /// <summary>The colour of a mark; a value that is no colour (an imported or hand-edited vault) must not stop the chapter from showing.</summary>
    static Color MarkColor(string hex) => Color.TryParse(hex, out var c) ? c : Color.Parse(PresetColors[0]);

    void OnVersePressed(int verse, PointerPressedEventArgs e, bool foreign = false)
    {
        var props = e.GetCurrentPoint(this).Properties;
        var withKeys = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!foreign)   // a column with another passage is only for reading and word study: its verse numbers are not those of the main text
        {
            // right click on text that is selected keeps the selection: the menu works on it
            if (!(props.IsRightButtonPressed && _sel is not null)) ClearTextSelection(true);
            if (props.IsLeftButtonPressed && withKeys) SelectVerseWithKeys(verse, e.KeyModifiers);
            else if (!(props.IsRightButtonPressed && _selVerses.Contains(verse) && _selVerses.Count > 1)) SelectVerse(verse);   // right click inside several chosen verses keeps them
            if (props.IsRightButtonPressed && _verseBoxes.TryGetValue(verse, out var anchor))
            {
                e.Handled = true; ShowVerseMenu(verse, anchor, SpanUnder(e)); return;
            }
        }
        // a marked verse has a colored box: a click on its edge (left of the text, where the verse number sits) lands on the box, not on the text
        var tb = e.Source as SelectableTextBlock;
        if (tb is null && e.Source is Border && _cells.FirstOrDefault(c => c.Data.Number == verse && !c.Data.Foreign && c.Data.Col == 0)?.Tb is { } own && e.GetPosition(own).X < own.Bounds.Width)
            tb = own;
        if (tb?.Tag is not CellInfo cell) return;
        var hit = tb.TextLayout.HitTestPoint(e.GetPosition(tb)).TextPosition;
        var span = cell.Spans.FirstOrDefault(s => hit >= s.Start && hit < s.End);
        if (span is not null)
        {
            if (span.Note is not null) ShowFootnote(tb, span);
            else if (StudyBtn.IsChecked == true) _ = ShowStudyAsync(span);
        }
        // a click on the verse number marks the whole verse: the colors show over it (like over selected text)
        var onNumber = cell.Runs.Count > 0 && hit < cell.Runs[0].Tb && span?.Note is null;   // the small sign beside the number is not the number
        if (!foreign && props.IsLeftButtonPressed && !withKeys && onNumber && _settings.SelectionPopup && !_settings.QuickMark)
        {
            e.Handled = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowSelectionPopup(verse), Avalonia.Threading.DispatcherPriority.Background);
        }
        else if (!foreign && props.IsLeftButtonPressed && !withKeys && onNumber && _settings.QuickMark)
        {
            // quick marking: a click on the verse number marks the whole verse in the quick color; on a verse of that color it takes the mark away again
            e.Handled = true;
            if (_wholeMarks.TryGetValue(verse, out var have) && string.Equals(have, QuickColor(), StringComparison.OrdinalIgnoreCase)) _ = RemoveMarksAsync();
            else _ = ApplyColorAsync(QuickColor());
        }
        else if (span?.Note is not null && props.IsLeftButtonPressed && !withKeys) e.Handled = true;   // its little window stays open: no selection starts here
        else if (!foreign && props.IsLeftButtonPressed && !withKeys) BeginTextSelection(e, tb, cell);
        else if (!foreign && props.IsLeftButtonPressed) e.Handled = true;   // Ctrl/Shift choose verses; the built-in selection must not start
    }

    void ShowFootnote(Control anchor, WordSpan span)
    {
        var title = new TextBlock { Text = span.Word == "°" ? L("Markierung") : L("Fußnote {0}", span.Word), FontWeight = FontWeight.SemiBold };
        title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        var body = new TextBlock { Text = span.Note, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 };
        new Flyout { Content = new StackPanel { Spacing = 4, Children = { title, body } }, Placement = PlacementMode.Pointer }.ShowAt(anchor);
    }

    async Task ShowStudyAsync(WordSpan span)
    {
        RevealSideTab(3);
        StudyPanel.Children.Clear();
        StudyPanel.Children.Add(new TextBlock { Text = Loc.Quote(span.Word.Trim()), FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        foreach (var id in span.Strongs)
        {
            try
            {
                StatusText.Text = L("Lade Wörterbuch …");
                var e = await _strongs.LookupAsync(id);
                StudyPanel.Children.Add(e is null ? new TextBlock { Text = id + L(": kein Eintrag") } : StudyCard(e));
                if (e is not null && await StepCardAsync(id) is { } step) StudyPanel.Children.Add(step);
                var all = new Button { Content = L("Alle Vorkommen"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
                var conc = $"{id}  {e?.Lemma}  „{span.Word.Trim()}“";
                all.Click += async (_, _) => await ShowConcordanceAsync(id, conc);
                var chart = new Button { Content = L("Vorkommen als Diagramm"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Margin = new(6, 0, 0, 0) };
                var chartWord = span.Word;
                chart.Click += async (_, _) => await ShowWordChartAsync(id, chartWord);
                StudyPanel.Children.Add(new WrapPanel { Children = { all, chart } });
            }
            catch (Exception ex) { StudyPanel.Children.Add(new TextBlock { Text = L("{0}: Wörterbuch nicht verfügbar ({1})", id, ex.Message), TextWrapping = TextWrapping.Wrap }); }
        }
        await AddWordExtrasAsync(span);
        var note = new TextBlock { Text = StrongsDictionary.Attribution + "  " + StepLexicon.Attribution, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        note.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        StudyPanel.Children.Add(note);
        StatusText.Text = "";
    }

    Control StudyCard(StrongsEntry e)
    {
        TextBlock Line(string label, string text)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0) };
            tb.Inlines!.Add(new Run(label + " ") { FontWeight = FontWeight.SemiBold });
            tb.Inlines.Add(new Run(text));
            return tb;
        }
        var head = new TextBlock { Text = e.Lemma, FontSize = 28, FontFamily = ScriptureFont, FlowDirection = e.Id[0] == 'H' ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        head.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        var sub = new TextBlock { Text = $"{e.Id}  ·  {e.Translit}{(e.Pronunciation.Length > 0 ? "  ·  " + e.Pronunciation : "")}" };
        sub.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        var panel = new StackPanel { Spacing = 2, Children = { head, sub } };
        var lines = new List<(Run Run, string English)>();
        void Add(string label, string english)
        {
            var tb = Line(label, Mean(english));
            lines.Add(((Run)tb.Inlines![1], english));
            panel.Children.Add(tb);
        }
        if (e.Derivation.Length > 0) Add(L("Herkunft:"), e.Derivation);
        if (e.Definition.Length > 0) Add(L("Bedeutung:"), e.Definition);
        if (e.KjvUsage.Length > 0) Add("KJV:", e.KjvUsage);
        if (lines.Any(l => _meanings.IsMissing(l.English)))
            _ = WarmMeaningsAsync(lines.Select(l => l.English), () => { foreach (var (run, en) in lines) run.Text = Mean(en); });
        var card = new Border { Child = panel, Padding = new(12), CornerRadius = new(10), BorderThickness = new(1) };
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        return card;
    }

    void AddColumn(TranslationInfo? preset = null)
    {
        if (_extras.Count >= MaxExtras || _langs.Count == 0) return;
        var lang = new ComboBox { Width = 128, ItemsSource = _langs };
        var trans = new ComboBox { Width = 210 };
        var remove = new Button { Width = 32, Height = 32, Classes = { "tool" }, Content = new Icon { Data = Geometry.Parse("M18 6L6 18 M6 6l12 12"), IconSize = 16 } };
        ToolTip.SetTip(remove, L("Spalte entfernen"));
        var passage = new Button { Classes = { "tool" }, Width = double.NaN, MinWidth = 70, MaxWidth = 150, Height = 32, Padding = new(10, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new(6, 0, 2, 0) };
        ToolTip.SetTip(passage, L("Andere Bibelstelle in dieser Spalte lesen, z. B. 1. Mose neben Daniel. Die Spalte läuft dann für sich."));
        var dot = new Border { Width = 12, Height = 12, CornerRadius = new(6), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new(10, 0, 4, 0) };
        var moveLeft = new Button { Width = 24, Height = 32, Classes = { "tool" }, Content = "‹" };
        var moveRight = new Button { Width = 24, Height = 32, Classes = { "tool" }, Content = "›" };
        ToolTip.SetTip(moveLeft, L("Spalte nach links")); ToolTip.SetTip(moveRight, L("Spalte nach rechts"));
        var root = new Border
        {
            Classes = { "pill" }, Margin = new(16, 8, 0, 8),
            Child = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { dot, moveLeft, moveRight, lang, trans, passage, remove } },
        };
        var col = new ExtraColumn(lang, trans, root, passage);
        UpdatePassageButton(col); AttachColumnMove(col, dot, moveLeft, moveRight);
        passage.Click += (_, _) => ChooseColumnPassage(col);

        lang.SelectionChanged += (_, _) =>
        {
            var list = _all.Where(x => x.LanguageName == (string?)lang.SelectedItem).OrderBy(x => x.Name).ToList();
            var main = (TranslationBox.SelectedItem as TranslationInfo)?.Id;
            trans.ItemsSource = list;
            trans.SelectedItem = list.FirstOrDefault(x => preset is not null && x.Id == preset.Id) ?? list.FirstOrDefault(x => x.Id != main) ?? list.FirstOrDefault();
        };
        trans.SelectionChanged += async (_, _) => await RedrawChapterAsync();
        remove.Click += async (_, _) =>
        {
            _extras.Remove(col); ParallelBar.Children.Remove(root); RecolorPills();
            ParallelBar.IsVisible = _extras.Count > 0;
            await RedrawChapterAsync();
        };
        EnsureSyncPill();
        if (!ParallelBar.Children.Contains(_syncPill!)) ParallelBar.Children.Insert(0, _syncPill!);
        EnsurePresetPill(); if (!ParallelBar.Children.Contains(_presetPill!)) ParallelBar.Children.Insert(1, _presetPill!);
        _extras.Add(col); ParallelBar.Children.Add(root); RecolorPills();
        ParallelBar.IsVisible = true;
        lang.SelectedItem = preset?.LanguageName ?? LanguageBox.SelectedItem;
    }

    /// <summary>Selected verse: soft accent fill; bookmarked verse: accent bar on the left. Otherwise the hover style applies.</summary>
    void Paint()
    {
        // The clicked verse gets a veil and bar in the accent color of the program (or gray / blue by choice), not in a mark color:
        // a mark color means "saved with this color", so a selection must never look like one.
        var ink = SelectionInk();
        foreach (var (n, box) in _verseBoxes.Concat(_colBoxes.Where((_, k) => !IsForeign(k + 1)).SelectMany(d => d)))
        {
            var whole = _wholeMarks.TryGetValue(n, out var hex) || _otherWhole.TryGetValue(n, out hex) ? MarkColor(hex) : (Color?)null;
            var selected = _selVerses.Count > 0 ? _selVerses.Contains(n) : n == _selected;
            if (whole is { } c) box.Background = new SolidColorBrush(c, 0.14);
            else if (selected) box.Background = new SolidColorBrush(ink, 0.12);
            else box.ClearValue(Border.BackgroundProperty);
            box.BorderBrush = selected ? new SolidColorBrush(ink, 0.55) : whole is { } c2 ? new SolidColorBrush(c2) : Brushes.Transparent;
        }
    }

    /// <summary>Scrolls so the verse sits near the top, after the new text has been laid out.</summary>
    void ScrollToVerse(int verse)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_independent)
            {
                for (var j = 0; j < _colScrolls.Count; j++)
                {
                    if (IsForeign(j)) continue;
                    var dict = j == 0 ? _verseBoxes : _colBoxes[j - 1];
                    if (dict.TryGetValue(verse, out var cb) && _colScrolls[j].Content is Control content && cb.TranslatePoint(new Point(0, 0), content) is { } q)
                        _colScrolls[j].Offset = new Vector(0, Math.Max(0, q.Y - 40));
                }
                return;
            }
            if (!_verseBoxes.TryGetValue(verse, out var box)) return;
            if (box.TranslatePoint(new Point(0, 0), ReaderPanel) is { } p) ReaderScroll.Offset = new Vector(0, Math.Max(0, p.Y + ReaderPanel.Margin.Top - 70));
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    void FitReader()
    {
        ReaderPanel.HorizontalAlignment = _singleColumn ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Stretch;
        ReaderScroll.VerticalScrollBarVisibility = _independent ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        ReaderPanel.Margin = _singleColumn ? new(0, 36, 0, 80) : _independent ? new(24, 12, 24, 16) : new(32, 28, 32, 80);
        ReaderPanel.Height = _independent ? Math.Max(240, ReaderScroll.Bounds.Height - 28) : double.NaN;
        if (_indepGrid is not null) _indepGrid.Height = Math.Max(160, ReaderPanel.Height - 70);
        var reserve = 0.0;
        ReaderPanel.Width = _singleColumn ? Math.Clamp(ReaderScroll.Bounds.Width - 64 - reserve, 320, _settings.ReaderWidth) : double.NaN;
    }

    /// <summary>A click into empty space (not on a verse, button or scroll bar) takes the selection away again.</summary>
    void OnReaderBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_selected is null || !e.GetCurrentPoint(ReaderScroll).Properties.IsLeftButtonPressed) return;
        var verseBoxes = _verseBoxes.Values.Concat(_colBoxes.SelectMany(d => d.Values)).ToHashSet();
        for (var v = e.Source as Avalonia.Visual; v is not null && !ReferenceEquals(v, ReaderScroll); v = v.GetVisualParent())   // Avalonia.VisualTree
            if (v is Border b && verseBoxes.Contains(b) || v is Button or Avalonia.Controls.Primitives.ScrollBar or ComboBox or TextBox or SelectableTextBlock) return;
        _selected = null; _selVerses.Clear(); _anchorVerse = null; ClearTextSelection(true); Paint();
        ThreadPane.SelectedVerse = null;
    }

    /// <summary>Numbers that were added afterwards are only an estimate: a dashed yellow line instead of a solid one.</summary>
    static readonly Avalonia.Media.TextDecorationCollection EstimatedUnderline =
    [
        new TextDecoration { Location = TextDecorationLocation.Underline, Stroke = new SolidColorBrush(Color.Parse("#E0A526")), StrokeThickness = 1.5, StrokeDashArray = [2, 2] },
    ];

    void SelectVerse(int n)
    {
        _selected = n; _selVerses.Clear(); _selVerses.Add(n); _anchorVerse = n; Paint();
        ThreadPane.SelectedVerse = n;
        var r = new VerseRef(_curBook, _curChapter, n);
        if (_noteRef != r) SaveNoteDraft();
        NoteTarget.Text = L("Notiz zu ") + Label(r);
        NoteEditor.Text = _noteLoaded = _vault.GetNote(r)?.Text ?? "";
        _noteRef = r;
        LoadNoteFolderBox(r);
        RefreshNotesTreeIfShown();   // the row of this note is shown as the current one (only when the list is seen: it was rebuilt on every click)
        _ = ShowXrefsAsync(r);
        if (TabActive(GrundtextTab)) _ = ShowGrundtextAsync(r);
        if (TabActive(GuideTab)) _ = ShowGuideAsync(r);
        FollowSentence(false);
        LibraryFollowReader();
    }

    /// <summary>Cross references of a verse: outgoing (it points to them), incoming (they point to it) or both.</summary>
    async Task ShowXrefsAsync(VerseRef r)
    {
        try
        {
            var dir = _settings.XrefDirection;
            XrefTitle.Text = L("Querverweise zu ") + Label(r) + " …";
            var refs = new List<CrossRef>();
            string? note = null;
            if (dir is "Out" or "Both") refs.AddRange((await _hello.GetCrossReferencesAsync(r.Book, r.Chapter, r.Verse)).Take(dir == "Both" ? 15 : 20));
            if (dir is "In" or "Both")
            {
                var incoming = IncomingRefs(r);
                if (incoming is null) note = L("Eingehende Verweise brauchen den Verweis-Datensatz (344 000 Verse). Er wird einmal geladen und lokal gespeichert: Knopf „Graph“ oben.");
                else refs.AddRange(incoming.Take(dir == "Both" ? 15 : 20));
            }
            refs = refs.GroupBy(x => x.Target).Select(g => g.First()).ToList();
            refs = VisibleXrefs(r, refs);   // minus what you hid for good
            if (TranslationBox.SelectedItem is not TranslationInfo t) return;
            var texts = await Task.WhenAll(refs.Select(x => VerseTextAsync(t.Id, x)));
            if (_selected != r.Verse || _curBook != r.Book || _curChapter != r.Chapter) return; // user moved on
            _xrefs = refs;
            var kind = dir switch { "In" => L("Eingehende"), "Both" => L("Ein- und ausgehende"), _ => L("Ausgehende") };
            XrefTitle.Text = refs.Count == 0 ? L("Keine Querverweise zu {0}", Label(r)) : L("{0} Querverweise zu {1}", kind, Label(r));
            if (note is not null) XrefTitle.Text += "\n" + note;
            XrefList.ItemsSource = refs.Zip(texts, (x, text) =>
                new Entry(x.Target, $"{Label(x.Target)}{(x.EndVerse > x.Target.Verse ? "–" + x.EndVerse : "")}\n{text}")).ToList();
        }
        catch (Exception ex) { XrefTitle.Text = L("Querverweise nicht verfügbar: ") + ex.Message; }
    }

    /// <summary>Verses that point to <paramref name="r"/>, best voted first; null while the link data is not loaded.</summary>
    List<CrossRef>? IncomingRefs(VerseRef r)
    {
        if (_graph?.Links is not { } links) return null;
        var node = _graph.Index(r.Book, r.Chapter);
        if (node < 0) return [];
        var v = links.IndexOf(node, r.Verse);
        var result = new List<CrossRef>();
        foreach (var i in links.ByVotes)
        {
            if (v < links.TgtStart[i] || v > links.TgtEnd[i]) continue;
            var sn = links.NodeOfVerse(links.Src[i]);
            var id = _graph.At(sn);
            result.Add(new CrossRef(new VerseRef(id.Book, id.Chapter, links.Src[i] - links.ChapterStart(sn) + 1), links.Src[i] - links.ChapterStart(sn) + 1, links.Votes[i]));
            if (result.Count >= 40) break;
        }
        return result;
    }

    void InitXrefDirection()
    {
        void Mark() { XrefOutBtn.Classes.Set("active", _settings.XrefDirection == "Out"); XrefInBtn.Classes.Set("active", _settings.XrefDirection == "In"); XrefBothBtn.Classes.Set("active", _settings.XrefDirection == "Both"); }
        void Set(string d) { _settings.XrefDirection = d; SaveSettings(); Mark(); if (_selected is int v) _ = ShowXrefsAsync(new VerseRef(_curBook, _curChapter, v)); }
        XrefOutBtn.Click += (_, _) => Set("Out"); XrefInBtn.Click += (_, _) => Set("In"); XrefBothBtn.Click += (_, _) => Set("Both");
        Mark();
    }

    async Task<string> VerseTextAsync(string translationId, CrossRef x)
    {
        try
        {
            var ch = await _provider.GetChapterAsync(translationId, x.Target.Book, x.Target.Chapter);
            var text = string.Join(" ", ch.Verses.Where(v => v.Number >= x.Target.Verse && v.Number <= x.EndVerse).Select(v => v.Text.Trim()));
            return text.Length > 160 ? text[..160] + "…" : text;
        }
        catch { return ""; }
    }

    VerseRef? _noteRef;      // the verse the note editor shows
    string _noteLoaded = ""; // its text as it was loaded or saved

    /// <summary>
    /// A note that was typed but not saved is kept when another verse is clicked, the vault changes or the program closes
    /// (it used to be replaced by the next verse's note without a word). Emptying a note still needs the delete button.
    /// </summary>
    void SaveNoteDraft()
    {
        var text = NoteEditor.Text ?? "";
        if (_noteRef is not { } r || text == _noteLoaded || string.IsNullOrWhiteSpace(text)) return;
        _vault.SetNote(r, text); if (SelectedNoteFolder() is long f1) _vault.SetNoteFolder(r, f1);
        _noteLoaded = text;
        StatusText.Text = L("Notiz zu {0} gespeichert.", Label(r));
        RefreshLists();
    }

    async Task SaveNoteAsync(string text)
    {
        // the verse the editor shows (its name stands above it), even when the click went into the empty space beside the text since
        var r = _noteRef ?? (_selected is int v ? new VerseRef(_curBook, _curChapter, v) : null);
        if (r is not { } at) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
        _vault.SetNote(at, text); if (!string.IsNullOrWhiteSpace(text) && SelectedNoteFolder() is long f2) _vault.SetNoteFolder(at, f2);
        _noteLoaded = text;
        RefreshLists();
        RefreshCells();
        await Task.CompletedTask;
    }

    void RefreshLists()
    {
        RefreshCollection();
        RefreshNotesTree();
    }

    async void OnEntryPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: Entry entry } list) return;
        list.SelectedItem = null;
        if (list == XrefList) { FollowXref(entry.Ref, _xrefs.FirstOrDefault(x => x.Target == entry.Ref)?.EndVerse ?? entry.Ref.Verse); return; }
        await GoToAsync(entry.Ref.Book, entry.Ref.Chapter, entry.Ref.Verse);
    }
}
