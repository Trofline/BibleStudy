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
    sealed record NavGroup(string Name, string Icon, NavTab[] Tabs, bool ShowVault = false, bool Windowed = false);

    /// <summary>Commentaries are human writings, not Scripture: off unless the user turns them on (applies after a restart).</summary>
    static bool CommentariesOn;
    static NavGroup[] NavGroups = MakeNavGroups();

    static NavTab[] CommentaryTabs() => CommentariesOn
        ? [new(CommentaryTab, K("Kommentare"), L("Freie Bibelkommentare (Matthew Henry, Jamieson-Fausset-Brown, Keil-Delitzsch, Tyndale u. a.) zum aktuellen Kapitel; der angeklickte Vers wird hervorgehoben."))]
        : [];

    /// <summary>The compact look: like in the early versions, everything in the side panel. The work areas Studium and Original are gone, their tabs sit in Vers, Suchen and Planen again.</summary>
    static bool CompactLook;

    /// <summary>The name a group shows: "Markierung" is the explorer of marks and notes (the side panel has "Markierungen" as well), "Mehr" holds the settings.</summary>
    static string RailName(string group) => group switch { "Mehr" => L("Optionen"), "Markierung" => L("Explorer"), _ => L(group) };

    /// <summary>The title of a group as a tab: the settings are "Einstellungen", the materials "Materialien" (the rail has room for "Material" only).</summary>
    static string GroupTitle(string group) => group switch { "Mehr" => "Einstellungen", "Material" => K("Materialien"), _ => group };

    static NavGroup[] MakeNavGroups()
    {
        var all = BuildNavGroups();
        if (!CompactLook) return all;
        var pool = all.SelectMany(g => g.Tabs).ToDictionary(t => t.Index);
        NavTab[] Pick(params int[] idx) => idx.Where(pool.ContainsKey).Select(i => pool[i]).ToArray();
        return all.Where(g => g.Name != "Studium" && g.Name != "Lernen").Select(g => g.Name switch
        {
            "Suchen" => g with { Tabs = Pick(0, StrongTab) },
            "Vers" => g with { Tabs = Pick(GuideTab, 3, PericopeTab, SentenceTab, GrundtextTab, 4, 5, AnalysisTab) },
            "Planen" => g with { Tabs = [.. g.Tabs, .. Pick(LearnTab)] },
            _ => g,
        }).ToArray();
    }

    static NavGroup[] BuildNavGroups() =>
    [
        new(K("Suchen"), "M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0z M21 21l-4.3-4.3",
        [
            new(0, K("Text"), L("Sucht in der gewählten Übersetzung: Wörter, „Wendungen“, Grundformen (L=…), Bibelstellen und Strong-Nummern.")),
        ]),
        new(K("Vers"), "M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z",
        [
            new(GuideTab, K("Übersicht"), L("Alles zum angeklickten Vers auf einen Blick: deine Notizen und Dokumente, andere Übersetzungen, Querverweise. Tipp: Rechtsklick auf einen Vers.")),
            new(GrundtextTab, K("Grundtext"), L("Der angeklickte Vers im Original, Wort für Wort mit Bedeutung.")),
            new(3, K("Wort"), L("Schalte oben „Wortstudie“ ein und tippe ein unterstrichenes Wort an: Bedeutung im Urtext.")),
            new(4, K("Querverweise"), L("Verse, die mit dem angeklickten Vers zusammenhängen.")),
            new(5, K("Vorkommen"), L("Alle Stellen, an denen ein Wort (Strong-Nummer) vorkommt, mit Verstext.")),
        ]),
        new(K("Bücher"), "M4 19.5A2.5 2.5 0 0 1 6.5 17H20 M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z",
        [
            ..CommentaryTabs(),
            new(FathersTab, K("Kirchenväter"), L("Was die Kirchenväter und Ausleger der Kirchengeschichte zum angeklickten Vers geschrieben haben (gemeinfreie Sammlung).")),
            new(LexiconTab, K("Lexikon"), L("Bibellexikon zu Personen und Orten (Easton's Bible Dictionary über Theographic). Oben stehen die Namen des aktuellen Kapitels.")),
        ], Windowed: true),
        new(K("Notizen"), "M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z",
        [
            new(2, K("Notizen"), L("Schreibe eine kurze Notiz zum angeklickten Vers. Darunter stehen alle deine Notizen; Klick springt zur Stelle.")),
            new(1, K("Markierungen"), L("Farbig markierte Wörter und Verse mit eigener Notiz, in Ordnern und mit Tags. Klick springt zur Stelle.")),
        ]),
        new(K("Markierung"), "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z M7 13h6",
        [
            new(ExplorerTab, K("Explorer"), L("Alle Markierungen und Notizen wie im Datei-Explorer: Ordner und Unterordner anlegen, Verse und Ordner per Ziehen einordnen. Doppelklick öffnet.")),
        ], Windowed: true),
        new(K("Merken"), "M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z",
        [
            new(BookmarksTab, K("Lesezeichen"), L("Deine gemerkten Verse: einfach eine Liste zum späteren Zurückspringen, ohne Notizen.")),
        ]),
        new(K("Dokumente"), "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M8 13h8 M8 17h8",
        [
            new(KnowledgeTab, K("Schreiben"), L("Eigene Texte und verknüpfte Dateien. Verse wie „Joh 3,16“ und [[Titel]] werden zu Links.")),
        ], Windowed: true),
        new(K("Material"), "M2 7a2 2 0 0 1 2-2h5l2 2h9a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2z M8 13h8 M8 16h5",
        [
            new(MaterialsTab, K("Materialien"), L("Die Unterlagen einer Bibelstunde: wähle einen Ordner, klicke eine Datei an, und sie öffnet sich hier. Tab wechselt zwischen den geöffneten Dateien.")),
            new(LiveTab, K("Live"), L("Die Bibelstunde auf den Handys: QR-Code scannen, mitlesen, Handout ansehen, Fragen schicken und abstimmen. Ohne App, im selben WLAN.")),
            new(ArchiveTab, K("Archiv"), L("Welche Stelle wann in der Bibelstunde dran war, mit Notizen und den Fragen des Abends.")),
        ], Windowed: true),
        new(K("Predigt"), "M4 21h16 M6 21V8l6-5 6 5v13 M12 3v5 M10 21v-5h4v5",
        [
            new(SermonTab, K("Predigt-Editor"), L("Predigt vorbereiten: Text, Kernaussage, Gliederung mit Versen, Notizen und Zeitplan; Export als Handout.")),
        ], Windowed: true),
        new(K("Planen"), "M10 12h11 M10 18h11 M10 6h11 M4 10h2 M4 6h1v4 M6 18H4c0-1 2-2 2-3s-1-1.5-2-1",
        [
            new(6, K("Leseliste"), L("Verse, die du der Reihe nach durchgehen willst.")),
            new(7, K("To-dos"), L("Deine To-dos fürs Studium, auf Wunsch mit Vers.")),
            new(PrayerTab, K("Gebete"), L("Deine Gebetsliste: Anliegen mit Datum und Vers, erhörte Gebete abhaken und festhalten.")),
            new(PlanTab, K("Lesepläne"), L("Bibel in einem Jahr und weitere Pläne: jeden Tag ein paar Kapitel, mit Fortschritt und Serie.")),
            new(MemoryTab, K("Auswendig lernen"), L("Bibelverse auswendig lernen: Lücken wachsen mit dem Können, mit Wiederholung.")),
            new(StatsTab, K("Statistik"), L("Wie viel du gelesen hast: Fortschritt in der Bibel, Serie, die letzten Wochen und was dein Vault enthält.")),
        ], Windowed: true),
        new(K("Zusammen"), "M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8a4 4 0 0 0 0 8z M23 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75",
        [
            new(CollabTab, K("Zusammen studieren"), L("Mit einer anderen Person direkt verbunden studieren: Chat, Verse und Dokumente teilen, die Bibelstelle mitlesen. Verschlüsselt, ohne Server und kostenlos.")),
        ]),
        new(K("KI"), "M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z M19 3v4 M17 5h4 M5 17v4 M3 19h4",
        [
            new(AiTab, K("Claude"), L("Dein lokaler Claude-Assistent. Er liest die Bibel über die Werkzeuge dieses Programms.")),
        ]),
        new(K("Studium"), "M22 10L12 5 2 10l10 5 10-5z M6 12v5c3 3 9 3 12 0v-5",
        [
            new(PericopeTab, K("Perikope"), L("Ein Abschnitt in Bildern: Wortarten, Verbformen, Schlüsselwörter und wo sie vorkommen, Rhythmus der Verse, Satzbau und Verbindungen der Sätze.")),
            new(SentenceTab, K("Satzdiagramm"), L("Der Satz des angeklickten Verses als klassisches Satzdiagramm: Subjekt, Verb, Objekt auf der Linie, alles andere darunter. Aus den Syntaxdaten des griechischen und hebräischen Textes.")),
            new(AnalysisTab, K("Analyse"), L("Werkzeuge zum besseren Verstehen: Schlüsselwörter, einmalige Wörter, verwandte Wörter und wo ein Wort in der Bibel vorkommt.")),
            new(SynopsisTab, K("Synopse"), L("Die Evangelien nebeneinander: dieselbe Begebenheit bei Matthäus, Markus, Lukas und Johannes, Unterschiede farbig.")),
        ], Windowed: true),
        new(K("Lernen"), "M5 8l6 6 M4 14l6-6 2-3 M2 5h12 M7 2h1 M22 22l-5-10-5 10 M14 18h6",
        [
            new(StrongTab, K("Strong-Wörterbuch"), L("Schlägt Wörter des Urtextes nach: Nummer (G3056), griechisches/hebräisches Wort, Umschrift oder englische Bedeutung.")),
            new(LearnTab, K("Sprachen"), L("Griechisch und Hebräisch lernen: Alphabet, Buchstaben-Quiz und Vokabeln mit Wiederholung.")),
        ], Windowed: true),
        new(K("Mehr"), "M21 4h-7 M10 4H3 M21 12h-9 M8 12H3 M21 20h-5 M12 20H3 M14 2v4 M8 10v4 M16 18v4",
        [
            new(SettingsTab, K("Einstellungen"), L("Aussehen, Verhalten, Vault, Sicherung, eigene Übersetzungen.")),
        ], Windowed: true),
        new(K("Hilfe"), "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0z M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3 M12 17h.01",
        [
            new(13, K("Hilfe"), L("So funktioniert das Programm.")),
            new(HandbookTab, K("Dokumentation"), L("Das ausführliche Handbuch: jede Funktion, jedes Tastenkürzel, Anwendung und Strategien. Mit Inhaltsverzeichnis und Suche.")),
            new(14, K("Maße & Info"), L("Maße, Gewichte und Abkürzungen aus der Bibel.")),
        ], Windowed: true),
    ];

    ComboBox? VaultQuickBox;
    Border? _vaultBar;

    /// <summary>The active vault is always visible in the collection, with a switcher and "new" – like a notebook in Obsidian.</summary>
    void BuildVaultBar()
    {
        VaultQuickBox = new ComboBox { MinWidth = 150, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        VaultQuickBox.SelectionChanged += async (_, _) => { if (!_vaultUiLoading && VaultQuickBox.SelectedItem is string n && n != _vault.Name) await SwitchVaultAsync(n); };
        var name = new TextBox { PlaceholderText = L("Name des neuen Vaults"), Width = 200 };
        var create = new Button { Content = L("Anlegen"), Classes = { "primary" }, Margin = new(0, 8, 0, 0) };
        var flyout = new Flyout { Content = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = L("Neuer Vault"), FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 6) }, name, create } } };
        create.Click += async (_, _) => { NewVaultBox.Text = name.Text; name.Text = ""; flyout.Hide(); await CreateVaultAsync(); };
        var add = new Button { Content = "＋", Classes = { "tool" }, Width = 32, Height = 32, Flyout = flyout };
        ToolTip.SetTip(add, L("Neuen Vault anlegen"));
        var pill = new Border { Classes = { "pill" }, Child = VaultQuickBox };
        Avalonia.Controls.DockPanel.SetDock(add, Avalonia.Controls.Dock.Right);
        var label = new TextBlock { Text = L("VAULT"), Classes = { "eyebrow" }, Margin = new(0, 0, 8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Avalonia.Controls.DockPanel.SetDock(label, Avalonia.Controls.Dock.Left);
        _vaultBar = new Border { Child = new DockPanel { Children = { label, add, pill } }, IsVisible = false };
        ToolTip.SetTip(_vaultBar, L("Ein Vault ist deine Mappe: Markierungen, Notizen, Dokumente, Leseliste und To-dos gehören immer zu einem Vault."));
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
            var neu = new MenuItem { Header = L("＋  Neuer Vault …") }; neu.Click += (_, _) => flyout.ShowAt(Anchor(VaultTopBtn));
            menu.Items.Add(neu);
            menu.ShowAt(Anchor(VaultTopBtn));
        };
        RefreshVaultUi();
    }

    void InitNav()
    {
        BuildVaultBar();
        foreach (var group in NavGroups.Where(g => !g.Windowed))
        {
            var g = group;
            var icon = new Icon { Data = Geometry.Parse(g.Icon), IconSize = 22, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            var label = new TextBlock { Text = L(g.Name), FontSize = 11, FontWeight = FontWeight.Medium, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            var button = new Button { Classes = { "rail" }, Tag = g, Content = new StackPanel { Spacing = 4, Children = { icon, label } } };
            ToolTip.SetTip(button, (g.Tabs.Length > 1 ? string.Join(" · ", g.Tabs.Select(t => L(t.Label))) : g.Tabs[0].Hint) + "\n" + L("Aus der Leiste ziehen: eigenes Fenster."));
            // drag a tab out of the side panel = its own window
            Avalonia.Point? dragFrom = null; var dragged = false;
            button.AddHandler(PointerPressedEvent, (object? _, Avalonia.Input.PointerPressedEventArgs e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) { dragFrom = e.GetPosition(this); dragged = false; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
            button.PointerReleased += (_, _) => dragFrom = null;
            button.PointerMoved += (_, e) =>
            {
                if (dragFrom is not { } from || dragged) return;
                var pos = e.GetPosition(this);
                if (Math.Abs(pos.X - from.X) + Math.Abs(pos.Y - from.Y) < 40 || SidePanel.Bounds.Contains(pos)) return;
                dragged = true; dragFrom = null; e.Pointer.Capture(null);
                var idx = g.Tabs.Any(t => t.Index == SideTabs.SelectedIndex) ? SideTabs.SelectedIndex : g.Tabs[0].Index;
                PopOutTab(idx, this.PointToScreen(pos) - new PixelPoint(120, 20));
            };
            button.Click += (_, _) =>
            {
                if (dragged) { dragged = false; return; }
                SetPanelOpen(true);
                if (!g.Tabs.Any(t => t.Index == SideTabs.SelectedIndex)) SideTabs.SelectedIndex = g.Tabs[0].Index;
                SyncNav();
                // the tab may already be the selected one (nothing changes then), but still be empty
                if (SideTabs.SelectedIndex == CollabTab) TabShown(CollabTab);
            };
            Rail.Children.Add(button);
        }
        // the work areas (books, documents, sermon, planning, settings) are reachable from the rail too: a click opens them as a tab of the main window
        // (Advanced mode only; the "adv" tag lets the easy mode hide them)
        Rail.Children.Add(new Border { Height = 1, Margin = new(8, 6), Background = Res("BsBorder"), Tag = "adv" });
        foreach (var group in NavGroups.Where(g => g.Windowed))
        {
            var g = group;
            var icon = new Icon { Data = Geometry.Parse(g.Icon), IconSize = 22, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            var label = new TextBlock { Text = RailName(g.Name), FontSize = 11, FontWeight = FontWeight.Medium, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            var button = new Button { Classes = { "rail" }, Tag = "adv", Content = new StackPanel { Spacing = 4, Children = { icon, label } } };
            ToolTip.SetTip(button, label.Text + ": " + (g.Tabs.Length > 1 ? string.Join(" · ", g.Tabs.Select(t => L(t.Label))) : g.Tabs[0].Hint));   // the name too: in a low window only the symbol is left
            // the group is looked up by name at the click: it may have been built anew since (commentaries on or off)
            button.Click += (_, _) => OpenWork(NavGroups.FirstOrDefault(x => x.Name == g.Name) ?? g);
            Rail.Children.Add(button);
        }
        SideTabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != SideTabs || _revertingTab) return;
            // work areas open as their own window; the side panel keeps showing what it showed
            if (GroupOf(SideTabs.SelectedIndex) is { Windowed: true } wg)
            {
                var idx = SideTabs.SelectedIndex;
                _revertingTab = true; SideTabs.SelectedIndex = _lastSideIndex; _revertingTab = false;
                OpenWork(wg, idx);
                return;
            }
            _lastSideIndex = SideTabs.SelectedIndex;
            if (SideTabs.SelectedItem is TabItem { Content: Control page }) AnimateIn(page, 10, 0);
            if (SideTabs.SelectedIndex == AnalysisTab) BuildAnalysisUi();
            else if (SideTabs.SelectedIndex == CollabTab) TabShown(CollabTab);
            else if (SideTabs.SelectedIndex is SentenceTab or PericopeTab) TabShown(SideTabs.SelectedIndex);
            else if (SideTabs.SelectedIndex == 2 && _notesStale) RefreshNotesTree();
            SyncNav();
        };
        if (GroupOf(SideTabs.SelectedIndex) is { Windowed: true }) SideTabs.SelectedIndex = 0;
        _lastSideIndex = SideTabs.SelectedIndex;
        SyncNav();
    }

    void SyncNav()
    {
        var current = SideTabs.SelectedIndex;
        var group = NavGroups.FirstOrDefault(g => g.Tabs.Any(t => t.Index == current)) ?? NavGroups[0];
        foreach (var b in Rail.Children.OfType<Button>()) b.Classes.Set("active", ReferenceEquals(b.Tag, group));

        SubPills.Children.Clear();
        var shownTabs = group.Tabs.Where(t => !_settings.EasyMode || EasySideTabs.Contains(t.Index)).ToArray();   // Easy mode offers no pill for a tab it hides
        if (shownTabs.Length > 1)
            foreach (var tab in shownTabs)
            {
                var t = tab;
                var pill = new Button { Classes = { "seg" }, Content = L(t.Label) };
                pill.Classes.Set("active", t.Index == current);
                pill.Click += (_, _) => SideTabs.SelectedIndex = t.Index;
                SubPills.Children.Add(pill);
            }
        SubPills.IsVisible = shownTabs.Length > 1;
        if (_vaultBar is not null) _vaultBar.IsVisible = group.ShowVault;
        var hint = group.Tabs.FirstOrDefault(t => t.Index == current)?.Hint ?? "";
        if (group.ShowVault) hint += $"";
        SubHint.Text = hint;   // the tip about dragging stood under every tab; it is in the tooltip of the rail now
    }
}
