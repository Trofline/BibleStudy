using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Start page: all books grouped by testament with reading progress; drill down to a chapter grid.</summary>
public sealed class HomeView : UserControl
{
    internal static readonly Dictionary<BookCategory, Color> Colors = new()
    {
        [BookCategory.Law] = Color.Parse("#eb568f"), [BookCategory.History] = Color.Parse("#cd4805"),
        [BookCategory.Poetry] = Color.Parse("#b48f05"), [BookCategory.MajorProphets] = Color.Parse("#cd489d"),
        [BookCategory.MinorProphets] = Color.Parse("#88a00d"), [BookCategory.Gospels] = Color.Parse("#17a7b4"),
        [BookCategory.Acts] = Color.Parse("#119245"), [BookCategory.Paul] = Color.Parse("#0593e6"),
        [BookCategory.General] = Color.Parse("#03ac8f"), [BookCategory.Revelation] = Color.Parse("#a06aec"),
        [BookCategory.Other] = Color.Parse("#8A8F98"),
    };

    internal static Color ColorOf(string bookId) => Colors[BookCategories.Of(bookId)];

    /// <summary>Raised when a chapter is picked (book id, chapter).</summary>
    public event Action<string, int>? ChapterChosen;
    /// <summary>Raised to mark chapters of a book as read/unread (book id, chapters, read).</summary>
    public event Action<string, IReadOnlyList<int>, bool>? ReadChanged;

    public string? OpenBook => _openBook;

    IReadOnlyList<BookInfo> _books = [];
    HashSet<(string, int)> _read = [];
    string? _openBook;
    readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    StackPanel? _panel;

    public HomeView()
    {
        Content = _scroll;
        _scroll.SizeChanged += (_, _) => Fit();
    }

    void Fit()
    {
        if (_panel is not null) _panel.Width = Math.Clamp(_scroll.Bounds.Width - 64, 400, 880);
    }

