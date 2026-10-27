using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>
/// Side panel navigation. The panel's tabs stay as they are (other code selects them by index), but the user sees five labelled
/// sections on a rail, the views of a section as pills, and one sentence saying what the current view is for.
/// </summary>
public partial class MainWindow
{
    sealed record NavTab(int Index, string Label, string Hint);
    sealed record NavGroup(string Name, string Icon, NavTab[] Tabs, bool ShowVault = false);

    static readonly NavGroup[] NavGroups =
    [
        new("Suchen", "M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0z M21 21l-4.3-4.3",
        [
            new(0, "Text", "Sucht in der gewählten Übersetzung: Wörter, „Wendungen“, Grundformen (L=…), Bibelstellen und Strong-Nummern."),
            new(StrongTab, "Strong-Wörterbuch", "Schlägt Wörter des Urtextes nach: Nummer (G3056), griechisches/hebräisches Wort, Umschrift oder englische Bedeutung."),
        ]),
        new("Studieren", "M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z",
        [
            new(GuideTab, "Übersicht", "Alles zum angeklickten Vers: deine Notizen, Markierungen und Dokumente, andere Übersetzungen, Querverweise. Tipp: Rechtsklick auf einen Vers."),
            new(3, "Wort", "Schalte oben „Wortstudie“ ein und tippe ein unterstrichenes Wort an: Bedeutung im Urtext."),
            new(GrundtextTab, "Grundtext", "Der angeklickte Vers im Original, Wort für Wort mit Bedeutung."),
            new(4, "Querverweise", "Verse, die mit dem angeklickten Vers zusammenhängen."),
            new(5, "Vorkommen", "Alle Stellen, an denen ein Wort (Strong-Nummer) vorkommt, mit Verstext."),
        ]),
        new("Sammlung", "M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.7-.9l-.8-1.2A2 2 0 0 0 7.9 3H4a2 2 0 0 0-2 2v13c0 1.1.9 2 2 2z",
        [
            new(1, "Markierungen", "Farbig markierte Verse und Wörter, sortiert in Ordnern (Fächern). Klick auf eine Markierung springt zur Stelle."),
            new(2, "Notizen", "Deine kurzen Notizen zu einzelnen Versen."),
            new(KnowledgeTab, "Dokumente", "Eigene Texte und verknüpfte Dateien. Verse wie „Joh 3,16“ und [[Titel]] werden zu Links."),
            new(6, "Leseliste", "Verse, die du der Reihe nach durchgehen willst."),
            new(7, "Aufgaben", "Deine Aufgaben fürs Studium, auf Wunsch mit Vers."),
        ], ShowVault: true),
        new("KI", "M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z M19 3v4 M17 5h4 M5 17v4 M3 19h4",
        [
            new(AiTab, "Claude", "Dein lokaler Claude-Assistent. Er liest die Bibel über die Werkzeuge dieses Programms."),
        ]),
        new("Mehr", "M21 4h-7 M10 4H3 M21 12h-9 M8 12H3 M21 20h-5 M12 20H3 M14 2v4 M8 10v4 M16 18v4",
        [
            new(SettingsTab, "Einstellungen", "Aussehen, Verhalten, Vault, Sicherung, eigene Übersetzungen."),
            new(13, "Hilfe", "So funktioniert das Programm."),
            new(14, "Maße & Info", "Maße, Gewichte und Abkürzungen aus der Bibel."),
        ]),
    ];

    ComboBox? VaultQuickBox;
    Border? _vaultBar;

