using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Work areas (books, documents, planning, settings) are tabs of the main window, like in a browser: a click switches in place.
/// Dragging a tab out of the strip makes it a loose window (also on a second monitor); dragging that window back onto the strip
/// snaps it in again. Single side panel tabs can be dragged out as well. The arrangement can be saved as a named layout.
/// </summary>
public partial class MainWindow
{
    /// <summary>One work area: its content either sits in the main window (Win == null) or in a window of its own.</summary>
    sealed class Detached
    {
        public Window? Win { get; set; }
        public required List<(TabItem Tab, Control Content, Border Wrap)> Items { get; init; }
        public required string Key { get; init; }
        public required string Title { get; init; }
        public required Control Panel { get; init; }
        public required Grid Host { get; init; }
        public required List<int> Indices { get; init; }
        public required bool Side { get; init; }        // a single side panel tab that was dragged out
        public Action<int>? SelectAction { get; set; }
        public int Current { get; set; }
        public bool Redock { get; set; }
    }

    readonly Dictionary<string, Detached> _windows = [];
    string? _activeWork;
    int _lastSideIndex;
    bool _revertingTab;
    bool _exiting;

    static readonly string[] WorkTabs = ["Bücher", "Dokumente", "Planen"];

    NavGroup? GroupOf(int tabIndex) => NavGroups.FirstOrDefault(g => g.Tabs.Any(t => t.Index == tabIndex));

    string TabLabel(int index) => GroupOf(index) is { } g && g.Tabs.First(t => t.Index == index) is var t ? (g.Tabs.Length > 1 ? $"{g.Name} – {t.Label}" : g.Name) : $"Fenster {index}";

    TabItem? TabItemAt(int index) => SideTabs.Items.OfType<TabItem>().ElementAtOrDefault(index);

    /// <summary>True while the tab is on screen: in the side panel, in the main window's work area or in its own window.</summary>
    bool TabActive(int index) =>
        SideTabs.SelectedIndex == index || _windows.Values.Any(d => d.Current == index && (d.Win is not null || d.Key == _activeWork));

    /// <summary>A tab became visible: load what it shows (for the current chapter, vault, settings …).</summary>
    void TabShown(int index)
    {
        if (index == CommentaryTab) _ = ShowCommentaryAsync();
        else if (index == LexiconTab) _ = ShowLexiconAsync();
        else if (index == SettingsTab) UpdateOfflineInfo();
        else if (index == KnowledgeTab) RefreshDocs();
        else if (index == PlanTab) BuildPlanUi();
    }

    // ---- building a work area ------------------------------------------------------------------------------------------

    Detached? Build(string key, List<int> indices, string title, int startIndex, bool side)
    {
        if (_windows.TryGetValue(key, out var open)) return open;
        var items = new List<(TabItem, Control, Border)>();
        foreach (var i in indices)
            if (TabItemAt(i) is { Content: Control c } tab) items.Add((tab, c, new Border()));
        if (items.Count == 0) return null;

        var host = new Grid();
        var pills = new WrapPanel { Margin = new(14, 10, 14, 0) };
        var hint = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(14, 4, 14, 4) };
        var group = GroupOf(indices[0]);
        // detach first, then attach: a control may have only one parent
        foreach (var it in items) { it.Item1.Content = null; it.Item3.Child = it.Item2; host.Children.Add(it.Item3); }

        var panel = new DockPanel();
        if (items.Count > 1) { DockPanel.SetDock(pills, Dock.Top); panel.Children.Add(pills); DockPanel.SetDock(hint, Dock.Top); panel.Children.Add(hint); }
        panel.Children.Add(host);

        var d = new Detached { Items = items, Key = key, Title = title, Panel = panel, Host = host, Indices = indices, Side = side };
        _windows[key] = d;

