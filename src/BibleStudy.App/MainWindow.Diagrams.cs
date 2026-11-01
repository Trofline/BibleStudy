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
            box.Bind(Border.BorderBrushProperty, this.GetResourceObservable(clause ? "BsAccent" : "BsBorder"));
            ToolTip.SetTip(box, $"{label}  {n.Line.Text}  {Mean(n.Line.Gloss)}".Trim());
            Canvas.SetLeft(box, n.X); Canvas.SetTop(box, n.Level * levelH);
            canvas.Children.Add(box);
            foreach (var k in n.Kids) Box(k);
        }
        foreach (var r in roots) Box(r);
        _syntaxView.Children.Add(new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
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
        dock.Children.Add(hint);
        dock.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform });
        var win = new Window { Title = L("Querverweis-Stern") + " · " + Label(r), Width = 720, Height = 760, MinWidth = 380, MinHeight = 400, Background = Res("BsBackground"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock };
        win.Closed += (_, _) => _starWindow = null;
        _starWindow = win;
        win.Show(this);
    }
}
