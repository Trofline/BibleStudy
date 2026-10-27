using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Family tree of a person of the Bible: ancestors to the left, descendants to the right. Data: Theographic (CC BY-SA 4.0).</summary>
public sealed class FamilyView : UserControl
{
    public event Action? CloseRequested;
    public Func<string, Task<PersonNode>>? PersonProvider { get; set; }
    public Func<Task<IReadOnlyList<LexiconItem>>>? PeopleProvider { get; set; }

    readonly FamilyCanvas _canvas = new();
    readonly TextBox _search = new() { Watermark = L("Person suchen (englisch), z. B. David, Abraham, Ruth …"), Width = 360 };
    readonly ListBox _suggest = new() { MaxHeight = 220, IsVisible = false };
    readonly NumericUpDown _up = new() { Minimum = 0, Maximum = 8, Value = 4, FormatString = "0", Width = 100 };
    readonly NumericUpDown _down = new() { Minimum = 0, Maximum = 6, Value = 3, FormatString = "0", Width = 100 };
    readonly TextBlock _hint = new() { FontSize = 12, Margin = new(16, 2, 16, 6), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _source = new() { FontSize = 11, Margin = new(16, 4, 16, 8) };
    readonly Dictionary<string, PersonNode> _cache = [];
    IReadOnlyList<LexiconItem> _people = [];
    List<LexiconItem> _matches = [];
    string _rootId = "";
    int _generation;

    public FamilyView()
    {
        _hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _source.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        _source.Text = L("Stammbaum: Theographic Bible Metadata (CC BY-SA 4.0) – nicht alle Verwandtschaften sind überliefert.");
        var back = new Button { Content = L("← Zum Text"), Margin = new(0, 0, 12, 0) };
        back.Click += (_, _) => CloseRequested?.Invoke();
        var bar = new WrapPanel { Margin = new(16, 10, 16, 4) };
        void Add(Control c, double right = 8) { c.Margin = new(0, 0, right, 4); c.VerticalAlignment = VerticalAlignment.Center; bar.Children.Add(c); }
        Add(back); Add(new TextBlock { Text = L("Stammbaum"), FontWeight = FontWeight.SemiBold }, 14); Add(_search, 14);
        Add(new TextBlock { Text = L("Generationen davor") }); Add(_up, 12); Add(new TextBlock { Text = L("danach") }); Add(_down);
        _suggest.SelectionChanged += async (_, _) =>
        {
            if (_suggest.SelectedIndex is >= 0 and var i && i < _matches.Count) { var id = _matches[i].Id; _suggest.IsVisible = false; _search.Text = _matches[i].Name; await ShowAsync(id); }
        };
        _search.TextChanged += (_, _) =>
        {
            var q = _search.Text?.Trim() ?? "";
            if (q.Length < 2 || _people.Count == 0) { _suggest.IsVisible = false; return; }
            _matches = _people.Where(p => p.Kind == "people" && p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderByDescending(p => p.References).Take(10).ToList();
            _suggest.ItemsSource = _matches.Select(m => m.Name).ToList();
            _suggest.IsVisible = _matches.Count > 0 && _search.IsFocused;
        };
        _up.ValueChanged += async (_, _) => { if (_rootId.Length > 0) await ShowAsync(_rootId); };
        _down.ValueChanged += async (_, _) => { if (_rootId.Length > 0) await ShowAsync(_rootId); };
        _canvas.Picked += async id => await ShowAsync(id);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        DockPanel.SetDock(_suggest, Dock.Top); _suggest.Margin = new(16, 0, 16, 4); _suggest.Width = 360; _suggest.HorizontalAlignment = HorizontalAlignment.Left; root.Children.Add(_suggest);
        DockPanel.SetDock(_hint, Dock.Top); root.Children.Add(_hint);
        DockPanel.SetDock(_source, Dock.Bottom); root.Children.Add(_source);
        root.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _canvas });
        Content = root;
    }

    public async Task OpenAsync(string? personId = null)
    {
        if (_people.Count == 0 && PeopleProvider is not null)
        {
            _hint.Text = L("Personenverzeichnis wird geladen (einmalig, danach offline) …");
            try { _people = await PeopleProvider(); } catch (Exception ex) { _hint.Text = L("Nicht verfügbar: ") + ex.Message; return; }
        }
        _hint.Text = L("Tippe einen Namen ein. Ein Klick auf einen Kasten setzt die Person in die Mitte.");
        await ShowAsync(personId ?? (_rootId.Length > 0 ? _rootId : "david_994"));
    }

    async Task<PersonNode?> GetAsync(string id)
    {
        if (_cache.TryGetValue(id, out var n)) return n;
        try { return _cache[id] = await PersonProvider!(id); } catch { return null; }
    }

    async Task ShowAsync(string id)
    {
        if (id.Length == 0) return;
        var gen = ++_generation; _rootId = id;
        _hint.Text = L("Lade Verwandtschaft …");
        var root = await GetAsync(id);
        if (root is null) { _hint.Text = L("Person nicht gefunden."); return; }
        // breadth-first: fetch each generation in parallel
        async Task Expand(IEnumerable<string> ids, int depth, bool up)
        {
            var level = ids.Distinct().ToList();
            for (var d = 0; d < depth && level.Count > 0; d++)
            {
                var nodes = await Task.WhenAll(level.Select(GetAsync));
                if (gen != _generation) return;
                level = nodes.Where(n => n is not null).SelectMany(n => up ? n!.Parents.Select(p => p.Id) : n!.Children.Select(c => c.Id)).Distinct().Where(x => !_cache.ContainsKey(x)).ToList();
            }
            await Task.WhenAll(level.Select(GetAsync)); // the last generation still needs its own names
        }
        await Expand([id], (int)(_up.Value ?? 4), true);
        await Expand([id], (int)(_down.Value ?? 3), false);
        if (gen != _generation) return;
        _canvas.SetTree(Build(id, (int)(_up.Value ?? 4), true), Build(id, (int)(_down.Value ?? 3), false), id);
        _hint.Text = L("{0}{1} · {2} Eltern, {3} Kinder, {4} Partner überliefert", root.Name, Years(root), root.Parents.Count, root.Children.Count, root.Partners.Count);
    }