    /// <summary>(Re)builds the page; keeps the chapter grid of <paramref name="openBook"/> open if given.</summary>
    public void Show(IReadOnlyList<BookInfo> books, HashSet<(string, int)> read, string? openBook = null)
    {
        _books = books; _read = read; _openBook = openBook;
        var panel = new StackPanel { Width = 880, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 36, 0, 60), Spacing = 6 };
        if (_openBook is not null && books.FirstOrDefault(b => b.Id == _openBook) is { } book) BuildChapters(panel, book);
        else BuildBooks(panel);
        _panel = panel;
        Fit();
        _scroll.Content = panel;
    }

    IBrush? Res(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) ? v as IBrush : null;

    TextBlock Heading(string text, double size, string colorKey = "BsText")
    {
        var tb = new TextBlock { Text = text, FontSize = size, FontFamily = (FontFamily)Application.Current!.FindResource("UiSerif")!, FontWeight = FontWeight.SemiBold };
        tb.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(colorKey));
        return tb;
    }

    TextBlock Caption(string text)
    {
        var tb = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        tb.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        return tb;
    }

    int ReadCount(BookInfo b) => Enumerable.Range(1, b.NumberOfChapters).Count(c => _read.Contains((b.Id, c)));

    void BuildBooks(StackPanel panel)
    {
        var total = _books.Sum(b => b.NumberOfChapters);
        var read = _books.Sum(ReadCount);
        panel.Children.Add(Heading("Bibel", 44, "BsAccent"));
        panel.Children.Add(Caption($"{read} von {total} Kapiteln gelesen · Buch wählen"));
        Section(panel, "Altes Testament", _books.Where(b => BookCategories.IsOldTestament(BookCategories.Of(b.Id))));
        Section(panel, "Neues Testament", _books.Where(b => BookCategories.IsNewTestament(BookCategories.Of(b.Id))));
        Section(panel, "Weitere Bücher", _books.Where(b => BookCategories.Of(b.Id) == BookCategory.Other));
    }

    void Section(StackPanel panel, string title, IEnumerable<BookInfo> books)
    {
        var list = books.OrderBy(b => b.Order).ToList();
        if (list.Count == 0) return;
        panel.Children.Add(new Border { Height = 18 });
        var head = new DockPanel { Margin = new(0, 0, 0, 4) };
        var all = new Button { Content = "Alles gelesen", FontSize = 12, Padding = new(10, 3) };
        DockPanel.SetDock(all, Dock.Right);
        all.Click += (_, _) => { foreach (var b in list) ReadChanged?.Invoke(b.Id, Enumerable.Range(1, b.NumberOfChapters).ToList(), true); };
        head.Children.Add(all);
        head.Children.Add(Heading(title, 26));
        panel.Children.Add(head);
        var wrap = new WrapPanel();
        foreach (var b in list) wrap.Children.Add(BookTile(b));
        panel.Children.Add(wrap);
    }

    Control BookTile(BookInfo b)
    {
        var color = Colors[BookCategories.Of(b.Id)];
        var frac = b.NumberOfChapters == 0 ? 0 : (double)ReadCount(b) / b.NumberOfChapters;
        var track = new Border { Height = 4, CornerRadius = new(2), Background = new SolidColorBrush(color, 0.28) };
        var fill = new Border { Height = 4, CornerRadius = new(2), Background = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Round(112 * frac) };
        var bar = new Grid { Margin = new(0, 6, 0, 0), Children = { track, fill } };
        var name = new TextBlock { Text = b.CommonName, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.Medium };
        var tile = new Border { Width = 132, Height = 56, Margin = new(0, 0, 8, 8), Padding = new(10, 8), CornerRadius = new(10), BorderThickness = new(1), Cursor = new Cursor(StandardCursorType.Hand), Child = new StackPanel { Children = { name, bar } } };
        tile.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        tile.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        ToolTip.SetTip(tile, $"{b.CommonName} · {ReadCount(b)}/{b.NumberOfChapters} gelesen");
        tile.PointerPressed += (_, e) => { if (e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) Show(_books, _read, b.Id); };
        return tile;
    }

    void BuildChapters(StackPanel panel, BookInfo b)
    {
        var chapters = Enumerable.Range(1, b.NumberOfChapters).ToList();
        var back = new Button { Content = "← Alle Bücher", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 12) };
        back.Click += (_, _) => Show(_books, _read);
        panel.Children.Add(back);
        panel.Children.Add(Heading(b.CommonName, 44, "BsAccent"));
        panel.Children.Add(Caption($"{ReadCount(b)} von {b.NumberOfChapters} Kapiteln gelesen · Klick öffnet, Rechtsklick oder Strg+Klick markiert als gelesen"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(0, 8, 0, 14) };
        var markAll = new Button { Content = "Alle als gelesen markieren" };
        markAll.Click += (_, _) => ReadChanged?.Invoke(b.Id, chapters, true);
        var clear = new Button { Content = "Zurücksetzen" };
        clear.Click += (_, _) => ReadChanged?.Invoke(b.Id, chapters, false);
        actions.Children.Add(markAll); actions.Children.Add(clear);
        panel.Children.Add(actions);

        var wrap = new WrapPanel();
        foreach (var c in chapters)
        {
            var isRead = _read.Contains((b.Id, c));
            var num = new TextBlock { Text = c.ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = isRead ? FontWeight.SemiBold : FontWeight.Normal };
            num.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(isRead ? "BsAccent" : "BsText"));
            var chip = new Border { Width = 56, Height = 46, Margin = new(0, 0, 8, 8), CornerRadius = new(10), BorderThickness = new(1), Cursor = new Cursor(StandardCursorType.Hand), Child = num };
            chip.Bind(Border.BackgroundProperty, this.GetResourceObservable(isRead ? "BsAccentSoft" : "BsSurface"));
            chip.Bind(Border.BorderBrushProperty, this.GetResourceObservable(isRead ? "BsAccent" : "BsBorder"));
            var chapter = c;
            chip.PointerPressed += (_, e) =>
            {
                var p = e.GetCurrentPoint(chip).Properties;
                if (p.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Control)) ReadChanged?.Invoke(b.Id, [chapter], !_read.Contains((b.Id, chapter)));
                else if (p.IsLeftButtonPressed) ChapterChosen?.Invoke(b.Id, chapter);
            };
            wrap.Children.Add(chip);
        }
        panel.Children.Add(wrap);
    }
}
