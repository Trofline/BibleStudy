using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>
/// The arrows of the reading paths in the margin left of the text, as the jumps beside the code in a disassembler: from one step to the next,
/// each arrow in a lane of its own (the short ones next to the text), an arrow back up dashed. Every step has its number in a small circle.
/// It lies over the text and takes no clicks; where the verses are it asks <see cref="BoxOf"/>.
/// </summary>
public sealed class PathGutter : Control
{
    public sealed record Badge(int Verse, int Number, Color Color, string Tip);
    public sealed record Arrow(int From, int To, int Lane, Color Color);

    public const double BadgeSize = 17, LaneGap = 7, MaxLanes = 5;

    IReadOnlyList<Badge> _badges = [];
    IReadOnlyList<Arrow> _arrows = [];

    /// <summary>The box of a verse in the text (null when it is not shown).</summary>
    public Func<int, Control?> BoxOf { get; set; } = _ => null;

    public PathGutter() { IsHitTestVisible = false; ClipToBounds = false; }

    /// <summary>The room the margin needs: the circles and the lanes of the arrows; 0 when there is nothing to show.</summary>
    public static double WidthFor(int lanes, bool badges) => !badges ? 0 : BadgeSize + 8 + Math.Min(lanes, MaxLanes) * LaneGap + (lanes > 0 ? 4 : 0);

    public void Show(IReadOnlyList<Badge> badges, IReadOnlyList<Arrow> arrows)
    {
        _badges = badges; _arrows = arrows;
        InvalidateVisual();
    }

    public bool HasContent => _badges.Count > 0;

    /// <summary>Where a verse begins (its top left corner) in this control.</summary>
    (double X, double Y, double H)? Place(int verse) =>
        BoxOf(verse) is { } box && box.IsEffectivelyVisible && box.TranslatePoint(default, this) is { } p ? (p.X, p.Y, box.Bounds.Height) : null;

    public override void Render(DrawingContext ctx)
    {
        if (_badges.Count == 0) return;
        double? edge = null;   // the left edge of the text (the same for every verse)
        foreach (var b in _badges) if (Place(b.Verse) is { } p) { edge = p.X; break; }
        if (edge is not { } left) return;
        var badgeRight = left - 3; var badgeLeft = badgeRight - BadgeSize;
        double Mid(double y, double h) => y + Math.Min(h / 2, 15);

        foreach (var a in _arrows)
        {
            if (Place(a.From) is not { } f || Place(a.To) is not { } t) continue;
            var lane = Math.Min(a.Lane, (int)MaxLanes - 1);
            var x = badgeLeft - 6 - lane * LaneGap;
            var y1 = Mid(f.Y, f.H) + 4; var y2 = Mid(t.Y, t.H) - 4;   // it leaves below the middle of the circle and comes in above it
            var down = a.To > a.From;
            var pen = new Pen(new SolidColorBrush(a.Color, 0.85), 1.6, down ? null : new DashStyle([3, 2], 0), PenLineCap.Round, PenLineJoin.Round);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(badgeLeft + 2, y1), false);
                c.LineTo(new Point(x, y1));
                c.LineTo(new Point(x, y2));
                c.LineTo(new Point(badgeLeft - 1, y2));
                c.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, g);
            // the head of the arrow, pointing at the circle of the next step
            var head = new StreamGeometry();
            using (var c = head.Open())
            {
                c.BeginFigure(new Point(badgeLeft + 1, y2), true);
                c.LineTo(new Point(badgeLeft - 5, y2 - 3.5));
                c.LineTo(new Point(badgeLeft - 5, y2 + 3.5));
                c.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(a.Color), null, head);
        }

        // the circles with the number of the step; several steps on one verse sit beside each other to the left
        foreach (var group in _badges.GroupBy(b => b.Verse))
        {
            if (Place(group.Key) is not { } p) continue;
            var i = 0;
            foreach (var b in group.OrderBy(b => b.Number))
            {
                var cx = badgeLeft + BadgeSize / 2 - i * (BadgeSize - 4); var cy = Mid(p.Y, p.H);
                ctx.DrawEllipse(new SolidColorBrush(b.Color), new Pen(Brushes.White, i > 0 ? 1 : 0), new Point(cx, cy), BadgeSize / 2, BadgeSize / 2);
                var text = new FormattedText(b.Number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Inter, sans-serif", FontStyle.Normal, FontWeight.Bold), b.Number > 99 ? 8 : 10, Ink(b.Color));
                ctx.DrawText(text, new Point(cx - text.Width / 2, cy - text.Height / 2));
                i++;
            }
        }
    }

    /// <summary>Black or white on the color of the path, whichever reads better.</summary>
    static IBrush Ink(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? Brushes.Black : Brushes.White;
}
