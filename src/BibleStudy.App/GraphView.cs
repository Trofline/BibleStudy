using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Full-size cross-reference graph: network around one chapter, or an arc diagram of the whole Bible.</summary>
public sealed class GraphView : UserControl
{
    public event Action<string, int>? OpenChapter;
    public event Action? BuildRequested;

    CrossRefGraph? _graph;
    Func<string, string> _bookName = s => s;
    int _center;
    int _arcBook = -1;

    readonly NetworkCanvas _net = new();
    readonly ArcCanvas _arc = new();
    readonly ToggleButton _netBtn = new() { Content = "Netzwerk", IsChecked = true };
    readonly ToggleButton _arcBtn = new() { Content = "Bogen" };
    readonly ComboBox _dirBox = new() { ItemsSource = new[] { "Von hier", "Hierher", "Beide" }, SelectedIndex = 0, Width = 110 };
    readonly Slider _countSlider = new() { Minimum = 8, Maximum = 60, Value = 24, Width = 150, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly ComboBox _bookBox = new() { Width = 170, IsVisible = false };
    readonly Slider _arcSlider = new() { Minimum = 300, Maximum = 8000, Value = 2500, Width = 150, IsVisible = false };
    readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _info = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _openBtn = new() { Content = "Im Text öffnen" };
    readonly StackPanel _buildPanel = new() { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460 };
    readonly TextBlock _buildText = new() { TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, IsVisible = false };
    readonly Button _buildBtn = new() { Content = "Querverweis-Daten laden", HorizontalAlignment = HorizontalAlignment.Left };
    bool _loadingBooks;

    public GraphView()
    {
        _info.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _buildText.Text = "Für den Graphen werden einmalig die Querverweise aller Kapitel geladen (etwa 1.200 kleine Dateien, danach offline). Quelle: OpenBible.info, CC BY 4.0.";
        _buildBtn.Click += (_, _) => { _buildBtn.IsEnabled = false; _progress.IsVisible = true; BuildRequested?.Invoke(); };
        _buildPanel.Children.Add(_buildText); _buildPanel.Children.Add(_buildBtn); _buildPanel.Children.Add(_progress);

        var strip = new WrapPanel { Margin = new(16, 10, 16, 6) };
        void Add(Control c, double right = 10) { c.Margin = new(0, 0, right, 4); c.VerticalAlignment = VerticalAlignment.Center; strip.Children.Add(c); }
        Add(_netBtn, 4); Add(_arcBtn, 16); Add(_dirBox); Add(new TextBlock { Text = "Knoten" }, 6); Add(_countSlider); Add(_bookBox); Add(_arcSlider);
        Add(_title); Add(_info); Add(_openBtn);

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
        var dock = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top); DockPanel.SetDock(legend, Dock.Bottom);
        dock.Children.Add(strip); dock.Children.Add(legend); dock.Children.Add(host);
        Content = dock;

        _netBtn.Click += (_, _) => SetMode(arc: false);
        _arcBtn.Click += (_, _) => SetMode(arc: true);
        _dirBox.SelectionChanged += (_, _) => RelayoutNetwork();
        _countSlider.ValueChanged += (_, _) => RelayoutNetwork();
        _arcSlider.ValueChanged += (_, _) => ReconfigureArcs();
        _bookBox.SelectionChanged += (_, _) => { if (!_loadingBooks) { _arcBook = _bookBox.SelectedIndex - 1; ReconfigureArcs(); } };
        _openBtn.Click += (_, _) => { if (_graph is not null) { var c = _graph.At(_center); OpenChapter?.Invoke(c.Book, c.Chapter); } };
        _net.Recenter += n => { _center = n; RelayoutNetwork(); };
        _net.Open += n => { if (_graph is not null) { var c = _graph.At(n); OpenChapter?.Invoke(c.Book, c.Chapter); } };
        _arc.Hovered += text => _info.Text = text;
        _arc.BookPicked += b => { _arcBook = b; _bookBox.SelectedIndex = b + 1; ReconfigureArcs(); };
        _arc.Open += n => { if (_graph is not null) { var c = _graph.At(n); OpenChapter?.Invoke(c.Book, c.Chapter); } };
        _net.Hovered += text => _info.Text = text;
        SetMode(arc: false);
    }

