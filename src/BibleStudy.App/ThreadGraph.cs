using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Graph beside the text: from each verse a thin band runs to the books its cross references point to.
/// Book bars on the right are as tall as the number of references they receive from this chapter.
/// Clicking a band or a bar reports the references behind it.
/// </summary>
public sealed class ThreadGraph : Control
{
    public sealed record Row(double Y, int Verse, IReadOnlyList<(string Book, IReadOnlyList<CrossRef> Refs)> Targets);

    /// <summary>Title, references, and the click position inside this control.</summary>
    public event Action<string, IReadOnlyList<CrossRef>, Point>? Picked;

    public const double GraphWidth = 340;
    const double BarX = 222, BarWidth = 12, Gap = 4, MinBar = 14;

    sealed record Band(string Book, int Verse, IReadOnlyList<CrossRef> Refs, Point[] Samples, double Width);

    IReadOnlyList<Row> _rows = [];
    Func<string, int> _order = _ => 0;
    Func<string, string> _label = s => s;
    readonly List<Band> _bands = [];
    readonly Dictionary<string, (double Top, double Height)> _bars = [];
    readonly Dictionary<string, List<CrossRef>> _bookRefs = [];
    double _builtHeight = -1;
    int _hoverBand = -1;
    string? _hoverBar;

    public void SetData(IReadOnlyList<Row> rows, Func<string, int> order, Func<string, string> label)
    {
        _rows = rows; _order = order; _label = label; _builtHeight = -1; _hoverBand = -1; _hoverBar = null;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(GraphWidth, 0);

    void Build()
    {
        _builtHeight = Bounds.Height;
        _bands.Clear(); _bars.Clear(); _bookRefs.Clear();
        if (_rows.Count == 0 || Bounds.Height < 60) return;
        var totals = new Dictionary<string, int>();
        foreach (var t in _rows.SelectMany(r => r.Targets))
        {
            totals[t.Book] = totals.GetValueOrDefault(t.Book) + t.Refs.Count;
            if (!_bookRefs.TryGetValue(t.Book, out var list)) _bookRefs[t.Book] = list = [];
            list.AddRange(t.Refs);
        }
        var books = totals.Keys.OrderBy(_order).ToList();
        var free = Math.Max(0, Bounds.Height - 8 - Gap * (books.Count - 1) - MinBar * books.Count);
        var sum = totals.Values.Sum();
        var y = 4.0;
        foreach (var b in books) { var h = MinBar + free * totals[b] / sum; _bars[b] = (y, h); y += h + Gap; }

        var used = new Dictionary<string, double>();
        foreach (var row in _rows.OrderBy(r => r.Y))
            foreach (var t in row.Targets.OrderBy(t => _order(t.Book)))
            {
                var (top, h) = _bars[t.Book];
                var slot = h * t.Refs.Count / totals[t.Book];
                var yt = top + used.GetValueOrDefault(t.Book) + slot / 2;
                used[t.Book] = used.GetValueOrDefault(t.Book) + slot;
                var samples = new Point[17];
                for (var i = 0; i < samples.Length; i++)
                {
                    double s = i / 16.0, u = 1 - s;
                    var x = 3 * u * s * s * (BarX * 0.45) + 3 * u * u * s * (BarX * 0.55) + s * s * s * BarX;
                    samples[i] = new Point(x, u * u * u * row.Y + 3 * u * u * s * row.Y + 3 * u * s * s * yt + s * s * s * yt);
                }
                _bands.Add(new Band(t.Book, row.Verse, t.Refs, samples, Math.Clamp(slot * 0.45, 0.7, 2.2)));
            }
    }

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size)); // pointer input everywhere
        if (Math.Abs(_builtHeight - Bounds.Height) > 0.5) Build();
        if (_bands.Count == 0) return;
        var muted = this.TryFindResource("BsMuted", ActualThemeVariant, out var m) && m is IBrush mb ? mb : Brushes.Gray;
        var text = this.TryFindResource("BsText", ActualThemeVariant, out var t) && t is IBrush tb ? tb : Brushes.White;

        foreach (var (book, (top, h)) in _bars)
        {
            var hot = book == _hoverBar;
            dc.DrawRectangle(new SolidColorBrush(HomeView.ColorOf(book)), hot ? new Pen(text, 1.5) : null, new RoundedRect(new Rect(BarX, top, BarWidth, h), 3));
            var name = _label(book);
            var label = new FormattedText($"{(name.Length > 11 ? name[..10] + "…" : name)} {_bookRefs[book].Count}", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 11, hot ? text : muted);
            dc.DrawText(label, new Point(BarX + BarWidth + 6, top + h / 2 - label.Height / 2));
        }

        var dim = _hoverBand >= 0 || _hoverBar is not null;
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < _bands.Count; i++)
            {
                var band = _bands[i];
                var hot = i == _hoverBand || band.Book == _hoverBar;
                if (hot != (pass == 1)) continue; // highlighted bands are drawn last
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(band.Samples[0], false);
                    for (var k = 1; k < band.Samples.Length; k++) ctx.LineTo(band.Samples[k]);
                    ctx.EndFigure(false);
                }
                var alpha = hot ? 0.95 : dim ? 0.18 : 0.42;
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(HomeView.ColorOf(band.Book), alpha), band.Width + (hot ? 1.2 : 0)), geometry);
            }
    }

    int HitBand(Point p)
    {
        var best = -1; var bestDistance = 5.0;
        for (var i = 0; i < _bands.Count; i++)
        {
            var s = _bands[i].Samples;
            for (var k = 0; k + 1 < s.Length; k++)
            {
                var d = DistanceToSegment(p, s[k], s[k + 1]);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
        }
        return best;
    }

    string? HitBar(Point p)
    {
        if (p.X < BarX - 2) return null;
        foreach (var (book, (top, h)) in _bars) if (p.Y >= top && p.Y <= top + h) return book;
        return null;
    }

    static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        var t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double px = a.X + t * dx - p.X, py = a.Y + t * dy - p.Y;
        return Math.Sqrt(px * px + py * py);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var bar = HitBar(p);
        var band = bar is null ? HitBand(p) : -1;
        if (band == _hoverBand && bar == _hoverBar) return;
        _hoverBand = band; _hoverBar = bar;
        Cursor = band >= 0 || bar is not null ? new Cursor(StandardCursorType.Hand) : null;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _hoverBand = -1; _hoverBar = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        if (HitBar(p) is { } bar) { Picked?.Invoke($"{_label(bar)} · alle Verweise aus diesem Kapitel", _bookRefs[bar], p); return; }
        if (HitBand(p) is var i and >= 0) Picked?.Invoke($"Vers {_bands[i].Verse} → {_label(_bands[i].Book)}", _bands[i].Refs, p);
    }
}
