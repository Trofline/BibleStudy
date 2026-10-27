using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
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

    const string CheckPath = "M20 6L9 17l-5-5";

    /// <summary>Raised when a chapter is picked (book id, chapter).</summary>
    public event Action<string, int>? ChapterChosen;
    /// <summary>Raised to mark chapters of a book as read/unread (book id, chapters, read).</summary>
    public event Action<string, IReadOnlyList<int>, bool>? ReadChanged;

    public string? OpenBook => _openBook;

    /// <summary>Where the user last read; shown as a "continue" button.</summary>
    public (string Book, int Chapter, string Label)? Resume { get; set; }

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
        if (_panel is not null) _panel.Width = Math.Clamp(_scroll.Bounds.Width - 64, 400, 940);
    }

    /// <summary>(Re)builds the page; keeps the chapter grid of <paramref name="openBook"/> open if given.</summary>
    public void Show(IReadOnlyList<BookInfo> books, HashSet<(string, int)> read, string? openBook = null)
    {
        _books = books; _read = read; _openBook = openBook;
        var panel = new StackPanel { Width = 940, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 40, 0, 70), Spacing = 6 };
        if (_openBook is not null && books.FirstOrDefault(b => b.Id == _openBook) is { } book) BuildChapters(panel, book);
        else BuildBooks(panel);
        _panel = panel;
        Fit();
        var offset = _scroll.Offset;
        _scroll.Content = panel;
        _scroll.Offset = offset; // bulk marking rebuilds the page; stay where we are
    }

    // ---- small helpers ------------------------------------------------------------------------------------------------

    IBrush? Res(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) ? v as IBrush : null;

    T Themed<T>(T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, this.GetResourceObservable(key));
        return control;
    }

    TextBlock Heading(string text, double size, string colorKey = "BsText")
    {
        var tb = new TextBlock { Text = text, FontSize = size, FontFamily = (FontFamily)Application.Current!.FindResource("UiSerif")!, FontWeight = FontWeight.SemiBold };
        return Themed(tb, TextBlock.ForegroundProperty, colorKey);
    }

    TextBlock Caption(string text, double size = 12) =>
        Themed(new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "BsMuted");

    Control Bar(double fraction, Color color, double height = 5)
    {
        var track = new Border { Height = height, CornerRadius = new(height / 2), Background = new SolidColorBrush(color, 0.22) };
        var fill = new Border { Height = height, CornerRadius = new(height / 2), Background = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Left };
        var grid = new Grid { Children = { track, fill } };
        grid.SizeChanged += (_, e) => fill.Width = Math.Round(e.NewSize.Width * Math.Clamp(fraction, 0, 1));
        return grid;
    }


    static Icon Ico(string path, double size) => new() { Data = Geometry.Parse(path), IconSize = size };

    int ReadCount(BookInfo b) => Enumerable.Range(1, b.NumberOfChapters).Count(c => _read.Contains((b.Id, c)));

    // ---- book overview ------------------------------------------------------------------------------------------------

    void BuildBooks(StackPanel panel)
    {
        var total = _books.Sum(b => b.NumberOfChapters);
        var read = _books.Sum(ReadCount);
        var accent = Res("BsAccent") is ISolidColorBrush sb ? sb.Color : Colors[BookCategory.Law];

        var hero = new DockPanel { Margin = new(0, 0, 0, 4) };
        if (Resume is { } r)
        {
            var go = new Button { Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center, Content = $"Weiterlesen: {r.Label}  →" };
            go.Click += (_, _) => ChapterChosen?.Invoke(r.Book, r.Chapter);
            DockPanel.SetDock(go, Dock.Right);
            hero.Children.Add(go);
        }
        hero.Children.Add(new StackPanel { Children = { Heading("Bibel", 46, "BsAccent"), Caption($"{read} von {total} Kapiteln gelesen · {(total == 0 ? 0 : 100 * read / total)} %", 13) } });
        panel.Children.Add(hero);
        panel.Children.Add(new Border { Margin = new(0, 6, 0, 0), Child = Bar(total == 0 ? 0 : (double)read / total, accent, 6) });

        Section(panel, "Altes Testament", _books.Where(b => BookCategories.IsOldTestament(BookCategories.Of(b.Id))));
        Section(panel, "Neues Testament", _books.Where(b => BookCategories.IsNewTestament(BookCategories.Of(b.Id))));
        Section(panel, "Weitere Bücher", _books.Where(b => BookCategories.Of(b.Id) == BookCategory.Other));
    }

    void Section(StackPanel panel, string title, IEnumerable<BookInfo> books)
    {
        var list = books.OrderBy(b => b.Order).ToList();
        if (list.Count == 0) return;
        var chapters = list.Sum(b => b.NumberOfChapters);
        var read = list.Sum(ReadCount);
        panel.Children.Add(new Border { Height = 22 });

        var head = new DockPanel { Margin = new(0, 0, 0, 8) };
        // One quiet menu instead of a loud button: marking a whole section is rare and should not be done by accident.
        var menu = new MenuFlyout();
        var markAll = new MenuItem { Header = $"Alle {chapters} Kapitel als gelesen markieren" };
        markAll.Click += (_, _) => { foreach (var b in list) ReadChanged?.Invoke(b.Id, Enumerable.Range(1, b.NumberOfChapters).ToList(), true); };
        var clear = new MenuItem { Header = "Lesefortschritt dieses Abschnitts zurücksetzen" };
        clear.Click += (_, _) => { foreach (var b in list) ReadChanged?.Invoke(b.Id, Enumerable.Range(1, b.NumberOfChapters).ToList(), false); };
        menu.Items.Add(markAll); menu.Items.Add(clear);
        var more = new Button { Classes = { "tool" }, Width = 32, Height = 32, Flyout = menu, Content = Ico("M5 12a1 1 0 1 0 2 0a1 1 0 1 0-2 0z M11 12a1 1 0 1 0 2 0a1 1 0 1 0-2 0z M17 12a1 1 0 1 0 2 0a1 1 0 1 0-2 0z", 18) };
        ToolTip.SetTip(more, "Abschnitt: alles als gelesen markieren oder zurücksetzen");
        DockPanel.SetDock(more, Dock.Right);
        head.Children.Add(more);
        var meta = Caption($"{read} / {chapters} Kapitel");
        meta.VerticalAlignment = VerticalAlignment.Center; meta.Margin = new(0, 0, 8, 0);
        DockPanel.SetDock(meta, Dock.Right);
        head.Children.Add(meta);
        head.Children.Add(Heading(title, 26));
        panel.Children.Add(head);

        var wrap = new WrapPanel();
        foreach (var b in list) wrap.Children.Add(BookTile(b));
        panel.Children.Add(wrap);
    }

    Control BookTile(BookInfo b)
    {
        var color = Colors[BookCategories.Of(b.Id)];
        var count = ReadCount(b);
        var done = b.NumberOfChapters > 0 && count == b.NumberOfChapters;
        var frac = b.NumberOfChapters == 0 ? 0 : (double)count / b.NumberOfChapters;

        var name = new TextBlock { Text = b.CommonName, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var number = Themed(new TextBlock { Text = done ? "" : $"{count}/{b.NumberOfChapters}", FontSize = 11, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "BsMuted");
        var top = new DockPanel { LastChildFill = true };
        if (done) { var tickIcon = Ico(CheckPath, 15); TextElement.SetForeground(tickIcon, new SolidColorBrush(color)); var check = new Border { Child = tickIcon, Margin = new(6, 0, 0, 0) }; DockPanel.SetDock(check, Dock.Right); top.Children.Add(check); }
        else { DockPanel.SetDock(number, Dock.Right); top.Children.Add(number); }
        top.Children.Add(name);

        var stripe = new Border { Width = 4, CornerRadius = new(2), Background = new SolidColorBrush(color), Margin = new(0, 0, 10, 0) };
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 8, Children = { top, Bar(frac, color, 4) } };
        var inner = new DockPanel { Children = { stripe, body } };
        DockPanel.SetDock(stripe, Dock.Left);

        var tile = new Border
        {
            Width = 148, Height = 62, Margin = new(0, 0, 10, 10), Padding = new(10, 9, 12, 9), CornerRadius = new(12), BorderThickness = new(1),
            Cursor = new Cursor(StandardCursorType.Hand), Child = inner, Classes = { "tile" },
        };
        Themed(tile, Border.BorderBrushProperty, "BsBorder");
        if (done) tile.Background = new SolidColorBrush(color, 0.10);
        else Themed(tile, Border.BackgroundProperty, "BsSurface");
        ToolTip.SetTip(tile, $"{b.CommonName} · {count}/{b.NumberOfChapters} gelesen");
        tile.PointerPressed += (_, e) => { if (e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) Show(_books, _read, b.Id); };
        return tile;
    }

    // ---- chapter grid of one book -------------------------------------------------------------------------------------

    void BuildChapters(StackPanel panel, BookInfo b)
    {
        var chapters = Enumerable.Range(1, b.NumberOfChapters).ToList();
        var color = Colors[BookCategories.Of(b.Id)];
        var count = ReadCount(b);

        var back = new Button { Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(-8, 0, 0, 10), Content = "←  Alle Bücher" };
        back.Click += (_, _) => Show(_books, _read);
        panel.Children.Add(back);

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { new Border { Width = 8, Height = 46, CornerRadius = new(4), Background = new SolidColorBrush(color) }, Heading(b.CommonName, 46, "BsAccent") } };
        panel.Children.Add(title);
        panel.Children.Add(Caption($"{count} von {b.NumberOfChapters} Kapiteln gelesen · Klick öffnet · Rechtsklick oder Strg+Klick markiert ein Kapitel", 13));
        panel.Children.Add(new Border { Margin = new(0, 8, 0, 0), Child = Bar(b.NumberOfChapters == 0 ? 0 : (double)count / b.NumberOfChapters, color, 6) });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(0, 12, 0, 16) };
        var markAll = new Button { Content = "Alle Kapitel als gelesen markieren", IsEnabled = count < b.NumberOfChapters };
        markAll.Click += (_, _) => ReadChanged?.Invoke(b.Id, chapters, true);
        var clear = new Button { Classes = { "ghost" }, Content = "Zurücksetzen", IsEnabled = count > 0 };
        clear.Click += (_, _) => ReadChanged?.Invoke(b.Id, chapters, false);
        actions.Children.Add(markAll); actions.Children.Add(clear);
        panel.Children.Add(actions);

        var next = chapters.FirstOrDefault(c => !_read.Contains((b.Id, c)));
        var wrap = new WrapPanel();
        foreach (var c in chapters)
        {
            var isRead = _read.Contains((b.Id, c));
            var isNext = c == next && count > 0 && !isRead;
            var num = new TextBlock { Text = c.ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 16, FontWeight = isRead || isNext ? FontWeight.SemiBold : FontWeight.Normal };
            Themed(num, TextBlock.ForegroundProperty, isRead || isNext ? "BsAccent" : "BsText");
            var content = new Grid { Children = { num } };
            if (isRead)
            {
                var tick = Ico(CheckPath, 11); tick.HorizontalAlignment = HorizontalAlignment.Right; tick.VerticalAlignment = VerticalAlignment.Top; tick.Margin = new(0, 4, 4, 0);
                tick.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("BsAccent"));
                content.Children.Add(tick);
            }
            var chip = new Border { Width = 58, Height = 48, Margin = new(0, 0, 8, 8), CornerRadius = new(12), BorderThickness = new(isNext ? 2 : 1), Cursor = new Cursor(StandardCursorType.Hand), Child = content, Classes = { "tile" } };
            Themed(chip, Border.BackgroundProperty, isRead ? "BsAccentSoft" : "BsSurface");
            Themed(chip, Border.BorderBrushProperty, isRead || isNext ? "BsAccent" : "BsBorder");
            ToolTip.SetTip(chip, isNext ? $"Kapitel {c}: als Nächstes ungelesen" : $"Kapitel {c}{(isRead ? " · gelesen" : "")}");
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
