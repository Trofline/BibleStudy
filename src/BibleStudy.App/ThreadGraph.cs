using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Threads beside the text (design after trekhleb.dev/bible-cross-references, MIT): every cross reference of the visible verses
/// is one fine bezier thread from its verse to the place it points to on a spine that holds the whole Bible, book by book.
/// Threads take the color of the genre they land in. Hover highlights, a click on a thread jumps there, a click on a book lists its references.
/// </summary>
public sealed class ThreadGraph : Control
{
    public sealed record Anchor(double Y, int Verse, IReadOnlyList<CrossRef> Refs);

    public event Action<CrossRef>? Jump;
    public event Action<string, IReadOnlyList<CrossRef>, Point>? Picked;
    public event Action? BuildRequested;

    public const double GraphWidth = 360;
    const double Gutter = 236, BarWidth = 8, Margin = 6;
    static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#9a9890"));
    static readonly IBrush Text = new SolidColorBrush(Color.Parse("#ecebe6"));

    sealed record Thread(double Y, double TargetY, int Genre, double Weight, CrossRef Ref, int BookIndex);
    sealed record Segment(int Book, double Top, double Bottom, int Genre);

    CrossRefGraph? _graph;
    int _chapterNode = -1;
    IReadOnlyList<Anchor> _anchors = [];
    Func<string, string> _name = s => s;
    readonly List<Thread> _threads = [];
    readonly List<Segment> _segments = [];
    double _builtHeight = -1;
    int _hoverThread = -1, _hoverBook = -1;
    bool _dirty = true;

