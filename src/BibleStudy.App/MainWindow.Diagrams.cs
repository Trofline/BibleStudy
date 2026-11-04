using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>
/// Two extra pictures next to the existing views: the syntax of a verse as a tree (subject, verb, object … as branches) and the
/// cross references of a verse as a star (the verse in the middle, the strongest references around it).
/// </summary>
public partial class MainWindow
{
    // ---- syntax tree -------------------------------------------------------------------------------------------------------

    sealed class TreeNode(SyntaxLine line) { public SyntaxLine Line = line; public List<TreeNode> Kids = []; public double X; public int Level; }

    /// <summary>The outline is a list with depths: it becomes a tree, leaves side by side, parents centered above their children.</summary>
    void AddTree(SyntaxVerse data)
    {
        if (data.Outline.Count == 0) { _syntaxView.Children.Add(new TextBlock { Text = L("Für diesen Vers gibt es keine Syntaxdaten."), Classes = { "caption" } }); return; }
        var roots = new List<TreeNode>(); var stack = new List<TreeNode>();
        foreach (var line in data.Outline)
        {
            var node = new TreeNode(line);
            while (stack.Count > 0 && stack[^1].Line.Depth >= line.Depth) stack.RemoveAt(stack.Count - 1);
            if (stack.Count == 0) roots.Add(node); else stack[^1].Kids.Add(node);
            stack.Add(node);
        }
        // wrapper groups without role and text would only be empty boxes: their children move up to the parent
        List<TreeNode> Lift(List<TreeNode> nodes)
        {
            var result = new List<TreeNode>();
            foreach (var n in nodes)
            {
                n.Kids = Lift(n.Kids);
                // a clause that holds only one part of the same words ("Satz [waren] unter dem …" > "Prädikatsergänzung" with the same text) shows that once
                if (n.Line.Kind == "clause" && n.Kids is [{ Kids.Count: 0 } only] && only.Line.Kind != "clause" && only.Line.Text.Length > 0 && only.Line.Text == n.Line.Text)
                {
                    if (string.IsNullOrEmpty(n.Line.Role)) n.Line = n.Line with { Role = only.Line.Role };
                    n.Kids = [];
                }
                if (n.Line.Kind != "clause" && n.Line.Text.Length == 0 && string.IsNullOrEmpty(n.Line.Role)) result.AddRange(n.Kids);
                else result.Add(n);
            }
            return result;
        }
        roots = Lift(roots);
        const double slot = 118, boxW = 108, boxH = 62, levelH = 96;
        double next = 0; var maxLevel = 0;
        void Place(TreeNode n, int level)
        {
            n.Level = level; maxLevel = Math.Max(maxLevel, level);
            if (n.Kids.Count == 0) { n.X = next; next += slot; return; }
            foreach (var k in n.Kids) Place(k, level + 1);
            n.X = (n.Kids[0].X + n.Kids[^1].X) / 2;
        }
        foreach (var r in roots) Place(r, 0);

        var hebrew = data.Words[0].Hebrew;
        var canvas = new Canvas { Width = Math.Max(next, slot) + 8, Height = (maxLevel + 1) * levelH + 10 };
        var lineBrush = Res("BsBorder");
        void Draw(TreeNode n)
        {
            foreach (var k in n.Kids)
            {
                var a = new Point(n.X + boxW / 2, n.Level * levelH + boxH); var b = new Point(k.X + boxW / 2, k.Level * levelH);
                var mid = (a.Y + b.Y) / 2;
                var path = new Avalonia.Controls.Shapes.Path
                {
                    Stroke = lineBrush, StrokeThickness = 1.6,
                    Data = new PathGeometry { Figures = [new PathFigure { StartPoint = a, Segments = [new BezierSegment { Point1 = new Point(a.X, mid), Point2 = new Point(b.X, mid), Point3 = b }] }] },
                };
                canvas.Children.Add(path);
                Draw(k);
            }
        }
        foreach (var r in roots) Draw(r);

        void Box(TreeNode n)
        {
            var clause = n.Line.Kind == "clause";
            var label = clause ? L("Satz") + (n.Line.Role is { Length: > 0 } ? " · " + RoleLabel(n.Line.Role) : "") : RoleLabel(n.Line.Role);
            var stack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            if (label.Length > 0) stack.Children.Add(new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            if (n.Line.Text.Length > 0)
            {
                stack.Children.Add(new TextBlock { Text = hebrew ? Heb(n.Line.Text) : n.Line.Text, FontSize = hebrew ? 17 : 14, FontFamily = hebrew ? HebrewFont : ScriptureFont, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = hebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight });
                if (n.Line.Gloss.Length > 0) stack.Children.Add(new TextBlock { Text = Mean(n.Line.Gloss), FontSize = 10, Opacity = 0.75, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            }
            var box = new Border { Width = boxW, Height = boxH, CornerRadius = new(10), BorderThickness = new(clause ? 2 : 1), Padding = new(4, 2), Child = stack };
            box.Bind(Border.BackgroundProperty, this.GetResourceObservable(clause ? "BsAccentSoft" : "BsSurface"));
            if (!clause && n.Line.Role is { Length: > 0 }) { box.BorderBrush = RoleBrush(n.Line.Role); box.BorderThickness = new(2); }
            else box.Bind(Border.BorderBrushProperty, this.GetResourceObservable(clause ? "BsAccent" : "BsBorder"));
            ToolTip.SetTip(box, $"{label}  {n.Line.Text}  {Mean(n.Line.Gloss)}".Trim());
            Canvas.SetLeft(box, n.X); Canvas.SetTop(box, n.Level * levelH);
            canvas.Children.Add(box);
            foreach (var k in n.Kids) Box(k);
        }
        foreach (var r in roots) Box(r);

        // zoom with Ctrl + mouse wheel; the picture can be saved as PNG
        canvas.Background = Res("BsBackground");
        var scale = new ScaleTransform(1, 1);
        var zoomHost = new LayoutTransformControl { Child = canvas, LayoutTransform = scale };
        var scroller = new ScrollViewer { Content = zoomHost, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        scroller.PointerWheelChanged += (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            var s = Math.Clamp(scale.ScaleX * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), 0.4, 3);
            scale.ScaleX = scale.ScaleY = s; e.Handled = true;
        };
        var save = new Button { Content = L("Als Bild speichern"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 6) };
        save.Click += (_, _) => ExportControlImage(canvas, L("Syntaxbaum") + "_" + Label(new VerseRef(_curBook, _curChapter, _selected ?? 1)));
        _syntaxView.Children.Add(new TextBlock { Text = L("Strg + Mausrad: zoomen. Farbe des Rahmens = Satzrolle."), Classes = { "caption" } });
        _syntaxView.Children.Add(new WrapPanel { Children = { save, new Border { Width = 8 }, PopOutButton(() => canvas, () => L("Syntaxbaum") + " · " + Label(new VerseRef(_curBook, _curChapter, _selected ?? 1))) } });
        _syntaxView.Children.Add(scroller);
    }

    static readonly string[] RolePalette = ["#4C9F70", "#D9822B", "#5B7FD1", "#C45A8E", "#8A6BC4", "#2FA1A8", "#B8A11F", "#C9514A"];

    /// <summary>The same role always gets the same color (so the tree can be read at a glance).</summary>
    static IBrush RoleBrush(string role)
    {
        var sum = 0; foreach (var ch in role) sum = (sum * 31 + ch) & 0xFFFF;
        return new SolidColorBrush(Color.Parse(RolePalette[sum % RolePalette.Length]));
    }

    /// <summary>
    /// A picture of the control in a window of its own, with zoom and save: for graphs that are too small in the side panel
    /// (or that should stay open next to the text, on a second screen).
    /// </summary>
    void ShowImageWindow(Control c, string title)
    {
        var w = (int)Math.Ceiling(c.Bounds.Width); var h = (int)Math.Ceiling(c.Bounds.Height);
        if (w < 2 || h < 2) return;
        // 1:1 on purpose: a bitmap with a higher dpi showed up enlarged and cut off in the window (the PNG export below is not affected)
        const double k = 1.0;
        Avalonia.Media.Imaging.RenderTargetBitmap bmp;
        try { bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)(w * k), (int)(h * k)), new Vector(96 * k, 96 * k)); bmp.Render(c); }
        catch (Exception ex) { StatusText.Text = L("Fehler: ") + ex.Message; return; }
        var image = new Image { Source = bmp, Width = w * k, Height = h * k, Stretch = Stretch.Fill, RenderTransformOrigin = RelativePoint.TopLeft };
        RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        var host = new Canvas { Background = Res("BsBackground"), ClipToBounds = true, Cursor = new Cursor(StandardCursorType.SizeAll), Children = { image } };
        double zoom = 1, ox = 0, oy = 0;
        bool fitted = false;
        void Apply()
        {
            image.RenderTransform = new MatrixTransform(new Matrix(zoom, 0, 0, zoom, ox, oy));
        }
        void Fit()
        {
            if (host.Bounds.Width < 100 || host.Bounds.Height < 100) return;
            zoom = Math.Clamp(Math.Min((host.Bounds.Width - 24) / (w * k), (host.Bounds.Height - 24) / (h * k)), 0.02, 1.5 / k);   // the whole picture, in both directions
            ox = (host.Bounds.Width - w * k * zoom) / 2; oy = (host.Bounds.Height - h * k * zoom) / 2;
            fitted = true; Apply();
        }
        void ZoomAt(Point p, double f)
        {
            var z = Math.Clamp(zoom * f, 0.02, 6 / k);
            f = z / zoom;
            ox = p.X - (p.X - ox) * f; oy = p.Y - (p.Y - oy) * f; zoom = z; Apply();
        }
        // wheel zooms around the mouse, dragging moves the picture
        host.PointerWheelChanged += (_, e) => { ZoomAt(e.GetPosition(host), e.Delta.Y > 0 ? 1.12 : 1 / 1.12); e.Handled = true; };
        Point? last = null;
        host.PointerPressed += (_, e) => { if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed || e.GetCurrentPoint(host).Properties.IsMiddleButtonPressed) { last = e.GetPosition(host); e.Pointer.Capture(host); } };
        host.PointerMoved += (_, e) => { if (last is { } l) { var p = e.GetPosition(host); ox += p.X - l.X; oy += p.Y - l.Y; last = p; Apply(); } };
        host.PointerReleased += (_, e) => { last = null; e.Pointer.Capture(null); };
        host.DoubleTapped += (_, _) => Fit();
        host.SizeChanged += (_, _) => { if (!fitted) Fit(); };
        var minus = new Button { Content = "−", Classes = { "tool" }, Width = 32, Height = 30 };
        var plus = new Button { Content = "+", Classes = { "tool" }, Width = 32, Height = 30 };
        var fit = new Button { Content = L("Fit"), Classes = { "tool" }, Width = double.NaN, MinWidth = 48, Padding = new(10, 0), Height = 30 };
        var save = new Button { Content = L("Als Bild speichern"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), Height = 30 };
        var hint = new TextBlock { Text = L("Ziehen: verschieben · Mausrad: zoomen · Doppelklick: einpassen"), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
        minus.Click += (_, _) => ZoomAt(new Point(host.Bounds.Width / 2, host.Bounds.Height / 2), 1 / 1.2);
        plus.Click += (_, _) => ZoomAt(new Point(host.Bounds.Width / 2, host.Bounds.Height / 2), 1.2);
        fit.Click += (_, _) => Fit();
        save.Click += (_, _) => ExportControlImage(c, title);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(12, 8), Children = { minus, plus, fit, save, hint } };
        DockPanel.SetDock(bar, Dock.Top);
        var win = new Window { Title = title, Width = Math.Clamp(w + 60, 680, 1500), Height = Math.Clamp(h + 130, 460, 950), MinWidth = 420, MinHeight = 240, Background = Res("BsBackground"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new DockPanel { Children = { bar, host } } };
        win.Show(this);
    }

