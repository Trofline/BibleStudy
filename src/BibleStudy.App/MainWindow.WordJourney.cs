using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// The journey of a word through the Bible: a path through all books in order; a light travels along it and every book where the
/// word occurs lights up as it arrives (bigger circle = more occurrences). A click on a place shows only that book in the list.
/// </summary>
public partial class MainWindow
{
    Window? _journey;

    void ShowWordJourney(TranslationInfo t)
    {
        var perBook = _concOcc.Where(o => _concForm is null || CleanWord(o.Word) == _concForm).GroupBy(o => o.Ref.Book).ToDictionary(g => g.Key, g => g.Count());
        if (perBook.Count == 0 || _books.Count < 2) return;

        const double width = 1000, height = 360, margin = 40;
        var canvas = new Canvas { Width = width, Height = height };
        Point At(int i) => new(margin + i * (width - 2 * margin) / (_books.Count - 1), height / 2 + Math.Sin(i * 0.33) * 70);

        // the road through all books
        var figure = new PathFigure { StartPoint = At(0) };
        for (var i = 1; i < _books.Count; i++) figure.Segments!.Add(new LineSegment { Point = At(i) });
        var road = new Avalonia.Controls.Shapes.Path { StrokeThickness = 2, Opacity = 0.35, Data = new PathGeometry { Figures = [figure] } };
        road.Bind(Shape.StrokeProperty, this.GetResourceObservable("BsBorder"));
        canvas.Children.Add(road);

        var hits = new List<(int Index, Ellipse Dot, TextBlock Label, string Name, int Count)>();
        for (var i = 0; i < _books.Count; i++)
        {
            var b = _books[i]; var p = At(i);
            if (!perBook.TryGetValue(b.Id, out var count))
            {
                var small = new Ellipse { Width = 5, Height = 5, Opacity = 0.4 };
                small.Bind(Shape.FillProperty, this.GetResourceObservable("BsBorder"));
                Canvas.SetLeft(small, p.X - 2.5); Canvas.SetTop(small, p.Y - 2.5); canvas.Children.Add(small);
                continue;
            }
            var r = 6 + Math.Sqrt(count) * 2.6;
            var color = HomeView.ColorOf(b.Id);
            var dot = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = new SolidColorBrush(color), Opacity = 0.18, Cursor = new Cursor(StandardCursorType.Hand) };
            Canvas.SetLeft(dot, p.X - r); Canvas.SetTop(dot, p.Y - r);
            var name = ShortBook(b); var id = b.Id; var n = count;
            ToolTip.SetTip(dot, $"{b.CommonName}: {n}×");
            dot.PointerPressed += (_, _) => { _concBook = id; ShowConcordance(t); _journey?.Close(); };
            var label = new TextBlock { Text = name, FontSize = 11, Opacity = 0 };
            label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsText"));
            label.Measure(Size.Infinity);
            Canvas.SetLeft(label, p.X - label.DesiredSize.Width / 2); Canvas.SetTop(label, p.Y - r - 18 - (hits.Count % 2) * 12);
            canvas.Children.Add(dot); canvas.Children.Add(label);
            hits.Add((i, dot, label, b.CommonName, count));
        }

        var glow = new Ellipse { Width = 22, Height = 22, Opacity = 0.9 };
        glow.Bind(Shape.FillProperty, this.GetResourceObservable("BsAccent"));
        glow.Effect = new BlurEffect { Radius = 5 };
        canvas.Children.Add(glow);

        var caption = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 6) };
        var again = new Button { Content = L("Noch einmal"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
        var hint = new TextBlock { Text = L("Klick auf einen Ort: nur dieses Buch in der Liste zeigen."), Classes = { "caption" }, Margin = new(0, 6, 0, 0) };

        DispatcherTimer? timer = null;
        void Play()
        {
            timer?.Stop();
            foreach (var h in hits) { h.Dot.Opacity = 0.18; h.Label.Opacity = 0; }
            var progress = 0.0; var reached = -1;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) =>
            {
                progress += 0.05;   // about 0.6 s per stop
                var k = Math.Min((int)progress, hits.Count - 1);
                var f = progress - k;
                var from = At(hits[k].Index); var to = k + 1 < hits.Count ? At(hits[k + 1].Index) : from;
                var ease = f * f * (3 - 2 * f);
                var pos = new Point(from.X + (to.X - from.X) * ease, from.Y + (to.Y - from.Y) * ease);
                Canvas.SetLeft(glow, pos.X - 11); Canvas.SetTop(glow, pos.Y - 11);
                if (k > reached) { reached = k; hits[k].Dot.Opacity = 0.95; hits[k].Label.Opacity = 1; caption.Text = $"{hits[k].Name} · {hits[k].Count}×"; }
                if (progress >= hits.Count - 1 + 0.01) { timer!.Stop(); caption.Text += "   " + L("Ende der Reise: {0} Vorkommen in {1} Büchern", perBook.Values.Sum(), perBook.Count); }
            };
            timer.Start();
        }
        again.Click += (_, _) => Play();

        var dock = new DockPanel { Margin = new(20) };
        var top = new StackPanel { Children = { new TextBlock { Text = L("Reise: {0}", ConcTitle.Text ?? ""), FontSize = 20, FontWeight = FontWeight.SemiBold }, caption } };
        DockPanel.SetDock(top, Dock.Top);
        var bottom = new StackPanel { Children = { again, hint } };
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(top); dock.Children.Add(bottom);
        dock.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform });
        _journey?.Close();
        var win = new Window { Title = L("Reise durch die Bibel"), Width = 1080, Height = 520, MinWidth = 520, MinHeight = 320, Background = Res("BsBackground"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock };
        win.Closed += (_, _) => { timer?.Stop(); _journey = null; };
        _journey = win;
        win.Show(this);
        Play();
    }
}
