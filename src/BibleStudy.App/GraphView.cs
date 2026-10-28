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
    readonly RefNetView _otnt = new() { IsVisible = false };
    readonly ChordView _chord = new() { IsVisible = false };
    readonly ForceGraphView _force = new() { IsVisible = false };
    readonly ToggleButton _chordBtn = new() { Content = L("Kreis") };
    readonly ToggleButton _authorBtn = new() { Content = L("Autoren") };
    readonly ToggleButton _wordBtn = new() { Content = L("Wort-Netz") };
    readonly ToggleButton _personBtn = new() { Content = L("Personen-Netz") };
    readonly ToggleButton _topicBtn = new() { Content = L("Themen-Netz") };
    readonly TextBox _netInput = new() { Width = 230, IsVisible = false };
    readonly Button _netGo = new() { Content = L("Zeigen"), Classes = { "primary" }, IsVisible = false };
    bool _promptShown;
    /// <summary>kind ("word", "person", "topic"), key, expand, status report → the nodes and edges to show (null: nothing found).</summary>
    public Func<string, string, bool, Action<string>, Task<NetData?>>? NetProvider { get; set; }
    /// <summary>A node of a word, person or topic network was asked to be opened: kind, key.</summary>
    public event Action<string, string>? NetOpen;
    readonly ToggleButton _otntBtn = new() { Content = L("AT ↔ NT") };
    readonly ComboBox _strengthBox = new() { ItemsSource = new[] { L("alle Verweise"), L("ab 10 je Buchpaar"), L("ab 30 je Buchpaar"), L("ab 100 je Buchpaar") }, SelectedIndex = 1, Width = 170, IsVisible = false };
    int _mode;   // 0 network, 1 arcs, 2 Old Testament ↔ New Testament
    public event Action<string>? OtNtBookPicked;
    readonly ToggleButton _netBtn = new() { Content = L("Netzwerk"), IsChecked = true };
    readonly ToggleButton _arcBtn = new() { Content = L("Bogen") };
    readonly ComboBox _dirBox = new() { ItemsSource = new[] { L("Von hier"), L("Hierher"), L("Beide") }, SelectedIndex = 2, Width = 110 };
    readonly Slider _countSlider = new() { Minimum = 4, Maximum = 40, Value = 14, Width = 140, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly TextBlock _countLabel = new() { Text = L("Anzahl 14") };
    readonly ComboBox _bookBox = new() { Width = 170, IsVisible = false };
    readonly Slider _arcSlider = new() { Minimum = 10000, Maximum = 344000, Value = 90000, Width = 150, IsVisible = false };
    readonly TextBlock _hint = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _detailTitle = new() { FontWeight = FontWeight.SemiBold, FontSize = 15 };
    readonly TextBlock _detailInfo = new() { FontSize = 12 };
    readonly WrapPanel _detailChips = new();
    readonly WrapPanel _detailActions = new();
    readonly Button _expandBtn = new() { Content = L("Erweitern") };
    readonly Button _centerBtn = new() { Content = L("Als Zentrum") };
    readonly Button _openBtn = new() { Content = L("Im Text öffnen") };
    readonly Button _resetBtn = new() { Content = L("Zurücksetzen") };
    readonly Border _detail = new() { CornerRadius = new(12), Padding = new(14, 10), Margin = new(16, 0, 16, 8), IsVisible = false };
    readonly StackPanel _buildPanel = new() { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460 };
    readonly TextBlock _buildText = new() { TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, IsVisible = false };
    readonly Button _buildBtn = new() { Content = L("Querverweis-Daten laden"), HorizontalAlignment = HorizontalAlignment.Left };
    bool _loadingBooks;

    public GraphView()
    {
        _hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _detailInfo.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _detailTitle.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        _detail.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        _detail.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        _detail.BorderThickness = new(1);
        _buildText.Text = L("Für den Graphen werden einmalig die Querverweise aller Kapitel geladen (etwa 1.200 kleine Dateien, danach offline). Quelle: OpenBible.info, CC BY 4.0.");
        _buildBtn.Click += (_, _) => { _buildBtn.IsEnabled = false; _progress.IsVisible = true; BuildRequested?.Invoke(); };
        _buildPanel.Children.Add(_buildText); _buildPanel.Children.Add(_buildBtn); _buildPanel.Children.Add(_progress);

        var back = new Button { Content = L("← Zum Text") };
        back.Click += (_, _) => CloseRequested?.Invoke();
        var strip = new WrapPanel { Margin = new(16, 10, 16, 6) };
        void Add(Control c, double right = 10) { c.Margin = new(0, 0, right, 4); c.VerticalAlignment = VerticalAlignment.Center; strip.Children.Add(c); }
        Add(back, 16); Add(_netBtn, 4); Add(_arcBtn, 4); Add(_otntBtn, 4); Add(_chordBtn, 4); Add(_authorBtn, 4); Add(_wordBtn, 4); Add(_personBtn, 4); Add(_topicBtn, 16); Add(_netInput, 6); Add(_netGo, 14); Add(_strengthBox); Add(_dirBox); Add(_countLabel, 6); Add(_countSlider, 14); Add(_bookBox); Add(_arcSlider, 14); Add(_resetBtn);

        _detailActions.Children.Add(_centerBtn); _detailActions.Children.Add(_expandBtn); _detailActions.Children.Add(_openBtn);
        foreach (var b in _detailActions.Children) b.Margin = new(0, 6, 8, 0);
        _detail.Child = new StackPanel { Children = { _detailTitle, _detailInfo, _detailChips, _detailActions } };

        var host = new Grid { Children = { _net, _arc, _otnt, _chord, _force, _buildPanel } };
        var legend = new WrapPanel { Margin = new(16, 4, 16, 8) };
        foreach (var (c, name) in new[] { (BookCategory.Law, L("Gesetz")), (BookCategory.History, L("Geschichte")), (BookCategory.Poetry, L("Poesie")), (BookCategory.MajorProphets, L("Große Propheten")),
                     (BookCategory.MinorProphets, L("Kleine Propheten")), (BookCategory.Gospels, L("Evangelien")), (BookCategory.Acts, L("Apostelgeschichte")), (BookCategory.Paul, L("Paulus")),
                     (BookCategory.General, L("Briefe")), (BookCategory.Revelation, L("Offenbarung")) })
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = new SolidColorBrush(HomeView.Colors[c]), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 5, 0) };
            var text = new TextBlock { Text = name, FontSize = 12, Margin = new(0, 0, 14, 0) };
            text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
            legend.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, text } });
        }
        var bottom = new StackPanel { Children = { _detail, legend } };
        var dock = new DockPanel();
        var hintRow = new Border { Height = 24, Padding = new(16, 0), Child = _hint };
        DockPanel.SetDock(strip, Dock.Top); DockPanel.SetDock(hintRow, Dock.Top); DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(strip); dock.Children.Add(hintRow); dock.Children.Add(bottom); dock.Children.Add(host);
        Content = dock;

        _netBtn.Click += (_, _) => SetView(0);
        _otntBtn.Click += (_, _) => SetView(2);
        _chordBtn.Click += (_, _) => SetView(3);
        _authorBtn.Click += (_, _) => SetView(4);
        _wordBtn.Click += (_, _) => SetView(5);
        _personBtn.Click += (_, _) => SetView(6);
        _topicBtn.Click += (_, _) => SetView(7);
        _chord.BookClicked += id => OtNtBookPicked?.Invoke(id);
        _chord.Hint += text => _hint.Text = text;
        _netGo.Click += async (_, _) => await LoadNetAsync(_netInput.Text ?? "", expand: false);
        _netInput.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await LoadNetAsync(_netInput.Text ?? "", expand: false); };
        _force.Hovered += text => { if (_mode >= 4) _hint.Text = text; };
        _force.Selected += ShowNetDetail;
        _force.Expand += async node => await LoadNetAsync(node.Key, expand: true);
        _strengthBox.SelectionChanged += (_, _) => DrawOtNt();
        _otnt.Hint += text => _hint.Text = text;
        _otnt.BookClicked += id => OtNtBookPicked?.Invoke(id);
        _arcBtn.Click += (_, _) => SetView(1);
        _dirBox.SelectionChanged += (_, _) => ResetNetwork();
        _countSlider.ValueChanged += (_, _) => { _countLabel.Text = L("Anzahl {0}", (int)_countSlider.Value); ResetNetwork(); };
        _arcSlider.ValueChanged += (_, _) => ReconfigureArcs();
        _bookBox.SelectionChanged += (_, _) => { if (!_loadingBooks) { _arcBook = _bookBox.SelectedIndex - 1; ReconfigureArcs(); } };
        _resetBtn.Click += (_, _) => { if (IsArc) { _arc.ResetView(); _arcBook = -1; _bookBox.SelectedIndex = 0; _selected = -1; _detail.IsVisible = false; ReconfigureArcs(); } else ResetNetwork(); };
        _centerBtn.Click += (_, _) => { if (_selected >= 0) { _center = _selected; ResetNetwork(); } };
        _expandBtn.Click += async (_, _) =>
        {
            if (_mode >= 5) { if (_force.SelectedNode is { } n) await LoadNetAsync(n.Key, expand: true); }
            else if (_selected >= 0 && _graph is not null) { _net.Expand(_graph, _selected, Direction, (int)_countSlider.Value); ShowDetail(_selected); }
        };
        _openBtn.Click += (_, _) =>
        {
            if (_mode >= 5) { if (_force.SelectedNode is { } n) NetOpen?.Invoke(_mode switch { 5 => "word", 6 => "person", _ => "topic" }, n.Key); }
            else OpenNode(_selected);
        };
        _net.Selected += n => { _selected = n; ShowDetail(n); };
        _net.Open += OpenNode;
        _arc.Selected += n => { _selected = n; ShowDetail(n); };
        _arc.BookPicked += b => { _arcBook = b; _bookBox.SelectedIndex = b + 1; ReconfigureArcs(); };
        _arc.Open += OpenNode;
        _arc.Hovered += text => _hint.Text = text;
        _net.Hovered += text => _hint.Text = text;
        SetView(0);
    }

    EdgeDirection Direction => _dirBox.SelectedIndex switch { 0 => EdgeDirection.Outgoing, 1 => EdgeDirection.Incoming, _ => EdgeDirection.Both };

    bool IsArc => _arcBtn.IsChecked == true;

    /// <summary>Shows the picture of the references between the testaments (also when the data arrives later).</summary>
    public void ShowOtNt() => SetView(2);

    void OpenNode(int node)
    {
        if (_graph is null || node < 0) return;
        var c = _graph.At(node);
        OpenChapter?.Invoke(c.Book, c.Chapter);
    }

    void SetView(int mode)
    {
        _mode = mode;
        var toggles = new[] { _netBtn, _arcBtn, _otntBtn, _chordBtn, _authorBtn, _wordBtn, _personBtn, _topicBtn };
        for (var i = 0; i < toggles.Length; i++) toggles[i].IsChecked = i == mode;
        var ready = _graph is not null;
        _net.IsVisible = mode == 0 && ready; _arc.IsVisible = mode == 1 && ready; _otnt.IsVisible = mode == 2 && ready; _chord.IsVisible = mode == 3 && ready;
        _force.IsVisible = (mode == 4 && ready) || mode >= 5;
        _dirBox.IsVisible = _countSlider.IsVisible = _countLabel.IsVisible = mode == 0;
        _bookBox.IsVisible = _arcSlider.IsVisible = mode == 1;
        _strengthBox.IsVisible = mode is >= 2 and <= 4;
        _netInput.IsVisible = _netGo.IsVisible = mode >= 5;
        _resetBtn.IsVisible = mode <= 1;
        _buildPanel.IsVisible = mode <= 4 && !ready && _promptShown;
        _selected = -1; _detail.IsVisible = false;
        _hint.Text = mode switch
        {
            1 => L("Kapitel anklicken: Verbindungen. Buchleiste anklicken: filtern."),
            2 => L("Jede Linie verbindet ein NT-Buch (rechts) mit einem AT-Buch (links); je dicker, desto mehr Verweise. Buch anklicken: seine Verse in der Suche."),
            3 => L("Alle Bücher im Kreis, die Bänder zeigen die Querverweise dazwischen. Buch anklicken: seine Verse in der Suche."),
            4 => L("Die Bibelschreiber (nach der traditionellen Zuschreibung, bei einigen Büchern umstritten) und die Verweise zwischen ihren Büchern. Knoten ziehen, Mausrad: zoomen."),
            5 => L("Wort eingeben: die Wörter, die oft in denselben Versen stehen (KJV mit Strong-Nummern). Mehrere mit +: nur Verse, in denen alle vorkommen. Doppelklick auf einen Knoten: mehr davon."),
            6 => L("Person eingeben: wer in denselben Kapiteln vorkommt. Mehrere mit +: nur Kapitel, in denen alle vorkommen. Doppelklick auf einen Knoten: mehr davon."),
            7 => L("Thema eingeben: verwandte Wörter und wie oft sie gemeinsam in einem Vers stehen. Doppelklick auf einen Knoten: mehr davon."),
            _ => L("Knoten anklicken: Details · Mausrad: zoomen · ziehen: verschieben"),
        };
        if (mode >= 5)
        {
            _netInput.Watermark = mode switch { 5 => L("Strong-Nummer oder Wort; mehrere mit +, z. B. Tag + Herr"), 6 => L("Person; mehrere mit +, z. B. Petrus + Johannes"), _ => L("Thema; mehrere mit +, z. B. Vergebung + Gnade") };
            _force.SetData(new NetData(), null);
            return;
        }
        if (!ready) return;
        switch (mode)
        {
            case 1: ReconfigureArcs(); break;
            case 2: DrawOtNt(); break;
            case 3: DrawChord(); break;
            case 4: DrawAuthors(); break;
            default: ResetNetwork(); break;
        }
    }

    static int Threshold(ComboBox box) => new[] { 1, 10, 30, 100 }[Math.Max(0, box.SelectedIndex)];

    /// <summary>The references between all pairs of books, added up.</summary>
    Dictionary<(string A, string B), int> BookPairs(int min)
    {
        var sums = new Dictionary<(string, string), int>();
        if (_graph is null) return sums;
        var order = _graph.Books.Select((b, i) => (b.Book, i)).ToDictionary(x => x.Book, x => x.i);
        foreach (var e in _graph.Edges)
        {
            var a = _graph.At(e.From).Book; var b = _graph.At(e.To).Book;
            if (a == b) continue;
            var key = order[a] < order[b] ? (a, b) : (b, a);
            sums[key] = sums.GetValueOrDefault(key) + e.Weight;
        }
        return sums.Where(p => p.Value >= min).ToDictionary(p => p.Key, p => p.Value);
    }

    void DrawChord()
    {
        if (_graph is null || _mode != 3) return;
        _chord.SetData(_graph.Books.Select(b => (b.Book, _bookName(b.Book))).ToList(), BookPairs(Threshold(_strengthBox)));
    }

    /// <summary>The writers of the Bible as a network: an edge for the references between books that different writers wrote.</summary>
    void DrawAuthors()
    {
        if (_graph is null || _mode != 4) return;
        var min = Threshold(_strengthBox);
        var weight = new Dictionary<(string, string), int>();
        var total = new Dictionary<string, int>();
        foreach (var ((a, b), w) in BookPairs(1))
        {
            var x = AuthorMap.Of(a); var y = AuthorMap.Of(b);
            if (x == y) continue;
            var key = string.CompareOrdinal(x, y) < 0 ? (x, y) : (y, x);
            weight[key] = weight.GetValueOrDefault(key) + w;
        }
        var data = new NetData();
        foreach (var ((x, y), w) in weight.Where(p => p.Value >= min * 3))
        {
            data.Edges.Add((x, y, w));
            total[x] = total.GetValueOrDefault(x) + w; total[y] = total.GetValueOrDefault(y) + w;
        }
        var max = Math.Max(1, total.Values.DefaultIfEmpty(1).Max());
        foreach (var (author, w) in total.OrderByDescending(t => t.Value))
            data.Nodes.Add(new NetNode(author, author, AuthorMap.BooksOf(author, _bookName), HomeView.ColorOf(AuthorMap.FirstBook(author)), 40.0 * w / max));
        _force.SetData(data, null);
    }

    async Task LoadNetAsync(string key, bool expand)
    {
        if (NetProvider is null || _mode < 5 || string.IsNullOrWhiteSpace(key)) return;
        var kind = _mode switch { 5 => "word", 6 => "person", _ => "topic" };
        _hint.Text = L("Wird geladen …");
        try
        {
            var data = await NetProvider(kind, key.Trim(), expand, status => _hint.Text = status);
            if (data is null || data.Nodes.Count == 0) { _hint.Text = L("Nichts gefunden."); return; }
            if (expand) _force.Merge(data); else _force.SetData(data, data.Nodes[0].Key);
            _hint.Text = L("{0} Knoten · Knoten anklicken: Details · Doppelklick: mehr davon · Mausrad: zoomen", data.Nodes.Count);
        }
        catch (Exception ex) { _hint.Text = L("Fehler: ") + ex.Message; }
    }

    void ShowNetDetail(NetNode? node)
    {
        if (node is null) { _detail.IsVisible = false; return; }
        _detailTitle.Text = node.Label;
        _detailInfo.Text = node.Sub ?? "";
        _detailChips.Children.Clear();
        _centerBtn.IsVisible = false;
        _expandBtn.IsVisible = _mode >= 5; _openBtn.IsVisible = _mode >= 5;
        _detail.IsVisible = true;
    }

    /// <summary>The cross references between the testaments, added up per pair of books.</summary>
    void DrawOtNt()
    {
        if (_graph is null || _mode != 2) return;
        var min = new[] { 1, 10, 30, 100 }[Math.Max(0, _strengthBox.SelectedIndex)];
        var sums = new Dictionary<(string Nt, string Ot), int>();
        foreach (var e in _graph.Edges)
        {
            var from = _graph.At(e.From).Book; var to = _graph.At(e.To).Book;
            if (BookCategories.IsNewTestament(BookCategories.Of(from)) && BookCategories.IsOldTestament(BookCategories.Of(to)))
                sums[(from, to)] = sums.GetValueOrDefault((from, to)) + e.Weight;
        }
        var count = sums.Where(p => p.Value >= min).ToDictionary(p => p.Key, p => p.Value);
        var ot = _graph.Books.Where(b => BookCategories.IsOldTestament(BookCategories.Of(b.Book))).Select(b => (b.Book, _bookName(b.Book))).ToList();
        var nt = _graph.Books.Where(b => BookCategories.IsNewTestament(BookCategories.Of(b.Book))).Select(b => (b.Book, _bookName(b.Book))).ToList();
        _otnt.SetData(ot, nt, count);
    }

    /// <summary>Called when the data is ready.</summary>
    public void SetGraph(CrossRefGraph graph, Func<string, string> bookName)
    {
        _graph = graph; _bookName = bookName;
        _buildPanel.IsVisible = false; _promptShown = false;
        _loadingBooks = true;
        _bookBox.ItemsSource = new[] { L("Alle Bücher") }.Concat(graph.Books.Select(b => bookName(b.Book))).ToList();
        _bookBox.SelectedIndex = 0; _arcBook = -1;
        _loadingBooks = false;
        SetView(_mode);
    }

    public void ShowBuildPrompt(string? message = null)
    {
        _promptShown = true; _buildPanel.IsVisible = _mode <= 4; _buildBtn.IsEnabled = true; _progress.IsVisible = false;
        if (message is not null) _buildText.Text = message;
        _net.IsVisible = _arc.IsVisible = _otnt.IsVisible = _chord.IsVisible = false;
        if (_mode == 4) _force.IsVisible = false;
    }

    public void ReportProgress(int done, int total)
    {
        _promptShown = true; _buildPanel.IsVisible = _mode <= 4; _progress.IsVisible = true; _buildBtn.IsEnabled = false;
        _progress.Maximum = total; _progress.Value = done;
        _buildText.Text = L("Lade Querverweise … {0}/{1} Kapitel", done, total);
    }

    public void FocusChapter(string book, int chapter)
    {
        if (_graph is null) return;
        var node = _graph.Index(book, chapter);
        if (node < 0) return;
        _center = node;
        if (_mode == 0) ResetNetwork();
    }

    string NodeName(int node) { var c = _graph!.At(node); return $"{_bookName(c.Book)} {c.Chapter}"; }

    void ResetNetwork()
    {
        if (_graph is null || _mode != 0) return;
        _selected = _center;
        _net.Layout(_graph, _center, Direction, (int)_countSlider.Value, _bookName);
        ShowDetail(_center);
    }

    void ReconfigureArcs()
    {
        if (_graph is null || _mode != 1) return;
        _arc.Configure(_graph, (int)_arcSlider.Value, _arcBook, _bookName);
    }

    void ShowDetail(int node)
    {
        if (_graph is null || node < 0) { _detail.IsVisible = false; return; }
        var outgoing = _graph.Neighbors(node, EdgeDirection.Outgoing);
        var incoming = _graph.Neighbors(node, EdgeDirection.Incoming);
        _detailTitle.Text = NodeName(node);
        _detailInfo.Text = L("{0} Verweise von hier · {1} Verweise hierher", outgoing.Sum(n => n.Weight), incoming.Sum(n => n.Weight));
        _detailChips.Children.Clear();
        foreach (var (other, weight) in _graph.Neighbors(node, EdgeDirection.Both).Take(10))
        {
            var chip = new Button { Content = $"{NodeName(other)} ({weight})", FontSize = 12, Padding = new(8, 2), Margin = new(0, 6, 6, 0) };
            ToolTip.SetTip(chip, L("Im Text öffnen"));
            var target = other;
            chip.Click += (_, _) => OpenNode(target);
            _detailChips.Children.Add(chip);
        }
        _centerBtn.IsVisible = _expandBtn.IsVisible = _mode == 0;
        _detail.IsVisible = true;
    }
}

/// <summary>Chapters placed by a force layout around a center; the network can grow by expanding any node.</summary>
sealed class NetworkCanvas : Control
{
    public event Action<int>? Selected, Open;
    public event Action<string>? Hovered;

    public NetworkCanvas() => ClipToBounds = true;

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
        Hovered?.Invoke(i < 0 ? "" : L("{0} · {1:0} Verbindungen im Netz", Label(i), _score[i]));
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