    /// <summary>Whatever big view fills the middle (graph, map, timeline, family tree) as a picture in its own window.</summary>
    void PopOutCurrentView()
    {
        var (view, name) = GraphPane.IsVisible ? ((Control)GraphPane, L("Graph")) : MapPane.IsVisible ? (MapPane, L("Karte")) : TimelinePane.IsVisible ? (TimelinePane, L("Zeitleiste")) : FamilyPane.IsVisible ? (FamilyPane, L("Stammbaum")) : (null!, "");
        if (view is null) { StatusText.Text = L("Öffne zuerst einen Graphen, die Karte, die Zeitleiste oder den Stammbaum."); return; }
        ShowImageWindow(view, name);
    }

    /// <summary>The button that opens a graph in a window of its own; <paramref name="source"/> gives the control at the time of the click.</summary>
    Button PopOutButton(Func<Control?> source, Func<string> title)
    {
        var b = new Button { Content = L("In eigenem Fenster öffnen"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 6) };
        b.Click += (_, _) => { if (source() is { } c) ShowImageWindow(c, title()); };
        return b;
    }

    /// <summary>Saves a control as a PNG picture (twice the size, sharp) into the export folder and opens it.</summary>
    void ExportControlImage(Control c, string baseName)
    {
        try
        {
            var w = (int)Math.Ceiling(c.Bounds.Width); var h = (int)Math.Ceiling(c.Bounds.Height);
            if (w < 2 || h < 2) return;
            var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(w * 2, h * 2), new Vector(192, 192));
            bmp.Render(c);
            var name = string.Concat(baseName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            Directory.CreateDirectory(ExportFolder);
            var path = System.IO.Path.Combine(ExportFolder, name + ".png");
            bmp.Save(path);
            StatusText.Text = L("Bild gespeichert: {0}", path);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { /* the path is in the status line */ }
        }
        catch (Exception ex) { StatusText.Text = L("Fehler: ") + ex.Message; }
    }

