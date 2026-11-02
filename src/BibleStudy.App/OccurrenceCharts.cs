using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>One bar per book (in Bible order): how often a word stands in it. Hovering names the book, a click asks for it.</summary>
sealed class BookBarChart : Control
{
    public event Action<string>? BookClicked;

    IReadOnlyList<(string Id, string Short, string Name)> _books = [];
    IReadOnlyDictionary<string, int> _counts = new Dictionary<string, int>();
    string? _hover;

    public BookBarChart() { ClipToBounds = true; Height = 210; Cursor = new Cursor(StandardCursorType.Arrow); }

    public void SetData(IReadOnlyList<(string Id, string Short, string Name)> books, IReadOnlyDictionary<string, int> counts)
    {
        _books = books; _counts = counts;
        InvalidateVisual();
    }

    (double Left, double Slot, double Base, double Top) Geometry() => (4, _books.Count == 0 ? 1 : (Bounds.Width - 8) / _books.Count, Bounds.Height - 44, 22);

    string? BookAt(Point p)
    {
        if (_books.Count == 0) return null;
        var (left, slot, _, _) = Geometry();
        var i = (int)Math.Floor((p.X - left) / slot);
        return i >= 0 && i < _books.Count ? _books[i].Id : null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var id = BookAt(e.GetPosition(this));
        if (id == _hover) return;
        _hover = id;
        var has = id is not null && _counts.TryGetValue(id, out var n) && n > 0;
        Cursor = new Cursor(has ? StandardCursorType.Hand : StandardCursorType.Arrow);
        ToolTip.SetTip(this, id is null ? null : $"{_books.First(b => b.Id == id).Name}: {_counts.GetValueOrDefault(id)}×");
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e) { _hover = null; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (BookAt(e.GetPosition(this)) is { } id && _counts.GetValueOrDefault(id) > 0) BookClicked?.Invoke(id);
    }

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (_books.Count == 0) return;
        var muted = (this.FindResource("BsMuted") as IBrush) ?? Brushes.Gray;
        var text = (this.FindResource("BsText") as IBrush) ?? Brushes.White;
        var (left, slot, baseY, top) = Geometry();
        var max = Math.Max(1, _books.Max(b => _counts.GetValueOrDefault(b.Id)));
        var barW = Math.Max(2, slot * 0.7);
        for (var i = 0; i < _books.Count; i++)
        {
            var b = _books[i];
            var n = _counts.GetValueOrDefault(b.Id);
            var x = left + i * slot + (slot - barW) / 2;
            var color = HomeView.ColorOf(b.Id);
            dc.DrawRectangle(new SolidColorBrush(color, n == 0 ? 0.25 : b.Id == _hover ? 1 : 0.8), null, new Rect(x, baseY - (n == 0 ? 2 : Math.Max(3, (baseY - top) * n / max)), barW, n == 0 ? 2 : Math.Max(3, (baseY - top) * n / max)));
            if (n > 0 && slot >= 14)
            {
                var label = new FormattedText(n.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 11, text);
                dc.DrawText(label, new Point(x + barW / 2 - label.Width / 2, baseY - (baseY - top) * n / max - 15));
            }
            // names in two rows, so neighbours do not run into each other
            var name = new FormattedText(b.Short, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 10.5, n > 0 ? text : muted);
            if (slot * 2 >= name.Width + 2 || i % 2 == 0)
                dc.DrawText(name, new Point(left + i * slot + slot / 2 - name.Width / 2, baseY + 4 + (i % 2) * 14));
        }
        dc.DrawLine(new Pen(muted, 1), new Point(left, baseY), new Point(left + slot * _books.Count, baseY));
    }
}

/// <summary>A ring: each slice is one rendering of the word, the number of all slices in the middle.</summary>
sealed class RenderingRing : Control
{
    static readonly string[] Palette = ["#F5B700", "#7C6A2B", "#5B5240", "#9A8C5A", "#C9A227", "#6E6046", "#B58E1F", "#857A55"];

    IReadOnlyList<(string Word, int Count)> _items = [];
    string _centerTop = "", _centerBottom = "";

    public RenderingRing() { Width = 220; Height = 220; }

    public static Color ColorFor(int i) => Color.Parse(Palette[i % Palette.Length]);

    public void SetData(IReadOnlyList<(string Word, int Count)> items, string centerTop, string centerBottom)
    {
        _items = items; _centerTop = centerTop; _centerBottom = centerBottom;
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        var total = _items.Sum(i => i.Count);
        if (total == 0) return;
        var text = (this.FindResource("BsText") as IBrush) ?? Brushes.White;
        var muted = (this.FindResource("BsMuted") as IBrush) ?? Brushes.Gray;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var outer = Math.Min(Bounds.Width, Bounds.Height) / 2 - 4;
        var inner = outer * 0.64;
        var angle = -Math.PI / 2;
        for (var i = 0; i < _items.Count; i++)
        {
            var sweep = 2 * Math.PI * _items[i].Count / total;
            var end = angle + (i == _items.Count - 1 ? 2 * Math.PI - (angle + Math.PI / 2) : sweep);
            if (sweep > 0.0005)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    var large = end - angle > Math.PI;
                    g.BeginFigure(new Point(c.X + outer * Math.Cos(angle), c.Y + outer * Math.Sin(angle)), true);
                    g.ArcTo(new Point(c.X + outer * Math.Cos(end), c.Y + outer * Math.Sin(end)), new Size(outer, outer), 0, large, SweepDirection.Clockwise);
                    g.LineTo(new Point(c.X + inner * Math.Cos(end), c.Y + inner * Math.Sin(end)));
                    g.ArcTo(new Point(c.X + inner * Math.Cos(angle), c.Y + inner * Math.Sin(angle)), new Size(inner, inner), 0, large, SweepDirection.CounterClockwise);
                    g.EndFigure(true);
                }
                dc.DrawGeometry(new SolidColorBrush(ColorFor(i)), new Pen(Brushes.Black, 0.5), geo);
            }
            angle = end;
        }
        var big = new FormattedText(_centerTop, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, FontWeight.Bold), 30, text);
        dc.DrawText(big, new Point(c.X - big.Width / 2, c.Y - big.Height / 2 - 12));
        var small = new FormattedText(_centerBottom, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 11, muted) { MaxTextWidth = inner * 1.5, TextAlignment = TextAlignment.Center };
        dc.DrawText(small, new Point(c.X - inner * 0.75, c.Y + 12));
    }
}
