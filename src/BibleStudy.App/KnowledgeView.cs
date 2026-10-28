using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>A node of the knowledge graph: a document, a Bible verse or a tag.</summary>
public sealed record KnowledgeNode(string Kind, string Label, string Key, Color Color);

/// <summary>The user's knowledge as a graph: documents, the verses they mention, their tags, and [[links]] between documents.</summary>
public sealed class KnowledgeView : UserControl
{
    public event Action? CloseRequested;
    public event Action<KnowledgeNode>? NodeOpened;

    readonly ForceCanvas _canvas = new();
    readonly TextBlock _hint = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    public KnowledgeView()
    {
        _hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        var back = new Button { Content = L("← Zum Text") };
        back.Click += (_, _) => CloseRequested?.Invoke();
        var strip = new WrapPanel { Margin = new(16, 10, 16, 6) };
        back.Margin = new(0, 0, 16, 4);
        strip.Children.Add(back);
        foreach (var (color, name) in new[] { ("#E3B65F", L("Dokument")), ("#0593e6", L("Bibelvers (Farbe nach Gattung)")), ("#9a9890", L("Tag")) })
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = new SolidColorBrush(Color.Parse(color)), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 5, 0) };
            var label = new TextBlock { Text = name, FontSize = 12, Margin = new(0, 0, 14, 0) };
            label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
            strip.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center, Children = { dot, label } });
        }
        var hintRow = new Border { Height = 24, Padding = new(16, 0), Child = _hint };
        var dock = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top); DockPanel.SetDock(hintRow, Dock.Top);
        dock.Children.Add(strip); dock.Children.Add(hintRow); dock.Children.Add(_canvas);
        Content = dock;
        _canvas.Hovered += text => _hint.Text = text;
        _canvas.Opened += n => NodeOpened?.Invoke(n);
    }

    public void SetData(IReadOnlyList<KnowledgeNode> nodes, IReadOnlyList<(int A, int B)> edges)
    {
        _canvas.SetData(nodes, edges);
        _hint.Text = nodes.Count == 0 ? L("Noch keine Dokumente. Lege im Reiter „Wissen“ Notizen an.") : L("{0} Knoten · Klick: Details · Doppelklick: öffnen · Mausrad: zoomen · ziehen: verschieben", nodes.Count);
    }
}

sealed class ForceCanvas : Control
{
    public event Action<string>? Hovered;
    public event Action<KnowledgeNode>? Opened;

    IReadOnlyList<KnowledgeNode> _nodes = [];
    IReadOnlyList<(int A, int B)> _edges = [];
    Point[] _pos = [];
    int[] _degree = [];
    int _hover = -1, _selected = -1;
    double _zoom = 1;
    Vector _pan;
    Point? _dragFrom;
    bool _dragged;

    public ForceCanvas() => ClipToBounds = true;

    static Vector Diff(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);

    public void SetData(IReadOnlyList<KnowledgeNode> nodes, IReadOnlyList<(int A, int B)> edges)
    {
        _nodes = nodes; _edges = edges; _hover = -1; _selected = -1; _zoom = 1; _pan = default;
        _degree = new int[nodes.Count];
        foreach (var (a, b) in edges) { _degree[a]++; _degree[b]++; }
        Layout();
        InvalidateVisual();
    }

