using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>List entry: first line of Display is the title, the rest (if any) the body.</summary>
public sealed record Entry(VerseRef Ref, string Display, long? Id = null, string? Color = null)
{
    public bool HasColor => Color is not null;
    public IBrush? ColorBrush => Color is null ? null : new SolidColorBrush(Avalonia.Media.Color.Parse(Color));
    public string Title => Display.Split('\n', 2)[0];
    public string Body => Display.Contains('\n') ? Display[(Display.IndexOf('\n') + 1)..] : "";
    public bool HasBody => Body.Length > 0;
}

/// <summary>An additional translation shown next to the main one.</summary>
public sealed record ExtraColumn(ComboBox Lang, ComboBox Trans, Control Root)
{
    public TranslationInfo? Translation => Trans.SelectedItem as TranslationInfo;
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
    readonly SyntaxProvider _syntax;
    readonly MeaningTranslator _meanings;
    int _fontSize = 22;
    bool _singleColumn = true;
    readonly string _root;
    readonly string _settingsPath;
    readonly AppSettings _settings;
    HashSet<(string Book, int Chapter)> _read = [];
    readonly List<(string Book, int Chapter)> _history = [];
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
        _provider = new CompositeProvider(_hello, _local);
        _strongs = new StrongsDictionary(http, Path.Combine(_root, "cache"));
        _syntax = new SyntaxProvider(http, Path.Combine(_root, "cache"));
        _meanings = new MeaningTranslator(Path.Combine(_root, "cache"));
        _store = new UserStore(Path.Combine(_root, "user.db"));
        _settingsPath = Path.Combine(_root, "settings.json");
        _settings = AppSettings.Load(_settingsPath);
        CommentariesOn = _settings.ShowCommentaries; NavGroups = MakeNavGroups();
        _vault = OpenVault(_settings.Vault);
        _read = _vault.GetReadChapters();
        _fontSize = _settings.FontSize;
        _startupHome = _settings.StartOnHome;
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
            await ShowChapterAsync(_selected);
        };
        FontMinusBtn.Click += (_, _) => FontSlider.Value = Math.Max(FontSlider.Minimum, FontSlider.Value - 1);
        FontPlusBtn.Click += (_, _) => FontSlider.Value = Math.Min(FontSlider.Maximum, FontSlider.Value + 1);
        StartHomeCheck.IsCheckedChanged += (_, _) => { _settings.StartOnHome = StartHomeCheck.IsChecked == true; SaveSettings(); };
        AutoReadCheck.IsCheckedChanged += (_, _) => { _settings.AutoMarkRead = AutoReadCheck.IsChecked == true; SaveSettings(); };
        OfflineBtn.Click += async (_, _) => await OfflineAsync();
        SideTabs.SelectionChanged += (_, _) => { if (SideTabs.SelectedIndex == SettingsTab) UpdateOfflineInfo(); };

        HomeBtn.Click += (_, _) => ShowHome();
        BackBtn.Click += async (_, _) => await GoBackAsync();
        ReadBtn.Click += (_, _) => SetRead(_curBook, [_curChapter], ReadBtn.IsChecked == true);
        Home.ChapterChosen += async (book, chapter) => await GoToAsync(book, chapter);
        Home.ReadChanged += SetRead;

        ActualThemeVariantChanged += async (_, _) => await ShowChapterAsync(_selected); // highlight opacity depends on the theme
        ReaderScroll.SizeChanged += (_, _) => FitReader();
        StudyBtn.IsCheckedChanged += async (_, _) => await OnStudyToggledAsync();

        LanguageBox.SelectionChanged += (_, _) => FillTranslations();
        TranslationBox.SelectionChanged += async (_, _) => await LoadBooksAsync();
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
        PrevBtn.Click += (_, _) => ChapterBox.Value -= 1;
        NextBtn.Click += (_, _) =>
        {
            if (_settings.AutoMarkRead && _curBook != "") SetRead(_curBook, [_curChapter], true);
            ChapterBox.Value += 1;
        };
        BookmarkBtn.Click += (_, _) => ToggleBookmark();
        ParallelBtn.Click += (_, _) => AddColumn();
        NoteSaveBtn.Click += async (_, _) => await SaveNoteAsync(NoteEditor.Text ?? "");
        NoteDeleteBtn.Click += async (_, _) => { NoteEditor.Text = ""; await SaveNoteAsync(""); };
        InitSearch();
        foreach (var list in new[] { SearchList, NoteList, XrefList, ConcList }) list.SelectionChanged += OnEntryPicked;
        InitVault();
        InitAi();
        InitGraph();
        InitPanel();
        InitStudy();
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

    async Task InitAsync()
    {
        try
        {
            SplashWindow.Report(0.6, L("Lade Übersetzungen …"));
            StatusText.Text = L("Lade Übersetzungen …");
            _all = await _provider.GetTranslationsAsync();
            _langs = _all.Select(t => t.LanguageName).Where(l => l != "").Distinct().Order().ToList();
            LanguageBox.ItemsSource = _langs;
            var wanted = _all.FirstOrDefault(t => t.Id == (_settings.TranslationId ?? "deu_elbbk")) ?? _all.FirstOrDefault(t => t.Id == "eng_kjv");
            _pendingTranslation = wanted?.Id;
            SplashWindow.Report(0.75, L("Lade Bücher …"));
            LanguageBox.SelectedItem = wanted?.LanguageName ?? _langs.FirstOrDefault();
        }
        catch (Exception ex) { StatusText.Text = L("Fehler: ") + ex.Message; }
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
        try { _settings.Save(_settingsPath); } catch (IOException) { /* settings are a convenience; never crash on save */ }
    }

    void ShowHome()
    {
        Home.Resume = _curBook == "" ? null : (_curBook, _curChapter, $"{BookName(_curBook)} {_curChapter}");
        var recent = new List<(string, string, string, Action)>();
        var votd = VerseOfTheDay();
        recent.Add((L("Vers des Tages"), Label(votd), L("Klicken zum Lesen"), () => _ = GoToAsync(votd.Book, votd.Chapter, votd.Verse)));
        var dueCards = Memory.Ids.Count(i => Memory.IsDue(i, DateTime.Now));
        if (dueCards > 0) recent.Add((L("Auswendig lernen"), L("{0} Verse fällig", dueCards), L("Klicken zum Üben"), () => OpenWork(NavGroups.First(g => g.Name == "Planen"), MemoryTab)));
        foreach (var d in _vault.GetDocuments().OrderByDescending(d => d.Updated).Take(4))
        {
            var doc = d;
            recent.Add((L("Dokument"), d.Title, Shorten(string.Join(" ", d.Body.Split((char)10).Where(l => !l.TrimStart().StartsWith("#") && !l.StartsWith("**Quelle")).Take(6)).Trim(), 80), () => { SetPanelOpen(true); SideTabs.SelectedIndex = KnowledgeTab; OpenDocument(doc); }));
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

    async Task GoBackAsync()
    {
        if (Home.IsVisible || GraphPane.IsVisible || FamilyPane.IsVisible || TimelinePane.IsVisible || MapPane.IsVisible || KnowledgePane.IsVisible) { ShowReader(); return; }
        if (_history.Count < 2) return;
        _history.RemoveAt(_history.Count - 1);
        var (book, chapter) = _history[^1];
        _history.RemoveAt(_history.Count - 1); // GoToAsync records it again
        await GoToAsync(book, chapter);
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
        OfflineInfo.Text = L("{0}: {1} von {2} Kapiteln offline verfügbar. Lizenz: {3}.", t.Name, count, total, (t.License.PublicDomain ? "gemeinfrei" : t.LicenseUrl));
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

    async Task LoadBooksAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return;
        try
        {
            _loading = true;
            var prev = (BookBox.SelectedItem as BookInfo)?.Id ?? _settings.BookId;
            _books = await _provider.GetBooksAsync(t.Id);
            BookBox.ItemsSource = _books;
            BookBox.SelectedItem = _books.FirstOrDefault(b => b.Id == prev) ?? _books.FirstOrDefault(b => b.Id == "JHN") ?? _books.FirstOrDefault();
            if (_firstBooks) { ChapterBox.Value = Math.Max(1, _settings.Chapter); _firstBooks = false; }
        }
        catch (Exception ex) { StatusText.Text = L("Fehler: ") + ex.Message; }
        finally { _loading = false; }
        if (Home.IsVisible) Home.Show(_books, _read, Home.OpenBook);
        if (SideTabs.SelectedIndex == SettingsTab) UpdateOfflineInfo();
        RefreshLists();
        await ShowChapterAsync();
    }

    async Task ShowChapterAsync(int? focusVerse = null)
    {
        if (_loading || TranslationBox.SelectedItem is not TranslationInfo t || BookBox.SelectedItem is not BookInfo b) return;
        SplashWindow.Report(0.88, L("Öffne Kapitel …"));
        try
        {
            var number = (int)(ChapterBox.Value ?? 1);
            var extraTs = _extras.Select(e => e.Translation).OfType<TranslationInfo>().ToList();
            var primaryTask = _provider.GetChapterAsync(t.Id, b.Id, number);
            var extraTasks = extraTs.Select(c => TryAsync(_provider.GetChapterAsync(c.Id, b.Id, number))).ToList();
            var ch = await primaryTask;
            var cols = new List<(TranslationInfo T, Chapter? C)> { (t, ch) };
            cols.AddRange(extraTs.Zip(await Task.WhenAll(extraTasks), (c, x) => (c, x)));

            _curBook = b.Id; _curChapter = ch.Number; _selected = null;
            CollabSendLocation(focusVerse);
            _curChapterData = ch;
            LoadChapterMarks();
            var noted = _vault.GetNotes().Where(n => n.Ref.Book == _curBook && n.Ref.Chapter == _curChapter).Select(n => n.Ref.Verse).ToHashSet();
            var size = cols.Count > 2 ? _fontSize - 3 : _fontSize;

            ReaderPanel.Children.Clear(); _verseBoxes.Clear();
            if (_animKey != $"{_curBook}{_curChapter}") { _animKey = $"{_curBook}{_curChapter}"; AnimateIn(ReaderPanel); }
            _singleColumn = cols.Count == 1;
            _independent = cols.Count > 1 && !_settings.ParallelSync;
            _colBoxes.Clear(); _colScrolls.Clear(); _indepGrid = null;
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
            if (_independent) BuildIndependent(cols, size, noted, notes!, t);
            else foreach (var num in cols.SelectMany(c => c.C?.Verses.Select(v => v.Number) ?? []).Distinct().Order())
            {
                var row = NewRow(cols.Count);
                for (var i = 0; i < cols.Count; i++)
                    AddCell(row, i, ColumnCell(VerseCell(cols[i].C?.Verses.FirstOrDefault(v => v.Number == num), num, cols[i].T, size, i == 0 && noted.Contains(num), notes[i],
                        _chapterMarks.Where(m => m.Ref.Verse == num && !m.WholeVerse && m.TranslationId == cols[i].T.Id).ToList()), i, cols.Count));
                var box = MakeVerseBox(XrefChips(num, t.Id) is { } chips ? new StackPanel { Children = { row, chips } } : row, num);
                _verseBoxes[num] = box;
                ReaderPanel.Children.Add(box);
            }
            Paint();
            ShowThread(_threadOn && cols.Count == 1);
            if (focusVerse is int fv && _verseBoxes.ContainsKey(fv)) { SelectVerse(fv); ScrollToVerse(fv); }
            else ReaderScroll.Offset = default;

            ReadBtn.IsChecked = _read.Contains((b.Id, number));
            if (_history.Count == 0 || _history[^1] != (b.Id, number)) { _history.Add((b.Id, number)); if (_history.Count > 100) _history.RemoveAt(0); }
            _settings.TranslationId = t.Id; _settings.BookId = b.Id; _settings.Chapter = number;
            UpdateTitle(b.CommonName, number, t.ShortName);
            LibraryFollowReader();
            if (_startupHome) { _startupHome = false; ShowHome(); }

            StatusText.Text = string.Join("  |  ", cols.Select(c =>
                L("{0}: {1}, Namensnennung: {2}", c.T.Name, (c.T.License.PublicDomain ? "gemeinfrei" : L("Lizenz ") + c.T.LicenseUrl), c.T.License.Attribution))) + L(" · Quelle: {0}", _provider.Name);
        }
        catch (Exception ex) { StatusText.Text = L("Kapitel nicht verfügbar: ") + ex.Message; }
        finally { SplashWindow.Done(); }
    }

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
    FontFamily ScriptureFont => _settings.ReaderFont switch
    {
        "Crimson Text" => UiSerif,
        var name => new FontFamily(name + ", Cambria, Georgia, serif"),
    };

    /// <summary>Clickable word range inside a verse cell (for the word study).</summary>
    sealed record WordSpan(int Start, int End, string[] Strongs, string Word, string? Note = null);

    static string NoteLetter(int i) => i < 26 ? ((char)('a' + i)).ToString() : NoteLetter(i / 26 - 1) + NoteLetter(i % 26);

    /// <summary>Maps a stretch of the cell's text (Tb) to its offset in the plain verse text (Raw).</summary>
    sealed record RunMap(int Tb, int Raw, int Len);

    /// <summary>Everything the pointer handlers need to know about one verse cell.</summary>
    sealed record CellInfo(List<WordSpan> Spans, List<RunMap> Runs, int Verse, string TranslationId, string RawText);

    SelectableTextBlock VerseCell(Verse? v, int number, TranslationInfo t, int size, bool hasNote, IReadOnlyDictionary<int, (string Letter, string Text)>? notes, IReadOnlyList<Mark> marks)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = size, LineHeight = size * _settings.LineSpacing, Margin = new(0, 0, 12, 0),
            TextAlignment = _settings.Justify ? TextAlignment.Justify : TextAlignment.Left,
            FontFamily = ScriptureFont,
            FlowDirection = t.TextDirection == "rtl" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
        if (v is null) return tb;
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
        var num = new Run(_settings.VerseNumbers == "Hidden" ? (hasNote ? "✎ " : "") : number + (hasNote ? "✎" : "") + " ") { FontSize = numSize, FontWeight = FontWeight.Bold, BaselineAlignment = _settings.VerseNumbers == "Normal" ? BaselineAlignment.Baseline : BaselineAlignment.Top };
        num.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsVerseNumber"));
        Add(num);
        foreach (var s in v.Segments)
        {
            if (s.NoteId is int nid)
            {
                if (notes is null || !notes.TryGetValue(nid, out var note)) { Add(new Run(" ")); continue; }
                var marker = new Run(note.Letter + " ") { FontSize = size * 0.62, FontWeight = FontWeight.Bold, BaselineAlignment = BaselineAlignment.Superscript };
                marker.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsAccent"));
                spans.Add(new WordSpan(pos, pos + marker.Text!.Length, [], note.Letter, note.Text));
                Add(marker);
                continue;
            }
            // Cut the segment where highlights start or end, so each piece has one background color.
            var cuts = new SortedSet<int> { 0, s.Text.Length };
            foreach (var m in marks) { cuts.Add(Math.Clamp(m.Start - raw, 0, s.Text.Length)); cuts.Add(Math.Clamp(m.End - raw, 0, s.Text.Length)); }
            var points = cuts.ToList();
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var piece = s.Text[points[i]..points[i + 1]];
                var from = raw + points[i];
                var run = new Run(piece);
                if (s.WordsOfJesus && _settings.JesusRed) run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsJesus"));
                if (marks.FirstOrDefault(m => m.Start <= from && m.End >= from + piece.Length) is { } mark) run.Background = HighlightBrush(mark.Color);
                if (s.Strongs is { Count: > 0 })
                {
                    spans.Add(new WordSpan(pos, pos + piece.Length, [.. s.Strongs], s.Text));
                    if (study) run.TextDecorations = TextDecorations.Underline;
                }
                runs.Add(new RunMap(pos, from, piece.Length));
                Add(run);
            }
            raw += s.Text.Length;
        }
        Add(new Run(" "));
        tb.Tag = new CellInfo(spans, runs, number, t.Id, string.Concat(v.Segments.Where(x => x.NoteId is null).Select(x => x.Text)));
        return tb;
    }

    /// <summary>Translucent so the text stays readable on both themes.</summary>
    IBrush HighlightBrush(string hex) => new SolidColorBrush(Color.Parse(hex), ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? 0.45 : 0.55);

    void OnVersePressed(int verse, PointerPressedEventArgs e)
    {
        SelectVerse(verse);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && _verseBoxes.TryGetValue(verse, out var anchor))
        {
            e.Handled = true; ShowVerseMenu(verse, anchor); return;
        }
        if (e.Source is not SelectableTextBlock { Tag: CellInfo cell } tb) return;
        var hit = tb.TextLayout.HitTestPoint(e.GetPosition(tb)).TextPosition;
        var span = cell.Spans.FirstOrDefault(s => hit >= s.Start && hit < s.End);
        if (span is null) return;
        if (span.Note is not null) ShowFootnote(tb, span);
        else if (StudyBtn.IsChecked == true) _ = ShowStudyAsync(span);
    }

    void ShowFootnote(Control anchor, WordSpan span)
    {
        var title = new TextBlock { Text = L("Fußnote {0}", span.Word), FontWeight = FontWeight.SemiBold };
        title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        var body = new TextBlock { Text = span.Note, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 };
        new Flyout { Content = new StackPanel { Spacing = 4, Children = { title, body } }, Placement = PlacementMode.Pointer }.ShowAt(anchor);
    }

    async Task ShowStudyAsync(WordSpan span)
    {
        SideTabs.SelectedIndex = 3;
        StudyPanel.Children.Clear();
        StudyPanel.Children.Add(new TextBlock { Text = $"„{span.Word.Trim()}“", FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        foreach (var id in span.Strongs)
        {
            try
            {
                StatusText.Text = L("Lade Wörterbuch …");
                var e = await _strongs.LookupAsync(id);
                StudyPanel.Children.Add(e is null ? new TextBlock { Text = id + L(": kein Eintrag") } : StudyCard(e));
                var all = new Button { Content = L("Alle Vorkommen"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
                var conc = $"{id}  {e?.Lemma}  „{span.Word.Trim()}“";
                all.Click += async (_, _) => await ShowConcordanceAsync(id, conc);
                StudyPanel.Children.Add(all);
            }
            catch (Exception ex) { StudyPanel.Children.Add(new TextBlock { Text = L("{0}: Wörterbuch nicht verfügbar ({1})", id, ex.Message), TextWrapping = TextWrapping.Wrap }); }
        }
        await AddWordExtrasAsync(span);
        var note = new TextBlock { Text = StrongsDictionary.Attribution, FontSize = 11, TextWrapping = TextWrapping.Wrap };
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
        var dot = new Border { Width = 12, Height = 12, CornerRadius = new(6), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new(10, 0, 4, 0) };
        var root = new Border
        {
            Classes = { "pill" }, Margin = new(16, 8, 0, 8),
            Child = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { dot, lang, trans, remove } },
        };
        var col = new ExtraColumn(lang, trans, root);

        lang.SelectionChanged += (_, _) =>
        {
            var list = _all.Where(x => x.LanguageName == (string?)lang.SelectedItem).OrderBy(x => x.Name).ToList();
            var main = (TranslationBox.SelectedItem as TranslationInfo)?.Id;
            trans.ItemsSource = list;
            trans.SelectedItem = list.FirstOrDefault(x => preset is not null && x.Id == preset.Id) ?? list.FirstOrDefault(x => x.Id != main) ?? list.FirstOrDefault();
        };
        trans.SelectionChanged += async (_, _) => await ShowChapterAsync(_selected);
        remove.Click += async (_, _) =>
        {
            _extras.Remove(col); ParallelBar.Children.Remove(root); RecolorPills();
            ParallelBar.IsVisible = _extras.Count > 0;
            await ShowChapterAsync(_selected);
        };
        EnsureSyncPill();
        if (!ParallelBar.Children.Contains(_syncPill!)) ParallelBar.Children.Insert(0, _syncPill!);
        _extras.Add(col); ParallelBar.Children.Add(root); RecolorPills();
        ParallelBar.IsVisible = true;
        lang.SelectedItem = preset?.LanguageName ?? LanguageBox.SelectedItem;
    }

    /// <summary>Selected verse: soft accent fill; bookmarked verse: accent bar on the left. Otherwise the hover style applies.</summary>
    void Paint()
    {
        var soft = Res("BsAccentSoft");
        foreach (var (n, box) in _verseBoxes.Concat(_colBoxes.SelectMany(d => d)))
        {
            var whole = _wholeMarks.TryGetValue(n, out var hex) ? Color.Parse(hex) : (Color?)null;
            if (n == _selected) box.Background = soft;
            else if (whole is { } c) box.Background = new SolidColorBrush(c, 0.14);
            else box.ClearValue(Border.BackgroundProperty);
            box.BorderBrush = n == _selected ? (Res("BsAccent") ?? Brushes.Gray) : whole is { } c2 ? new SolidColorBrush(c2) : Brushes.Transparent;
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

    void SelectVerse(int n)
    {
        _selected = n; Paint();
        var r = new VerseRef(_curBook, _curChapter, n);
        NoteTarget.Text = L("Notiz zu ") + Label(r);
        NoteEditor.Text = _vault.GetNotes().FirstOrDefault(x => x.Ref == r)?.Text ?? "";
        _ = ShowXrefsAsync(r);
        if (TabActive(GrundtextTab)) _ = ShowGrundtextAsync(r);
        if (TabActive(GuideTab)) _ = ShowGuideAsync(r);
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

    async Task SaveNoteAsync(string text)
    {
        if (_selected is not int v) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
        _vault.SetNote(new VerseRef(_curBook, _curChapter, v), text);
        RefreshLists();
        await ShowChapterAsync(v);
    }

    void RefreshLists()
    {
        RefreshCollection();
        var noteRows = _vault.GetNotes();
        NotesEmpty.IsVisible = noteRows.Count == 0;
        NoteList.ItemsSource = noteRows.Select(n =>
        {
            var t = n.Text.ReplaceLineEndings(" ");
            return new Entry(n.Ref, $"{Label(n.Ref)}\n{(t.Length > 100 ? t[..100] + "…" : t)}");
        }).ToList();
    }

    async void OnEntryPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: Entry entry } list) return;
        list.SelectedItem = null;
        await GoToAsync(entry.Ref.Book, entry.Ref.Chapter, entry.Ref.Verse);
    }
}