    public void SetData(CrossRefGraph? graph, int chapterNode, IReadOnlyList<Anchor> anchors, Func<string, string> bookName)
    {
        _graph = graph; _chapterNode = chapterNode; _anchors = anchors; _name = bookName; _dirty = true;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(GraphWidth, 0);

    double YOf(double verse) => Margin + verse / _graph!.Links!.VerseCount * (Bounds.Height - 2 * Margin);

    void Build()
    {
        _dirty = false; _builtHeight = Bounds.Height;
        _threads.Clear(); _segments.Clear(); _hoverThread = -1; _hoverBook = -1;
        var links = _graph?.Links;
        if (links is null || Bounds.Height < 60) return;
        var g = _graph!;
        for (var b = 0; b < g.Books.Count; b++)
        {
            var first = g.BookStart(b); var last = first + g.Books[b].Chapters - 1;
            _segments.Add(new Segment(b, YOf(links.ChapterStart(first)), YOf(links.ChapterStart(last) + links.ChapterVerseCount(last)), Math.Min(9, (int)BookCategories.Of(g.Books[b].Book))));
        }
        foreach (var anchor in _anchors)
            foreach (var r in anchor.Refs)
            {
                var node = g.Index(r.Target.Book, r.Target.Chapter);
                if (node < 0) continue;
                var mid = (links.IndexOf(node, r.Target.Verse) + links.IndexOf(node, Math.Max(r.Target.Verse, r.EndVerse)) + 1) / 2.0;
                _threads.Add(new Thread(anchor.Y, YOf(mid), links.VerseGenre[links.IndexOf(node, r.Target.Verse)], ArcRenderer.VoteWeight(r.Score), r, g.BookIndexOf(node)));
            }
    }

    static Point Bezier(double y, double ty, double s)
    {
        var bend = Gutter * 0.55; double u = 1 - s;
        return new Point(3 * u * s * s * (Gutter - bend) + 3 * u * u * s * bend + s * s * s * (Gutter + 2), u * u * u * y + 3 * u * u * s * y + 3 * u * s * s * ty + s * s * s * ty);
    }

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (_graph?.Links is null)
        {
            var hint = new FormattedText(L("Querverweis-Daten laden ▸"), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 13, Text);
            dc.DrawText(hint, new Point(24, 24));
            return;
        }
        if (_dirty || Math.Abs(_builtHeight - Bounds.Height) > 0.5) Build();

        var emphasized = _hoverThread >= 0 ? _threads[_hoverThread] : null;
        var hasEmphasis = emphasized is not null || _hoverBook >= 0;
        var barLeft = Gutter + 2;
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < _threads.Count; i++)
            {
                var t = _threads[i];
                var hot = i == _hoverThread || t.BookIndex == _hoverBook;
                if (hot != (pass == 1)) continue; // emphasized threads on top
                var color = ArcRenderer.GenreColor(t.Genre);
                var alpha = hot ? 0.95 : hasEmphasis ? 0.06 + 0.1 * t.Weight : 0.18 + 0.5 * t.Weight;
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    var bend = Gutter * 0.55;
                    ctx.BeginFigure(new Point(0, t.Y), false);
                    ctx.CubicBezierTo(new Point(bend, t.Y), new Point(Gutter - bend, t.TargetY), new Point(barLeft, t.TargetY));
                    ctx.EndFigure(false);
                }
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(color, alpha), hot ? 1.6 : 1), geometry);
                dc.DrawRectangle(new SolidColorBrush(color, alpha), null, new Rect(barLeft - 4, t.TargetY - 0.5, 4, 1));
            }

        var labelX = barLeft + BarWidth + 5;
        foreach (var s in _segments)
            dc.DrawRectangle(new SolidColorBrush(ArcRenderer.GenreColor(s.Genre), s.Book % 2 == 0 ? 1 : 0.72), null, new Rect(barLeft, s.Top, BarWidth, Math.Max(0.5, s.Bottom - s.Top)));
        foreach (var s in _segments)
        {
            var hot = s.Book == _hoverBook;
            if (s.Bottom - s.Top < 10 && !hot) continue;
            var full = _name(_graph.Books[s.Book].Book);
            var label = new FormattedText(full, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 10, hot ? Text : Muted);
            if (label.Width > Bounds.Width - labelX - 2) label = new FormattedText(full.Length > 4 ? full[..3] + "." : full, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 10, hot ? Text : Muted);
            dc.DrawText(label, new Point(labelX, (s.Top + s.Bottom) / 2 - label.Height / 2));
        }
        if (_chapterNode >= 0)
        {
            var links = _graph.Links;
            double top = YOf(links.ChapterStart(_chapterNode)) - 2, bottom = Math.Max(YOf(links.ChapterStart(_chapterNode) + links.ChapterVerseCount(_chapterNode)) + 2, top + 4), mid = (top + bottom) / 2;
            dc.DrawRectangle(Text, null, new Rect(barLeft - 3, top, 2, bottom - top));
            var arrow = new StreamGeometry();
            using (var ctx = arrow.Open()) { ctx.BeginFigure(new Point(barLeft - 3, mid - 4), true); ctx.LineTo(new Point(barLeft - 8, mid)); ctx.LineTo(new Point(barLeft - 3, mid + 4)); ctx.EndFigure(true); }
            dc.DrawGeometry(Text, null, arrow);
        }
    }

    int HitThread(Point p)
    {
        var best = -1; var bestDistance = 4.5;
        for (var i = 0; i < _threads.Count; i++)
        {
            var t = _threads[i];
            var previous = Bezier(t.Y, t.TargetY, 0);
            for (var k = 1; k <= 14; k++)
            {
                var next = Bezier(t.Y, t.TargetY, k / 14.0);
                var d = DistanceToSegment(p, previous, next);
                if (d < bestDistance) { bestDistance = d; best = i; }
                previous = next;
            }
        }
        return best;
    }

    int HitBook(Point p)
    {
        if (p.X < Gutter) return -1;
        foreach (var s in _segments) if (p.Y >= s.Top - 1 && p.Y <= Math.Max(s.Bottom, s.Top + 3) + 1 && _threads.Any(t => t.BookIndex == s.Book)) return s.Book;
        return -1;
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
        if (_graph?.Links is null) return;
        var p = e.GetPosition(this);
        var book = HitBook(p);
        var thread = book >= 0 ? -1 : HitThread(p);
        if (thread == _hoverThread && book == _hoverBook) return;
        _hoverThread = thread; _hoverBook = book;
        Cursor = thread >= 0 || book >= 0 ? new Cursor(StandardCursorType.Hand) : null;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _hoverThread = -1; _hoverBook = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (_graph?.Links is null) { BuildRequested?.Invoke(); return; }
        var p = e.GetPosition(this);
        if (HitBook(p) is var b and >= 0)
        {
            Picked?.Invoke(L("{0} · Verweise aus den sichtbaren Versen", _name(_graph.Books[b].Book)), _threads.Where(t => t.BookIndex == b).Select(t => t.Ref).ToList(), p);
            return;
        }
        if (HitThread(p) is var i and >= 0) Jump?.Invoke(_threads[i].Ref);
    }
}
