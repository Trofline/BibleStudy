using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// Small start window that shows how far loading is. It never waits for anything: it appears at once, is updated by the loading
/// code (<see cref="Report"/>) and closes as soon as the first chapter is on screen (<see cref="Done"/>).
/// </summary>
sealed class SplashWindow : Window
{
    static SplashWindow? _current;
    readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Value = 0.05, Height = 4, Width = 260, Foreground = new SolidColorBrush(Color.Parse("#E3B65F")), Background = new SolidColorBrush(Color.Parse("#22314f")) };
    readonly TextBlock _text = new() { Text = "Starte …", FontSize = 12.5, Foreground = new SolidColorBrush(Color.Parse("#9fb0cf")), HorizontalAlignment = HorizontalAlignment.Center };

    public SplashWindow(string logo = "Book")
    {
        _current = this;
        Width = 420; Height = 300; CanResize = false; ShowInTaskbar = false; Topmost = true;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#0f1b33"));
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }];
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 14 };
        panel.Children.Add(new Image { Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://BibleStudy.App/Assets/" + (logo == "Circle" ? "app-circle.png" : "app.png")))), Width = 112, Height = 112 });
        panel.Children.Add(new TextBlock { Text = "BibleStudy", FontSize = 24, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#e8e6dc")), HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(_bar);
        panel.Children.Add(_text);
        panel.Children.Add(new TextBlock { Text = "Version " + AppPaths.Version, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#5d6e8f")), HorizontalAlignment = HorizontalAlignment.Center });
        Content = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#2a3d63")), BorderThickness = new(1), Child = panel };
        // never stay for good, whatever happens
        DispatcherTimer.RunOnce(Done, TimeSpan.FromSeconds(25));
    }

    /// <summary>Progress 0..1 and what is being loaded; safe to call from any thread, does nothing when there is no splash.</summary>
    public static void Report(double progress, string text)
    {
        var s = _current;
        if (s is null) return;
        Dispatcher.UIThread.Post(() => { s._bar.Value = Math.Max(s._bar.Value, progress); s._text.Text = text; }, DispatcherPriority.Render);
    }

    public static void Done()
    {
        var s = _current;
        if (s is null) return;
        _current = null;
        Dispatcher.UIThread.Post(() =>
        {
            s._bar.Value = 1; s._text.Text = "Fertig";
            s.Opacity = 0;
            DispatcherTimer.RunOnce(s.Close, TimeSpan.FromMilliseconds(220));
        });
    }
}