    EdgeDirection Direction => _dirBox.SelectedIndex switch { 1 => EdgeDirection.Incoming, 2 => EdgeDirection.Both, _ => EdgeDirection.Outgoing };

    bool IsArc => _arcBtn.IsChecked == true;

    void SetMode(bool arc)
    {
        _arcBtn.IsChecked = arc; _netBtn.IsChecked = !arc;
        _net.IsVisible = !arc && _graph is not null; _arc.IsVisible = arc && _graph is not null;
        _dirBox.IsVisible = _countSlider.IsVisible = !arc;
        _bookBox.IsVisible = _arcSlider.IsVisible = arc;
        _openBtn.IsVisible = !arc;
        _info.Text = arc ? "Bogen: Buch anklicken zum Filtern, Doppelklick öffnet ein Kapitel" : "Klick: neues Zentrum, Doppelklick: im Text öffnen";
        if (_graph is not null) { if (arc) ReconfigureArcs(); else RelayoutNetwork(); }
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

    public bool HasGraph => _graph is not null;

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
        if (!IsArc) RelayoutNetwork();
    }

    void RelayoutNetwork()
    {
        if (_graph is null || IsArc) return;
        var c = _graph.At(_center);
        _title.Text = $"{_bookName(c.Book)} {c.Chapter}";
        _net.Layout(_graph, _center, Direction, (int)_countSlider.Value, _bookName);
    }

    void ReconfigureArcs()
    {
        if (_graph is null || !IsArc) return;
        _title.Text = _arcBook >= 0 ? _bookName(_graph.Books[_arcBook].Book) : "Ganze Bibel";
        _arc.Configure(_graph, (int)_arcSlider.Value, _arcBook, _bookName);
    }
}

/// <summary>Chapters around one center chapter, placed by a force layout; node color = book category.</summary>
sealed class NetworkCanvas : Control
{
    public event Action<int>? Recenter, Open;
    public event Action<string>? Hovered;

    CrossRefGraph? _g;
    List<int> _nodes = [];
    Point[] _pos = [];
    List<(int A, int B, int W)> _edges = [];
    int[] _toCenter = [];
    int _hover = -1;
    Func<string, string> _name = s => s;


    public void Layout(CrossRefGraph g, int center, EdgeDirection dir, int count, Func<string, string> name)
    {
        _g = g; _name = name;
        var neighbors = g.Neighbors(center, dir).Take(count).ToList();
        _nodes = [center, .. neighbors.Select(n => n.Other)];
        _toCenter = [0, .. neighbors.Select(n => n.Weight)];
        _edges = [];
        for (var i = 0; i < _nodes.Count; i++)
            for (var j = i + 1; j < _nodes.Count; j++)
                if (g.Weight(_nodes[i], _nodes[j], EdgeDirection.Both) is var w and > 0) _edges.Add((i, j, w));
        ForceLayout();
        _hover = -1;
        InvalidateVisual();
    }

    /// <summary>Fruchterman-Reingold on a [-1,1] square; the center chapter stays in the middle.</summary>
    void ForceLayout()
    {
        var n = _nodes.Count;
        var rnd = new Random(42);
        _pos = new Point[n];
        for (var i = 1; i < n; i++)
        {
            var a = 2 * Math.PI * (i - 1) / Math.Max(1, n - 1);
            _pos[i] = new Point(Math.Cos(a) * 0.85 + (rnd.NextDouble() - 0.5) * 0.1, Math.Sin(a) * 0.85 + (rnd.NextDouble() - 0.5) * 0.1);
        }
        var k = Math.Sqrt(4.0 / n) * 1.6;
        var temperature = 0.35;
        var disp = new Vector[n];
        for (var iter = 0; iter < 300; iter++)
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
            for (var i = 1; i < n; i++)
            {
                disp[i] -= new Vector(_pos[i].X, _pos[i].Y) * 0.12;
                var len = disp[i].Length;
                if (len > 0) _pos[i] += disp[i] / len * Math.Min(len, temperature);
                _pos[i] = new Point(Math.Clamp(_pos[i].X, -1, 1), Math.Clamp(_pos[i].Y, -1, 1));
            }
            temperature *= 0.985;
        }
        // Stretch the result to use the whole canvas (the layout itself only cares about relative distances).
        var extent = _pos.Skip(1).DefaultIfEmpty(new Point(1, 1)).Max(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
        if (extent > 0.01) for (var i = 1; i < n; i++) _pos[i] = new Point(_pos[i].X / extent, _pos[i].Y / extent);
    }