    /// <summary>The active vault is always visible in the collection, with a switcher and "new" – like a notebook in Obsidian.</summary>
    void BuildVaultBar()
    {
        VaultQuickBox = new ComboBox { MinWidth = 150, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        VaultQuickBox.SelectionChanged += async (_, _) => { if (!_vaultUiLoading && VaultQuickBox.SelectedItem is string n && n != _vault.Name) await SwitchVaultAsync(n); };
        var name = new TextBox { Watermark = "Name des neuen Vaults", Width = 200 };
        var create = new Button { Content = "Anlegen", Classes = { "primary" }, Margin = new(0, 8, 0, 0) };
        var flyout = new Flyout { Content = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = "Neuer Vault", FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 6) }, name, create } } };
        create.Click += async (_, _) => { NewVaultBox.Text = name.Text; name.Text = ""; flyout.Hide(); await CreateVaultAsync(); };
        var add = new Button { Content = "＋", Classes = { "tool" }, Width = 32, Height = 32, Flyout = flyout };
        ToolTip.SetTip(add, "Neuen Vault anlegen");
        var pill = new Border { Classes = { "pill" }, Child = VaultQuickBox };
        Avalonia.Controls.DockPanel.SetDock(add, Avalonia.Controls.Dock.Right);
        var label = new TextBlock { Text = "VAULT", Classes = { "eyebrow" }, Margin = new(0, 0, 8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Avalonia.Controls.DockPanel.SetDock(label, Avalonia.Controls.Dock.Left);
        _vaultBar = new Border { Child = new DockPanel { Children = { label, add, pill } }, IsVisible = false };
        ToolTip.SetTip(_vaultBar, "Ein Vault ist deine Mappe: Markierungen, Notizen, Dokumente, Leseliste und Aufgaben gehören immer zu einem Vault.");
        SubNav.Children.Insert(0, _vaultBar);
        VaultTopBtn.Click += (_, _) =>
        {
            var menu = new MenuFlyout();
            foreach (var n in VaultNames())
            {
                var name2 = n; var item = new MenuItem { Header = (name2 == _vault.Name ? "✓  " : "     ") + name2 };
                item.Click += async (_, _) => { if (name2 != _vault.Name) await SwitchVaultAsync(name2); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var neu = new MenuItem { Header = "＋  Neuer Vault …" }; neu.Click += (_, _) => flyout.ShowAt(VaultTopBtn);
            menu.Items.Add(neu);
            menu.ShowAt(VaultTopBtn);
        };
        RefreshVaultUi();
    }

    void InitNav()
    {
        BuildVaultBar();
        foreach (var group in NavGroups)
        {
            var g = group;
            var icon = new Icon { Data = Geometry.Parse(g.Icon), IconSize = 22, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            var label = new TextBlock { Text = g.Name, FontSize = 11, FontWeight = FontWeight.Medium, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            var button = new Button { Classes = { "rail" }, Tag = g, Content = new StackPanel { Spacing = 4, Children = { icon, label } } };
            ToolTip.SetTip(button, g.Tabs.Length > 1 ? string.Join(" · ", g.Tabs.Select(t => t.Label)) : g.Tabs[0].Hint);
            button.Click += (_, _) =>
            {
                SetPanelOpen(true);
                if (!g.Tabs.Any(t => t.Index == SideTabs.SelectedIndex)) SideTabs.SelectedIndex = g.Tabs[0].Index;
                SyncNav();
            };
            Rail.Children.Add(button);
        }
        SideTabs.SelectionChanged += (_, e) => { if (e.Source == SideTabs) SyncNav(); };
        SyncNav();
    }

    void SyncNav()
    {
        var current = SideTabs.SelectedIndex;
        var group = NavGroups.FirstOrDefault(g => g.Tabs.Any(t => t.Index == current)) ?? NavGroups[0];
        foreach (var b in Rail.Children.OfType<Button>()) b.Classes.Set("active", b.Tag == group);

        SubPills.Children.Clear();
        if (group.Tabs.Length > 1)
            foreach (var tab in group.Tabs)
            {
                var t = tab;
                var pill = new Button { Classes = { "seg" }, Content = t.Label };
                pill.Classes.Set("active", t.Index == current);
                pill.Click += (_, _) => SideTabs.SelectedIndex = t.Index;
                SubPills.Children.Add(pill);
            }
        SubPills.IsVisible = group.Tabs.Length > 1;
        if (_vaultBar is not null) _vaultBar.IsVisible = group.ShowVault;
        var hint = group.Tabs.FirstOrDefault(t => t.Index == current)?.Hint ?? "";
        if (group.ShowVault) hint += $"";
        SubHint.Text = hint;
    }
}
