using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Full-size cross-reference graph: a growing network around chapters, or an arc diagram of the whole Bible.</summary>
public sealed class GraphView : UserControl
{
    public event Action<string, int>? OpenChapter;
    public event Action? BuildRequested, CloseRequested;

    CrossRefGraph? _graph;
    Func<string, string> _bookName = s => s;
    int _center;
    int _arcBook = -1;
    int _selected = -1;

    readonly NetworkCanvas _net = new();
    readonly ArcCanvas _arc = new();
    readonly ToggleButton _netBtn = new() { Content = "Netzwerk", IsChecked = true };
    readonly ToggleButton _arcBtn = new() { Content = "Bogen" };
    readonly ComboBox _dirBox = new() { ItemsSource = new[] { "Von hier", "Hierher", "Beide" }, SelectedIndex = 2, Width = 110 };
    readonly Slider _countSlider = new() { Minimum = 4, Maximum = 40, Value = 14, Width = 140, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly TextBlock _countLabel = new() { Text = "Anzahl 14" };
    readonly ComboBox _bookBox = new() { Width = 170, IsVisible = false };
    readonly Slider _arcSlider = new() { Minimum = 500, Maximum = 12000, Value = 6000, Width = 150, IsVisible = false };
    readonly TextBlock _hint = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _detailTitle = new() { FontWeight = FontWeight.SemiBold, FontSize = 15 };
    readonly TextBlock _detailInfo = new() { FontSize = 12 };
    readonly WrapPanel _detailChips = new();
    readonly WrapPanel _detailActions = new();
    readonly Button _expandBtn = new() { Content = "Erweitern" };
    readonly Button _centerBtn = new() { Content = "Als Zentrum" };
    readonly Button _openBtn = new() { Content = "Im Text öffnen" };
    readonly Button _resetBtn = new() { Content = "Zurücksetzen" };
    readonly Border _detail = new() { CornerRadius = new(12), Padding = new(14, 10), Margin = new(16, 0, 16, 8), IsVisible = false };
    readonly StackPanel _buildPanel = new() { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460 };
    readonly TextBlock _buildText = new() { TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, IsVisible = false };
    readonly Button _buildBtn = new() { Content = "Querverweis-Daten laden", HorizontalAlignment = HorizontalAlignment.Left };
    bool _loadingBooks;

    public GraphView()
    {
        _hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _detailInfo.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _detailTitle.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        _detail.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        _detail.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        _detail.BorderThickness = new(1);
        _buildText.Text = "Für den Graphen werden einmalig die Querverweise aller Kapitel geladen (etwa 1.200 kleine Dateien, danach offline). Quelle: OpenBible.info, CC BY 4.0.";
        _buildBtn.Click += (_, _) => { _buildBtn.IsEnabled = false; _progress.IsVisible = true; BuildRequested?.Invoke(); };
        _buildPanel.Children.Add(_buildText); _buildPanel.Children.Add(_buildBtn); _buildPanel.Children.Add(_progress);

        var back = new Button { Content = "← Zum Text" };
        back.Click += (_, _) => CloseRequested?.Invoke();
        var strip = new WrapPanel { Margin = new(16, 10, 16, 6) };
        void Add(Control c, double right = 10) { c.Margin = new(0, 0, right, 4); c.VerticalAlignment = VerticalAlignment.Center; strip.Children.Add(c); }
        Add(back, 16); Add(_netBtn, 4); Add(_arcBtn, 16); Add(_dirBox); Add(_countLabel, 6); Add(_countSlider, 14); Add(_bookBox); Add(_arcSlider, 14); Add(_resetBtn); Add(_hint);

        _detailActions.Children.Add(_centerBtn); _detailActions.Children.Add(_expandBtn); _detailActions.Children.Add(_openBtn);
        foreach (var b in _detailActions.Children) b.Margin = new(0, 6, 8, 0);
        _detail.Child = new StackPanel { Children = { _detailTitle, _detailInfo, _detailChips, _detailActions } };

        var host = new Grid { Children = { _net, _arc, _buildPanel } };
        var legend = new WrapPanel { Margin = new(16, 4, 16, 8) };
        foreach (var (c, name) in new[] { (BookCategory.Law, "Gesetz"), (BookCategory.History, "Geschichte"), (BookCategory.Poetry, "Poesie"), (BookCategory.MajorProphets, "Große Propheten"),
                     (BookCategory.MinorProphets, "Kleine Propheten"), (BookCategory.Gospels, "Evangelien"), (BookCategory.Acts, "Apostelgeschichte"), (BookCategory.Paul, "Paulus"),
                     (BookCategory.General, "Briefe"), (BookCategory.Revelation, "Offenbarung") })
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = new SolidColorBrush(HomeView.Colors[c]), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 5, 0) };
            var text = new TextBlock { Text = name, FontSize = 12, Margin = new(0, 0, 14, 0) };
            text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
            legend.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, text } });
        }
        var bottom = new StackPanel { Children = { _detail, legend } };
        var dock = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top); DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(strip); dock.Children.Add(bottom); dock.Children.Add(host);
        Content = dock;

        _netBtn.Click += (_, _) => SetMode(arc: false);
        _arcBtn.Click += (_, _) => SetMode(arc: true);
        _dirBox.SelectionChanged += (_, _) => ResetNetwork();
        _countSlider.ValueChanged += (_, _) => { _countLabel.Text = $"Anzahl {(int)_countSlider.Value}"; ResetNetwork(); };
        _arcSlider.ValueChanged += (_, _) => ReconfigureArcs();
        _bookBox.SelectionChanged += (_, _) => { if (!_loadingBooks) { _arcBook = _bookBox.SelectedIndex - 1; ReconfigureArcs(); } };
        _resetBtn.Click += (_, _) => { if (IsArc) { _arcBook = -1; _bookBox.SelectedIndex = 0; _selected = -1; _detail.IsVisible = false; ReconfigureArcs(); } else ResetNetwork(); };
        _centerBtn.Click += (_, _) => { if (_selected >= 0) { _center = _selected; ResetNetwork(); } };
        _expandBtn.Click += (_, _) => { if (_selected >= 0 && _graph is not null) { _net.Expand(_graph, _selected, Direction, (int)_countSlider.Value); ShowDetail(_selected); } };
        _openBtn.Click += (_, _) => OpenNode(_selected);
        _net.Selected += n => { _selected = n; ShowDetail(n); };
        _net.Open += OpenNode;
        _arc.Selected += n => { _selected = n; ShowDetail(n); };
        _arc.BookPicked += b => { _arcBook = b; _bookBox.SelectedIndex = b + 1; ReconfigureArcs(); };
        _arc.Open += OpenNode;
        _arc.Hovered += text => _hint.Text = text;
        _net.Hovered += text => _hint.Text = text;
        SetMode(arc: false);
    }

    EdgeDirection Direction => _dirBox.SelectedIndex switch { 0 => EdgeDirection.Outgoing, 1 => EdgeDirection.Incoming, _ => EdgeDirection.Both };

    bool IsArc => _arcBtn.IsChecked == true;

    void OpenNode(int node)
    {
        if (_graph is null || node < 0) return;
        var c = _graph.At(node);
        OpenChapter?.Invoke(c.Book, c.Chapter);
    }

    void SetMode(bool arc)
    {
        _arcBtn.IsChecked = arc; _netBtn.IsChecked = !arc;
        _net.IsVisible = !arc && _graph is not null; _arc.IsVisible = arc && _graph is not null;
        _dirBox.IsVisible = _countSlider.IsVisible = _countLabel.IsVisible = !arc;
        _bookBox.IsVisible = _arcSlider.IsVisible = arc;
        _selected = -1; _detail.IsVisible = false;
        _hint.Text = arc ? "Kapitel anklicken: Verbindungen. Buchleiste anklicken: filtern." : "Knoten anklicken: Details · Mausrad: zoomen · ziehen: verschieben";
        if (_graph is not null) { if (arc) ReconfigureArcs(); else ResetNetwork(); }
    }

    /// <summary>Called when the data is ready.</summary>
    public void SetGraph(CrossRefGraph graph, Func<string, string> bookName)
    {
        _graph = graph; _bookName = bookName;
        _buildPanel.IsVisible = false;
        _loadingBooks = true;
        _bookBox.ItemsSource = new[] { "Alle Bücher" }.Concat(graph.Books.Select(b => bookName(b.Book))).ToList();
        _bookBox.SelectedIndex = 0; _arcBook = -1;
        _loadingBooks = false;
        SetMode(IsArc);
    }

    public void ShowBuildPrompt(string? message = null)
    {
        _buildPanel.IsVisible = true; _buildBtn.IsEnabled = true; _progress.IsVisible = false;
        if (message is not null) _buildText.Text = message;
        _net.IsVisible = _arc.IsVisible = false;
    }

    public void ReportProgress(int done, int total)
    {
        _buildPanel.IsVisible = true; _progress.IsVisible = true; _buildBtn.IsEnabled = false;
        _progress.Maximum = total; _progress.Value = done;
        _buildText.Text = $"Lade Querverweise … {done}/{total} Kapitel";
    }

    public void FocusChapter(string book, int chapter)
    {
        if (_graph is null) return;
        var node = _graph.Index(book, chapter);
        if (node < 0) return;
        _center = node;
        if (!IsArc) ResetNetwork();
    }

    string NodeName(int node) { var c = _graph!.At(node); return $"{_bookName(c.Book)} {c.Chapter}"; }

    void ResetNetwork()
    {
        if (_graph is null || IsArc) return;
        _selected = _center;
        _net.Layout(_graph, _center, Direction, (int)_countSlider.Value, _bookName);
        ShowDetail(_center);
    }

    void ReconfigureArcs()
    {
        if (_graph is null || !IsArc) return;
        _arc.Configure(_graph, (int)_arcSlider.Value, _arcBook, _bookName);
    }

    void ShowDetail(int node)
    {
        if (_graph is null || node < 0) { _detail.IsVisible = false; return; }
        var outgoing = _graph.Neighbors(node, EdgeDirection.Outgoing);
        var incoming = _graph.Neighbors(node, EdgeDirection.Incoming);
        _detailTitle.Text = NodeName(node);
        _detailInfo.Text = $"{outgoing.Sum(n => n.Weight)} Verweise von hier · {incoming.Sum(n => n.Weight)} Verweise hierher";
        _detailChips.Children.Clear();
        foreach (var (other, weight) in _graph.Neighbors(node, EdgeDirection.Both).Take(10))
        {
            var chip = new Button { Content = $"{NodeName(other)} ({weight})", FontSize = 12, Padding = new(8, 2), Margin = new(0, 6, 6, 0) };
            ToolTip.SetTip(chip, "Im Text öffnen");
            var target = other;
            chip.Click += (_, _) => OpenNode(target);
            _detailChips.Children.Add(chip);
        }
        _centerBtn.IsVisible = _expandBtn.IsVisible = !IsArc;
        _detail.IsVisible = true;
    }
}

