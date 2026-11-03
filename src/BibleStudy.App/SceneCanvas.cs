using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using BibleStudy.Core.Diagram;

namespace BibleStudy.App;

/// <summary>Draws a laid-out sentence diagram (lines and texts) onto a canvas.</summary>
public static class SceneCanvas
{
    public static Canvas Build(DScene scene, FontFamily font, IBrush? background, Color ink, bool dark, Func<DWord, string>? tip = null, Action<DWord>? click = null)
    {
        var canvas = new Canvas { Width = scene.Width, Height = scene.Height, Background = background };
        var stroke = new SolidColorBrush(ink);
        foreach (var l in scene.Lines)
        {
            var line = new Line
            {
                StartPoint = new Point(l.X1, l.Y1), EndPoint = new Point(l.X2, l.Y2), Stroke = stroke, StrokeLineCap = PenLineCap.Round,
                StrokeThickness = l.Style switch { DLineStyle.Baseline or DLineStyle.Divider => 2.2, DLineStyle.Stilt => 2, DLineStyle.Dashed => 1.4, _ => 1.7 },
            };
            if (l.Style == DLineStyle.Dashed) line.StrokeDashArray = [4, 3.2];
            canvas.Children.Add(line);
        }
        foreach (var t in scene.Texts)
        {
            var tb = new TextBlock
            {
                Text = t.Text, FontSize = t.Size, Foreground = new SolidColorBrush(Color.Parse(DiagramSvg.ColorOf(t.Kind, dark))), FontFamily = font,
                FontWeight = t.Kind is DTextKind.Verb or DTextKind.Subject ? FontWeight.SemiBold : FontWeight.Normal,
                FontStyle = t.Kind is DTextKind.Gloss or DTextKind.Implied ? FontStyle.Italic : FontStyle.Normal,
            };
            tb.Measure(Size.Infinity);
            var size = tb.DesiredSize;
            var baseline = size.Height * 0.8;   // the text line sits at about four fifths of the box
            var left = t.Anchor switch { DAnchor.Left => t.X, DAnchor.Right => t.X - size.Width, _ => t.X - size.Width / 2 };
            Canvas.SetLeft(tb, left); Canvas.SetTop(tb, t.Y - baseline);
            if (t.Angle != 0)
            {
                tb.RenderTransformOrigin = new RelativePoint(0, 0.8, RelativeUnit.Relative);
                tb.RenderTransform = new RotateTransform(t.Angle);
            }
            if (t.Word is { } word)
            {
                if (tip is not null) ToolTip.SetTip(tb, tip(word));
                if (click is not null && word.Strong.Length > 0)
                {
                    tb.Cursor = new Cursor(StandardCursorType.Hand);
                    tb.PointerPressed += (_, e) => { if (e.GetCurrentPoint(tb).Properties.IsLeftButtonPressed) click(word); };
                }
            }
            canvas.Children.Add(tb);
        }
        return canvas;
    }
}
