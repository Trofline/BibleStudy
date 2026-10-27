using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>Text where «hits» are shown highlighted (bold, accent color, soft background).</summary>
public sealed class MarkedBlock : TextBlock
{
    public static readonly StyledProperty<string?> MarkedProperty = AvaloniaProperty.Register<MarkedBlock, string?>(nameof(Marked));

    public string? Marked { get => GetValue(MarkedProperty); set => SetValue(MarkedProperty, value); }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkedProperty) Rebuild();
    }

    void Rebuild()
    {
        Inlines?.Clear();
        var text = Marked ?? "";
        var accent = this.TryFindResource("BsAccent", ActualThemeVariant, out var a) ? a as IBrush : null;
        var soft = this.TryFindResource("BsAccentSoft", ActualThemeVariant, out var s) ? s as IBrush : null;
        var pos = 0;
        while (pos < text.Length)
        {
            var open = text.IndexOf('«', pos);
            if (open < 0) { Inlines!.Add(new Run(text[pos..])); break; }
            var close = text.IndexOf('»', open + 1);
            if (close < 0) { Inlines!.Add(new Run(text[pos..])); break; }
            if (open > pos) Inlines!.Add(new Run(text[pos..open]));
            Inlines!.Add(new Run(text[(open + 1)..close]) { FontWeight = FontWeight.Bold, Foreground = accent, Background = soft });
            pos = close + 1;
        }
    }
}