/// <summary>Chapters placed by a force layout around a center; the network can grow by expanding any node.</summary>
sealed class NetworkCanvas : Control
{
    public event Action<int>? Selected, Open;
    public event Action<string>? Hovered;

    CrossRefGraph? _g;
    List<int> _nodes = [];
    Point[] _pos = [];
    List<(int A, int B, int W)> _edges = [];
    double[] _score = [];
    int _hover = -1, _selected = -1;
    Func<string, string> _name = s => s;
    double _zoom = 1;
    Vector _pan;
    Point? _dragFrom;
    bool _dragged;

    /// <summary>Starts a new network: the center plus its strongest neighbors.</summary>
    public void Layout(CrossRefGraph g, int center, EdgeDirection dir, int count, Func<string, string> name)
    {
        _g = g; _name = name;
        _nodes = [center, .. g.Neighbors(center, dir).Take(count).Select(n => n.Other)];
        _pos = new Point[_nodes.Count];
        for (var i = 1; i < _nodes.Count; i++)
        {
            var a = 2 * Math.PI * (i - 1) / Math.Max(1, _nodes.Count - 1);
            _pos[i] = new Point(Math.Cos(a) * 0.85, Math.Sin(a) * 0.85);
        }
        _zoom = 1; _pan = default; _selected = 0; _hover = -1;
        Relayout(300, 0.35);
    }

