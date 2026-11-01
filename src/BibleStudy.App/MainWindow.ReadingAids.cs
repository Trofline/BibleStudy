using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace BibleStudy.App;

/// <summary>Reading aids: high contrast, an easier-to-read typeface, Hebrew without vowel signs or accents.</summary>
public partial class MainWindow
{
    /// <summary>The original colors of the theme, kept so that "high contrast" can be switched off again.</summary>
    readonly Dictionary<(ThemeVariant, string), object?> _plainColors = [];

    /// <summary>Hebrew text as the settings want it: vowel points (U+05B0–05C7) and cantillation accents (U+0591–05AF) can each be hidden.</summary>
    string Heb(string text)
    {
        if (_settings.HebrewVowels && _settings.HebrewAccents) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!_settings.HebrewAccents && c is >= '֑' and <= '֯') continue;
            if (!_settings.HebrewVowels && c is >= 'ְ' and <= 'ֽ' or 'ֿ' or 'ׁ' or 'ׂ' or 'ׄ' or 'ׅ' or 'ׇ') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    void InitReadingAids()
    {
        HighContrastCheck.IsChecked = _settings.HighContrast; ReadableTextCheck.IsChecked = _settings.ReadableText;
        HebrewVowelsCheck.IsChecked = _settings.HebrewVowels; HebrewAccentsCheck.IsChecked = _settings.HebrewAccents;
        GlassCheck.IsChecked = _settings.GlassDesign;
        GlassCheck.IsCheckedChanged += (_, _) => { _settings.GlassDesign = GlassCheck.IsChecked == true; ApplyGlass(); SaveSettings(); };
        ApplyGlass();
        async void Redraw() { SaveSettings(); await ShowChapterAsync(_selected); }
        HighContrastCheck.IsCheckedChanged += (_, _) => { _settings.HighContrast = HighContrastCheck.IsChecked == true; ApplyContrast(); SaveSettings(); };
        ReadableTextCheck.IsCheckedChanged += (_, _) => { _settings.ReadableText = ReadableTextCheck.IsChecked == true; Redraw(); };
        HebrewVowelsCheck.IsCheckedChanged += (_, _) => { _settings.HebrewVowels = HebrewVowelsCheck.IsChecked == true; Redraw(); };
        HebrewAccentsCheck.IsCheckedChanged += (_, _) => { _settings.HebrewAccents = HebrewAccentsCheck.IsChecked == true; Redraw(); };
        ApplyContrast();
    }

    /// <summary>Stronger text, muted text and borders in both themes (the accent and background colors stay).</summary>
    void ApplyContrast()
    {
        var res = Application.Current!.Resources;
        var strong = new Dictionary<ThemeVariant, (string Text, string Muted, string Border)>
        {
            [ThemeVariant.Light] = ("#000000", "#2E2A22", "#6B6354"),
            [ThemeVariant.Dark] = ("#FFFFFF", "#D8DDE3", "#7A8594"),
        };
        foreach (var (variant, colors) in strong)
        {
            if (!res.ThemeDictionaries.TryGetValue(variant, out var provider) || provider is not ResourceDictionary d) continue;
            foreach (var (key, hex) in new[] { ("BsText", colors.Text), ("BsMuted", colors.Muted), ("BsBorder", colors.Border) })
            {
                if (!_plainColors.ContainsKey((variant, key))) _plainColors[(variant, key)] = d.TryGetValue(key, out var old) ? old : null;
                if (_settings.HighContrast) d[key] = new SolidColorBrush(Color.Parse(hex));
                else if (_plainColors[(variant, key)] is { } original) d[key] = original;
            }
        }
    }
}

public partial class MainWindow
{
    readonly Dictionary<(ThemeVariant, string), object?> _solidColors = [];

    /// <summary>
    /// Liquid-glass design: a soft colored backdrop, translucent frosted surfaces, a bright glass rim that fades along the edge with a
    /// faint prism tint, soft shadows and a lifting hover. (No real light refraction: Avalonia has no backdrop distortion.) Off = plain colors.
    /// </summary>
    void ApplyGlass()
    {
        var on = _settings.GlassDesign;
        TransparencyLevelHint = [WindowTransparencyLevel.None];
        Classes.Set("glass", on);
        var res = Application.Current!.Resources;
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            if (!res.ThemeDictionaries.TryGetValue(variant, out var provider) || provider is not ResourceDictionary d) continue;
            var dark = variant == ThemeVariant.Dark;
            IBrush Aurora() => new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = dark
                    ? [new(Color.Parse("#0B1220"), 0), new(Color.Parse("#1B1740"), 0.45), new(Color.Parse("#0E3A4A"), 0.8), new(Color.Parse("#241238"), 1)]
                    : [new(Color.Parse("#E8EEFF"), 0), new(Color.Parse("#F6E9F4"), 0.45), new(Color.Parse("#E2F4F1"), 0.8), new(Color.Parse("#FBEFE0"), 1)],
            };
            foreach (var key in new[] { "BsBackground", "BsSurface", "BsSurface2" })
            {
                if (!_solidColors.ContainsKey((variant, key))) _solidColors[(variant, key)] = d.TryGetValue(key, out var old) ? old : null;
                if (!on) { if (_solidColors[(variant, key)] is { } original) d[key] = original; continue; }
                // opaque (views are stacked on each other, a see-through surface would show the one below), lightly tinted toward blue-violet
                if (key == "BsBackground") d[key] = Aurora();
                else if (_solidColors[(variant, key)] is SolidColorBrush plain)
                    d[key] = new SolidColorBrush(Mix(plain.Color, dark ? Color.Parse("#7C86FF") : Color.Parse("#B9C4FF"), key == "BsSurface" ? 0.10 : 0.16));
            }
        }
    }
}
