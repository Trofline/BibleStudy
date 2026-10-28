using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
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

    /// <summary>The window content shrinks slightly and fades out (about 0.18 s) before the program closes or goes to the tray.</summary>
    async Task PlayCloseAnimationAsync()
    {
        if (Content is not Control root) return;
        root.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new CubicEaseIn() },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new CubicEaseIn() },
        ];
        root.Opacity = 0;
        root.RenderTransform = TransformOperations.Parse("scale(0.96)");
        await Task.Delay(185);
    }

    void ResetAfterCloseAnimation()
    {
        if (Content is not Control root) return;
        root.Transitions = null; root.Opacity = 1; root.RenderTransform = null;
    }
}
