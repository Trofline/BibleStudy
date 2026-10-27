using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Work areas (documents, books, planning, settings) live in their own windows so the Bible text stays calm.
/// Side panel tabs can be dragged out into windows (also onto a second monitor) and dragged back in.
/// The arrangement can be saved as a named layout ("desktop") and loaded again.
/// </summary>
public partial class MainWindow
{
    sealed class Detached
    {
        public required Window Win { get; init; }
        public required List<(TabItem Tab, Control Content, Border Wrap)> Items { get; init; }
        public required string Key { get; init; }
        public Action<int>? SelectAction { get; set; }
    }

    readonly Dictionary<string, Detached> _windows = [];
    int _lastSideIndex;
    bool _revertingTab;
    bool _exiting;

    NavGroup? GroupOf(int tabIndex) => NavGroups.FirstOrDefault(g => g.Tabs.Any(t => t.Index == tabIndex));

    string TabLabel(int index) => GroupOf(index) is { } g && g.Tabs.First(t => t.Index == index) is var t ? (g.Tabs.Length > 1 ? $"{g.Name} – {t.Label}" : g.Name) : $"Fenster {index}";

    TabItem? TabItemAt(int index) => SideTabs.Items.OfType<TabItem>().ElementAtOrDefault(index);

    void OpenGroupWindow(NavGroup g, int? start = null) =>
        OpenDetached("group:" + g.Name, g.Tabs.Select(t => t.Index).ToList(), g.Name == "Mehr" ? "Einstellungen" : g.Name, start ?? g.Tabs[0].Index, null, placeholder: false);

    void PopOutTab(int index, PixelPoint? at = null) =>
        OpenDetached("tab:" + index, [index], TabLabel(index), index, at, placeholder: true);

    void OpenDetached(string key, List<int> indices, string title, int startIndex, PixelPoint? at, bool placeholder)
    {
        if (_windows.TryGetValue(key, out var open)) { open.Win.Activate(); SelectDetachedTab(open, startIndex); return; }
        var items = new List<(TabItem, Control, Border)>();
        foreach (var i in indices)
            if (TabItemAt(i) is { Content: Control c } tab) items.Add((tab, c, new Border { Child = null }));
        if (items.Count == 0) return;

        var host = new Grid();
        var pills = new WrapPanel { Margin = new(14, 10, 14, 0) };
        var hint = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(14, 4, 14, 4) };
        var group = GroupOf(indices[0]);
        foreach (var it in items) { it.Item1.Content = null; it.Item3.Child = it.Item2; host.Children.Add(it.Item3); }

        var saved = _settings.Windows.TryGetValue(key, out var s) ? s : null;
        var content = new DockPanel();
        if (items.Count > 1) { DockPanel.SetDock(pills, Dock.Top); content.Children.Add(pills); DockPanel.SetDock(hint, Dock.Top); content.Children.Add(hint); }
        content.Children.Add(host);

        var win = new Window
        {
            Title = $"BibleStudy – {title}", Width = saved?.W ?? (items.Count > 1 ? 640 : 520), Height = saved?.H ?? 800, MinWidth = 320, MinHeight = 240,
            Background = Res("BsSurface"), Content = content, Icon = Icon,
            WindowStartupLocation = saved is null && at is null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.Manual,
        };
        if (at is { } p) win.Position = p; else if (saved is not null) win.Position = new PixelPoint(saved.X, saved.Y);
        if (saved is { Maximized: true }) win.WindowState = WindowState.Maximized;

        var d = new Detached { Win = win, Items = items, Key = key };
        _windows[key] = d;

        if (placeholder)
        {
            var back = new Button { Content = "Zurück in die Leiste holen", HorizontalAlignment = HorizontalAlignment.Left, Classes = { "primary" } };
            back.Click += (_, _) => win.Close();
            items[0].Item1.Content = new StackPanel
            {
                Margin = new(16, 24), Spacing = 10,
                Children = { new TextBlock { Text = "Dieser Bereich ist ausgelagert.", FontWeight = FontWeight.SemiBold }, new TextBlock { Text = "Ziehe das Fenster zurück auf die Leiste, oder klicke:", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }, back },
            };
        }
        else foreach (var it in items) it.Item1.Content = null;

