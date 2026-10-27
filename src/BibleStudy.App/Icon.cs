using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>Stroke icon on a 24×24 grid (Lucide-style path data); takes the inherited foreground color.</summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));
    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<Icon, double>(nameof(IconSize), 18);

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, IconSizeProperty, TextElement.ForegroundProperty);
        AffectsMeasure<Icon>(IconSizeProperty);
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public double IconSize { get => GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(IconSize, IconSize);

    public override void Render(DrawingContext context)
    {
        if (Data is null) return;
        var scale = IconSize / 24.0;
        using var _ = context.PushTransform(Matrix.CreateScale(scale, scale));
        context.DrawGeometry(null, new Pen(TextElement.GetForeground(this), 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Data);
    }
}
