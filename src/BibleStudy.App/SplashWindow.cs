using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// Transparent start window: the logo floats on the desktop with a thin progress bar. The bar is animated by the compositor (render
/// thread), so it keeps growing smoothly while the UI thread is busy building the main window. It never waits for anything: it
/// closes as soon as the first chapter is on screen (<see cref="Done"/>).
/// </summary>
sealed class SplashWindow : Window
{
    static SplashWindow? _current;

    // The bar eases from 2 % towards 97 % over this time; Done() finishes it from wherever it is.
    const double GrowSeconds = 3.6;
    const double From = 0.02, To = 0.97;

    readonly Border _fill = new() { Background = new SolidColorBrush(Color.Parse("#E3B65F")), CornerRadius = new(2) };
    readonly TextBlock _text = new() { Text = "Starte …", FontSize = 12.5, Foreground = new SolidColorBrush(Color.Parse("#dfe4ee")), HorizontalAlignment = HorizontalAlignment.Center };
    readonly Image _logo;
    readonly DateTime _started = DateTime.UtcNow;

    public SplashWindow(string logo = "Book")
    {
        _current = this;
        Width = 440; Height = 360; CanResize = false; ShowInTaskbar = false; Topmost = true;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() }];

        using var s = AssetLoader.Open(new Uri("avares://BibleStudy.App/Assets/" + (logo == "Circle" ? "app-circle.png" : "app.png")));
        _logo = new Image { Source = new Bitmap(s), Width = 150, Height = 150, HorizontalAlignment = HorizontalAlignment.Center, RenderTransformOrigin = RelativePoint.Center };

        var track = new Border { Width = 280, Height = 4, CornerRadius = new(2), ClipToBounds = true, Background = new SolidColorBrush(Color.Parse("#ffffff"), 0.22), Child = _fill };
        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 16,
            Effect = new DropShadowEffect { BlurRadius = 22, OffsetX = 0, OffsetY = 4, Color = Colors.Black, Opacity = 0.75 },
        };
        panel.Children.Add(_logo);
        panel.Children.Add(new TextBlock { Text = "BibleStudy", FontSize = 26, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#f4f1e8")), HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(track);
        panel.Children.Add(_text);
        panel.Children.Add(new TextBlock { Text = "Version " + AppPaths.Version, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#b8c0d0")), HorizontalAlignment = HorizontalAlignment.Center });
        Content = panel;

        Opened += (_, _) => StartAnimations(panel);
        // never stay for good, whatever happens
        DispatcherTimer.RunOnce(Done, TimeSpan.FromSeconds(25));
    }

    /// <summary>Animations that run on the render thread and therefore do not stall while the main window is being built.</summary>
    void StartAnimations(Control panel)
    {
        try
        {
            var fill = ElementComposition.GetElementVisual(_fill);
            if (fill is not null)
            {
                fill.CenterPoint = new Avalonia.Vector3D(0, 0, 0);
                var grow = fill.Compositor.CreateVector3DKeyFrameAnimation();
                grow.Duration = TimeSpan.FromSeconds(GrowSeconds);
                grow.InsertKeyFrame(0f, new Avalonia.Vector3D(From, 1, 1));
                grow.InsertKeyFrame(1f, new Avalonia.Vector3D(To, 1, 1), new CubicEaseOut());
                fill.StartAnimation("Scale", grow);
            }
            if (ElementComposition.GetElementVisual(panel) is { } pv)
            {
                var fade = pv.Compositor.CreateScalarKeyFrameAnimation();
                fade.Duration = TimeSpan.FromMilliseconds(320);
                fade.InsertKeyFrame(0f, 0f); fade.InsertKeyFrame(1f, 1f, new CubicEaseOut());
                pv.StartAnimation("Opacity", fade);
            }
            if (ElementComposition.GetElementVisual(_logo) is { } lv)
            {
                lv.CenterPoint = new Avalonia.Vector3D(75, 75, 0);
                var pulse = lv.Compositor.CreateVector3DKeyFrameAnimation();
                pulse.Duration = TimeSpan.FromSeconds(2.2);
                pulse.IterationBehavior = Avalonia.Rendering.Composition.Animations.AnimationIterationBehavior.Forever;
                pulse.InsertKeyFrame(0f, new Avalonia.Vector3D(1, 1, 1));
                pulse.InsertKeyFrame(0.5f, new Avalonia.Vector3D(1.04, 1.04, 1), new CubicEaseOut());
                pulse.InsertKeyFrame(1f, new Avalonia.Vector3D(1, 1, 1), new CubicEaseOut());
                lv.StartAnimation("Scale", pulse);
            }
        }
        catch { /* animations are decoration only */ }
    }

    /// <summary>Where the compositor's bar is right now (same curve as the animation: cubic ease-out).</summary>
    double CurrentProgress()
    {
        var x = Math.Clamp((DateTime.UtcNow - _started).TotalSeconds / GrowSeconds, 0, 1);
        return From + (To - From) * (1 - Math.Pow(1 - x, 3));
    }

    /// <summary>What is being loaded; safe to call from any thread, does nothing when there is no splash.</summary>
    public static void Report(double progress, string text)
    {
        var s = _current;
        if (s is null) return;
        Dispatcher.UIThread.Post(() => s._text.Text = text, DispatcherPriority.Render);
    }

    public static void Done()
    {
        var s = _current;
        if (s is null) return;
        _current = null;
        Dispatcher.UIThread.Post(() =>
        {
            s._text.Text = "Fertig";
            try
            {
                if (ElementComposition.GetElementVisual(s._fill) is { } fill)
                {
                    var finish = fill.Compositor.CreateVector3DKeyFrameAnimation();
                    finish.Duration = TimeSpan.FromMilliseconds(170);
                    finish.InsertKeyFrame(0f, new Avalonia.Vector3D(s.CurrentProgress(), 1, 1));
                    finish.InsertKeyFrame(1f, new Avalonia.Vector3D(1, 1, 1), new CubicEaseOut());
                    fill.StartAnimation("Scale", finish);
                }
            }
            catch { /* the bar just stays where it is */ }
            DispatcherTimer.RunOnce(() => s.Opacity = 0, TimeSpan.FromMilliseconds(200));
            DispatcherTimer.RunOnce(s.Close, TimeSpan.FromMilliseconds(380));
        });
    }
}
