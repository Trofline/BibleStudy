using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>Small entrance animations (fade and slide). Switched off by the setting "Animationen".</summary>
public partial class MainWindow
{
    string? _animKey;

    /// <summary>Lets a control that just appeared fade in while sliding by (dx, dy) pixels into place.</summary>
    void AnimateIn(Control c, double dx = 0, double dy = 12, int delayMs = 0)
    {
        if (!_settings.Animations) { c.Opacity = 1; c.RenderTransform = null; return; }
        c.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new CubicEaseOut() },
        ];
        if (_settings.AnimationLevel == 0) { dx /= 3; dy /= 3; }
        c.Opacity = 0;
        c.RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translate({dx}px, {dy}px)"));
        void Go() { c.Opacity = 1; c.RenderTransform = TransformOperations.Parse("translate(0px, 0px)"); }
        if (delayMs <= 0) Dispatcher.UIThread.Post(Go, DispatcherPriority.Background);
        else DispatcherTimer.RunOnce(Go, TimeSpan.FromMilliseconds(delayMs));
    }

    /// <summary>Items of a list slide in one after the other (at most 12, so long lists do not make the user wait).</summary>
    void Stagger(IEnumerable<Control> items, int stepMs = 35)
    {
        var i = 0;
        foreach (var c in items.Take(12)) AnimateIn(c, 0, 10, i++ * stepMs);
    }

    bool _closeAnimating, _closeAnimated;

    static readonly string[] CloseStyles = ["soft", "blur", "fade", "tube", "none"];

    /// <summary>The chosen way of saying goodbye; "none" when the animations are off.</summary>
    string CloseStyle => !_settings.Animations ? "none"
        : _settings.CloseAnimation is { Length: > 0 } c && CloseStyles.Contains(c) ? c
        : _settings.AnimationLevel == 0 ? "fade" : "soft";

    /// <summary>The goodbye, in the variant the user chose in the settings, before the program closes or goes to the tray.</summary>
    async Task PlayCloseAnimationAsync()
    {
        if (Content is not Control root) return;
        root.Transitions = null;
        try
        {
            switch (CloseStyle)
            {
                case "fade": await RunFramesAsync(180, t => root.Opacity = 1 - t * t); break;
                case "blur": await PlayBlurAsync(root); break;
                case "tube": await PlayTubeAsync(root); break;
                case "none": return;
                default: await PlaySoftAsync(root); break;
            }
        }
        catch (Exception ex) { ErrorLog.Write(ex); /* an animation that cannot run must not keep the program from closing */ }
        root.Opacity = 0; Opacity = 0;
    }

    /// <summary>The picture sinks back and softly goes out: a small zoom out and a fade, the whole window fades with it.
    /// Only transform and opacity (cheap to draw, so it stays smooth); Avalonia has no animator for RenderTransform, so the frames are set here.</summary>
    async Task PlaySoftAsync(Control root)
    {
        root.RenderTransformOrigin = Avalonia.RelativePoint.Center;
        await RunFramesAsync(340, t =>
        {
            var move = 1 - Math.Pow(1 - t, 3);          // fast at first, then settling
            var fade = t * t * (3 - 2 * t);             // smooth start and end
            var scale = 1 - 0.07 * move;
            root.RenderTransform = new ScaleTransform(scale, scale);
            root.Opacity = 1 - fade;
            Opacity = 1 - fade * fade;                   // the window itself goes out a little later
        });
    }

    /// <summary>The picture drifts out of focus: it blurs more and more, grows a little towards the viewer and fades out.</summary>
    async Task PlayBlurAsync(Control root)
    {
        root.RenderTransformOrigin = Avalonia.RelativePoint.Center;
        await RunFramesAsync(480, t =>
        {
            var e = t * t * (3 - 2 * t);
            var scale = 1 + 0.05 * e;
            root.RenderTransform = new ScaleTransform(scale, scale);
            root.Effect = new ImmutableBlurEffect(Math.Round(34 * e));   // whole numbers only: the picture is not made anew for every fraction
            root.Opacity = t < 0.35 ? 1 : 1 - Math.Pow((t - 0.35) / 0.65, 1.5);
            Opacity = t < 0.7 ? 1 : 1 - (t - 0.7) / 0.3;
        });
    }

    /// <summary>Like an old tube TV switching off: the picture collapses to a glowing line, the line to a point, the point goes out.</summary>
    async Task PlayTubeAsync(Control root)
    {
        root.RenderTransformOrigin = Avalonia.RelativePoint.Center;
        await RunFramesAsync(260, t =>
        {
            var e = t * t * t;
            root.RenderTransform = new ScaleTransform(1, 1 - 0.996 * e);
            root.Effect = new ImmutableDropShadowEffect(0, 0, 40 * e, Colors.White, e);
        });
        await RunFramesAsync(300, t =>
        {
            var e = t * t * t;
            root.RenderTransform = new ScaleTransform(1 - 0.99 * e, 0.004);
            root.Effect = new ImmutableDropShadowEffect(0, 0, 40 + 10 * e, Colors.White, 1);
            root.Opacity = t < 0.75 ? 1 : 1 - (t - 0.75) / 0.25;
        });
    }

    /// <summary>Calls <paramref name="step"/> with 0..1 for the given time, about 60 times a second.</summary>
    static async Task RunFramesAsync(int ms, Action<double> step)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / ms);
            step(t);
            if (t >= 1) return;
            await Task.Delay(8);
        }
    }

    void ResetAfterCloseAnimation()
    {
        if (Content is not Control root) return;
        root.Transitions = null; root.Opacity = 1; root.RenderTransform = null; root.Effect = null; Opacity = 1;
    }
}
