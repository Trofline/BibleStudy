using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// Language, translation, book and chapter are chosen from fixed-size grids (everything at a glance) instead of scrolling dropdown lists.
/// The hidden combo boxes still hold the data, so the rest of the program does not change.
/// </summary>
public partial class MainWindow
{
    /// <summary>Smooth color along the order of the books: gold → orange → rose → violet → blue → teal → green.</summary>
    internal static Color BookGradient(double t)
    {
        (double T, Color C)[] stops =
        [
            (0.00, Color.Parse("#E7C45A")), (0.18, Color.Parse("#EC9A4E")), (0.34, Color.Parse("#E4687E")), (0.48, Color.Parse("#C26ED1")),
            (0.62, Color.Parse("#7A86EE")), (0.74, Color.Parse("#4FA6E8")), (0.86, Color.Parse("#3CBFB4")), (1.00, Color.Parse("#66C47A")),
        ];
        t = Math.Clamp(t, 0, 1);
        for (var i = 1; i < stops.Length; i++)
            if (t <= stops[i].T)
            {
                var k = (t - stops[i - 1].T) / (stops[i].T - stops[i - 1].T);
                Color a = stops[i - 1].C, b = stops[i].C;
                return Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
            }
        return stops[^1].C;
    }

    internal static Color BookColorAt(IReadOnlyList<BookInfo> books, string id)
    {
        var i = books.ToList().FindIndex(b => b.Id == id);
        return BookGradient(books.Count <= 1 || i < 0 ? 0 : (double)i / (books.Count - 1));
    }

    void InitPickers()
    {
        LangBtn.Click += (_, _) => ShowLanguagePicker();
        TransBtn.Click += (_, _) => ShowTranslationPicker();
        BookBtn.Click += (_, _) => ShowBookPicker();
        ChapterBtn.Click += (_, _) => ShowChapterPicker();
        LanguageBox.SelectionChanged += (_, _) => UpdatePickerLabels();
        TranslationBox.SelectionChanged += (_, _) => UpdatePickerLabels();
        BookBox.SelectionChanged += (_, _) => UpdatePickerLabels();
        ChapterBox.ValueChanged += (_, _) => UpdatePickerLabels();
        UpdatePickerLabels();
    }

