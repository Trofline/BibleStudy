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
