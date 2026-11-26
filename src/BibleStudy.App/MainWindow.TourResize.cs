using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace BibleStudy.App;

/// <summary>
/// The boxes of the tours (the bubbles of the short tour, the card of the deep tour, the tab tours) get a grip on their right edge:
/// dragging it makes the box wider or narrower. The width is kept for all tours and for the next start.
/// </summary>
public partial class MainWindow
{
    const double TourMinWidth = 260, TourMaxWidth = 900;

    /// <summary>The width of a tour box whose usual width is <paramref name="usual"/>, with what the person dragged.</summary>
    double TourWidth(double usual)
    {
        if (_phone) return PW(usual);
        var max = Math.Max(TourMinWidth, Math.Min(TourMaxWidth, (Bounds.Width > 0 ? Bounds.Width : TourMaxWidth) - 80));
        return Math.Clamp(usual + _settings.TourWidthExtra, TourMinWidth, max);
    }

    /// <summary>
    /// Puts the grip to the right of the content. <paramref name="sized"/> is the control whose width changes (the box itself, or the content of a bubble).
    /// On a phone the box already fills the width, so there is no grip there.
    /// </summary>
    Control WithTourGrip(Control content, Control sized, double usual, double edge)
    {
        if (_phone) return content;
        var bar = new Border { Width = 4, Height = 36, CornerRadius = new(2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        bar.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsBorder"));
        var grip = new Border
        {
            Width = 12, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeWestEast),
            Margin = new(4, 0, -edge, 0), Child = bar,
        };
        ToolTip.SetTip(grip, L("Ziehen: breiter oder schmaler"));

        double? from = null; var startWidth = 0.0;
        double ScreenX(PointerEventArgs e)
        {
            // the bubble of the short tour moves while it grows, so the position is taken on the screen, not in the box
            var scale = TopLevel.GetTopLevel(grip)?.RenderScaling ?? 1;
            return grip.PointToScreen(e.GetPosition(grip)).X / scale;
        }
        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
            from = ScreenX(e); startWidth = sized.Bounds.Width > 0 ? sized.Bounds.Width : sized.Width;
            e.Pointer.Capture(grip); e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (from is not double x) return;
            var width = Math.Clamp(startWidth + ScreenX(e) - x, TourMinWidth, Math.Max(TourMinWidth, Math.Min(TourMaxWidth, Bounds.Width - 80)));
            sized.Width = width;
            _settings.TourWidthExtra = width - usual;
            e.Handled = true;
        };
        grip.PointerReleased += (_, e) => { if (from is null) return; from = null; e.Pointer.Capture(null); SaveSettings(); e.Handled = true; };
        grip.PointerCaptureLost += (_, _) => from = null;
        grip.PointerEntered += (_, _) => bar.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsAccent"));
        grip.PointerExited += (_, _) => { if (from is null) bar.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsBorder")); };
        // the presenter of a bubble has a largest width of its own; the grip decides now
        grip.AttachedToVisualTree += (_, _) => { if (grip.FindAncestorOfType<Avalonia.Controls.Primitives.FlyoutPresenter>() is { } presenter) presenter.MaxWidth = double.PositiveInfinity; };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(content);
        Grid.SetColumn(grip, 1);
        grid.Children.Add(grip);
        return grid;
    }
}