        // pills to switch between the tabs of a group window
        void Select(int index)
        {
            for (var k = 0; k < items.Count; k++) items[k].Item3.IsVisible = indices[k] == index;
            foreach (var b in pills.Children.OfType<Button>()) b.Classes.Set("active", (int)b.Tag! == index);
            hint.Text = group?.Tabs.FirstOrDefault(t => t.Index == index)?.Hint ?? "";
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
        Select(indices.Contains(startIndex) ? startIndex : indices[0]);

        void Restore()
        {
            if (!_windows.Remove(key)) return;
            SaveWindowState(key, win);
            host.Children.Clear();
            foreach (var (tab, c, wrap) in items) { wrap.Child = null; tab.Content = c; }
            win.Content = null;
        }
        win.Closing += (_, _) => Restore();

        // dragging a detached side tab back over the side panel docks it again
        if (placeholder)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!_windows.ContainsKey(key) || !SidePanel.IsVisible) return;
                var topLeft = SidePanel.PointToScreen(new Point(0, 0));
                var scale = RenderScaling;
                var rect = new PixelRect(topLeft, new PixelSize((int)(SidePanel.Bounds.Width * scale), (int)(SidePanel.Bounds.Height * scale)));
                var center = new PixelPoint(win.Position.X + (int)(win.Bounds.Width * scale / 2), win.Position.Y + 24);
                if (rect.Contains(center)) win.Close();
            };
            win.PositionChanged += (_, _) => { timer.Stop(); timer.Start(); };
        }
        win.Show(this);
        SyncNav();
    }

    void SelectDetachedTab(Detached d, int index) => d.SelectAction?.Invoke(index);

    void SaveWindowState(string key, Window w)
    {
        _settings.Windows[key] = new WindowBounds(w.Position.X, w.Position.Y, (int)w.Width, (int)w.Height, w.WindowState == WindowState.Maximized) { Key = key };
        SaveSettings();
    }

    void CloseAllDetached() { foreach (var w in _windows.Values.ToList()) w.Win.Close(); }

    // ---- layouts (desktops) --------------------------------------------------------------------------------------------

    LayoutPreset CaptureLayout() => new()
    {
        SideOpen = SidePanel.IsVisible, SideWidth = SidePanel.Width, SideTab = _lastSideIndex,
        Windows = _windows.Select(p => new WindowBounds(p.Value.Win.Position.X, p.Value.Win.Position.Y, (int)p.Value.Win.Width, (int)p.Value.Win.Height, p.Value.Win.WindowState == WindowState.Maximized) { Key = p.Key }).ToList(),
    };

    void ApplyLayout(LayoutPreset l)
    {
        CloseAllDetached();
        SidePanel.Width = Math.Clamp(l.SideWidth, 300, 900); _settings.SideWidth = SidePanel.Width;
        SetPanelOpen(l.SideOpen);
        if (l.SideTab >= 0 && l.SideTab < SideTabs.ItemCount && GroupOf(l.SideTab) is { Windowed: false }) SideTabs.SelectedIndex = l.SideTab;
        foreach (var w in l.Windows)
        {
            _settings.Windows[w.Key] = w;
            if (w.Key.StartsWith("group:") && NavGroups.FirstOrDefault(g => "group:" + g.Name == w.Key) is { } g) OpenGroupWindow(g);
            else if (w.Key.StartsWith("tab:") && int.TryParse(w.Key[4..], out var idx)) PopOutTab(idx);
        }
        SaveSettings();
    }

    static readonly string[] BuiltInLayouts = ["Lesen", "Studium", "Schreiben"];

    LayoutPreset? BuiltIn(string name) => name switch
    {
        "Lesen" => new() { SideOpen = false },
        "Studium" => new() { SideOpen = true, SideWidth = 460, SideTab = GuideTab },
        "Schreiben" => new() { SideOpen = true, SideWidth = 400, SideTab = GuideTab, Windows = [new(80, 80, 720, 900, false) { Key = "group:Dokumente" }] },
        _ => null,
    };

    void ShowLayoutMenu()
    {
        var menu = new MenuFlyout();
        void Add(string text, Action act, bool enabled = true) { var i = new MenuItem { Header = text, IsEnabled = enabled }; i.Click += (_, _) => act(); menu.Items.Add(i); }
        menu.Items.Add(new MenuItem { Header = "Layouts (Desktops)", IsEnabled = false });
        foreach (var name in BuiltInLayouts.Concat(_settings.Layouts.Keys.Order()))
        {
            var n = name;
            Add("   " + name, () => { ApplyLayout(_settings.Layouts.GetValueOrDefault(n) ?? BuiltIn(n)!); StatusText.Text = $"Layout „{n}“ geladen."; });
        }
        menu.Items.Add(new Separator());
        Add("Aktuelle Anordnung speichern …", () =>
        {
            var name = new TextBox { Watermark = "Name, z. B. „Predigtvorbereitung“", Width = 240 };
            var save = new Button { Content = "Speichern", Classes = { "primary" }, Margin = new(0, 8, 0, 0) };
            var fly = new Flyout { Content = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = "Layout speichern", FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 6) }, name, save } } };
            save.Click += (_, _) =>
            {
                var n = name.Text?.Trim() ?? "";
                if (n.Length == 0) return;
                _settings.Layouts[n] = CaptureLayout(); SaveSettings(); fly.Hide(); StatusText.Text = $"Layout „{n}“ gespeichert: Leiste, Breite, Reiter und offene Fenster mit Position.";
            };
            fly.ShowAt(LayoutTopBtn);
        });
        foreach (var n in _settings.Layouts.Keys.Order().ToList()) { var name = n; Add($"Layout „{name}“ löschen", () => { _settings.Layouts.Remove(name); SaveSettings(); }); }
        menu.ShowAt(LayoutTopBtn);
    }

    void InitWindows()
    {
        LayoutTopBtn.Click += (_, _) => ShowLayoutMenu();
        SettingsTopBtn.Click += (_, _) => OpenGroupWindow(NavGroups.First(g => g.Name == "Mehr"), SettingsTab);
        BooksWinBtn.Click += (_, _) => OpenGroupWindow(NavGroups.First(g => g.Name == "Bücher"));
        DocsWinBtn.Click += (_, _) => OpenGroupWindow(NavGroups.First(g => g.Name == "Dokumente"));
        PlanWinBtn.Click += (_, _) => OpenGroupWindow(NavGroups.First(g => g.Name == "Planen"));
        PopOutDocsBtn.Click += (_, _) => OpenGroupWindow(NavGroups.First(g => g.Name == "Dokumente"));
        RefreshLayoutBox();
        LayoutLoadBtn.Click += (_, _) =>
        {
            var name = LayoutBox.SelectedItem as string;
            var preset = name is null ? null : _settings.Layouts.GetValueOrDefault(name) ?? BuiltIn(name);
            if (preset is null) { LayoutInfo.Text = "Wähle zuerst ein Layout."; return; }
            ApplyLayout(preset); LayoutInfo.Text = $"Layout „{name}“ geladen.";
        };
        LayoutSaveBtn.Click += (_, _) =>
        {
            var name = LayoutNameBox.Text?.Trim() ?? "";
            if (name.Length == 0) { LayoutInfo.Text = "Gib dem Layout einen Namen."; return; }
            _settings.Layouts[name] = CaptureLayout(); SaveSettings(); LayoutNameBox.Text = "";
            RefreshLayoutBox(name); LayoutInfo.Text = $"Layout „{name}“ gespeichert.";
        };
        LayoutDeleteBtn.Click += (_, _) =>
        {
            if (LayoutBox.SelectedItem is string n && _settings.Layouts.Remove(n)) { SaveSettings(); RefreshLayoutBox(); LayoutInfo.Text = $"Layout „{n}“ gelöscht."; }
            else LayoutInfo.Text = "Eingebaute Layouts lassen sich nicht löschen.";
        };
    }

    void RefreshLayoutBox(string? select = null)
    {
        var names = BuiltInLayouts.Concat(_settings.Layouts.Keys.Order()).ToList();
        LayoutBox.ItemsSource = names;
        LayoutBox.SelectedItem = select ?? names.First();
    }
}
