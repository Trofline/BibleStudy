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
        InitializeComponent();
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BibleStudy");
        var http = _http = new HttpClient();
        _hello = new HelloAoProvider(http, Path.Combine(_root, "cache"));
        _local = new LocalBibleStore(Path.Combine(_root, "local.db"));
        _provider = new CompositeProvider(_hello, _local);
        _strongs = new StrongsDictionary(http, Path.Combine(_root, "cache"));
        _store = new UserStore(Path.Combine(_root, "user.db"));
        _settingsPath = Path.Combine(_root, "settings.json");
        _settings = AppSettings.Load(_settingsPath);
        _vault = OpenVault(_settings.Vault);
        _read = _vault.GetReadChapters();
        _fontSize = _settings.FontSize;
        _startupHome = _settings.StartOnHome;
        FontSlider.Value = _fontSize;
        FontLabel.Text = $"Schriftgröße: {_fontSize}";
        StartHomeCheck.IsChecked = _settings.StartOnHome;
        AutoReadCheck.IsChecked = _settings.AutoMarkRead;
        ThemeBox.SelectedIndex = _settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        ApplyTheme(ThemeBox.SelectedIndex);

        ThemeBox.SelectionChanged += (_, _) => { ApplyTheme(ThemeBox.SelectedIndex); _settings.Theme = new[] { "Default", "Light", "Dark" }[Math.Max(0, ThemeBox.SelectedIndex)]; SaveSettings(); };
        ThemeBtn.Click += (_, _) => ThemeBox.SelectedIndex = (ThemeBox.SelectedIndex + 1) % 3;
        FontSlider.ValueChanged += async (_, e) =>
        {
            _fontSize = (int)e.NewValue; _settings.FontSize = _fontSize; FontLabel.Text = $"Schriftgröße: {_fontSize}";
            await ShowChapterAsync(_selected);
        };
        FontMinusBtn.Click += (_, _) => FontSlider.Value = Math.Max(FontSlider.Minimum, FontSlider.Value - 1);
        FontPlusBtn.Click += (_, _) => FontSlider.Value = Math.Min(FontSlider.Maximum, FontSlider.Value + 1);
        StartHomeCheck.IsCheckedChanged += (_, _) => { _settings.StartOnHome = StartHomeCheck.IsChecked == true; SaveSettings(); };
        AutoReadCheck.IsCheckedChanged += (_, _) => { _settings.AutoMarkRead = AutoReadCheck.IsChecked == true; SaveSettings(); };
        OfflineBtn.Click += async (_, _) => await OfflineAsync();
        SideTabs.SelectionChanged += (_, _) => { if (SideTabs.SelectedIndex == SettingsTab) UpdateOfflineInfo(); };
        Closing += (_, _) => SaveSettings();

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
        SearchBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await SearchAsync(); };
        foreach (var list in new[] { SearchList, NoteList, XrefList, ConcList }) list.SelectionChanged += OnEntryPicked;
        InitVault();
        InitAi();
        InitGraph();
        InitPanel();
        InitStudy();
        InitStrongSearch();
        InitImport();
        StartControl();
        Opened += async (_, _) => await InitAsync();
    }

    string BookName(string id) => _books.FirstOrDefault(b => b.Id == id)?.CommonName ?? id;
    string Label(VerseRef r) => $"{BookName(r.Book)} {r.Chapter}:{r.Verse}";

    async Task InitAsync()
    {
        try
        {
            StatusText.Text = "Lade Übersetzungen …";
            _all = await _provider.GetTranslationsAsync();
            _langs = _all.Select(t => t.LanguageName).Where(l => l != "").Distinct().Order().ToList();
            LanguageBox.ItemsSource = _langs;
            var wanted = _all.FirstOrDefault(t => t.Id == (_settings.TranslationId ?? "deu_elbbk")) ?? _all.FirstOrDefault(t => t.Id == "eng_kjv");
            _pendingTranslation = wanted?.Id;
            LanguageBox.SelectedItem = wanted?.LanguageName ?? _langs.FirstOrDefault();
        }
        catch (Exception ex) { StatusText.Text = "Fehler: " + ex.Message; }
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
        Home.Show(_books, _read, Home.OpenBook);
        Home.IsVisible = true; ReaderHost.IsVisible = false; GraphPane.IsVisible = false; MapPane.IsVisible = false;
    }

    void ShowReader() { Home.IsVisible = false; GraphPane.IsVisible = false; MapPane.IsVisible = false; ReaderHost.IsVisible = true; OnReaderShown(); }

    async Task GoToAsync(string bookId, int chapter, int? verse = null)
    {
        var book = _books.FirstOrDefault(b => b.Id == bookId);
        if (book is null) { StatusText.Text = "Buch in dieser Übersetzung nicht vorhanden."; return; }
        _loading = true;
        BookBox.SelectedItem = book;
        ChapterBox.Value = chapter;
        _loading = false;
        ShowReader();
        await ShowChapterAsync(verse);
    }

    async Task GoBackAsync()
    {
        if (Home.IsVisible || GraphPane.IsVisible || MapPane.IsVisible) { ShowReader(); return; }
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
        OfflineInfo.Text = $"{t.Name}: {count} von {total} Kapiteln offline verfügbar. Lizenz: {(t.License.PublicDomain ? "gemeinfrei" : t.LicenseUrl)}.";
    }

    async Task OfflineAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t || _building) return;
        _building = true; OfflineBtn.IsEnabled = false;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => OfflineInfo.Text = $"{t.ShortName}: {p.Done}/{p.Total} Kapitel geladen …");
            await OfflineDownloader.DownloadAsync(_provider, t.Id, progress);
            UpdateOfflineInfo();
        }
        catch (Exception ex) { OfflineInfo.Text = "Fehler: " + ex.Message; }
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
        catch (Exception ex) { StatusText.Text = "Fehler: " + ex.Message; }
        finally { _loading = false; }
        if (Home.IsVisible) Home.Show(_books, _read, Home.OpenBook);
        if (SideTabs.SelectedIndex == SettingsTab) UpdateOfflineInfo();
        RefreshLists();
        await ShowChapterAsync();
    }

    async Task ShowChapterAsync(int? focusVerse = null)
    {
        if (_loading || TranslationBox.SelectedItem is not TranslationInfo t || BookBox.SelectedItem is not BookInfo b) return;
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
            _curChapterData = ch;
            LoadChapterMarks();
            var noted = _vault.GetNotes().Where(n => n.Ref.Book == _curBook && n.Ref.Chapter == _curChapter).Select(n => n.Ref.Verse).ToHashSet();
            var size = cols.Count > 2 ? _fontSize - 3 : _fontSize;

            ReaderPanel.Children.Clear(); _verseBoxes.Clear();
            _singleColumn = cols.Count == 1;
            FitReader();
            var kicker = new TextBlock { Text = b.CommonName.ToUpperInvariant(), FontSize = 13, FontWeight = FontWeight.SemiBold, LetterSpacing = 3, Margin = new(8, 0, 0, 0) };
            kicker.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
            var numeral = new TextBlock { Text = ch.Number.ToString(), FontSize = 64, FontFamily = UiSerif, FontWeight = FontWeight.SemiBold, Margin = new(8, 0, 0, 14) };
            numeral.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
            ReaderPanel.Children.Add(kicker);
            ReaderPanel.Children.Add(numeral);
            if (cols.Count > 1)
            {
                var head = NewRow(cols.Count);
                for (var i = 0; i < cols.Count; i++)
                    AddCell(head, i, new TextBlock { Text = cols[i].T.ShortName + (cols[i].C is null ? " (Kapitel nicht verfügbar)" : ""), FontWeight = FontWeight.Bold, Opacity = 0.7, Margin = new(8, 0, 12, 4) });
                ReaderPanel.Children.Add(head);
            }
            var notes = cols.Select(c => c.C?.Footnotes?.OrderBy(f => f.NoteId).Select((f, i) => (f.NoteId, Letter: NoteLetter(i), f.Text))
                .ToDictionary(x => x.NoteId, x => (x.Letter, x.Text))).ToList();
            await LoadChapterRefsAsync(b.Id, number);
            foreach (var num in cols.SelectMany(c => c.C?.Verses.Select(v => v.Number) ?? []).Distinct().Order())
            {
                var row = NewRow(cols.Count);
                for (var i = 0; i < cols.Count; i++)
                    AddCell(row, i, VerseCell(cols[i].C?.Verses.FirstOrDefault(v => v.Number == num), num, cols[i].T, size, i == 0 && noted.Contains(num), notes[i],
                        _chapterMarks.Where(m => m.Ref.Verse == num && !m.WholeVerse && m.TranslationId == cols[i].T.Id).ToList()));
                var box = new Border { Child = XrefChips(num, t.Id) is { } chips ? new StackPanel { Children = { row, chips } } : row, Padding = new(10, 3), CornerRadius = new(8), BorderThickness = new(3, 0, 0, 0), BorderBrush = Brushes.Transparent, Classes = { "verse" } };
                var n = num;
                box.AddHandler(PointerPressedEvent, (object? _, PointerPressedEventArgs e) => OnVersePressed(n, e), RoutingStrategies.Tunnel, handledEventsToo: true);
                box.AddHandler(PointerReleasedEvent, (object? _, PointerReleasedEventArgs e) => CaptureSelection(e), RoutingStrategies.Tunnel, handledEventsToo: true);
                _verseBoxes[n] = box;
                ReaderPanel.Children.Add(box);
            }
            Paint();
            ShowThread(_threadOn && cols.Count == 1);
            if (focusVerse is int fv && _verseBoxes.ContainsKey(fv)) { SelectVerse(fv); _verseBoxes[fv].BringIntoView(); }
            else ReaderScroll.Offset = default;

            ReadBtn.IsChecked = _read.Contains((b.Id, number));
            if (_history.Count == 0 || _history[^1] != (b.Id, number)) { _history.Add((b.Id, number)); if (_history.Count > 100) _history.RemoveAt(0); }
            _settings.TranslationId = t.Id; _settings.BookId = b.Id; _settings.Chapter = number;
            if (_startupHome) { _startupHome = false; ShowHome(); }

            StatusText.Text = string.Join("  |  ", cols.Select(c =>
                $"{c.T.Name}: {(c.T.License.PublicDomain ? "gemeinfrei" : "Lizenz " + c.T.LicenseUrl)}, Namensnennung: {c.T.License.Attribution}")) + $" · Quelle: {_provider.Name}";
        }
        catch (Exception ex) { StatusText.Text = "Kapitel nicht verfügbar: " + ex.Message; }
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
    static readonly FontFamily ScriptureFont = UiSerif;

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
            TextWrapping = TextWrapping.Wrap, FontSize = size, LineHeight = size * 1.7, Margin = new(0, 0, 12, 0),
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
        var num = new Run(number + (hasNote ? "✎" : "") + " ") { FontSize = 14, FontWeight = FontWeight.Bold, BaselineAlignment = BaselineAlignment.Top };
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
                if (s.WordsOfJesus) run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsJesus"));
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
        if (e.Source is not SelectableTextBlock { Tag: CellInfo cell } tb) return;
        var hit = tb.TextLayout.HitTestPoint(e.GetPosition(tb)).TextPosition;
        var span = cell.Spans.FirstOrDefault(s => hit >= s.Start && hit < s.End);
        if (span is null) return;
        if (span.Note is not null) ShowFootnote(tb, span);
        else if (StudyBtn.IsChecked == true) _ = ShowStudyAsync(span);
    }

    void ShowFootnote(Control anchor, WordSpan span)
    {
        var title = new TextBlock { Text = $"Fußnote {span.Word}", FontWeight = FontWeight.SemiBold };
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
                StatusText.Text = "Lade Wörterbuch …";
                var e = await _strongs.LookupAsync(id);
                StudyPanel.Children.Add(e is null ? new TextBlock { Text = id + ": kein Eintrag" } : StudyCard(e));
                var all = new Button { Content = "Alle Vorkommen", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
                var conc = $"{id}  {e?.Lemma}  „{span.Word.Trim()}“";
                all.Click += async (_, _) => await ShowConcordanceAsync(id, conc);
                StudyPanel.Children.Add(all);
            }
            catch (Exception ex) { StudyPanel.Children.Add(new TextBlock { Text = $"{id}: Wörterbuch nicht verfügbar ({ex.Message})", TextWrapping = TextWrapping.Wrap }); }
        }
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
        if (e.Derivation.Length > 0) panel.Children.Add(Line("Herkunft:", e.Derivation));
        if (e.Definition.Length > 0) panel.Children.Add(Line("Bedeutung:", e.Definition));
        if (e.KjvUsage.Length > 0) panel.Children.Add(Line("KJV:", e.KjvUsage));
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
        ToolTip.SetTip(remove, "Spalte entfernen");
        var root = new Border
        {
            Classes = { "pill" }, Margin = new(16, 8, 0, 8),
            Child = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { lang, trans, remove } },
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
            _extras.Remove(col); ParallelBar.Children.Remove(root);
            ParallelBar.IsVisible = _extras.Count > 0;
            await ShowChapterAsync(_selected);
        };
        _extras.Add(col); ParallelBar.Children.Add(root);
        ParallelBar.IsVisible = true;
        lang.SelectedItem = preset?.LanguageName ?? LanguageBox.SelectedItem;
    }

    /// <summary>Selected verse: soft accent fill; bookmarked verse: accent bar on the left. Otherwise the hover style applies.</summary>
    void Paint()
    {
        var soft = Res("BsAccentSoft");
        foreach (var (n, box) in _verseBoxes)
        {
            var whole = _wholeMarks.TryGetValue(n, out var hex) ? Color.Parse(hex) : (Color?)null;
            if (n == _selected) box.Background = soft;
            else if (whole is { } c) box.Background = new SolidColorBrush(c, 0.14);
            else box.ClearValue(Border.BackgroundProperty);
            box.BorderBrush = whole is { } c2 ? new SolidColorBrush(c2) : Brushes.Transparent;
        }
    }

    void FitReader()
    {
        ReaderPanel.HorizontalAlignment = _singleColumn ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Stretch;
        ReaderPanel.Margin = _singleColumn ? new(0, 36, 0, 80) : new(32, 28, 32, 80);
        var reserve = 0.0;
        ReaderPanel.Width = _singleColumn ? Math.Clamp(ReaderScroll.Bounds.Width - 64 - reserve, 320, 760) : double.NaN;
    }

    void SelectVerse(int n)
    {
        _selected = n; Paint();
        var r = new VerseRef(_curBook, _curChapter, n);
        NoteTarget.Text = "Notiz zu " + Label(r);
        NoteEditor.Text = _vault.GetNotes().FirstOrDefault(x => x.Ref == r)?.Text ?? "";
        _ = ShowXrefsAsync(r);
        if (SideTabs.SelectedIndex == GrundtextTab) _ = ShowGrundtextAsync(r);
    }

    async Task ShowXrefsAsync(VerseRef r)
    {
        try
        {
            XrefTitle.Text = "Querverweise zu " + Label(r) + " …";
            var refs = (await _hello.GetCrossReferencesAsync(r.Book, r.Chapter, r.Verse)).Take(20).ToList();
            if (TranslationBox.SelectedItem is not TranslationInfo t) return;
            var texts = await Task.WhenAll(refs.Select(x => VerseTextAsync(t.Id, x)));
            if (_selected != r.Verse || _curBook != r.Book || _curChapter != r.Chapter) return; // user moved on
            _xrefs = refs;
            XrefTitle.Text = refs.Count == 0 ? $"Keine Querverweise zu {Label(r)}" : $"Querverweise zu {Label(r)}";
            XrefList.ItemsSource = refs.Zip(texts, (x, text) =>
                new Entry(x.Target, $"{Label(x.Target)}{(x.EndVerse > x.Target.Verse ? "–" + x.EndVerse : "")}\n{text}")).ToList();
        }
        catch (Exception ex) { XrefTitle.Text = "Querverweise nicht verfügbar: " + ex.Message; }
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

    async Task ShowConcordanceAsync(string strongId, string title)
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t || _building) return;
        SideTabs.SelectedIndex = 5;
        ConcTitle.Text = title;
        ConcList.ItemsSource = null;
        try
        {
            if (!_store.IsStrongsIndexed(t.Id))
            {
                _building = true;
                var progress = new Progress<(int Done, int Total)>(p => ConcInfo.Text = $"Einmalig: Konkordanz für {t.ShortName} wird aufgebaut … {p.Done}/{p.Total} Kapitel");
                await ConcordanceBuilder.BuildAsync(_provider, _store, t.Id, progress);
            }
            var occ = _store.GetOccurrences(t.Id, strongId, ConcordanceLimit);
            var renderings = occ.GroupBy(o => o.Word.Trim('.', ',', ';', ':', '!', '?', '(', ')').ToLowerInvariant())
                .OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key} ({g.Count()})");
            ConcInfo.Text = occ.Count == 0 ? $"Keine Vorkommen in {t.ShortName}."
                : $"{occ.Count}{(occ.Count >= ConcordanceLimit ? "+" : "")}× in {t.ShortName}. Wiedergaben: {string.Join(", ", renderings)}";
            ConcList.ItemsSource = occ.Select(o => new Entry(o.Ref, $"{Label(o.Ref)} · {o.Word.Trim()}")).ToList();
        }
        catch (Exception ex) { ConcInfo.Text = "Konkordanz nicht verfügbar: " + ex.Message; }
        finally { _building = false; }
    }

    async Task SaveNoteAsync(string text)
    {
        if (_selected is not int v) { StatusText.Text = "Erst einen Vers anklicken."; return; }
        _vault.SetNote(new VerseRef(_curBook, _curChapter, v), text);
        RefreshLists();
        await ShowChapterAsync(v);
    }

    void RefreshLists()
    {
        RefreshCollection();
        NoteList.ItemsSource = _vault.GetNotes().Select(n =>
        {
            var t = n.Text.ReplaceLineEndings(" ");
            return new Entry(n.Ref, $"{Label(n.Ref)}\n{(t.Length > 100 ? t[..100] + "…" : t)}");
        }).ToList();
    }

    async Task SearchAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t || string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        try
        {
            if (!_store.IsIndexed(t.Id))
            {
                SearchInfo.Text = $"Einmalig: {t.ShortName} wird für die Suche geladen und indexiert …";
                var verses = await _provider.GetAllVersesAsync(t.Id);
                await Task.Run(() => _store.Index(t.Id, verses));
            }
            IReadOnlyCollection<string>? scope = SearchScopeBox.SelectedIndex switch
            {
                1 => _books.Where(b => BookCategories.IsOldTestament(BookCategories.Of(b.Id))).Select(b => b.Id).ToList(),
                2 => _books.Where(b => BookCategories.IsNewTestament(BookCategories.Of(b.Id))).Select(b => b.Id).ToList(),
                3 => [_curBook],
                _ => null,
            };
            var hits = _store.Search(t.Id, SearchBox.Text, 200, scope);
            SearchList.ItemsSource = hits.Select(h => new Entry(h.Ref, $"{Label(h.Ref)}\n{h.Snippet}")).ToList();
            SearchInfo.Text = hits.Count == 0 ? "Keine Treffer." : $"{hits.Count}{(hits.Count >= 200 ? "+" : "")} Treffer in {t.ShortName}";
        }
        catch (Exception ex) { SearchInfo.Text = "Fehler: " + ex.Message; }
    }

    async void OnEntryPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: Entry entry } list) return;
        list.SelectedItem = null;
        await GoToAsync(entry.Ref.Book, entry.Ref.Chapter, entry.Ref.Verse);
    }
}
