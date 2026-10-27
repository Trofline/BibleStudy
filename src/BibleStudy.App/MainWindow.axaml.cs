using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

public sealed record Entry(VerseRef Ref, string Display);

public partial class MainWindow : Window
{
    static readonly IBrush MarkBrush = new SolidColorBrush(Color.FromArgb(50, 255, 190, 0));
    static readonly IBrush SelectBrush = new SolidColorBrush(Color.FromArgb(60, 70, 130, 200));

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
        _provider = new HelloAoProvider(new HttpClient(), Path.Combine(root, "cache"));
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
        NoteSaveBtn.Click += async (_, _) => await SaveNoteAsync(NoteEditor.Text ?? "");
        NoteDeleteBtn.Click += async (_, _) => { NoteEditor.Text = ""; await SaveNoteAsync(""); };
        SearchBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await SearchAsync(); };
        foreach (var list in new[] { SearchList, BookmarkList, NoteList }) list.SelectionChanged += OnEntryPicked;
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
            var langs = _all.Select(t => t.LanguageName).Where(l => l != "").Distinct().Order().ToList();
            LanguageBox.ItemsSource = langs;
            LanguageBox.SelectedItem = langs.Contains("English") ? "English" : langs.FirstOrDefault();
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
            FlowDirection = t.TextDirection == "rtl" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
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
            var ch = await _provider.GetChapterAsync(t.Id, b.Id, (int)(ChapterBox.Value ?? 1));
            _curBook = b.Id; _curChapter = ch.Number; _selected = null;
            _marked = _store.GetBookmarks().Where(r => r.Book == _curBook && r.Chapter == _curChapter).Select(r => r.Verse).ToHashSet();
            var noted = _store.GetNotes().Where(n => n.Ref.Book == _curBook && n.Ref.Chapter == _curChapter).Select(n => n.Ref.Verse).ToHashSet();

            ReaderPanel.Children.Clear(); _verseBoxes.Clear();
            ReaderPanel.Children.Add(new TextBlock { Text = $"{b.CommonName} {ch.Number}", FontSize = 28, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 12) });
            foreach (var v in ch.Verses)
            {
                var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 19, LineHeight = 32 };
                tb.Inlines!.Add(new Run(v.Number + (noted.Contains(v.Number) ? "✎" : "") + " ") { FontSize = 12, FontWeight = FontWeight.Bold, BaselineAlignment = BaselineAlignment.Top, Foreground = Brushes.SteelBlue });
                foreach (var s in v.Segments)
                    tb.Inlines.Add(new Run(s.Text + " ") { Foreground = s.WordsOfJesus ? Brushes.IndianRed : null });
                var box = new Border { Child = tb, Padding = new(8, 2), CornerRadius = new(6) };
                var n = v.Number;
                box.AddHandler(PointerPressedEvent, (object? _, PointerPressedEventArgs _) => SelectVerse(n), RoutingStrategies.Bubble | RoutingStrategies.Tunnel, handledEventsToo: true);
                _verseBoxes[n] = box;
                ReaderPanel.Children.Add(box);
            }
            Paint();
            if (focusVerse is int fv && _verseBoxes.ContainsKey(fv)) { SelectVerse(fv); _verseBoxes[fv].BringIntoView(); }
            else ReaderScroll.Offset = default;

            var l = t.License;
            StatusText.Text = $"{t.Name} · {(l.PublicDomain ? "Gemeinfrei" : "Lizenz: " + t.LicenseUrl)} · Namensnennung: {l.Attribution} · Quelle: {_provider.Name}";
        }
        catch (Exception ex) { StatusText.Text = "Kapitel nicht verfügbar: " + ex.Message; }
    }

    void Paint()
    {
        foreach (var (n, box) in _verseBoxes)
            box.Background = n == _selected ? SelectBrush : _marked.Contains(n) ? MarkBrush : null;
    }

    void SelectVerse(int n)
    {
        _selected = n; Paint();
        var r = new VerseRef(_curBook, _curChapter, n);
        NoteTarget.Text = "Notiz zu " + Label(r);
        NoteEditor.Text = _store.GetNotes().FirstOrDefault(x => x.Ref == r)?.Text ?? "";
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
            return new Entry(n.Ref, $"{Label(n.Ref)} – {(t.Length > 100 ? t[..100] + "…" : t)}");
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
            SearchList.ItemsSource = hits.Select(h => new Entry(h.Ref, $"{Label(h.Ref)}  {h.Snippet}")).ToList();
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