        void Select(int index)
        {
            d.Current = index;
            for (var k = 0; k < items.Count; k++) items[k].Item3.IsVisible = indices[k] == index;
            foreach (var b in pills.Children.OfType<Button>()) b.Classes.Set("active", (int)b.Tag! == index);
            hint.Text = group?.Tabs.FirstOrDefault(t => t.Index == index)?.Hint ?? "";
            TabShown(index);
        }
        if (items.Count > 1)
            foreach (var t in group!.Tabs.Where(t => indices.Contains(t.Index)))
            {
                var idx = t.Index;
                var b = new Button { Classes = { "seg" }, Content = t.Label, Tag = idx };
                b.Click += (_, _) => Select(idx);
                pills.Children.Add(b);
            }
        d.SelectAction = Select;

        if (side)
        {
            var back = new Button { Content = "Zurück in die Leiste holen", HorizontalAlignment = HorizontalAlignment.Left, Classes = { "primary" } };
            back.Click += (_, _) => d.Win?.Close();
            items[0].Item1.Content = new StackPanel
            {
                Margin = new(16, 24), Spacing = 10,
                Children = { new TextBlock { Text = "Dieser Bereich ist ausgelagert.", FontWeight = FontWeight.SemiBold }, new TextBlock { Text = "Ziehe das Fenster zurück auf die Leiste, oder klicke:", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }, back },
            };
        }
        Select(indices.Contains(startIndex) ? startIndex : indices[0]);
        return d;
    }

    /// <summary>Puts the content back into its (hidden) tab items and forgets the work area.</summary>
    void Restore(Detached d)
    {
        if (!_windows.Remove(d.Key)) return;
        d.Host.Children.Clear();
        foreach (var (tab, c, wrap) in d.Items) { wrap.Child = null; tab.Content = c; }
        if (d.Panel.Parent is Panel p) p.Children.Remove(d.Panel);
        if (_activeWork == d.Key) ShowWork(null); else RebuildTabStrip();
        SyncNav();
    }

    // ---- docked: tabs of the main window ---------------------------------------------------------------------------------

    void OpenWork(NavGroup g, int? start = null)
    {
        var key = "group:" + g.Name;
        if (_windows.TryGetValue(key, out var d))
        {
            if (start is int s) d.SelectAction?.Invoke(s);
            if (d.Win is not null) d.Win.Activate(); else ShowWork(key);
            return;
        }
        d = Build(key, g.Tabs.Select(t => t.Index).ToList(), g.Name == "Mehr" ? "Einstellungen" : g.Name, start ?? g.Tabs[0].Index, side: false);
        if (d is null) return;
        DockWork(d);
        ShowWork(key);
    }

    void DockWork(Detached d)
    {
        d.Win = null;
        d.Panel.IsVisible = false;
        if (d.Panel.Parent is null) WorkHost.Children.Add(d.Panel);
    }

    /// <summary>Shows the work tab with the key in the main window, or the reader when null.</summary>
    void ShowWork(string? key)
    {
        if (key is not null && !(_windows.TryGetValue(key, out var d) && d.Win is null)) key = null;
        var before = _activeWork;
        _activeWork = key;
        foreach (var w in _windows.Values.Where(w => w.Win is null)) w.Panel.IsVisible = w.Key == key;
        if (key is not null && key != before && _windows.TryGetValue(key, out var fresh)) AnimateIn(fresh.Panel);
        WorkHost.IsVisible = key is not null;
        UpdateReadBar();
        RebuildTabStrip();
        if (key is not null && _windows.TryGetValue(key, out var shown)) TabShown(shown.Current);
    }

    void UpdateReadBar() => ReadBar.IsVisible = ReaderHost.IsVisible && !WorkHost.IsVisible && !_settings.FocusMode;

    // ---- tab strip -------------------------------------------------------------------------------------------------------

    void RebuildTabStrip()
    {
        TabStrip.Children.Clear();
        TabStrip.Children.Add(MakeTab("Lesen", null, "M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z"));
        foreach (var d in _windows.Values.Where(w => !w.Side).ToList())
        {
            var g = NavGroups.First(x => "group:" + x.Name == d.Key);
            TabStrip.Children.Add(MakeTab(d.Title, d.Key, g.Icon, closable: true));
        }
        // "+" opens a new tab, like in a browser or the Explorer
        var plus = new Button { Classes = { "tool" }, Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 0, 2), Content = new Icon { Data = Geometry.Parse("M12 5v14 M5 12h14"), IconSize = 14 } };
        ToolTip.SetTip(plus, "Neuer Reiter");
        plus.Click += (_, _) =>
        {
            var menu = new MenuFlyout();
            foreach (var name in new[] { "Bücher", "Dokumente", "Planen", "Mehr" })
            {
                var g = NavGroups.First(x => x.Name == name);
                var item = new MenuItem { Header = name == "Mehr" ? "Einstellungen" : name, IsEnabled = !_windows.ContainsKey("group:" + name) };
                item.Click += (_, _) => OpenWork(g, name == "Mehr" ? SettingsTab : null);
                menu.Items.Add(item);
            }
            menu.ShowAt(plus);
        };
        TabStrip.Children.Add(plus);
    }

    Button MakeTab(string label, string? key, string icon, bool closable = false)
    {
        var windowed = key is not null && _windows.TryGetValue(key, out var w) && w.Win is not null;
        var active = !windowed && key == _activeWork;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        row.Children.Add(new Icon { Data = Geometry.Parse(icon), IconSize = 16, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = label + (windowed ? "  ⧉" : ""), VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Classes = { "wtab" }, Content = row };
        b.Classes.Set("active", active);
        ToolTip.SetTip(b, key is null ? "Zurück zum Bibeltext" : windowed ? "Liegt in einem eigenen Fenster – klicken holt es nach vorn" : "Klicken wechselt hierher. Nach unten ziehen macht ein eigenes Fenster daraus.");
        if (closable)
        {
            var x = new Button { Classes = { "tool" }, Width = 22, Height = 22, Padding = new(0), Content = new Icon { Data = Geometry.Parse("M18 6L6 18 M6 6l12 12"), IconSize = 12 } };
            x.Click += (_, e) => { e.Handled = true; if (key is not null && _windows.TryGetValue(key, out var d)) { if (d.Win is not null) d.Win.Close(); else Restore(d); } };
            row.Children.Add(x);
        }

        Point? from = null; var dragged = false;
        b.AddHandler(PointerPressedEvent, (object? _, PointerPressedEventArgs e) => { if (key is not null && e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) { from = e.GetPosition(this); dragged = false; } }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        b.PointerReleased += (_, _) => from = null;
        b.PointerMoved += (_, e) =>
        {
            if (from is null || dragged || key is null) return;
            var pos = e.GetPosition(this);
            var top = TabBar.TranslatePoint(new Point(0, 0), this)?.Y ?? 60;
            if (pos.Y < top + TabBar.Bounds.Height + 34 && pos.Y > top - 34) return;
            dragged = true; from = null; e.Pointer.Capture(null);
            OpenWorkWindow(NavGroups.First(g => "group:" + g.Name == key), this.PointToScreen(pos) - new PixelPoint(160, 20));
        };
        b.Click += (_, _) =>
        {
            if (dragged) { dragged = false; return; }
            if (key is null) { ShowReader(); return; }
            OpenWork(NavGroups.First(g => "group:" + g.Name == key));
        };
        return b;
    }

    // ---- windows ---------------------------------------------------------------------------------------------------------

    void OpenWorkWindow(NavGroup g, PixelPoint? at)
    {
        var key = "group:" + g.Name;
        if (_windows.TryGetValue(key, out var d))
        {
            if (d.Win is not null) { d.Win.Activate(); return; }
            WorkHost.Children.Remove(d.Panel); d.Panel.IsVisible = true;
            var wasActive = _activeWork == key;
            OpenWindowFor(d, at);
            if (wasActive) ShowWork(null);
            return;
        }
        d = Build(key, g.Tabs.Select(t => t.Index).ToList(), g.Name == "Mehr" ? "Einstellungen" : g.Name, g.Tabs[0].Index, side: false);
        if (d is not null) OpenWindowFor(d, at);
    }

    void PopOutTab(int index, PixelPoint? at = null)
    {
        var d = Build("tab:" + index, [index], TabLabel(index), index, side: true);
        if (d is not null && d.Win is null) OpenWindowFor(d, at);
        else d?.Win?.Activate();
    }

    void OpenWindowFor(Detached d, PixelPoint? at)
    {
        var saved = _settings.Windows.TryGetValue(d.Key, out var s) && s.W > 0 ? s : null;
        var win = new Window
        {
            Title = $"BibleStudy – {d.Title}", Width = saved?.W ?? (d.Items.Count > 1 ? 640 : 520), Height = saved?.H ?? 800, MinWidth = 320, MinHeight = 240,
            Background = Res("BsSurface"), Content = d.Panel, Icon = Icon,
            WindowStartupLocation = saved is null && at is null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.Manual,
        };
        if (at is { } p) win.Position = p; else if (saved is not null) win.Position = new PixelPoint(saved.X, saved.Y);
        if (saved is { Maximized: true }) win.WindowState = WindowState.Maximized;
        d.Win = win; d.Panel.IsVisible = true;

        win.Closing += (_, _) =>
        {
            SaveWindowState(d.Key, win);
            win.Content = null; d.Win = null;
            if (d.Redock && !d.Side) { d.Redock = false; DockWork(d); ShowWork(d.Key); }
            else Restore(d);
        };

        // dragging the loose window back over the tab strip (or the side panel for side tabs) docks it again
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_windows.ContainsKey(d.Key) || d.Win is null) return;
            var target = d.Side ? SidePanel : (Control)TabBar;
            if (!target.IsVisible) return;
            var scale = RenderScaling;
            var pad = d.Side ? 0 : (int)(20 * scale);
            var topLeft = target.PointToScreen(new Point(0, 0));
            var rect = new PixelRect(topLeft.X, topLeft.Y - pad, (int)(target.Bounds.Width * scale), (int)(target.Bounds.Height * scale) + 2 * pad);
            var center = new PixelPoint(win.Position.X + (int)(win.Bounds.Width * scale / 2), win.Position.Y + (int)(14 * scale));
            if (rect.Contains(center)) { d.Redock = true; win.Close(); }
        };
        win.PositionChanged += (_, _) => { timer.Stop(); timer.Start(); };

        win.Show(this);
        RebuildTabStrip();
        SyncNav();
    }

    void SaveWindowState(string key, Window w)
    {
        _settings.Windows[key] = new WindowBounds(w.Position.X, w.Position.Y, (int)w.Width, (int)w.Height, w.WindowState == WindowState.Maximized) { Key = key };
        SaveSettings();
    }

    void CloseAllDetached()
    {
        foreach (var d in _windows.Values.ToList())
        {
            if (d.Win is not null) d.Win.Close(); else Restore(d);
        }
    }

    // ---- layouts (desktops) --------------------------------------------------------------------------------------------

    LayoutPreset CaptureLayout() => new()
    {
        SideOpen = SidePanel.IsVisible, SideWidth = SidePanel.Width, SideTab = _lastSideIndex, ActiveWork = _activeWork,
        Windows = _windows.Values.Select(d => d.Win is { } w
            ? new WindowBounds(w.Position.X, w.Position.Y, (int)w.Width, (int)w.Height, w.WindowState == WindowState.Maximized) { Key = d.Key }
            : new WindowBounds(0, 0, 0, 0, false) { Key = d.Key }).ToList(),
    };

    void ApplyLayout(LayoutPreset l, string? name = null)
    {
        CloseAllDetached();
        SidePanel.Width = Math.Clamp(l.SideWidth, 300, 900); _settings.SideWidth = SidePanel.Width;
        SetPanelOpen(l.SideOpen);
        if (l.SideTab >= 0 && l.SideTab < SideTabs.ItemCount && GroupOf(l.SideTab) is { Windowed: false }) SideTabs.SelectedIndex = l.SideTab;
        OpenLayoutWindows(l);
        SetActiveLayout(name);
    }

    /// <summary>Opens the tabs and windows a layout lists (also used to restore the last session).</summary>
    void OpenLayoutWindows(LayoutPreset l)
    {
        foreach (var w in l.Windows)
        {
            if (w.Key.StartsWith("group:") && NavGroups.FirstOrDefault(g => "group:" + g.Name == w.Key) is { } g)
            {
                if (w.W > 0) { _settings.Windows[w.Key] = w; OpenWorkWindow(g, null); }
                else OpenWork(g);
            }
            else if (w.Key.StartsWith("tab:") && int.TryParse(w.Key[4..], out var idx)) { _settings.Windows[w.Key] = w; PopOutTab(idx); }
        }
        ShowWork(l.ActiveWork);
        SaveSettings();
    }

    void SetActiveLayout(string? name)
    {
        _settings.ActiveLayout = name; SaveSettings();
        ToolTip.SetTip(LayoutTopBtn, name is null ? "Layouts: Anordnung speichern und laden" : $"Layouts – aktiv: {name}");
    }

    static readonly string[] BuiltInLayouts = ["Lesen", "Studium", "Schreiben"];

    LayoutPreset? BuiltIn(string name) => name switch
    {
        "Lesen" => new() { SideOpen = false },
        "Studium" => new() { SideOpen = true, SideWidth = 460, SideTab = GuideTab },
        "Schreiben" => new() { SideOpen = true, SideWidth = 400, SideTab = GuideTab, ActiveWork = "group:Dokumente", Windows = [new(0, 0, 0, 0, false) { Key = "group:Dokumente" }] },
        _ => null,
    };

    LayoutPreset? FindLayout(string name) => _settings.Layouts.GetValueOrDefault(name) ?? BuiltIn(name);

    void SaveLayoutAs(string name)
    {
        _settings.Layouts[name] = CaptureLayout();
        SetActiveLayout(name);
        RefreshLayoutBox(name);
        StatusText.Text = $"Layout „{name}“ gespeichert: Leiste, Breite, Reiter und offene Fenster mit Position.";
    }

    /// <summary>Layout panel: all layouts as a list (click = load), a name field to save the current arrangement as a new layout.</summary>
    void ShowLayoutMenu()
    {
        var fly = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var panel = new StackPanel { Spacing = 6, Width = 300 };
        fly.Content = panel;
        var name = new TextBox { Watermark = "Name, z. B. „Predigtvorbereitung“" };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

        void Fill()
        {
            panel.Children.Clear();
            panel.Children.Add(new TextBlock { Text = "LAYOUTS", Classes = { "eyebrow" } });
            panel.Children.Add(new TextBlock { Text = "Ein Layout merkt sich Seitenleiste, Reiter und Fenster mit Position. Klick lädt es.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            foreach (var n0 in BuiltInLayouts.Concat(_settings.Layouts.Keys.Except(BuiltInLayouts).Order()))
            {
                var n = n0; var custom = _settings.Layouts.ContainsKey(n);
                var load = new Button { Classes = { "seg" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = (n == _settings.ActiveLayout ? "✓  " : "     ") + n };
                load.Classes.Set("active", n == _settings.ActiveLayout);
                load.Click += (_, _) => { ApplyLayout(FindLayout(n)!, n); StatusText.Text = $"Layout „{n}“ geladen."; Fill(); };
                var row = new DockPanel();
                if (custom)
                {
                    var del = new Button { Classes = { "tool" }, Width = 28, Height = 28, Content = new Icon { Data = Geometry.Parse("M18 6L6 18 M6 6l12 12"), IconSize = 13 } };
                    ToolTip.SetTip(del, "Layout löschen");
                    del.Click += (_, _) => { _settings.Layouts.Remove(n); if (_settings.ActiveLayout == n) SetActiveLayout(null); else SaveSettings(); RefreshLayoutBox(); Fill(); };
                    DockPanel.SetDock(del, Dock.Right); row.Children.Add(del);
                    var over = new Button { Classes = { "tool" }, Width = 28, Height = 28, Content = new Icon { Data = Geometry.Parse("M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z M17 21v-8H7v8 M7 3v5h8"), IconSize = 13 } };
                    ToolTip.SetTip(over, "Mit der aktuellen Anordnung überschreiben");
                    over.Click += (_, _) => { SaveLayoutAs(n); info.Text = $"„{n}“ überschrieben."; Fill(); };
                    DockPanel.SetDock(over, Dock.Right); row.Children.Add(over);
                }
                row.Children.Add(load);
                panel.Children.Add(row);
            }
            panel.Children.Add(new Separator());
            panel.Children.Add(new TextBlock { Text = "NEUES LAYOUT SPEICHERN", Classes = { "eyebrow" } });
            panel.Children.Add(name);
            var save = new Button { Content = "Als neues Layout speichern", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch };
            save.Click += (_, _) => Commit();
            panel.Children.Add(save);
            panel.Children.Add(info);
        }

        void Commit()
        {
            var n = name.Text?.Trim() ?? "";
            if (n.Length == 0) { info.Text = "Gib dem Layout einen Namen."; return; }
            if (BuiltInLayouts.Contains(n)) { info.Text = "Diesen Namen tragen schon eingebaute Layouts – wähle einen anderen."; return; }
            var existed = _settings.Layouts.ContainsKey(n);
            SaveLayoutAs(n); name.Text = ""; Fill();
            info.Text = existed ? $"„{n}“ überschrieben." : $"Neues Layout „{n}“ gespeichert.";
        }
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        Fill();
        fly.ShowAt(LayoutTopBtn);
    }

    void InitWindows()
    {
        LayoutTopBtn.Click += (_, _) => ShowLayoutMenu();
        SettingsTopBtn.Click += (_, _) => OpenWork(NavGroups.First(g => g.Name == "Mehr"), SettingsTab);
        PopOutDocsBtn.Click += (_, _) => OpenWork(NavGroups.First(g => g.Name == "Dokumente"));
        // anything else that takes over the main area ends the work tab
        foreach (var pane in new Control[] { Home, GraphPane, MapPane, TimelinePane, KnowledgePane })
            pane.PropertyChanged += (_, e) => { { if (e.Property != IsVisibleProperty || !pane.IsVisible) return; AnimateIn(pane); if (_activeWork is not null) ShowWork(null); } };
        WorkHost.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) UpdateReadBar(); };
        RebuildTabStrip();
        SetActiveLayout(_settings.ActiveLayout);
        RefreshLayoutBox();
        LayoutLoadBtn.Click += (_, _) =>
        {
            var name = LayoutBox.SelectedItem as string;
            var preset = name is null ? null : FindLayout(name);
            if (preset is null) { LayoutInfo.Text = "Wähle zuerst ein Layout."; return; }
            ApplyLayout(preset, name); LayoutInfo.Text = $"Layout „{name}“ geladen.";
        };
        LayoutSaveBtn.Click += (_, _) =>
        {
            var name = LayoutNameBox.Text?.Trim() ?? "";
            if (name.Length == 0) { LayoutInfo.Text = "Gib dem Layout einen Namen."; return; }
            if (BuiltInLayouts.Contains(name)) { LayoutInfo.Text = "Diesen Namen tragen schon eingebaute Layouts."; return; }
            LayoutNameBox.Text = ""; SaveLayoutAs(name); LayoutInfo.Text = $"Layout „{name}“ gespeichert (neues Layout).";
        };
        LayoutDeleteBtn.Click += (_, _) =>
        {
            if (LayoutBox.SelectedItem is string n && _settings.Layouts.Remove(n)) { if (_settings.ActiveLayout == n) SetActiveLayout(null); SaveSettings(); RefreshLayoutBox(); LayoutInfo.Text = $"Layout „{n}“ gelöscht."; }
            else LayoutInfo.Text = "Eingebaute Layouts lassen sich nicht löschen.";
        };
    }

    void RefreshLayoutBox(string? select = null)
    {
        var names = BuiltInLayouts.Concat(_settings.Layouts.Keys.Except(BuiltInLayouts).Order()).ToList();
        LayoutBox.ItemsSource = names;
        LayoutBox.SelectedItem = select ?? _settings.ActiveLayout ?? names.First();
    }
}
