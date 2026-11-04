using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Look settings beyond the accent color: the background of the dark design and the color of what you have selected.</summary>
public partial class MainWindow
{
    /// <summary>A dark background: page, cards, raised parts, hover and lines.</summary>
    sealed record DarkPalette(string Key, string Background, string Surface, string Surface2, string Hover, string Border);

    static readonly DarkPalette[] DarkPalettes =
    [
        new("Nachtblau", "#0F1216", "#161A20", "#1F252D", "#2A323D", "#262D37"),
        new("Schwarz", "#000000", "#0B0B0B", "#151515", "#242424", "#1F1F1F"),
        new("Grau", "#131313", "#1C1C1C", "#252525", "#313131", "#2D2D2D"),
        new("Warm", "#15110D", "#1D1813", "#27211A", "#342B21", "#2F271E"),
        new("Wald", "#0E1311", "#151B18", "#1D2521", "#28342E", "#232E28"),
        new("Violett", "#130F18", "#1A1520", "#231D2A", "#2F2738", "#2A2332"),
    ];

    DarkPalette CurrentDark => DarkPalettes.FirstOrDefault(p => p.Key == _settings.DarkBackground) ?? DarkPalettes[0];

    string[] DarkNames => [L("Nachtblau"), L("Schwarz"), L("Grau"), L("Warm"), L("Wald"), L("Violett")];

    void InitAppearance()
    {
        DarkBackgroundBox.ItemsSource = DarkNames;
        DarkBackgroundBox.SelectedIndex = Math.Max(0, Array.FindIndex(DarkPalettes, p => p.Key == _settings.DarkBackground));
        DarkBackgroundBox.SelectionChanged += (_, _) =>
        {
            if (DarkBackgroundBox.SelectedIndex < 0) return;
            _settings.DarkBackground = DarkPalettes[DarkBackgroundBox.SelectedIndex].Key; SaveSettings();
            ApplyDarkBackground(); ApplyAccent(_settings.Accent);
        };
        SelectionColorBox.ItemsSource = new[] { L("Akzentfarbe"), L("Grau"), L("Blau") };
        SelectionColorBox.SelectedIndex = _settings.SelectionColor switch { "Gray" => 1, "Blue" => 2, _ => 0 };
        SelectionColorBox.SelectionChanged += async (_, _) =>
        {
            if (SelectionColorBox.SelectedIndex < 0) return;
            _settings.SelectionColor = SelectionColorBox.SelectedIndex switch { 1 => "Gray", 2 => "Blue", _ => "Accent" }; SaveSettings();
            Paint(); await RedrawChapterAsync();
        };
        ApplyDarkBackground();
        SelectWordsCheck.IsChecked = _settings.SelectWholeWords;
        SelectWordsCheck.IsCheckedChanged += (_, _) => { _settings.SelectWholeWords = SelectWordsCheck.IsChecked == true; SaveSettings(); };
        BuildSubMarkPanel();
    }

    /// <summary>The colors for a sub-mark: the first one (the accent color of the program) follows the accent, the others are fixed.</summary>
    void BuildSubMarkPanel()
    {
        SubMarkPanel.Children.Clear();
        foreach (var hex in new[] { "" }.Concat(PresetColors).Concat(_settings.CustomSubColors))
        {
            var chosen = NormalizeHex(_settings.SubMarkColor) is { } cur ? string.Equals(cur, hex, StringComparison.OrdinalIgnoreCase) : hex.Length == 0;
            var dot = new Border
            {
                Width = 26, Height = 26, CornerRadius = new(13), Margin = new(0, 0, 8, 6), Cursor = new Cursor(StandardCursorType.Hand),
                BorderThickness = new(chosen ? 3 : 1), BorderBrush = chosen ? Res("BsText") : Res("BsBorder"),
            };
            if (hex.Length == 0) dot.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsAccent")); else dot.Background = new SolidColorBrush(MarkColor(hex));
            ToolTip.SetTip(dot, hex.Length == 0 ? L("Akzentfarbe") : hex);
            var captured = hex;
            dot.PointerPressed += (_, _) => { _settings.SubMarkColor = captured; SaveSettings(); BuildSubMarkPanel(); };
            SubMarkPanel.Children.Add(dot);
        }
        var add = new Border
        {
            Width = 26, Height = 26, CornerRadius = new(13), Margin = new(0, 0, 8, 6), Cursor = new Cursor(StandardCursorType.Hand), BorderThickness = new(1), BorderBrush = Res("BsBorder"),
            Child = new TextBlock { Text = "+", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 16 },
        };
        ToolTip.SetTip(add, L("Eigene Farbe wählen"));
        add.PointerPressed += (_, _) => ShowCustomSubMarkColor();
        SubMarkPanel.Children.Add(add);
    }

