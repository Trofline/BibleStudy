using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>List entry: first line of Display is the title, the rest (if any) the body.</summary>
public sealed record Entry(VerseRef Ref, string Display)
{
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
    bool _building;
    readonly StrongsDictionary _strongs;
    int _fontSize = 22;
    bool _singleColumn = true;
    readonly List<ExtraColumn> _extras = [];
    IReadOnlyList<string> _langs = [];

    IBrush? Res(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) ? v as IBrush : null;

    readonly IBibleProvider _provider;
    readonly UserStore _store;
    IReadOnlyList<TranslationInfo> _all = [];
    IReadOnlyList<BookInfo> _books = [];
    readonly Dictionary<int, Border> _verseBoxes = [];
    HashSet<int> _marked = [];
    string _curBook = "";
    int _curChapter;
    int? _selected;
    bool _loading;

    public MainWindow()
    {
        InitializeComponent();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BibleStudy");
        var http = new HttpClient();
        _provider = _hello = new HelloAoProvider(http, Path.Combine(root, "cache"));
        _strongs = new StrongsDictionary(http, Path.Combine(root, "cache"));
        ActualThemeVariantChanged += (_, _) => Paint();
        ReaderScroll.SizeChanged += (_, _) => FitReader();
        StudyBtn.IsCheckedChanged += async (_, _) => await ShowChapterAsync(_selected);
        FontMinusBtn.Click += async (_, _) => { _fontSize = Math.Max(13, _fontSize - 1); await ShowChapterAsync(_selected); };
        FontPlusBtn.Click += async (_, _) => { _fontSize = Math.Min(34, _fontSize + 1); await ShowChapterAsync(_selected); };
        ThemeBtn.Click += (_, _) =>
        {
            var app = Avalonia.Application.Current!;
            app.RequestedThemeVariant = app.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Light ? Avalonia.Styling.ThemeVariant.Dark
                : app.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? Avalonia.Styling.ThemeVariant.Default : Avalonia.Styling.ThemeVariant.Light;
            StatusText.Text = "Design: " + app.RequestedThemeVariant;
        };
        _store = new UserStore(Path.Combine(root, "user.db"));

        LanguageBox.SelectionChanged += (_, _) => FillTranslations();
        TranslationBox.SelectionChanged += async (_, _) => await LoadBooksAsync();
        BookBox.SelectionChanged += async (_, _) =>
        {
            if (BookBox.SelectedItem is not BookInfo b) return;
            ChapterBox.Maximum = b.NumberOfChapters;
            if (_loading) return;
            _loading = true; ChapterBox.Value = 1; _loading = false;
            await ShowChapterAsync();
        };
        ChapterBox.ValueChanged += async (_, _) => await ShowChapterAsync();
        PrevBtn.Click += (_, _) => ChapterBox.Value -= 1;
        NextBtn.Click += (_, _) => ChapterBox.Value += 1;
        BookmarkBtn.Click += (_, _) => ToggleBookmark();
        ParallelBtn.Click += (_, _) => AddColumn();
        NoteSaveBtn.Click += async (_, _) => await SaveNoteAsync(NoteEditor.Text ?? "");
        NoteDeleteBtn.Click += async (_, _) => { NoteEditor.Text = ""; await SaveNoteAsync(""); };
        SearchBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await SearchAsync(); };
        foreach (var list in new[] { SearchList, BookmarkList, NoteList, XrefList, ConcList }) list.SelectionChanged += OnEntryPicked;
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
            LanguageBox.SelectedItem = _langs.Contains("English") ? "English" : _langs.FirstOrDefault();
        }
        catch (Exception ex) { StatusText.Text = "Fehler: " + ex.Message; }
    }

    void FillTranslations()
    {
        var list = _all.Where(t => t.LanguageName == (string?)LanguageBox.SelectedItem).OrderBy(t => t.Name).ToList();
        TranslationBox.ItemsSource = list;
        TranslationBox.SelectedItem = list.FirstOrDefault(t => t.Id == "eng_kjv") ?? list.FirstOrDefault();
    }

    async Task LoadBooksAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return;
        try
        {
            _loading = true;
            var prev = (BookBox.SelectedItem as BookInfo)?.Id;
            _books = await _provider.GetBooksAsync(t.Id);
            BookBox.ItemsSource = _books;
            BookBox.SelectedItem = _books.FirstOrDefault(b => b.Id == prev) ?? _books.FirstOrDefault(b => b.Id == "JHN") ?? _books.FirstOrDefault();
        }
        catch (Exception ex) { StatusText.Text = "Fehler: " + ex.Message; }
        finally { _loading = false; }
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
            _marked = _store.GetBookmarks().Where(r => r.Book == _curBook && r.Chapter == _curChapter).Select(r => r.Verse).ToHashSet();
            var noted = _store.GetNotes().Where(n => n.Ref.Book == _curBook && n.Ref.Chapter == _curChapter).Select(n => n.Ref.Verse).ToHashSet();
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
            foreach (var num in cols.SelectMany(c => c.C?.Verses.Select(v => v.Number) ?? []).Distinct().Order())
            {
                var row = NewRow(cols.Count);
                for (var i = 0; i < cols.Count; i++)
                    AddCell(row, i, VerseCell(cols[i].C?.Verses.FirstOrDefault(v => v.Number == num), num, cols[i].T, size, i == 0 && noted.Contains(num)));
                var box = new Border { Child = row, Padding = new(10, 3), CornerRadius = new(8), BorderThickness = new(3, 0, 0, 0), BorderBrush = Brushes.Transparent, Classes = { "verse" } };
                var n = num;
                box.AddHandler(PointerPressedEvent, (object? _, PointerPressedEventArgs e) => OnVersePressed(n, e), RoutingStrategies.Tunnel, handledEventsToo: true);
                _verseBoxes[n] = box;
                ReaderPanel.Children.Add(box);
            }
            Paint();
            if (focusVerse is int fv && _verseBoxes.ContainsKey(fv)) { SelectVerse(fv); _verseBoxes[fv].BringIntoView(); }
            else ReaderScroll.Offset = default;

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
    sealed record WordSpan(int Start, int End, string[] Strongs, string Word);

    SelectableTextBlock VerseCell(Verse? v, int number, TranslationInfo t, int size, bool hasNote)
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
        var pos = 0;
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
            var run = new Run(s.Text);
            if (s.WordsOfJesus) run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsJesus"));
            if (s.Strongs is { Count: > 0 })
            {
                spans.Add(new WordSpan(pos, pos + s.Text.Length, [.. s.Strongs], s.Text));
                if (study) run.TextDecorations = TextDecorations.Underline;
            }
            Add(run);
        }
        Add(new Run(" "));
        tb.Tag = spans;
        return tb;
    }

    void OnVersePressed(int verse, PointerPressedEventArgs e)
    {
        SelectVerse(verse);
        if (StudyBtn.IsChecked != true || e.Source is not SelectableTextBlock { Tag: List<WordSpan> spans } tb) return;
        var hit = tb.TextLayout.HitTestPoint(e.GetPosition(tb)).TextPosition;
        var span = spans.FirstOrDefault(s => hit >= s.Start && hit < s.End);
        if (span is not null) _ = ShowStudyAsync(span);
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

    void AddColumn()
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
            trans.SelectedItem = list.FirstOrDefault(x => x.Id != main) ?? list.FirstOrDefault();
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
        lang.SelectedItem = LanguageBox.SelectedItem;
    }

    /// <summary>Selected verse: soft accent fill; bookmarked verse: accent bar on the left. Otherwise the hover style applies.</summary>
    void Paint()
    {
        var soft = Res("BsAccentSoft"); var accent = Res("BsAccent");
        foreach (var (n, box) in _verseBoxes)
        {
            if (n == _selected) box.Background = soft; else box.ClearValue(Border.BackgroundProperty);
            box.BorderBrush = _marked.Contains(n) ? accent : Brushes.Transparent;
        }
    }

    void FitReader()
    {
        ReaderPanel.HorizontalAlignment = _singleColumn ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Stretch;
        ReaderPanel.Margin = _singleColumn ? new(0, 36, 0, 80) : new(32, 28, 32, 80);
        ReaderPanel.Width = _singleColumn ? Math.Clamp(ReaderScroll.Bounds.Width - 64, 320, 760) : double.NaN;
    }

    void SelectVerse(int n)
    {
        _selected = n; Paint();
        var r = new VerseRef(_curBook, _curChapter, n);
        NoteTarget.Text = "Notiz zu " + Label(r);
        NoteEditor.Text = _store.GetNotes().FirstOrDefault(x => x.Ref == r)?.Text ?? "";
        _ = ShowXrefsAsync(r);
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

    void ToggleBookmark()
    {
        if (_selected is not int v) { StatusText.Text = "Erst einen Vers anklicken."; return; }
        var on = _store.ToggleBookmark(new VerseRef(_curBook, _curChapter, v));
        if (on) _marked.Add(v); else _marked.Remove(v);
        Paint(); RefreshLists();
    }

    async Task SaveNoteAsync(string text)
    {
        if (_selected is not int v) { StatusText.Text = "Erst einen Vers anklicken."; return; }
        _store.SetNote(new VerseRef(_curBook, _curChapter, v), text);
        RefreshLists();
        await ShowChapterAsync(v);
    }

    void RefreshLists()
    {
        BookmarkList.ItemsSource = _store.GetBookmarks().Select(r => new Entry(r, Label(r))).ToList();
        NoteList.ItemsSource = _store.GetNotes().Select(n =>
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
            var hits = _store.Search(t.Id, SearchBox.Text);
            SearchList.ItemsSource = hits.Select(h => new Entry(h.Ref, $"{Label(h.Ref)}\n{h.Snippet}")).ToList();
            SearchInfo.Text = hits.Count == 0 ? "Keine Treffer." : $"{hits.Count}{(hits.Count >= 200 ? "+" : "")} Treffer in {t.ShortName}";
        }
        catch (Exception ex) { SearchInfo.Text = "Fehler: " + ex.Message; }
    }

    async void OnEntryPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: Entry entry } list) return;
        list.SelectedItem = null;
        var book = _books.FirstOrDefault(b => b.Id == entry.Ref.Book);
        if (book is null) { StatusText.Text = "Buch in dieser Übersetzung nicht vorhanden."; return; }
        _loading = true;
        BookBox.SelectedItem = book;
        ChapterBox.Value = entry.Ref.Chapter;
        _loading = false;
        await ShowChapterAsync(entry.Ref.Verse);
    }
}