    /// <summary>Adds the strongest neighbors of <paramref name="node"/> that are not yet shown.</summary>
    public void Expand(CrossRefGraph g, int node, EdgeDirection dir, int count)
    {
        var at = _nodes.IndexOf(node);
        if (at < 0) return;
        var rnd = new Random(_nodes.Count);
        var positions = _pos.ToList();
        foreach (var (other, _) in g.Neighbors(node, dir).Where(n => !_nodes.Contains(n.Other)).Take(count).ToList())
        {
            _nodes.Add(other);
            positions.Add(new Point(_pos[at].X + (rnd.NextDouble() - 0.5) * 0.3, _pos[at].Y + (rnd.NextDouble() - 0.5) * 0.3));
        }
        _pos = positions.ToArray();
        Relayout(220, 0.12);
    }

    void Relayout(int iterations, double temperature)
    {
        var g = _g!;
        _edges = [];
        for (var i = 0; i < _nodes.Count; i++)
            for (var j = i + 1; j < _nodes.Count; j++)
                if (g.Weight(_nodes[i], _nodes[j], EdgeDirection.Both) is var w and > 0) _edges.Add((i, j, w));
        _score = new double[_nodes.Count];
        foreach (var (a, b, w) in _edges) { _score[a] += w; _score[b] += w; }
        ForceLayout(iterations, temperature);
        InvalidateVisual();
    }

