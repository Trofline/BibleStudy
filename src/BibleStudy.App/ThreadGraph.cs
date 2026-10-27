using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>
/// Graph beside the text: from each verse a band runs to the books its cross references point to.
/// Book bars on the right are as tall as the number of references they receive from this chapter.
/// </summary>
public sealed class ThreadGraph : Control
{
    public sealed record Row(double Y, IReadOnlyList<(string Book, int Count)> Targets);

    public const double GraphWidth = 340;
    IReadOnlyList<Row> _rows = [];
    Func<string, int> _order = _ => 0;
    Func<string, string> _label = s => s;

    public void SetData(IReadOnlyList<Row> rows, Func<string, int> order, Func<string, string> label)
    {
        _rows = rows; _order = order; _label = label;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(GraphWidth, 0);

    public override void Render(DrawingContext dc)
    {
        var height = Bounds.Height;
        if (_rows.Count == 0 || height < 60) return;
        var totals = new Dictionary<string, int>();
        foreach (var t in _rows.SelectMany(r => r.Targets)) totals[t.Book] = totals.GetValueOrDefault(t.Book) + t.Count;
        var books = totals.Keys.OrderBy(_order).ToList();

        const double barX = 222, barWidth = 12, gap = 4, minBar = 14;
        var free = Math.Max(0, height - 8 - gap * (books.Count - 1) - minBar * books.Count);
        var sum = totals.Values.Sum();
        var bars = new Dictionary<string, (double Top, double Height)>();
        var y = 4.0;
        foreach (var b in books) { var h = minBar + free * totals[b] / sum; bars[b] = (y, h); y += h + gap; }

        var muted = this.TryFindResource("BsMuted", ActualThemeVariant, out var m) && m is IBrush mb ? mb : Brushes.Gray;
        foreach (var b in books)
        {
            var (top, h) = bars[b];
            dc.DrawRectangle(new SolidColorBrush(HomeView.ColorOf(b)), null, new RoundedRect(new Rect(barX, top, barWidth, h), 3));
            var name = _label(b); var label = new FormattedText($"{(name.Length > 11 ? name[..10] + "…" : name)} {totals[b]}", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 11, muted);
            dc.DrawText(label, new Point(barX + barWidth + 6, top + h / 2 - label.Height / 2));
        }

        var used = new Dictionary<string, double>();
        foreach (var row in _rows.OrderBy(r => r.Y))
            foreach (var t in row.Targets.OrderBy(t => _order(t.Book)))
            {
                var (top, h) = bars[t.Book];
                var slot = h * t.Count / totals[t.Book];
                var yt = top + used.GetValueOrDefault(t.Book) + slot / 2;
                used[t.Book] = used.GetValueOrDefault(t.Book) + slot;
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(0, row.Y), false);
                    ctx.CubicBezierTo(new Point(barX * 0.55, row.Y), new Point(barX * 0.45, yt), new Point(barX, yt));
                    ctx.EndFigure(false);
                }
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(HomeView.ColorOf(t.Book), 0.55), Math.Clamp(slot, 1, 5)), geometry);
            }
    }
}