    /// <summary>A window with a color picker for an own sub-mark color; it is kept in the list and chosen at once.</summary>
    void ShowCustomSubMarkColor() => ShowCustomColor(NormalizeHex(_settings.SubMarkColor) is { } cur ? MarkColor(cur) : MarkColor(SubMarkColor()), hex =>
    {
        if (!PresetColors.Contains(hex, StringComparer.OrdinalIgnoreCase) && !_settings.CustomSubColors.Contains(hex, StringComparer.OrdinalIgnoreCase))
        {
            _settings.CustomSubColors.Add(hex);
            if (_settings.CustomSubColors.Count > 6) _settings.CustomSubColors.RemoveAt(0);
        }
        _settings.SubMarkColor = hex; SaveSettings(); BuildSubMarkPanel();
    });

    /// <summary>A small window with a color picker; <paramref name="chosen"/> gets the color (#RRGGBB) when the user saves.</summary>
    void ShowCustomColor(Color start, Action<string> chosen)
    {
        var picker = new ColorView { Color = start, IsAlphaVisible = false };
        var save = new Button { Content = L("Speichern"), Classes = { "primary" }, Margin = new(8, 0, 0, 0) };
        var cancel = new Button { Content = L("Abbrechen") };
        var stack = new StackPanel
        {
            Spacing = 10, Margin = new(20),
            Children = { new TextBlock { Text = L("Eigene Farbe wählen"), Classes = { "h2" } }, picker, new WrapPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { cancel, save } } },
        };
        var win = new Window { Title = L("Eigene Farbe wählen"), Width = 420, SizeToContent = SizeToContent.Height, CanResize = false, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = stack };
        save.Click += (_, _) =>
        {
            chosen(HexOf(picker.Color)); win.Close();
        };
        cancel.Click += (_, _) => win.Close();
        win.Show(this);
    }

    /// <summary>Puts the chosen dark background into the dark theme; the light theme is not touched.</summary>
    void ApplyDarkBackground()
    {
        var res = Application.Current!.Resources;
        if (!res.ThemeDictionaries.TryGetValue(ThemeVariant.Dark, out var provider) || provider is not ResourceDictionary d) return;
        var p = CurrentDark;
        IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
        d["BsBackground"] = B(p.Background); d["BsSurface"] = B(p.Surface); d["BsSurface2"] = B(p.Surface2); d["BsHover"] = B(p.Hover); d["BsBorder"] = B(p.Border);
        d["BsAccentFg"] = B(p.Surface);
        d["ComboBoxBackgroundPointerOver"] = B(p.Hover); d["ComboBoxBackgroundPressed"] = B(p.Hover);
        d["TabItemHeaderBackgroundUnselectedPointerOver"] = B(p.Hover);
    }

    /// <summary>The color that shows what is selected (a verse or text): the accent color of the program unless another one is chosen.</summary>
    Color SelectionInk() => _settings.SelectionColor switch
    {
        "Gray" => (Res("BsText") as ISolidColorBrush)?.Color ?? Colors.Gray,
        "Blue" => ActualThemeVariant == ThemeVariant.Dark ? Color.Parse("#6CA8FF") : Color.Parse("#1F5FBF"),
        _ => (Res("BsAccent") as ISolidColorBrush)?.Color ?? Colors.DodgerBlue,
    };
}
