using Avalonia;
using Avalonia.Controls;

namespace BibleStudy.App;

/// <summary>
/// The top bar must not lose things when the window gets narrow. The "Go to" field takes what is left and shrinks; when even that is
/// not enough, first the program name, then the vault name give way, then the less used icons move behind a "…" button (see <see cref="RunSpill"/>).
/// </summary>
public partial class MainWindow
{
    const double QuickJumpMin = 44, QuickJumpWish = 72, QuickJumpMax = 230;   // the field gets at least the minimum; things move behind "…" until it has the wish

    string? _jumpHint;

    void InitTopBar()
    {
        // least needed first: they leave in this order and come back in the opposite one
        _topSpill =
        [
            new(KeysTopBtn, () => !_settings.EasyMode), new(LayoutTopBtn, () => true), new(SplitTopBtn, () => !_settings.EasyMode), new(MarksTopBtn, () => true), new(FeedbackTopBtn, () => !_settings.EasyMode),
            new(TodoTopBtn, () => !_settings.EasyMode), new(ThemeBtn, () => true), new(ModeTopBtn, () => true), new(VaultTopBtn, () => true),
            new(LangBtn, () => true, ((Panel)LangBtn.Parent!).Children.OfType<Avalonia.Controls.Shapes.Rectangle>().First()),
            new(LogoImage, () => true, null, Silent: true),
        ];
        TopBar.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) FitTopBar(); };
        VaultTopText.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) FitTopBar(); };
        TopMoreBtn.Click += (_, _) => ShowSpill(TopMoreBtn, _topHidden);
        FitTopBar();
    }

    void FitTopBar()
    {
        var width = TopBar.Bounds.Width - TopBar.Padding.Left - TopBar.Padding.Right;
        if (width <= 0) return;
        const double gap = 10;   // the margin of the field
        // measure the fixed parts with everything shown
        AppTitleText.IsVisible = true; VaultTopText.IsVisible = true;
        double Room()
        {
            double sum = 0;
            Remeasure(TopGrid);
            foreach (var child in TopGrid.Children)
            {
                if (child == QuickJumpHost) continue;
                child.Measure(Size.Infinity);
                sum += child.DesiredSize.Width + child.Margin.Left + child.Margin.Right;
            }
            return width - sum - gap;
        }
        RunSpill(TopMoreBtn, _topSpill, _topHidden, () => true);
        if (Room() < QuickJumpWish) AppTitleText.IsVisible = false;
        if (Room() < QuickJumpWish) VaultTopText.IsVisible = false;
        RunSpill(TopMoreBtn, _topSpill, _topHidden, () => Room() >= QuickJumpWish);
        QuickJumpPill.Width = Math.Clamp(Room(), QuickJumpMin, QuickJumpMax);
        // in a field this narrow the hint would only show its first letters
        _jumpHint ??= QuickJumpBox.PlaceholderText;
        QuickJumpBox.PlaceholderText = QuickJumpPill.Width < 130 ? "" : _jumpHint;
    }
}