    static Vector Diff(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);

    Point Map(Point p) => new(Bounds.Width / 2 + p.X * (Bounds.Width / 2 - 70), Bounds.Height / 2 + p.Y * (Bounds.Height / 2 - 40));

    double Radius(int i) => i == 0 ? 15 : 5 + 9 * Math.Sqrt(_toCenter[i] / (double)Math.Max(1, _toCenter.Skip(1).DefaultIfEmpty(1).Max()));

    string Label(int i) { var c = _g!.At(_nodes[i]); return $"{_name(c.Book)} {c.Chapter}"; }

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size)); // receive pointer input everywhere
        if (_g is null || _nodes.Count == 0) return;
        var text = this.TryFindResource("BsText", ActualThemeVariant, out var t) && t is IBrush tb ? tb : Brushes.White;
        var maxW = Math.Max(1, _edges.DefaultIfEmpty((0, 0, 1)).Max(e => e.Item3));
        foreach (var (a, b, w) in _edges)
        {
            var strength = Math.Log(1 + w) / Math.Log(1 + maxW);
            var touches = _hover >= 0 && (a == _hover || b == _hover);
            var color = HomeView.ColorOf(_g.At(_nodes[a]).Book);
            dc.DrawLine(new Pen(new SolidColorBrush(color, touches ? 0.95 : 0.12 + 0.4 * strength), (touches ? 1.5 : 0.6) + 2.2 * strength), Map(_pos[a]), Map(_pos[b]));
        }
        for (var i = 0; i < _nodes.Count; i++)
        {
            var c = Map(_pos[i]);
            var color = HomeView.ColorOf(_g.At(_nodes[i]).Book);
            dc.DrawEllipse(new SolidColorBrush(color), i == _hover ? new Pen(text, 2) : null, c, Radius(i), Radius(i));
            if (i == 0 || i == _hover || _nodes.Count <= 30 || Radius(i) > 9)
            {
                var label = new FormattedText(Label(i), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, i == 0 ? 13 : 11, text);
                dc.DrawText(label, new Point(c.X - label.Width / 2, c.Y + Radius(i) + 2));
            }
        }
    }

    int HitTest(Point p)
    {
        for (var i = _nodes.Count - 1; i >= 0; i--)
        {
            var c = Map(_pos[i]);
            if (Diff(c, p).Length <= Radius(i) + 4) return i;
        }
        return -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var i = HitTest(e.GetPosition(this));
        if (i == _hover) return;
        _hover = i;
        Cursor = i >= 0 ? new Cursor(StandardCursorType.Hand) : null;
        Hovered?.Invoke(i < 0 ? "" : i == 0 ? $"{Label(0)} (Zentrum)" : $"{Label(i)} · {_toCenter[i]} Verbindungen zum Zentrum");
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var i = HitTest(e.GetPosition(this));
        if (i < 0) return;
        if (e.ClickCount >= 2) Open?.Invoke(_nodes[i]); else if (i > 0) Recenter?.Invoke(_nodes[i]);
    }
}

/// <summary>All chapters on one line in Bible order; arcs connect chapters that reference each other.</summary>
sealed class ArcCanvas : Control
{
    public event Action<string>? Hovered;
    public event Action<int>? BookPicked, Open;

    CrossRefGraph? _g;
    Func<string, string> _name = s => s;
    readonly List<(StreamGeometry Geometry, GraphEdge Edge, double Strength)> _arcs = [];
    int _filter = -1, _hover = -1;
    double _lastWidth, _lastHeight;
    int _maxArcs;