    static Vector Diff(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Fruchterman-Reingold on a [-1,1] square, stretched to fill the canvas afterwards.</summary>
    void ForceLayout(int iterations, double temperature)
    {
        var n = _nodes.Count;
        var k = Math.Sqrt(4.0 / n) * 1.6;
        var disp = new Vector[n];
        for (var iter = 0; iter < iterations; iter++)
        {
            Array.Clear(disp);
            for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                {
                    var d = Diff(_pos[i], _pos[j]);
                    var dist = Math.Max(d.Length, 0.001);
                    var push = d / dist * (k * k / dist);
                    disp[i] += push; disp[j] -= push;
                }
            foreach (var (a, b, w) in _edges)
            {
                var d = Diff(_pos[a], _pos[b]);
                var dist = Math.Max(d.Length, 0.001);
                var pull = d / dist * (dist * dist / k * 0.1 * (1 + Math.Log(w)));
                disp[a] -= pull; disp[b] += pull;
            }
            for (var i = 0; i < n; i++)
            {
                disp[i] -= new Vector(_pos[i].X, _pos[i].Y) * 0.12;
                var len = disp[i].Length;
                if (len > 0) _pos[i] += disp[i] / len * Math.Min(len, temperature);
                _pos[i] = new Point(Math.Clamp(_pos[i].X, -1.5, 1.5), Math.Clamp(_pos[i].Y, -1.5, 1.5));
            }
            temperature *= 0.985;
        }
        double cx = _pos.Average(p => p.X), cy = _pos.Average(p => p.Y);
        var extent = Math.Max(0.01, _pos.Max(p => Math.Max(Math.Abs(p.X - cx), Math.Abs(p.Y - cy))));
        for (var i = 0; i < n; i++) _pos[i] = new Point((_pos[i].X - cx) / extent, (_pos[i].Y - cy) / extent);
    }

    Point Map(Point p) => new((Bounds.Width / 2 + p.X * (Bounds.Width / 2 - 70)) * _zoom + _pan.X, (Bounds.Height / 2 + p.Y * (Bounds.Height / 2 - 40)) * _zoom + _pan.Y);

    double Radius(int i) => (i == 0 ? 15 : 5) + 8 * Math.Sqrt(_score[i] / Math.Max(1, _score.Max())) * (i == _selected ? 1.2 : 1);

    string Label(int i) { var c = _g!.At(_nodes[i]); return $"{_name(c.Book)} {c.Chapter}"; }

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (_g is null || _nodes.Count == 0) return;
        var text = this.TryFindResource("BsText", ActualThemeVariant, out var t) && t is IBrush tb ? tb : Brushes.White;
        var maxW = Math.Max(1, _edges.DefaultIfEmpty((0, 0, 1)).Max(e => e.Item3));
        var focus = _hover >= 0 ? _hover : _selected;
        foreach (var (a, b, w) in _edges)
        {
            var strength = Math.Log(1 + w) / Math.Log(1 + maxW);
            var touches = focus >= 0 && (a == focus || b == focus);
            var color = HomeView.ColorOf(_g.At(_nodes[a]).Book);
            var alpha = touches ? 0.9 : focus >= 0 ? 0.05 + 0.15 * strength : 0.12 + 0.4 * strength;
            dc.DrawLine(new Pen(new SolidColorBrush(color, alpha), (touches ? 1.2 : 0.5) + 2 * strength), Map(_pos[a]), Map(_pos[b]));
        }
        for (var i = 0; i < _nodes.Count; i++)
        {
            var c = Map(_pos[i]);
            var color = HomeView.ColorOf(_g.At(_nodes[i]).Book);
            var radius = Radius(i);
            dc.DrawEllipse(new SolidColorBrush(color), i == _selected ? new Pen(text, 2.5) : i == _hover ? new Pen(text, 1.5) : null, c, radius, radius);
            if (i == 0 || i == _selected || i == _hover || _nodes.Count <= 28 || radius > 10)
            {
                var label = new FormattedText(Label(i), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, i == _selected ? 13 : 11, text);
                dc.DrawText(label, new Point(c.X - label.Width / 2, c.Y + radius + 2));
            }
        }
    }