    // ---- cross reference star ------------------------------------------------------------------------------------------------

    Window? _starWindow;

    /// <summary>The verse in the middle, its strongest cross references around it; the thicker the line, the stronger the reference. A click goes there.</summary>
    async Task ShowXrefStarAsync(VerseRef r)
    {
        IReadOnlyList<CrossRef> refs;
        try { refs = (await _hello.GetCrossReferencesAsync(r.Book, r.Chapter, r.Verse)).Take(14).ToList(); }
        catch (Exception ex) { StatusText.Text = L("Fehler: ") + ex.Message; return; }
        if (refs.Count == 0) { StatusText.Text = L("Keine Querverweise gefunden."); return; }

        const double size = 640, cx = size / 2, cy = size / 2, radius = 235;
        var canvas = new Canvas { Width = size, Height = size };
        var top = Math.Max(1, refs.Max(x => x.Score));
        var hub = new Point(cx, cy);
        var spots = refs.Select((x, i) =>
        {
            var angle = -Math.PI / 2 + 2 * Math.PI * i / refs.Count;
            return (x, p: new Point(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle)));
        }).ToList();
        foreach (var (x, p) in spots)
        {
            var share = (double)x.Score / top;
            var line = new Line { StartPoint = hub, EndPoint = p, StrokeThickness = 1.2 + 4 * share, Opacity = 0.35 + 0.6 * share };
            line.Bind(Shape.StrokeProperty, this.GetResourceObservable("BsAccent"));
            canvas.Children.Add(line);
        }
        foreach (var (x, p) in spots)
        {
            var target = x.Target;
            var node = new Border { Padding = new(10, 5), CornerRadius = new(14), BorderThickness = new(1.5), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = Label(target) + (x.EndVerse > target.Verse ? "–" + x.EndVerse : ""), FontSize = 12, FontWeight = FontWeight.SemiBold } };
            node.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
            node.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsAccent"));
            node.Measure(Size.Infinity);
            Canvas.SetLeft(node, p.X - node.DesiredSize.Width / 2); Canvas.SetTop(node, p.Y - node.DesiredSize.Height / 2);
            node.PointerPressed += async (_, _) => { _starWindow?.Close(); await GoToAsync(target.Book, target.Chapter, target.Verse); };
            canvas.Children.Add(node);
        }
        var center = new Border { Padding = new(16, 10), CornerRadius = new(20), Child = new TextBlock { Text = Label(r), FontWeight = FontWeight.Bold, FontSize = 15 } };
        center.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsAccent"));
        ((TextBlock)center.Child).Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccentFg"));
        center.Measure(Size.Infinity);
        Canvas.SetLeft(center, cx - center.DesiredSize.Width / 2); Canvas.SetTop(center, cy - center.DesiredSize.Height / 2);
        canvas.Children.Add(center);

        _starWindow?.Close();
        var hint = new TextBlock { Text = L("Stern aus den wichtigsten Querverweisen. Klick auf einen Knoten: hingehen."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
        var dock = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(hint, Dock.Top);
        var saveStar = new Button { Content = L("Als Bild speichern"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 8) };
        saveStar.Click += (_, _) => ExportControlImage(canvas, L("Querverweis-Stern") + "_" + Label(r));
        DockPanel.SetDock(saveStar, Dock.Top);
        canvas.Background = Res("BsBackground");
        dock.Children.Add(hint);
        dock.Children.Add(saveStar);
        dock.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform });
        var win = new Window { Title = L("Querverweis-Stern") + " · " + Label(r), Width = 720, Height = 760, MinWidth = 380, MinHeight = 400, Background = Res("BsBackground"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock };
        win.Closed += (_, _) => _starWindow = null;
        _starWindow = win;
        win.Show(this);
    }
}