    public void Configure(CrossRefGraph g, int maxArcs, int bookFilter, Func<string, string> name)
    {
        _g = g; _name = name; _filter = bookFilter; _maxArcs = maxArcs;
        Rebuild();
        InvalidateVisual();
    }

    double X(int node) => 20 + (node + 0.5) / _g!.NodeCount * (Bounds.Width - 40);
    double Baseline => Bounds.Height - 54;

    void Rebuild()
    {
        _arcs.Clear();
        if (_g is null || Bounds.Width < 50) return;
        _lastWidth = Bounds.Width; _lastHeight = Bounds.Height;
        var edges = (_filter < 0 ? _g.Edges : _g.Edges.Where(e => _g.BookIndexOf(e.From) == _filter || _g.BookIndexOf(e.To) == _filter)).Take(_maxArcs).ToList();
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

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_g is not null) { Rebuild(); InvalidateVisual(); }
    }

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (_g is null) return;
        if (Math.Abs(_lastWidth - Bounds.Width) > 1 || Math.Abs(_lastHeight - Bounds.Height) > 1) Rebuild();
        var muted = this.TryFindResource("BsMuted", ActualThemeVariant, out var m) && m is IBrush mb ? mb : Brushes.Gray;
        var text = this.TryFindResource("BsText", ActualThemeVariant, out var t) && t is IBrush tb ? tb : Brushes.White;
        foreach (var (geometry, edge, strength) in _arcs)
        {
            var related = _hover >= 0 && (edge.From == _hover || edge.To == _hover);
            if (_hover >= 0 && related) continue;
            var color = HomeView.ColorOf(_g.At(edge.From).Book);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(color, (_hover >= 0 ? 0.05 : 0.1) + 0.5 * strength), 0.5 + 1.5 * strength), geometry);
        }
        foreach (var (geometry, edge, strength) in _arcs.Where(a => _hover >= 0 && (a.Edge.From == _hover || a.Edge.To == _hover)))
            dc.DrawGeometry(null, new Pen(text, 0.8 + 1.6 * strength), geometry);

        for (var b = 0; b < _g.Books.Count; b++)
        {
            var x1 = X(_g.BookStart(b)) - 0.5 * (Bounds.Width - 40) / _g.NodeCount;
            var x2 = X(_g.BookStart(b) + _g.Books[b].Chapters - 1) + 0.5 * (Bounds.Width - 40) / _g.NodeCount;
            var color = HomeView.ColorOf(_g.Books[b].Book);
            dc.DrawRectangle(new SolidColorBrush(color, _filter < 0 || _filter == b ? 1 : 0.35), null, new Rect(x1, Baseline + 6, Math.Max(1, x2 - x1 - 1), 8));
            if (x2 - x1 > 26)
            {
                var label = new FormattedText(Short(_name(_g.Books[b].Book)), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 10, muted);
                dc.DrawText(label, new Point(x1 + (x2 - x1 - label.Width) / 2, Baseline + 18));
            }
        }
    }

    static string Short(string name) => name.Length <= 5 ? name : name[..4] + ".";

    int NodeAt(double x)
    {
        if (_g is null) return -1;
        var i = (int)Math.Floor((x - 20) / (Bounds.Width - 40) * _g.NodeCount);
        return i >= 0 && i < _g.NodeCount ? i : -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var node = p.Y > Baseline - 4 ? NodeAt(p.X) : -1;
        if (node == _hover) return;
        _hover = node;
        if (node < 0) Hovered?.Invoke("");
        else
        {
            var c = _g!.At(node);
            var total = _arcs.Where(a => a.Edge.From == node || a.Edge.To == node).Sum(a => a.Edge.Weight);
            Hovered?.Invoke($"{_name(c.Book)} {c.Chapter} · {total} Verbindungen in der Ansicht");
        }
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_g is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var node = NodeAt(e.GetPosition(this).X);
        if (node < 0) return;
        if (e.ClickCount >= 2) Open?.Invoke(node); else BookPicked?.Invoke(_g.BookIndexOf(node));
    }
}