    static string Years(PersonNode n) => n.BirthYear is int b ? $" ({Y(b)}{(n.DeathYear is int d ? " – " + Y(d) : "")})" : "";
    static string Y(int y) => y < 0 ? $"{-y} v. Chr." : $"{y} n. Chr.";

    FamilyCanvas.Tree Build(string id, int depth, bool up, HashSet<string>? seen = null)
    {
        seen ??= [];
        _cache.TryGetValue(id, out var p);
        var node = new FamilyCanvas.Tree(id, p?.Name ?? id, p?.Gender ?? "", p is null ? "" : Years(p).Trim(), []);
        if (depth == 0 || p is null || !seen.Add(id)) return node;
        foreach (var (cid, _) in up ? p.Parents : p.Children) node.Kids.Add(Build(cid, depth - 1, up, seen));
        return node;
    }

    sealed class FamilyCanvas : Control
    {
        public sealed record Tree(string Id, string Name, string Gender, string Years, List<Tree> Kids);
        public event Action<string>? Picked;
        const double W = 176, H = 38, Gx = 54, Gy = 12;
        readonly List<(Rect Box, Tree Node, bool Root)> _boxes = [];
        readonly List<(Point From, Point To)> _lines = [];
        string _rootId = "";

        public void SetTree(Tree ancestors, Tree descendants, string rootId)
        {
            _rootId = rootId; _boxes.Clear(); _lines.Clear();
            // y positions: leaves are stacked, parents sit in the middle of their children
            double next = 20;
            double Layout(Tree t, int depth, List<(Tree, int, double)> output)
            {
                double y;
                if (t.Kids.Count == 0) { y = next; next += H + Gy; }
                else { var ys = t.Kids.Select(k => Layout(k, depth + 1, output)).ToList(); y = (ys.Min() + ys.Max()) / 2; }
                output.Add((t, depth, y));
                return y;
            }

            var a = new List<(Tree, int, double)>(); var d = new List<(Tree, int, double)>();
            next = 20; var ya = Layout(ancestors, 0, a); next = 20; var yd = Layout(descendants, 0, d);
            var rootY = Math.Max(ya, yd);
            var shiftA = rootY - ya; var shiftD = rootY - yd;
            var depthA = a.Max(t => t.Item2);
            double X(int depth, int dir) => 20 + (depthA + dir * depth) * (W + Gx);
            var pos = new Dictionary<Tree, Rect>();
            foreach (var (t, depth, y) in a) pos[t] = new Rect(X(depth, -1), y + shiftA, W, H);
            foreach (var (t, depth, y) in d) if (depth > 0) pos[t] = new Rect(X(depth, 1), y + shiftD, W, H);
            pos[descendants] = pos[ancestors];
            void Walk(Tree t, int dir)
            {
                var from = pos[t];
                foreach (var k in t.Kids)
                {
                    Walk(k, dir);
                    var to = pos[k];
                    _lines.Add(dir < 0 ? (new Point(from.Left, from.Center.Y), new Point(to.Right, to.Center.Y)) : (new Point(from.Right, from.Center.Y), new Point(to.Left, to.Center.Y)));
                }
            }
            Walk(ancestors, -1); Walk(descendants, 1);
            foreach (var (t, depth, _) in a) _boxes.Add((pos[t], t, depth == 0));
            foreach (var (t, depth, _) in d) if (depth > 0) _boxes.Add((pos[t], t, false));
            var maxX = _boxes.Max(b => b.Box.Right) + 40; var maxY = _boxes.Max(b => b.Box.Bottom) + 40;
            Width = maxX; Height = maxY;
            InvalidateVisual();
        }

        public override void Render(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            var line = new Pen(new SolidColorBrush(Color.Parse("#6b7280"), 0.7), 1.3);
            foreach (var (f, t) in _lines)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open()) { ctx.BeginFigure(f, false); var mx = (f.X + t.X) / 2; ctx.CubicBezierTo(new Point(mx, f.Y), new Point(mx, t.Y), t); ctx.EndFigure(false); }
                dc.DrawGeometry(null, line, g);
            }
            foreach (var (box, node, root) in _boxes)
            {
                var color = root ? Color.Parse("#E3B65F") : node.Gender == "Female" ? Color.Parse("#d98cb3") : Color.Parse("#5b8def");
                dc.DrawRectangle(new SolidColorBrush(color, root ? 0.9 : 0.22), new Pen(new SolidColorBrush(color), root ? 2 : 1), new RoundedRect(box, 9));
                var fg = new SolidColorBrush(root ? Color.Parse("#161A20") : Color.Parse("#e6e4dc"));
                var name = new FormattedText(node.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 12, fg) { MaxTextWidth = W - 12, MaxTextHeight = 16 };
                dc.DrawText(name, new Point(box.X + 8, box.Y + (node.Years.Length > 0 ? 3 : 10)));
                if (node.Years.Length > 0) dc.DrawText(new FormattedText(node.Years, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 9.5, fg) { MaxTextWidth = W - 12 }, new Point(box.X + 8, box.Y + 21));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var p = e.GetPosition(this);
            foreach (var (box, node, root) in _boxes) if (box.Contains(p) && !root) { Picked?.Invoke(node.Id); return; }
        }
    }
}
