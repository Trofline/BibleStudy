using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

public sealed record NetNode(string Key, string Label, string? Sub, Color Color, double Weight);

/// <summary>A small network: nodes (with a size and a color) and weighted edges between them, by node key.</summary>
public sealed class NetData
{
    public List<NetNode> Nodes { get; } = [];
    public List<(string A, string B, int Weight)> Edges { get; } = [];
}

/// <summary>
/// A network that arranges itself (nodes push each other away, edges pull them together). Drag a node to move it, drag the background to pan,
/// wheel to zoom, click to select (its neighbors light up), double-click to ask for more around that node.
/// </summary>
sealed class ForceGraphView : Control
{
    public event Action<NetNode?>? Selected;
    public event Action<NetNode>? Expand;
    public event Action<string>? Hovered;

    readonly List<NetNode> _nodes = [];
    readonly Dictionary<string, int> _index = [];
    readonly List<(int A, int B, int W)> _edges = [];
    Point[] _pos = [];
    Vector[] _vel = [];
    int _selected = -1, _hover = -1, _drag = -1, _ticks;
    double _cx, _cy, _scale = 1;
    Point? _panFrom; Vector _panCenter; bool _moved;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    readonly Random _rng = new(7);
    string? _centerKey;

    public ForceGraphView()
    {
        ClipToBounds = true;
        _timer.Tick += (_, _) => Step();
    }

    public NetNode? SelectedNode => _selected >= 0 ? _nodes[_selected] : null;
    public bool Has(string key) => _index.ContainsKey(key);

    public void SetData(NetData data, string? centerKey)
    {
        _nodes.Clear(); _index.Clear(); _edges.Clear();
        _pos = []; _vel = []; _selected = -1; _hover = -1; _cx = 0; _cy = 0; _scale = 1; _centerKey = centerKey;
        Merge(data);
        Fit();
        Selected?.Invoke(null);
    }

    /// <summary>Adds nodes and edges that are not there yet; existing nodes keep their places, new ones appear next to a neighbor.</summary>
    public void Merge(NetData data)
    {
        var oldCount = _nodes.Count;
        foreach (var n in data.Nodes)
            if (!_index.ContainsKey(n.Key)) { _index[n.Key] = _nodes.Count; _nodes.Add(n); }
        foreach (var (a, b, w) in data.Edges)
            if (_index.TryGetValue(a, out var ia) && _index.TryGetValue(b, out var ib) && ia != ib && !_edges.Any(e => (e.A == ia && e.B == ib) || (e.A == ib && e.B == ia)))
                _edges.Add((ia, ib, w));
        var pos = new Point[_nodes.Count]; var vel = new Vector[_nodes.Count];
        for (var i = 0; i < oldCount; i++) { pos[i] = _pos[i]; vel[i] = _vel[i]; }
        for (var i = oldCount; i < _nodes.Count; i++)
        {
            var anchor = _edges.Where(e => e.A == i || e.B == i).Select(e => e.A == i ? e.B : e.A).Where(o => o < i).Cast<int?>().FirstOrDefault();
            var angle = _rng.NextDouble() * Math.PI * 2;
            var around = anchor is int a ? pos[a] : new Point(0, 0);
            var radius = anchor is null ? 40 + _rng.NextDouble() * 120 : 60 + _rng.NextDouble() * 30;
            pos[i] = new Point(around.X + Math.Cos(angle) * radius, around.Y + Math.Sin(angle) * radius);
        }
        _pos = pos; _vel = vel;
        _ticks = oldCount == 0 ? 0 : 150;   // a growing network settles briefly, a new one runs longer
        _timer.Start();
        InvalidateVisual();
    }

    void Fit()
    {
        if (_nodes.Count == 0) return;
        _scale = 1;
    }

    double RadiusOf(int i) => Math.Clamp(5 + 2.2 * Math.Sqrt(Math.Max(0, _nodes[i].Weight)), 5, 26);

