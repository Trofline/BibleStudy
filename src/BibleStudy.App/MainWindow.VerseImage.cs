using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Makes a square picture of the clicked verse (for sharing): text, reference, translation, in the program's colors.</summary>
public partial class MainWindow
{
    void ExportVerseImage()
    {
        if (_selected is not int v) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
        var r = new VerseRef(_curBook, _curChapter, v);
        var text = VerseTextOf(v).Trim();
        var translation = (TranslationBox.SelectedItem as TranslationInfo)?.ShortName ?? "";
        var accent = (Res("BsAccent") as ISolidColorBrush)?.Color ?? Color.Parse("#E3B65F");
        var size = text.Length switch { < 100 => 60, < 200 => 50, < 320 => 42, < 480 => 34, _ => 28 };

        var quote = new TextBlock { Text = "„" + text + "“", FontSize = size, FontFamily = ScriptureFont, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.35, TextAlignment = TextAlignment.Center };
        var reference = new TextBlock { Text = $"{Label(r)}  ·  {translation}", FontSize = 30, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 36, 0, 0) };
        var mark = new TextBlock { Text = "BibleStudy", FontSize = 20, Opacity = 0.45, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 40) };
        var root = new Grid
        {
            Width = 1080, Height = 1080,
            Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new(Color.Parse("#12151B"), 0), new(Color.Parse("#1F2630"), 0.7), new(Color.FromArgb(255, (byte)(accent.R / 4 + 20), (byte)(accent.G / 4 + 20), (byte)(accent.B / 4 + 20)), 1) } },
        };
        root.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(90, 0), Children = { quote, reference } });
        root.Children.Add(mark);
        root.Measure(new Size(1080, 1080)); root.Arrange(new Rect(0, 0, 1080, 1080));

        var bmp = new RenderTargetBitmap(new PixelSize(1080, 1080), new Vector(96, 96));
        bmp.Render(root);
        var name = string.Concat(Label(r).Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var path = Path.Combine(ExportFolder, $"{name}.png");
        bmp.Save(path);
        StatusText.Text = L("Bild gespeichert: {0}", path);
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { /* no viewer: the path is in the status line */ }
    }
}