    static Control PickerLabel(string text) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 8,
        Children =
        {
            new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150, VerticalAlignment = VerticalAlignment.Center },
            new Icon { Data = Geometry.Parse("M6 9l6 6 6-6"), IconSize = 14, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    void UpdatePickerLabels()
    {
        LangBtn.Content = PickerLabel(LanguageBox.SelectedItem as string ?? L("Sprache"));
        TransBtn.Content = PickerLabel(TranslationBox.SelectedItem is TranslationInfo t ? t.ShortName : L("Übersetzung"));
        BookBtn.Content = PickerLabel(BookBox.SelectedItem is BookInfo b ? b.CommonName : L("Buch"));
        ChapterBtn.Content = PickerLabel(((int)(ChapterBox.Value ?? 1)).ToString());
    }

    Flyout NewPickerFlyout(Control content) => new() { Content = content, Placement = PlacementMode.BottomEdgeAlignedLeft };

    Button PickerTile(string text, bool selected, Action pick, double width, Color? underline = null, double height = 40, string? tip = null)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = selected ? FontWeight.Bold : FontWeight.Medium },
            Width = width, Height = height, Margin = new(0, 0, 6, 6), Padding = new(4, 0), CornerRadius = new(10),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Background = selected ? Res("BsAccentSoft") : Res("BsSurface2"), Foreground = selected ? Res("BsAccent") : Res("BsText"),
            BorderThickness = underline is null ? new(0) : new(0, 0, 0, 3), BorderBrush = underline is Color c ? new SolidColorBrush(c) : null,
        };
        b.Classes.Add("ghost");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => pick();
        return b;
    }

    TextBlock PickerHeading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 15, Margin = new(0, 4, 0, 8) };

    // ---- books --------------------------------------------------------------------------------------------------------

    /// <summary>The main book button: choosing a book opens the chapters of that book right away (one click less).</summary>
    void ShowBookPicker() => ShowBookPicker(BookBtn, (BookBox.SelectedItem as BookInfo)?.Id, book =>
    {
        var sameBook = (BookBox.SelectedItem as BookInfo)?.Id == book.Id;
        var chapter = sameBook ? (int)(ChapterBox.Value ?? 1) : 1;
        BookBox.SelectedItem = book;   // shows chapter 1 already; the chapter list below only refines it
        if (book.NumberOfChapters > 1) ShowChapterPicker(ChapterBtn, book, chapter, c => ChapterBox.Value = c);
    });

    /// <summary>Book grid under any button. <paramref name="reset"/> adds a first tile (for a column that may follow the main text again).</summary>
    void ShowBookPicker(Control anchor, string? current, Action<BookInfo> pick, (string Label, Action Do)? reset = null)
    {
        if (_books.Count == 0) return;
        Flyout? flyout = null;
        void Choose(BookInfo book) { flyout!.Hide(); Avalonia.Threading.Dispatcher.UIThread.Post(() => pick(book)); }
        var search = new TextBox { Watermark = L("Tippen zum Filtern, z. B. „Joh“ + Enter"), Margin = new(0, 0, 0, 6) };
        var tiles = new StackPanel();
        List<BookInfo> shown = [];
        void Fill()
        {
            tiles.Children.Clear(); shown.Clear();
            var q = SearchQuery.Fold(search.Text?.Trim() ?? "", true).Replace(" ", "").Replace(".", "");
            bool Match(BookInfo b) => q.Length == 0 || SearchQuery.Fold(b.CommonName + ShortBook(b) + b.Id, true).Replace(" ", "").Replace(".", "").Contains(q);
            foreach (var (title, pred) in new (string, Func<BookInfo, bool>)[]
            {
                (L("Altes Testament"), b => BookCategories.IsOldTestament(BookCategories.Of(b.Id))),
                (L("Neues Testament"), b => BookCategories.IsNewTestament(BookCategories.Of(b.Id))),
                (L("Weitere Bücher"), b => BookCategories.Of(b.Id) == BookCategory.Other),
            })
            {
                var list = _books.Where(pred).Where(Match).OrderBy(b => b.Order).ToList();
                if (list.Count == 0) continue;
                tiles.Children.Add(PickerHeading(title));
                var wrap = new WrapPanel();
                foreach (var b in list)
                {
                    var book = b; shown.Add(b);
                    wrap.Children.Add(PickerTile(ShortBook(b), b.Id == current || (q.Length > 0 && shown.Count == 1), () => Choose(book), 90, BookColorAt(_books, b.Id), 42, b.CommonName));
                }
                tiles.Children.Add(wrap);
            }
        }
        search.TextChanged += (_, _) => Fill();
        search.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter && shown.Count > 0) { e.Handled = true; Choose(shown[0]); } };
        Fill();
        var panel = new StackPanel { Width = 640, Children = { search, tiles } };
        if (reset is { } r)
        {
            var back = new Button { Content = r.Label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 6) };
            back.Click += (_, _) => { flyout!.Hide(); r.Do(); };
            panel.Children.Insert(0, back);
        }
        flyout = NewPickerFlyout(new ScrollViewer { Content = panel, MaxHeight = Math.Max(300, Bounds.Height - 130), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        flyout.ShowAt(anchor);
        search.Focus();
    }

    // ---- chapters -----------------------------------------------------------------------------------------------------

    void ShowChapterPicker()
    {
        if (BookBox.SelectedItem is BookInfo book) ShowChapterPicker(ChapterBtn, book, (int)(ChapterBox.Value ?? 1), c => ChapterBox.Value = c);
    }

    void ShowChapterPicker(Control anchor, BookInfo book, int current, Action<int> pick)
    {
        Flyout? flyout = null;
        var color = BookColorAt(_books, book.Id);
        var panel = new StackPanel { Width = Math.Min(620, 70 + Math.Ceiling(Math.Sqrt(book.NumberOfChapters)) * 56) };
        panel.Children.Add(PickerHeading(L("{0} – Kapitel wählen", book.CommonName)));
        var number = new TextBox { Watermark = L("Nummer tippen (1–{0}) + Enter", book.NumberOfChapters), Margin = new(0, 0, 0, 8) };
        number.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter || !int.TryParse(number.Text, out var n)) return;
            e.Handled = true; flyout!.Hide(); pick(Math.Clamp(n, 1, book.NumberOfChapters));
        };
        panel.Children.Add(number);
        var wrap = new WrapPanel();
        for (var c = 1; c <= book.NumberOfChapters; c++)
        {
            var chapter = c;
            var read = _read.Contains((book.Id, c));
            var tile = PickerTile(c.ToString(), c == current, () => { flyout!.Hide(); pick(chapter); }, 50, read ? color : null, 40, read ? "gelesen" : null);
            wrap.Children.Add(tile);
        }
        panel.Children.Add(wrap);
        flyout = NewPickerFlyout(new ScrollViewer { Content = panel, MaxHeight = Math.Max(300, Bounds.Height - 130), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        flyout.ShowAt(anchor);
        number.Focus();
    }

    // ---- language -----------------------------------------------------------------------------------------------------

    void ShowLanguagePicker()
    {
        var langs = (LanguageBox.ItemsSource as IEnumerable<string>)?.ToList() ?? _langs.ToList();
        if (langs.Count == 0) return;
        Flyout? flyout = null;
        var current = LanguageBox.SelectedItem as string;
        var search = new TextBox { Watermark = L("Sprache suchen …"), Margin = new(0, 0, 0, 10) };
        var grid = new WrapPanel();
        void Fill()
        {
            grid.Children.Clear();
            var q = SearchQuery.Fold(search.Text?.Trim() ?? "", true);
            var common = new[] { "German", "English", "French", "Spanish", "Italian", "Latin", "Greek", "Hebrew" };
            foreach (var l in langs.OrderBy(l => q.Length == 0 && Array.IndexOf(common, l) is var i and >= 0 ? i : 99).ThenBy(l => l))
            {
                if (q.Length > 0 && !SearchQuery.Fold(l, true).Contains(q)) continue;
                var lang = l;
                grid.Children.Add(PickerTile(l, l == current, () => { LanguageBox.SelectedItem = lang; flyout!.Hide(); }, 150, null, 38));
            }
        }
        search.TextChanged += (_, _) => Fill();
        Fill();
        var stack = new StackPanel { Width = 660, Children = { search, new ScrollViewer { Content = grid, Height = Math.Min(430, Math.Max(250, Bounds.Height - 230)), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } } };
        flyout = NewPickerFlyout(stack);
        flyout.ShowAt(LangBtn);
        search.Focus();
    }

    // ---- translation --------------------------------------------------------------------------------------------------

    void ShowTranslationPicker()
    {
        if (TranslationBox.ItemsSource is not IEnumerable<TranslationInfo> list) return;
        var items = list.ToList();
        Flyout? flyout = null;
        var current = (TranslationBox.SelectedItem as TranslationInfo)?.Id;
        var panel = new WrapPanel { Width = 700 };
        foreach (var t in items)
        {
            var tr = t;
            var license = t.License.PublicDomain ? "gemeinfrei" : t.License.Attribution;
            var content = new StackPanel
            {
                Spacing = 1, HorizontalAlignment = HorizontalAlignment.Left,
                Children = { new TextBlock { Text = t.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = $"{t.ShortName} · {Shorten(license, 40)}", Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis } },
            };
            var b = new Button { Content = content, Width = 340, Height = 52, Margin = new(0, 0, 6, 6), Padding = new(12, 0), CornerRadius = new(10), HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = t.Id == current ? Res("BsAccentSoft") : Res("BsSurface2"), Foreground = Res("BsText") };
            b.Classes.Add("ghost");
            b.Click += (_, _) => { TranslationBox.SelectedItem = tr; flyout!.Hide(); };
            panel.Children.Add(b);
        }
        flyout = NewPickerFlyout(new ScrollViewer { Content = panel, Width = 720, MaxHeight = Math.Max(300, Bounds.Height - 130), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        flyout.ShowAt(TransBtn);
    }
}
