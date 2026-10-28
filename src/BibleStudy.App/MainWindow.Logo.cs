using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace BibleStudy.App;

/// <summary>Two program symbols to choose from; the choice applies to the window, taskbar, title bar, tray and start window.</summary>
public partial class MainWindow
{
    static readonly (string Key, string Name)[] Logos = [("Book", K("Buch")), ("Circle", K("Kreis"))];

    void InitLogo()
    {
        ApplyLogo(_settings.Logo, save: false);
        foreach (var (key, name) in Logos)
        {
            var k = key;
            using var s = AssetLoader.Open(new Uri("avares://BibleStudy.App/Assets/" + LogoAsset(key)));
            var btn = new Button
            {
                Classes = { "seg" }, Height = 52, Margin = new(0, 0, 8, 6), Padding = new(10, 0), Tag = key,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new Image { Source = new Bitmap(s), Width = 36, Height = 36 }, new TextBlock { Text = L(name), VerticalAlignment = VerticalAlignment.Center } } },
            };
            btn.Click += (_, _) => ApplyLogo(k, save: true);
            LogoPanel.Children.Add(btn);
        }
        MarkLogoButtons();
    }

    void ApplyLogo(string key, bool save)
    {
        if (Logos.All(l => l.Key != key)) key = "Book";
        _settings.Logo = key;
        Icon = MakeIcon(key);
        if (_tray is not null) _tray.Icon = Icon;
        using (var s = AssetLoader.Open(new Uri("avares://BibleStudy.App/Assets/" + LogoAsset(key)))) LogoImage.Source = new Bitmap(s);
        if (save) { SaveSettings(); MarkLogoButtons(); }
    }

    void MarkLogoButtons()
    {
        foreach (var b in LogoPanel.Children.OfType<Button>()) b.Classes.Set("active", (string?)b.Tag == _settings.Logo);
    }
}