    /// <summary>Fruchterman-Reingold, then stretched to the canvas.</summary>
    void Layout()
    {
        var n = _nodes.Count;
        _pos = new Point[n];
        if (n == 0) return;
        var rnd = new Random(7);
        for (var i = 0; i < n; i++) { var a = 2 * Math.PI * i / n; _pos[i] = new Point(Math.Cos(a) + (rnd.NextDouble() - 0.5) * 0.2, Math.Sin(a) + (rnd.NextDouble() - 0.5) * 0.2); }
        var k = Math.Sqrt(4.0 / n) * 1.3; var temperature = 0.35; var disp = new Vector[n];
        for (var iter = 0; iter < 280; iter++)
        {
            Array.Clear(disp);
            for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                {
                    var d = Diff(_pos[i], _pos[j]); var dist = Math.Max(d.Length, 0.001);
                    var push = d / dist * (k * k / dist); disp[i] += push; disp[j] -= push;
                }
            foreach (var (a, b) in _edges)
            {
                var d = Diff(_pos[a], _pos[b]); var dist = Math.Max(d.Length, 0.001);
                var pull = d / dist * (dist * dist / k * 0.6); disp[a] -= pull; disp[b] += pull;
            }
            for (var i = 0; i < n; i++)
            {
                disp[i] -= new Vector(_pos[i].X, _pos[i].Y) * 0.15;
                var len = disp[i].Length;
                if (len > 0) _pos[i] += disp[i] / len * Math.Min(len, temperature);
            }
            temperature *= 0.985;
        }
        double cx = _pos.Average(p => p.X), cy = _pos.Average(p => p.Y);
        var extent = Math.Max(0.01, _pos.Max(p => Math.Max(Math.Abs(p.X - cx), Math.Abs(p.Y - cy))));
        for (var i = 0; i < n; i++) _pos[i] = new Point((_pos[i].X - cx) / extent, (_pos[i].Y - cy) / extent);
    }

    Point Map(Point p) => new((Bounds.Width / 2 + p.X * (Bounds.Width / 2 - 80)) * _zoom + _pan.X, (Bounds.Height / 2 + p.Y * (Bounds.Height / 2 - 50)) * _zoom + _pan.Y);

    double Radius(int i) => (_nodes[i].Kind == "doc" ? 8 : 4.5) + 1.6 * Math.Sqrt(_degree[i]);

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var text = this.TryFindResource("BsText", ActualThemeVariant, out var t) && t is IBrush tb ? tb : Brushes.White;
        var focus = _hover >= 0 ? _hover : _selected;
        foreach (var (a, b) in _edges)
        {
            var touches = focus >= 0 && (a == focus || b == focus);
            dc.DrawLine(new Pen(new SolidColorBrush(_nodes[b].Color, touches ? 0.9 : focus >= 0 ? 0.08 : 0.3), touches ? 1.8 : 0.9), Map(_pos[a]), Map(_pos[b]));
        }
        for (var i = 0; i < _nodes.Count; i++)
        {
            var c = Map(_pos[i]); var r = Radius(i);
            dc.DrawEllipse(new SolidColorBrush(_nodes[i].Color), i == _selected ? new Pen(text, 2.5) : i == _hover ? new Pen(text, 1.5) : null, c, r, r);
            if (_nodes[i].Kind == "doc" || i == _hover || i == _selected || _nodes.Count < 40)
            {
                var label = new FormattedText(_nodes[i].Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, _nodes[i].Kind == "doc" ? 12 : 10.5, text);
                dc.DrawText(label, new Point(c.X - label.Width / 2, c.Y + r + 2));
            }
        }
    }

    int HitTest(Point p)
    {
        for (var i = _nodes.Count - 1; i >= 0; i--) if (Diff(Map(_pos[i]), p).Length <= Radius(i) + 4) return i;
        return -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_dragFrom is { } from && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (Diff(p, from).Length > 3) _dragged = true;
            if (_dragged) { _pan += Diff(p, from); _dragFrom = p; InvalidateVisual(); }
            return;
        }
        var i = HitTest(p);
        if (i == _hover) return;
        _hover = i;
        Cursor = i >= 0 ? new Cursor(StandardCursorType.Hand) : null;
        Hovered?.Invoke(i < 0 ? "" : L("{0} · {1} Verbindungen", _nodes[i].Label, _degree[i]));
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        var i = HitTest(p);
        if (i >= 0)
        {
            _selected = i; InvalidateVisual();
            if (e.ClickCount >= 2) Opened?.Invoke(_nodes[i]);
            return;
        }
        _dragFrom = p; _dragged = false;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) => _dragFrom = null;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var next = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.4, 5);
        var factor = next / _zoom;
        _pan = new Vector(p.X - (p.X - _pan.X) * factor, p.Y - (p.Y - _pan.Y) * factor);
        _zoom = next;
        InvalidateVisual();
        e.Handled = true;
    }
}