    int HitTest(Point p)
    {
        for (var i = _nodes.Count - 1; i >= 0; i--)
            if (Diff(Map(_pos[i]), p).Length <= Radius(i) + 4) return i;
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
        Hovered?.Invoke(i < 0 ? "" : $"{Label(i)} · {_score[i]:0} Verbindungen im Netz");
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        var i = HitTest(p);
        if (i >= 0)
        {
            if (e.ClickCount >= 2) Open?.Invoke(_nodes[i]);
            else { _selected = i; Selected?.Invoke(_nodes[i]); InvalidateVisual(); }
            return;
        }
        _dragFrom = p; _dragged = false;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) => _dragFrom = null;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        var next = Math.Clamp(_zoom * factor, 0.4, 5);
        factor = next / _zoom;
        _pan = new Vector(p.X - (p.X - _pan.X) * factor, p.Y - (p.Y - _pan.Y) * factor); // zoom around the cursor
        _zoom = next;
        InvalidateVisual();
        e.Handled = true;
    }
}

/// <summary>All chapters on one line in Bible order; arcs connect chapters that reference each other. The base layer is cached as a bitmap.</summary>
sealed class ArcCanvas : Control
{
    public event Action<string>? Hovered;
    public event Action<int>? Selected, BookPicked, Open;

    CrossRefGraph? _g;
    Func<string, string> _name = s => s;
    readonly List<(StreamGeometry Geometry, GraphEdge Edge, double Strength)> _arcs = [];
    RenderTargetBitmap? _cache;
    int _filter = -1, _hover = -1, _selected = -1, _maxArcs;
    bool _dirty = true;

    public void Configure(CrossRefGraph g, int maxArcs, int bookFilter, Func<string, string> name)
    {
        _g = g; _name = name; _filter = bookFilter; _maxArcs = maxArcs; _dirty = true;
        InvalidateVisual();
    }

