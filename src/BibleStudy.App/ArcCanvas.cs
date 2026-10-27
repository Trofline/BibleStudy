using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Arc diagram of the whole Bible, verse by verse (design and rendering after trekhleb.dev/bible-cross-references, MIT).
/// The glowing background is computed off the UI thread into a bitmap; hover and selection draw crisp arcs on top.
/// Wheel zooms, dragging pans.
/// </summary>
sealed class ArcCanvas : Control
{
    public event Action<string>? Hovered;
    public event Action<int>? Selected, BookPicked, Open;

    static readonly string[] GenreNames =
        ["Gesetz", "Geschichtsbücher", "Poesie und Weisheit", "Große Propheten", "Kleine Propheten", "Evangelien", "Apostelgeschichte", "Paulusbriefe", "Katholische Briefe", "Offenbarung"];

    CrossRefGraph? _g;
    VerseLinkSet? _links;
    Func<string, string> _name = s => s;
    int[] _ids = [];
    int _count = 150_000, _filter = -1, _hoverNode = -1, _selectedNode = -1;
    double _vs, _ve, _bmpVs, _bmpVe;
    WriteableBitmap? _bmp;
    CancellationTokenSource? _cts;
    Point? _dragFrom;
    double _dragVs;
    bool _dragged;
    int _overlayNode = -2;
    readonly List<(StreamGeometry Geometry, IBrush Brush)> _overlay = [];

    double Baseline => Bounds.Height - 58;
    double AxisWidth => Bounds.Width - 64;
    double X(double verse) => 32 + (verse - _vs) / (_ve - _vs) * AxisWidth;
    double VerseAt(double x) => _vs + (x - 32) / AxisWidth * (_ve - _vs);

    public void Configure(CrossRefGraph g, int count, int bookFilter, Func<string, string> name)
    {
        _g = g; _links = g.Links; _name = name; _count = count; _filter = bookFilter;
        if (_links is null) return;
        if (_ve <= _vs) { _vs = 0; _ve = _links.VerseCount; }
        var (bookStart, bookEnd) = bookFilter >= 0 ? BookRange(bookFilter) : (0, int.MaxValue);
        _ids = _links.ByVotes.Where(i => bookFilter < 0 || InRange(_links.Src[i], bookStart, bookEnd) || InRange(_links.TgtStart[i], bookStart, bookEnd)).Take(count).ToArray();
        _overlayNode = -2;
        Request();
        InvalidateVisual();
    }

    public void ResetView()
    {
        if (_links is null) return;
        _vs = 0; _ve = _links.VerseCount; _selectedNode = -1;
        Request(); InvalidateVisual();
    }

    static bool InRange(int verse, int start, int end) => verse >= start && verse < end;

    (int Start, int End) BookRange(int book)
    {
        var first = _g!.BookStart(book);
        var last = first + _g.Books[book].Chapters - 1;
        return (_links!.ChapterStart(first), _links.ChapterStart(last) + _links.ChapterVerseCount(last));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Request();
    }

