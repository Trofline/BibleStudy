using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>
/// The references between the testaments as ribbons: Old Testament books on the left, New Testament books on the right, a ribbon for each pair,
/// the thicker the more cross references run between the two. Hovering a book lights up its ribbons, a click asks for its verses.
/// </summary>
sealed class RefNetView : Control
{
    public event Action<string>? BookClicked;
    public event Action<string>? Hint;

    IReadOnlyList<(string Id, string Name)> _ot = [], _nt = [];
    IReadOnlyDictionary<(string Nt, string Ot), int> _count = new Dictionary<(string, string), int>();
    string? _hover;
    const double Top = 12, LabelWidth = 150;

    public RefNetView() => ClipToBounds = true;

    public void SetData(IReadOnlyList<(string Id, string Name)> ot, IReadOnlyList<(string Id, string Name)> nt, IReadOnlyDictionary<(string Nt, string Ot), int> count)
    {
        _ot = ot; _nt = nt; _count = count;
        InvalidateVisual();
    }

    double RowHeight(int n) => (Bounds.Height - Top * 2) / Math.Max(1, n);
    double YOt(int i) => Top + (i + 0.5) * RowHeight(_ot.Count);
    double YNt(int j) => Top + (j + 0.5) * RowHeight(_nt.Count);

    IBrush Res(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    public override void Render(DrawingContext dc)
    {
        var text = Res("BsText", Brushes.White);
        var muted = Res("BsMuted", Brushes.Gray);
        var x0 = LabelWidth + 10; var x1 = Bounds.Width - LabelWidth - 10;
        if (_count.Count == 0 || x1 <= x0 + 40)
        {
            dc.DrawText(new FormattedText(L("Keine Verweise in dieser Stärke."), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 14, muted), new Point(24, 20));
            return;
        }
        var max = _count.Values.Max();
        var otIndex = _ot.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var ntIndex = _nt.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);

        // ribbons: the weak ones first, so that strong ribbons lie on top
        foreach (var (pair, n) in _count.OrderBy(p => p.Value))
        {
            if (!otIndex.TryGetValue(pair.Ot, out var i) || !ntIndex.TryGetValue(pair.Nt, out var j)) continue;
            var lit = _hover is null || _hover == pair.Ot || _hover == pair.Nt;
            var color = HomeView.ColorOf(pair.Ot);
            var width = 0.6 + 9.0 * Math.Sqrt((double)n / max);
            var a = new Point(x0, YOt(i)); var b = new Point(x1, YNt(j));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(a, false);
                var mid = (x0 + x1) / 2;
                ctx.CubicBezierTo(new Point(mid, a.Y), new Point(mid, b.Y), b);
                ctx.EndFigure(false);
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(color, lit ? (_hover is null ? 0.45 : 0.85) : 0.06), width), g);
        }

        void Labels(IReadOnlyList<(string Id, string Name)> list, bool left)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var y = left ? YOt(i) : YNt(i);
                var active = _hover == list[i].Id;
                var f = new FormattedText(list[i].Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, active ? FontWeight.SemiBold : FontWeight.Normal), 11.5, active ? text : muted);
                dc.DrawText(f, new Point(left ? x0 - 10 - f.Width : x1 + 10, y - f.Height / 2));
                dc.DrawEllipse(new SolidColorBrush(HomeView.ColorOf(list[i].Id)), null, new Point(left ? x0 - 3 : x1 + 3, y), active ? 4 : 2.5, active ? 4 : 2.5);
            }
        }
        Labels(_ot, true); Labels(_nt, false);
    }

    string? BookAt(Point p)
    {
        var x0 = LabelWidth + 10; var x1 = Bounds.Width - LabelWidth - 10;
        if (p.X < x0 + 6) { var h = RowHeight(_ot.Count); var i = (int)((p.Y - Top) / h); return i >= 0 && i < _ot.Count ? _ot[i].Id : null; }
        if (p.X > x1 - 6) { var h = RowHeight(_nt.Count); var j = (int)((p.Y - Top) / h); return j >= 0 && j < _nt.Count ? _nt[j].Id : null; }
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var book = BookAt(e.GetPosition(this));
        if (book == _hover) return;
        _hover = book;
        Cursor = book is null ? null : new Cursor(StandardCursorType.Hand);
        if (book is null) Hint?.Invoke("");
        else
        {
            var total = _count.Where(c => c.Key.Ot == book || c.Key.Nt == book).Sum(c => c.Value);
            Hint?.Invoke(L("{0}: {1} Verweise zum anderen Testament", (_ot.Concat(_nt).First(b => b.Id == book)).Name, total));
        }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e) { _hover = null; Hint?.Invoke(""); InvalidateVisual(); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (BookAt(e.GetPosition(this)) is { } book) BookClicked?.Invoke(book);
    }
}
