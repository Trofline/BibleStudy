using Avalonia;
using Avalonia.Controls;

namespace BibleStudy.App;

/// <summary>
/// The top bar must not lose things when the window gets narrow. The "Go to" field takes what is left and shrinks; when even that is
/// not enough, first the program name, then the vault name give way (the icons stay).
/// </summary>
public partial class MainWindow
{
    const double QuickJumpMin = 96, QuickJumpMax = 230;

    void InitTopBar()
    {
        TopBar.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) FitTopBar(); };
        VaultTopText.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) FitTopBar(); };
        FitTopBar();
    }

    void FitTopBar()
    {
        var width = TopBar.Bounds.Width - TopBar.Padding.Left - TopBar.Padding.Right;
        if (width <= 0) return;
        // measure the fixed parts with everything shown
        AppTitleText.IsVisible = true; VaultTopText.IsVisible = true;
        double Fixed()
        {
            double sum = 0;
            foreach (var child in TopGrid.Children)
            {
                if (child == QuickJumpHost) continue;
                child.Measure(Size.Infinity);
                sum += child.DesiredSize.Width + child.Margin.Left + child.Margin.Right;
            }
            return sum;
        }
        const double gap = 10;   // the margin of the field
        var room = width - Fixed() - gap;
        if (room < QuickJumpMin) { AppTitleText.IsVisible = false; room = width - Fixed() - gap; }
        if (room < QuickJumpMin) { VaultTopText.IsVisible = false; room = width - Fixed() - gap; }
        QuickJumpPill.Width = Math.Clamp(room, QuickJumpMin, QuickJumpMax);
    }
}