    void Request()
    {
        if (_links is null || Bounds.Width < 100 || Bounds.Height < 100) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var layout = new ArcRenderer.Layout((int)(Bounds.Width * scale), (int)(Bounds.Height * scale), scale);
        var links = _links; var ids = _ids; double vs = _vs, ve = _ve;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(60, cts.Token);
                var pixels = ArcRenderer.Render(links, ids, vs, ve, layout, cts.Token);
                Dispatcher.UIThread.Post(() => { if (!cts.IsCancellationRequested) Apply(pixels, layout, vs, ve); });
            }
            catch (OperationCanceledException) { /* a newer render replaced this one */ }
        });
    }

    void Apply(byte[] pixels, ArcRenderer.Layout layout, double vs, double ve)
    {
        var bitmap = new WriteableBitmap(new PixelSize(layout.Width, layout.Height), new Vector(96 * layout.Scale, 96 * layout.Scale), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = bitmap.Lock())
            for (var y = 0; y < layout.Height; y++) Marshal.Copy(pixels, y * layout.Width * 4, fb.Address + y * fb.RowBytes, layout.Width * 4);
        _bmp?.Dispose();
        _bmp = bitmap; _bmpVs = vs; _bmpVe = ve;
        InvalidateVisual();
    }

    // ---- drawing ------------------------------------------------------------------------------------------------------

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(13, 13, 13)), null, new Rect(Bounds.Size));
        if (_g is null || _links is null) return;
        if (_bmp is not null)
        {
            // Until the new render is ready, show the old one transformed to the current window.
            var k = (_bmpVe - _bmpVs) / (_ve - _vs);
            var x0 = 32 + (_bmpVs - _vs) / (_ve - _vs) * AxisWidth - 32 * k;
            dc.DrawImage(_bmp, new Rect(_bmp.Size), new Rect(x0, 0, Bounds.Width * k, Bounds.Height));
        }
        DrawOverlay(dc);
        DrawAxis(dc);
    }

    void DrawAxis(DrawingContext dc)
    {
        var g = _g!; var links = _links!;
        var muted = new SolidColorBrush(Color.Parse("#9a9890"));
        var genreSpans = new Dictionary<int, (double X1, double X2)>();
        for (var b = 0; b < g.Books.Count; b++)
        {
            var (s, e) = BookRange(b);
            double x1 = X(s), x2 = X(e);
            if (x2 < 0 || x1 > Bounds.Width) continue;
            var genre = Math.Min(9, (int)BookCategories.Of(g.Books[b].Book));
            var color = ArcRenderer.GenreColor(genre);
            dc.DrawRectangle(new SolidColorBrush(color, _filter < 0 || _filter == b ? (b % 2 == 0 ? 1 : 0.72) : 0.3), null, new Rect(x1, Baseline + 6, Math.Max(0.6, x2 - x1 - 0.5), 7));
            genreSpans[genre] = genreSpans.TryGetValue(genre, out var span) ? (Math.Min(span.X1, x1), Math.Max(span.X2, x2)) : (x1, x2);
            if (x2 - x1 > 30)
            {
                var name = _name(g.Books[b].Book);
                var label = new FormattedText(name.Length * 5.2 < x2 - x1 ? name : name.Length <= 4 ? name : name[..3] + ".", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 10, muted);
                dc.DrawText(label, new Point(x1 + (x2 - x1 - label.Width) / 2, Baseline + 17));
            }
        }
        foreach (var (genre, (x1, x2)) in genreSpans)
        {
            var label = new FormattedText(GenreNames[genre].ToUpperInvariant(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 9, muted);
            var center = (Math.Max(x1, 0) + Math.Min(x2, Bounds.Width)) / 2;
            if (Math.Min(x2, Bounds.Width) - Math.Max(x1, 0) < label.Width + 24) continue;
            dc.DrawText(label, new Point(center - label.Width / 2 + 7, Baseline + 33));
            dc.DrawLine(new Pen(new SolidColorBrush(ArcRenderer.GenreColor(genre)), 2), new Point(center - label.Width / 2 - 3, Baseline + 39), new Point(center - label.Width / 2 + 3, Baseline + 39));
        }
        if (_selectedNode >= 0)
        {
            double x1 = X(links.ChapterStart(_selectedNode)), x2 = X(links.ChapterStart(_selectedNode) + links.ChapterVerseCount(_selectedNode));
            dc.DrawRectangle(Brushes.White, null, new Rect(x1, Baseline + 2, Math.Max(2, x2 - x1), 3));
        }
    }

    /// <summary>Crisp arcs of the hovered (or selected) chapter on top of the glow, colored like the glow.</summary>
    void DrawOverlay(DrawingContext dc)
    {
        var node = _hoverNode >= 0 ? _hoverNode : _selectedNode;
        if (node < 0) return;
        if (_overlayNode != node) BuildOverlay(node);
        foreach (var (geometry, brush) in _overlay) dc.DrawGeometry(null, new Pen(brush, 1.3), geometry);
    }

    void BuildOverlay(int node)
    {
        _overlayNode = node;
        _overlay.Clear();
        var links = _links!;
        int start = links.ChapterStart(node), end = start + links.ChapterVerseCount(node);
        var taken = 0;
        foreach (var id in links.ByVotes)
        {
            if (!InRange(links.Src[id], start, end) && !InRange(links.TgtStart[id], start, end)) continue;
            double a = X(links.Src[id] + 0.5), b = X((links.TgtStart[id] + links.TgtEnd[id] + 1) / 2.0);
            var radius = Math.Abs(b - a) / 2;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(a, Baseline), false);
                ctx.ArcTo(new Point(b, Baseline), new Size(Math.Max(radius, 0.5), Math.Min(radius, Baseline - 16)), 0, false, a < b ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                ctx.EndFigure(false);
            }
            var c0 = ArcRenderer.GenreColor(links.VerseGenre[links.Src[id]]); var c1 = ArcRenderer.GenreColor(links.VerseGenre[links.TgtStart[id]]);
            var alpha = 0.35 + 0.6 * ArcRenderer.VoteWeight(links.Votes[id]);
            _overlay.Add((geometry, new LinearGradientBrush
            {
                StartPoint = new RelativePoint(a, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(b == a ? a + 1 : b, 0, RelativeUnit.Absolute),
                GradientStops = [new GradientStop(Color.FromArgb((byte)(alpha * 255), c0.R, c0.G, c0.B), 0), new GradientStop(Color.FromArgb((byte)(alpha * 255), c1.R, c1.G, c1.B), 1)],
            }));
            if (++taken >= 600) break;
        }
    }

    // ---- input --------------------------------------------------------------------------------------------------------

    int NodeAt(double x)
    {
        if (_links is null) return -1;
        var verse = VerseAt(x);
        return verse < 0 || verse >= _links.VerseCount ? -1 : _links.NodeOfVerse((int)verse);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_links is null) return;
        var p = e.GetPosition(this);
        if (_dragFrom is { } from && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (Math.Abs(p.X - from.X) > 3) _dragged = true;
            if (!_dragged) return;
            var span = _ve - _vs;
            var shift = -(p.X - from.X) / AxisWidth * span;
            _vs = Math.Clamp(_dragVs + shift, 0, _links.VerseCount - span); _ve = _vs + span;
            Request(); InvalidateVisual();
            return;
        }
        var node = NodeAt(p.X);
        if (node == _hoverNode) return;
        _hoverNode = node;
        if (node < 0) Hovered?.Invoke("");
        else { var c = _g!.At(node); Hovered?.Invoke($"{_name(c.Book)} {c.Chapter}"); }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _hoverNode = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_g is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragFrom = e.GetPosition(this); _dragVs = _vs; _dragged = false;
        var node = NodeAt(_dragFrom.Value.X);
        if (node >= 0 && e.ClickCount >= 2) Open?.Invoke(node);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragFrom is { } from && !_dragged && _g is not null)
        {
            var node = NodeAt(from.X);
            if (node >= 0)
            {
                if (from.Y > Baseline + 3) BookPicked?.Invoke(_g.BookIndexOf(node));
                else { _selectedNode = node; _overlayNode = -2; Selected?.Invoke(node); InvalidateVisual(); }
            }
        }
        _dragFrom = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (_links is null) return;
        var cursor = VerseAt(e.GetPosition(this).X);
        var span = Math.Clamp((_ve - _vs) * (e.Delta.Y > 0 ? 1 / 1.25 : 1.25), 200, _links.VerseCount);
        var ratio = (cursor - _vs) / (_ve - _vs);
        _vs = Math.Clamp(cursor - ratio * span, 0, _links.VerseCount - span); _ve = _vs + span;
        _overlayNode = -2;
        Request(); InvalidateVisual();
        e.Handled = true;
    }
}