    double X(int node) => 20 + (node + 0.5) / _g!.NodeCount * (Bounds.Width - 40);
    double Baseline => Bounds.Height - 54;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _dirty = true;
    }

    void BuildArcs()
    {
        _arcs.Clear();
        var edges = (_filter < 0 ? _g!.Edges : _g!.Edges.Where(e => _g.BookIndexOf(e.From) == _filter || _g.BookIndexOf(e.To) == _filter)).Take(_maxArcs).ToList();
        var maxW = Math.Max(1, edges.DefaultIfEmpty(new GraphEdge(0, 0, 1)).Max(e => e.Weight));
        foreach (var e in edges.OrderBy(e => e.Weight)) // weak first, strong on top
        {
            double x1 = X(e.From), x2 = X(e.To), r = Math.Abs(x2 - x1) / 2;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(x1, Baseline), false);
                ctx.ArcTo(new Point(x2, Baseline), new Size(Math.Max(r, 0.5), Math.Min(r * 1.1, Baseline - 40)), 0, false, x1 < x2 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                ctx.EndFigure(false);
            }
            _arcs.Add((geometry, e, Math.Log(1 + e.Weight) / Math.Log(1 + maxW)));
        }
    }

    /// <summary>Draws arcs and book bar into a bitmap once; hovering then only repaints the bitmap plus a few highlighted arcs.</summary>
    void RebuildCache()
    {
        _dirty = false;
        _cache?.Dispose(); _cache = null;
        if (_g is null || Bounds.Width < 50 || Bounds.Height < 50) return;
        BuildArcs();
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        _cache = new RenderTargetBitmap(new PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        using var dc = _cache.CreateDrawingContext();
        var muted = this.TryFindResource("BsMuted", ActualThemeVariant, out var m) && m is IBrush mb ? mb : Brushes.Gray;
        foreach (var (geometry, edge, strength) in _arcs)
        {
            var color = HomeView.ColorOf(_g.At(edge.From).Book);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(color, 0.07 + 0.42 * strength), 0.45 + 1.1 * strength), geometry);
        }
        var slot = (Bounds.Width - 40) / _g.NodeCount;
        for (var b = 0; b < _g.Books.Count; b++)
        {
            var x1 = X(_g.BookStart(b)) - slot / 2;
            var x2 = X(_g.BookStart(b) + _g.Books[b].Chapters - 1) + slot / 2;
            dc.DrawRectangle(new SolidColorBrush(HomeView.ColorOf(_g.Books[b].Book), _filter < 0 || _filter == b ? 1 : 0.3), null, new Rect(x1, Baseline + 6, Math.Max(1, x2 - x1 - 1), 8));
            if (x2 - x1 > 26)
            {
                var name = _name(_g.Books[b].Book);
                var label = new FormattedText(name.Length <= 5 ? name : name[..4] + ".", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 10, muted);
                dc.DrawText(label, new Point(x1 + (x2 - x1 - label.Width) / 2, Baseline + 18));
            }
        }
    }

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (_g is null) return;
        if (_dirty) RebuildCache();
        if (_cache is not null) dc.DrawImage(_cache, new Rect(_cache.Size), new Rect(Bounds.Size));
        var focus = _hover >= 0 ? _hover : _selected;
        if (focus < 0) return;
        var text = this.TryFindResource("BsText", ActualThemeVariant, out var t) && t is IBrush tb ? tb : Brushes.White;
        foreach (var (geometry, edge, strength) in _arcs.Where(a => a.Edge.From == focus || a.Edge.To == focus))
            dc.DrawGeometry(null, new Pen(text, 0.8 + 1.4 * strength), geometry);
        dc.DrawLine(new Pen(text, 2), new Point(X(focus), Baseline + 2), new Point(X(focus), Baseline + 14));
    }

    int NodeAt(double x)
    {
        if (_g is null) return -1;
        var i = (int)Math.Floor((x - 20) / (Bounds.Width - 40) * _g.NodeCount);
        return i >= 0 && i < _g.NodeCount ? i : -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var node = NodeAt(e.GetPosition(this).X);
        if (node == _hover) return;
        _hover = node;
        if (node < 0) Hovered?.Invoke("");
        else { var c = _g!.At(node); Hovered?.Invoke($"{_name(c.Book)} {c.Chapter}"); }
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_g is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        var node = NodeAt(p.X);
        if (node < 0) return;
        if (e.ClickCount >= 2) { Open?.Invoke(node); return; }
        if (p.Y > Baseline + 4) { BookPicked?.Invoke(_g.BookIndexOf(node)); return; }   // book bar: filter
        _selected = node;
        Selected?.Invoke(node);
        InvalidateVisual();
    }
}