    void Step()
    {
        var n = _nodes.Count;
        if (n == 0) { _timer.Stop(); return; }
        var cool = Math.Max(0.02, 1 - _ticks / 320.0);
        var force = new Vector[n];
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                var d = _pos[i] - _pos[j];
                var dist2 = Math.Max(30, d.X * d.X + d.Y * d.Y);
                var f = 5200 / dist2;
                var dist = Math.Sqrt(dist2);
                var v = new Vector(d.X / dist * f, d.Y / dist * f);
                force[i] += v; force[j] -= v;
            }
        var maxW = Math.Max(1, _edges.Count == 0 ? 1 : _edges.Max(e => e.W));
        foreach (var (a, b, w) in _edges)
        {
            var d = _pos[b] - _pos[a];
            var dist = Math.Max(1, Math.Sqrt(d.X * d.X + d.Y * d.Y));
            var rest = 70 + 90 * (1 - (double)w / maxW);
            var f = (dist - rest) * 0.045;
            var v = new Vector(d.X / dist * f, d.Y / dist * f);
            force[a] += v; force[b] -= v;
        }
        var energy = 0.0;
        for (var i = 0; i < n; i++)
        {
            if (i == _drag) continue;
            force[i] += new Vector(-_pos[i].X, -_pos[i].Y) * 0.012;   // gentle pull to the middle keeps loose parts near
            _vel[i] = (_vel[i] + force[i] * 0.5) * 0.82;
            var step = _vel[i] * cool;
            var len = Math.Sqrt(step.X * step.X + step.Y * step.Y);
            if (len > 18) step = step / len * 18;
            _pos[i] += step;
            energy += len;
        }
        _ticks++;
        if (_ticks > 600 || (_ticks > 60 && energy / n < 0.05)) _timer.Stop();
        InvalidateVisual();
    }

    Point ToScreen(Point p) => new((p.X - _cx) * _scale + Bounds.Width / 2, (p.Y - _cy) * _scale + Bounds.Height / 2);
    Point ToWorld(Point s) => new((s.X - Bounds.Width / 2) / _scale + _cx, (s.Y - Bounds.Height / 2) / _scale + _cy);

    IBrush Res(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    public override void Render(DrawingContext dc)
    {
        var text = Res("BsText", Brushes.White);
        var muted = Res("BsMuted", Brushes.Gray);
        if (_nodes.Count == 0)
        {
            dc.DrawText(new FormattedText(L("Noch nichts zu zeigen."), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 14, muted), new Point(24, 20));
            return;
        }
        var focus = _selected >= 0 ? _selected : _hover;
        var near = new HashSet<int>();
        if (focus >= 0) { near.Add(focus); foreach (var (a, b, _) in _edges) { if (a == focus) near.Add(b); else if (b == focus) near.Add(a); } }
        var maxW = Math.Max(1, _edges.Count == 0 ? 1 : _edges.Max(e => e.W));
        foreach (var (a, b, w) in _edges)
        {
            var lit = focus < 0 || a == focus || b == focus;
            var color = _nodes[a].Color;
            dc.DrawLine(new Pen(new SolidColorBrush(color, lit ? 0.55 : 0.08), 0.8 + 4.5 * Math.Sqrt((double)w / maxW)), ToScreen(_pos[a]), ToScreen(_pos[b]));
        }
        for (var i = 0; i < _nodes.Count; i++)
        {
            var p = ToScreen(_pos[i]);
            if (p.X < -40 || p.Y < -40 || p.X > Bounds.Width + 40 || p.Y > Bounds.Height + 40) continue;
            var lit = focus < 0 || near.Contains(i);
            var r = RadiusOf(i) * Math.Clamp(_scale, 0.7, 1.6);
            var isCenter = _nodes[i].Key == _centerKey;
            dc.DrawEllipse(new SolidColorBrush(_nodes[i].Color, lit ? 1 : 0.25), new Pen(i == _selected ? text : Brushes.Black, i == _selected ? 2.5 : isCenter ? 2 : 1), p, r, r);
            if (lit || _scale > 1.2)
            {
                var label = new FormattedText(_nodes[i].Label + (_nodes[i].Sub is { Length: > 0 } s ? "  " + s : ""), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default, FontStyle.Normal, i == focus || isCenter ? FontWeight.SemiBold : FontWeight.Normal), 12, lit ? text : muted);
                dc.DrawRectangle(new SolidColorBrush(Color.Parse("#0d1b2a"), 0.55), null, new Rect(p.X + r + 2, p.Y - label.Height / 2 - 1, label.Width + 6, label.Height + 2));
                dc.DrawText(label, new Point(p.X + r + 5, p.Y - label.Height / 2));
            }
        }
    }

    int HitTest(Point screen)
    {
        for (var i = _nodes.Count - 1; i >= 0; i--)
        {
            var p = ToScreen(_pos[i]);
            var r = Math.Max(9, RadiusOf(i) * Math.Clamp(_scale, 0.7, 1.6));
            if ((p.X - screen.X) * (p.X - screen.X) + (p.Y - screen.Y) * (p.Y - screen.Y) <= r * r) return i;
        }
        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        _moved = false;
        var hit = HitTest(p);
        if (hit >= 0)
        {
            if (e.ClickCount >= 2) { Expand?.Invoke(_nodes[hit]); return; }
            _drag = hit;
        }
        else { _panFrom = p; _panCenter = new Vector(_cx, _cy); }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_drag >= 0)
        {
            _moved = true; _pos[_drag] = ToWorld(p); _vel[_drag] = default;
            if (!_timer.IsEnabled) { _ticks = 400; _timer.Start(); }
            InvalidateVisual();
            return;
        }
        if (_panFrom is { } from)
        {
            if (Math.Abs(p.X - from.X) + Math.Abs(p.Y - from.Y) > 3) _moved = true;
            _cx = _panCenter.X - (p.X - from.X) / _scale; _cy = _panCenter.Y - (p.Y - from.Y) / _scale;
            InvalidateVisual();
            return;
        }
        var h = HitTest(p);
        if (h == _hover) return;
        _hover = h;
        Cursor = h >= 0 ? new Cursor(StandardCursorType.Hand) : null;
        Hovered?.Invoke(h < 0 ? "" : $"{_nodes[h].Label} {_nodes[h].Sub}");
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_moved)
        {
            var hit = _drag >= 0 ? _drag : HitTest(e.GetPosition(this));
            if (hit != _selected) { _selected = hit; Selected?.Invoke(hit >= 0 ? _nodes[hit] : null); InvalidateVisual(); }
        }
        _drag = -1; _panFrom = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var before = ToWorld(p);
        _scale = Math.Clamp(_scale * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.25, 4);
        _cx = before.X - (p.X - Bounds.Width / 2) / _scale; _cy = before.Y - (p.Y - Bounds.Height / 2) / _scale;
        InvalidateVisual();
        e.Handled = true;
    }
}
