using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BibleStudy.Core.Import;

namespace BibleStudy.App;

/// <summary>
/// All books on a circle (in Bible order, the arc as long as the book has references), a band between two books for the references that run
/// between them. Hovering a book lights up its bands, a click asks for its verses.
/// </summary>
sealed class ChordView : Control
{
    public event Action<string>? BookClicked;
    public event Action<string>? Hint;

    IReadOnlyList<(string Id, string Name)> _books = [];
    IReadOnlyDictionary<(string A, string B), int> _pairs = new Dictionary<(string, string), int>();
    readonly Dictionary<string, (double Start, double End)> _arcs = [];
    string? _hover;

    public ChordView() => ClipToBounds = true;

    public void SetData(IReadOnlyList<(string Id, string Name)> books, IReadOnlyDictionary<(string A, string B), int> pairs)
    {
        _books = books; _pairs = pairs;
        InvalidateVisual();
    }

    static string Abbreviation(string id) { var i = Array.IndexOf(BookData.UsfmIds, id); return i >= 0 ? BookData.GermanAbbreviations[i] : id; }

    IBrush Res(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    double Radius => Math.Max(60, Math.Min(Bounds.Width, Bounds.Height) / 2 - 56);
    Point Center => new(Bounds.Width / 2, Bounds.Height / 2);
    Point OnCircle(double angle, double r) => new(Center.X + Math.Cos(angle) * r, Center.Y + Math.Sin(angle) * r);

    void Layout()
    {
        _arcs.Clear();
        var total = new Dictionary<string, double>();
        foreach (var ((a, b), w) in _pairs) { total[a] = total.GetValueOrDefault(a) + w; total[b] = total.GetValueOrDefault(b) + w; }
        var used = _books.Where(b => total.ContainsKey(b.Id)).ToList();
        if (used.Count == 0) return;
        // a mix of "every book equal" and "by number of references": the small books stay visible and the big ones still look big
        var weights = used.Select(b => 0.35 / used.Count + 0.65 * total[b.Id] / total.Values.Sum()).ToList();
        var sum = weights.Sum();
        const double gap = 0.012;
        var span = Math.PI * 2 - gap * used.Count;
        var angle = -Math.PI / 2;
        for (var i = 0; i < used.Count; i++)
        {
            var len = span * weights[i] / sum;
            _arcs[used[i].Id] = (angle, angle + len);
            angle += len + gap;
        }
    }

    public override void Render(DrawingContext dc)
    {
        var text = Res("BsText", Brushes.White);
        var muted = Res("BsMuted", Brushes.Gray);
        Layout();
        if (_arcs.Count == 0)
        {
            dc.DrawText(new FormattedText(L("Keine Verweise in dieser Stärke."), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 14, muted), new Point(24, 20));
            return;
        }
        var r = Radius;
        var max = _pairs.Values.Max();
        foreach (var (pair, w) in _pairs.OrderBy(p => p.Value))
        {
            if (!_arcs.TryGetValue(pair.A, out var a) || !_arcs.TryGetValue(pair.B, out var b)) continue;
            var lit = _hover is null || _hover == pair.A || _hover == pair.B;
            var from = OnCircle((a.Start + a.End) / 2, r - 4); var to = OnCircle((b.Start + b.End) / 2, r - 4);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(from, false);
                ctx.CubicBezierTo(OnCircle((a.Start + a.End) / 2, r * 0.25), OnCircle((b.Start + b.End) / 2, r * 0.25), to);
                ctx.EndFigure(false);
            }
            var color = HomeView.ColorOf(_hover == pair.B ? pair.A : _hover == pair.A ? pair.B : pair.A);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(color, lit ? (_hover is null ? 0.22 : 0.7) : 0.03), 0.6 + 7 * Math.Sqrt((double)w / max)), g);
        }
        foreach (var book in _books)
        {
            if (!_arcs.TryGetValue(book.Id, out var arc)) continue;
            var active = _hover == book.Id;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(OnCircle(arc.Start, r), false);
                var steps = Math.Max(2, (int)((arc.End - arc.Start) * 40));
                for (var s = 1; s <= steps; s++) ctx.LineTo(OnCircle(arc.Start + (arc.End - arc.Start) * s / steps, r));
                ctx.EndFigure(false);
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(HomeView.ColorOf(book.Id)), active ? 10 : 6), g);
            var mid = (arc.Start + arc.End) / 2;
            if (arc.End - arc.Start > 0.07 || active)
            {
                var f = new FormattedText(Abbreviation(book.Id), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, active ? FontWeight.SemiBold : FontWeight.Normal), 11, active ? text : muted);
                var p = OnCircle(mid, r + 16);
                dc.DrawText(f, new Point(p.X - f.Width / 2, p.Y - f.Height / 2));
            }
        }
    }

    string? BookAt(Point p)
    {
        var d = p - Center;
        var dist = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        if (dist < Radius - 14 || dist > Radius + 30) return null;
        var angle = Math.Atan2(d.Y, d.X);
        if (angle < -Math.PI / 2) angle += Math.PI * 2;
        foreach (var (id, arc) in _arcs) if (angle >= arc.Start && angle <= arc.End) return id;
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
            var total = _pairs.Where(p => p.Key.A == book || p.Key.B == book).Sum(p => p.Value);
            Hint?.Invoke(L("{0}: {1} Verweise zu anderen Büchern", _books.First(b => b.Id == book).Name, total));
        }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e) { _hover = null; Hint?.Invoke(""); InvalidateVisual(); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (BookAt(e.GetPosition(this)) is { } book) BookClicked?.Invoke(book);
    }
}
