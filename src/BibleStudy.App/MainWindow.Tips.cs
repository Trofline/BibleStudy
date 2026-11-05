using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// A small card in the corner of the reader that tells once about shortcuts that can not be seen anywhere (Tab, middle mouse button,
/// the side buttons of the mouse). It waits until no other window (patch notes, a question) is open.
/// </summary>
public partial class MainWindow
{
    const string KeysTipId = "keys-1";

    void ShowNewKeysTip()
    {
        if (_settings.TipShown == KeysTipId || _touring) return;
        if (!IsVisible || OwnedWindows.Count > 0) { DispatcherTimer.RunOnce(ShowNewKeysTip, TimeSpan.FromSeconds(5)); return; }
        _settings.TipShown = KeysTipId; SaveSettings();

        string Key(string id) => _keyActions.FirstOrDefault(a => a.Id == id) is { } a && GestureOf(a) is { Length: > 0 } g ? Pretty(g) : "–";
        var lines = new (string Keys, string Text)[]
        {
            (Key("tabswitch"), L("wechselt zwischen Lesen und dem letzten Reiter. Ein Klick auf den kleinen Punkt am Reiter legt fest, zu welchem.")),
            (L("Mittlere Maustaste"), L("schaltet Schnellmarkieren ein und aus.")),
            (Key("back") + " / " + Key("forward"), L("blättern durch die zuletzt gelesenen Stellen, ebenso die Seitentasten der Maus.")),
            (L("Strg") + "+Z", L("macht im Explorer (Reiter „Markierung“) Löschen und Verschieben rückgängig.")),
        };
        var list = new StackPanel { Spacing = 6 };
        foreach (var (keys, text) in lines)
        {
            var chip = new Border { CornerRadius = new(6), Padding = new(7, 2), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = keys, FontWeight = FontWeight.SemiBold, FontSize = 12 } };
            chip.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsAccentSoft"));
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new(8, 1, 0, 0) };
            var row = new DockPanel();
            DockPanel.SetDock(chip, Dock.Left);
            row.Children.Add(chip); row.Children.Add(t);
            list.Children.Add(row);
        }
        var ok = new Button { Content = L("Verstanden"), Classes = { "primary" } };
        var all = new Button { Content = L("Alle Tastenkürzel"), Classes = { "ghost" }, Margin = new(8, 0, 0, 0) };
        var card = new Border
        {
            CornerRadius = new(14), Padding = new(18, 14), Margin = new(0, 0, 24, 24), BorderThickness = new(1), MaxWidth = 430,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, ZIndex = 50,
            BoxShadow = BoxShadows.Parse("0 6 24 0 #40000000"),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = L("Neu: Tastenkürzel"), FontSize = 16, FontWeight = FontWeight.SemiBold },
                    list,
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { ok, all } },
                },
            },
        };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsAccent"));
        Grid.SetRowSpan(card, 2); Grid.SetColumnSpan(card, 2);
        ok.Click += (_, _) => ReaderHost.Children.Remove(card);
        all.Click += (_, _) => { ReaderHost.Children.Remove(card); ShowKeysWindow(); };
        ReaderHost.Children.Add(card);
    }
}
