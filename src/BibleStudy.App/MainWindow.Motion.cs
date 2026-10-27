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
    void AnimateIn(Control c, double dx = 0, double dy = 12)
    {
        if (!_settings.Animations) { c.Opacity = 1; c.RenderTransform = null; return; }
        c.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new CubicEaseOut() },
        ];
        c.Opacity = 0;
        c.RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translate({dx}px, {dy}px)"));
        Dispatcher.UIThread.Post(() => { c.Opacity = 1; c.RenderTransform = TransformOperations.Parse("translate(0px, 0px)"); }, DispatcherPriority.Background);
    }
}
