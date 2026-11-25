using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// The settings start with an overview: a search field and eight tiles (Appearance, Reading, Bibles and data …). A tile shows the cards of its
/// area, the search shows every card that has the words. The cards themselves are the ones of the page as before: nothing is left out.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// The areas with the cards they hold, by their position on the page (0 Display, 1 Reading, 2 Windows and layouts, 3 Shortcuts, 4 Program and start,
    /// 5 Vault, 6 Own translation, 7 Download manager, 8 Offline, 9 Sources, 10 Plugins, 11 Feedback, 12 Backup, 13 Sync, 14 AI, 15 Updates, 16 Help).
    /// </summary>
    static readonly (string Name, string Hint, string Icon, int[] Cards)[] SettingsAreas =
    [
        (K("Aussehen"), K("Sprache, Farben, Schrift"), "M12 22a10 10 0 1 1 10-10c0 2.2-1.8 3-3.5 3H16a2 2 0 0 0-1.5 3.3A1.6 1.6 0 0 1 12 22z M7.5 10.5h.01 M10.5 7.5h.01 M14.5 7.5h.01 M17.5 10.5h.01", [0]),
        (K("Lesen"), K("Markieren, Vorlesen, Verhalten"), "M2 4h6a4 4 0 0 1 4 4v12a3 3 0 0 0-3-3H2z M22 4h-6a4 4 0 0 0-4 4v12a3 3 0 0 1 3-3h7z", [1]),
        (K("Bibeln und Daten"), K("Downloads, Import, Quellen"), "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3", [7, 8, 6, 9, 10]),
        (K("Meine Daten"), K("Vaults, Sicherung, Import"), "M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.7-.9l-.8-1.2A2 2 0 0 0 7.9 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2z", [5, 12]),
        (K("Synchronisieren"), K("PC und Handy"), "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5 M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16 M16 16h5v5", [13]),
        (K("Fenster und Start"), K("Layouts, Tastenkürzel, Start"), "M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M2 9h20 M6 6.5h.01 M9 6.5h.01", [4, 2, 3]),
        (K("KI"), K("Claude, Ollama"), "M12 3l1.9 5.8L20 11l-6.1 2.2L12 19l-1.9-5.8L4 11l6.1-2.2z M19 3v4 M21 5h-4", [14]),
        (K("Hilfe und Updates"), K("Hilfe, Feedback, Updates"), "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M12 16v-4 M12 8h.01", [16, 15, 11]),
    ];

    readonly List<Border> _settingCards = [];
    readonly HashSet<Border> _platformHidden = [];
    readonly List<Control> _settingTiles = [];
    StackPanel? _settingsOverview, _settingsHead, _settingsResults;
    UniformGrid? _settingsTilesGrid;
    TextBlock? _settingsHeadTitle, _settingsNone;
    TextBox? _settingsSearch;

    void InitSettingsLayout()
    {
        var cards = SettingsStack.Children.OfType<Border>()
            .Where(b => b.Classes.Contains("card") && b.Child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock tb && tb.Classes.Contains("eyebrow"))
            .ToList();
        foreach (var card in cards)
        {
            var panel = (StackPanel)card.Child!;
            _settingCards.Add(card);
            // what only a computer has, left out on a phone: keyboard shortcuts, windows and layouts, start with Windows and the tray,
            // plugins (programs for a computer), and the AI (the Claude Code app, Ollama and the MCP server run on a computer)
            if (AppPlatform.IsMobile && panel.GetLogicalDescendants().Any(d => d == KeysPanel || d == LayoutNameBox || d == StartWithWindowsCheck || d == PluginsList || d == AiContextBox))
            {
                card.IsVisible = false; _platformHidden.Add(card); continue;
            }
            if (AppPlatform.IsMobile && panel.GetLogicalDescendants().Contains(StartMenuBtn)) ((TextBlock)panel.Children[0]).Text = L("FEEDBACK");
        }

        // the list of sections beside the page is not needed any more: the tiles and the search take its place
        if (SettingsJump.Parent is ScrollViewer tocScroll) (tocScroll.Parent as Panel)?.Children.Remove(tocScroll);
        else (SettingsJump.Parent as Panel)?.Children.Remove(SettingsJump);

        _settingsSearch = new TextBox { PlaceholderText = L("Einstellung suchen, z. B. „Schrift“ oder „Sicherung“"), Margin = new(0, 0, 0, 4) };
        _settingsSearch.TextChanged += (_, _) => ShowSettingsArea(null);
        _settingsNone = new TextBlock { Text = L("Keine Einstellung gefunden."), Classes = { "caption" }, IsVisible = false };
        _settingsTilesGrid = new UniformGrid { Columns = 2 };
        for (var a = 0; a < SettingsAreas.Length; a++)
        {
            var area = a;
            var (name, hint, icon, ids) = SettingsAreas[a];
            var content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    TileIcon(icon),
                    new TextBlock { Text = L(name), FontWeight = FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0) },
                    new TextBlock { Text = L(hint), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                },
            };
            var tile = new Button
            {
                Classes = { "ghost" }, Padding = new(0), Margin = new(4),
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = new Border { Classes = { "card" }, Child = content, MinHeight = 104 },
            };
            tile.Click += (_, _) => ShowSettingsArea(area);
            // an area whose cards are all for a computer only does not show on a phone
            tile.IsVisible = ids.Any(i => i < _settingCards.Count && !_platformHidden.Contains(_settingCards[i]));
            _settingTiles.Add(tile);
            _settingsTilesGrid.Children.Add(tile);
        }
        _settingsResults = new StackPanel { Spacing = 2 };
        _settingsOverview = new StackPanel { Spacing = 8, Children = { _settingsSearch, _settingsNone, _settingsResults, _settingsTilesGrid } };

        var back = new Button { Content = L("← Alle Einstellungen"), Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(8, 4) };
        back.Click += (_, _) => { if (_settingsSearch is { } s) s.Text = ""; ShowSettingsArea(null); };
        _settingsHeadTitle = new TextBlock { FontSize = 22, FontWeight = FontWeight.SemiBold, Margin = new(4, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        _settingsHead = new StackPanel { Spacing = 4, IsVisible = false, Children = { back, _settingsHeadTitle } };

        SettingsStack.Children.Insert(0, _settingsHead);
        SettingsStack.Children.Insert(0, _settingsOverview);
        // two tiles side by side on a phone, up to four on a wide screen
        SettingsScroll.SizeChanged += (_, e) => { if (_settingsTilesGrid is { } g) g.Columns = Math.Clamp((int)(e.NewSize.Width / 230), 2, 4); };
        ShowSettingsArea(null);
    }

    Icon TileIcon(string data)
    {
        var icon = new Icon { Data = Geometry.Parse(data), IconSize = 22, HorizontalAlignment = HorizontalAlignment.Left };
        icon.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        return icon;
    }

    int? _settingsArea;

    /// <summary>Null: the overview (or the found cards while something is typed in the search); a number: the cards of that area.</summary>
    void ShowSettingsArea(int? area)
    {
        if (_settingsOverview is null || _settingsHead is null) return;
        var query = area is null ? (_settingsSearch?.Text ?? "").Trim() : "";
        var searching = query.Length > 0;
        _settingsArea = area;
        _settingsOverview.IsVisible = area is null;
        _settingsTilesGrid!.IsVisible = !searching;
        _settingsHead.IsVisible = area is not null;
        if (area is { } a) _settingsHeadTitle!.Text = L(SettingsAreas[a].Name);
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = 0;
        _settingsResults!.Children.Clear();
        _resultSeen.Clear();
        // the order of the area (the most used card first), the rest of the page after them stays hidden
        var order = area is { } o ? SettingsAreas[o].Cards : [];
        for (var i = 0; i < _settingCards.Count; i++)
        {
            var card = _settingCards[i];
            if (_platformHidden.Contains(card)) continue;
            card.IsVisible = area is not null && order.Contains(i);
            if (searching) shown += AddResults(card, i, words);
        }
        if (area is not null)
        {
            // the cards of the area in its order, right after the heading
            var at = SettingsStack.Children.IndexOf(_settingsHead) + 1;
            foreach (var i in order)
                if (i < _settingCards.Count) { SettingsStack.Children.Remove(_settingCards[i]); SettingsStack.Children.Insert(at++, _settingCards[i]); }
        }
        _settingsNone!.IsVisible = searching && shown == 0;
        _settingsResults!.IsVisible = searching;
        SettingsScroll.Offset = default;
    }

    readonly HashSet<string> _resultSeen = [];

    /// <summary>
    /// The settings of a card whose label has all the words: one row each (the label, under it the area), a tap opens the area there.
    /// Labels are the texts of switches and buttons and the headings and captions before a choice.
    /// </summary>
    int AddResults(Border card, int index, string[] words)
    {
        var area = Array.FindIndex(SettingsAreas, a => a.Cards.Contains(index));
        if (area < 0) return 0;
        var count = 0;
        foreach (var d in card.GetLogicalDescendants().OfType<Control>())
        {
            if (_settingsResults!.Children.Count >= 40) break;
            if (!d.IsVisible || d.GetLogicalAncestors().OfType<Control>().Any(x => !x.IsVisible && x != card)) continue;
            var text = d switch
            {
                CheckBox { Content: string c } => c,
                RadioButton { Content: string r } => r,
                Button { Content: string b } => b,
                TextBlock tb when tb.Parent is not ContentControl && !tb.Classes.Contains("eyebrow") && (tb.Text?.Length ?? 0) is > 1 and < 90 => tb.Text,
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(text)) continue;
            var low = text.ToLowerInvariant();
            if (!words.All(w => low.Contains(w, StringComparison.Ordinal)) || !_resultSeen.Add(text)) continue;
            var target = d;
            var row = new Button
            {
                Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(10, 6),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = L(SettingsAreas[area].Name), Classes = { "caption" } },
                    },
                },
            };
            row.Click += (_, _) => RevealSetting(target);
            _settingsResults.Children.Add(row);
            count++;
        }
        return count;
    }

    /// <summary>Opens the area of the settings that holds this control (for the tour and for "take me to …"), then shows it.</summary>
    void RevealSetting(Control target)
    {
        var card = target.GetLogicalAncestors().OfType<Border>().FirstOrDefault(_settingCards.Contains) ?? (target as Border is { } b && _settingCards.Contains(b) ? b : null);
        if (card is null) return;
        var index = _settingCards.IndexOf(card);
        var area = Array.FindIndex(SettingsAreas, a => a.Cards.Contains(index));
        if (area < 0) return;
        if (_settingsArea != area) ShowSettingsArea(area);
        // the setting near the top, with what follows it below (only showing it at the lower edge hid what it is about)
        Dispatcher.UIThread.Post(() => target.BringIntoView(new Rect(0, 0, Math.Max(1, target.Bounds.Width), Math.Max(target.Bounds.Height, SettingsScroll.Bounds.Height * 0.75))), DispatcherPriority.Background);
    }
}
