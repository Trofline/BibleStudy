using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Side panels can be moved into their own windows (also onto a second monitor), and the arrangement can be saved as a named
/// layout ("desktop") and loaded again – like desktops in IDA Pro.
/// </summary>
public partial class MainWindow
{
    readonly Dictionary<int, Window> _popped = [];
    readonly Dictionary<int, Control> _placeholders = [];

    string TabLabel(int index) => NavGroups.SelectMany(g => g.Tabs.Select(t => (g, t))).FirstOrDefault(x => x.t.Index == index) is { t: not null } hit ? $"{hit.g.Name} – {hit.t.Label}" : $"Fenster {index}";

    TabItem? TabItemAt(int index) => SideTabs.Items.OfType<TabItem>().ElementAtOrDefault(index);

    void PopOutTab(int index, WindowState? restore = null)
    {
        if (_popped.TryGetValue(index, out var existing)) { existing.Activate(); return; }
        if (TabItemAt(index) is not { Content: Control content } tab) return;
        var saved = _settings.Windows.TryGetValue(index.ToString(), out var s) ? s : null;

        // placeholder in the side panel while the content lives in its own window
        var back = new Button { Content = "Zurück in die Seitenleiste holen", HorizontalAlignment = HorizontalAlignment.Left, Classes = { "primary" } };
        var placeholder = new StackPanel
        {
            Margin = new(16, 24), Spacing = 10,
            Children = { new TextBlock { Text = "Dieses Fenster ist ausgelagert.", FontWeight = FontWeight.SemiBold }, new TextBlock { Text = "Du kannst es frei verschieben, auch auf einen zweiten Monitor. Schließt du es, kommt es hierher zurück.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }, back },
        };
        tab.Content = placeholder;
        _placeholders[index] = placeholder;

        var win = new Window
        {
            Title = $"BibleStudy – {TabLabel(index)}", Width = saved?.W ?? 560, Height = saved?.H ?? 780, MinWidth = 320, MinHeight = 240,
            Background = Res("BsSurface"), Content = new Border { Padding = new(0, 4, 0, 0), Child = content },
            WindowStartupLocation = saved is null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.Manual,
        };
        if (saved is not null) win.Position = new PixelPoint(saved.X, saved.Y);
        if (saved is { Maximized: true }) win.WindowState = WindowState.Maximized;
        _popped[index] = win;
        void Restore()
        {
            if (!_popped.Remove(index)) return;
            SaveWindowState(index, win);
            win.Content = null;
            tab.Content = content;
            _placeholders.Remove(index);
        }
        win.Closing += (_, _) => Restore();
        back.Click += (_, _) => win.Close();
        Closing += (_, _) => { if (_popped.ContainsKey(index)) win.Close(); };
        win.Show(this);
        SideTabs.SelectedIndex = SideTabs.SelectedIndex;   // refresh the placeholder
        SyncNav();
    }

    void SaveWindowState(int index, Window w)
    {
        _settings.Windows[index.ToString()] = new WindowBounds(w.Position.X, w.Position.Y, (int)w.Width, (int)w.Height, w.WindowState == WindowState.Maximized);
        SaveSettings();
    }

    void CloseAllPopped() { foreach (var w in _popped.Values.ToList()) w.Close(); }

    // ---- layouts (desktops) --------------------------------------------------------------------------------------------

    LayoutPreset CaptureLayout() => new()
    {
        SideOpen = SidePanel.IsVisible, SideWidth = SidePanel.Width, SideTab = SideTabs.SelectedIndex,
        Windows = _popped.Select(p => new WindowBounds(p.Value.Position.X, p.Value.Position.Y, (int)p.Value.Width, (int)p.Value.Height, p.Value.WindowState == WindowState.Maximized) { Tab = p.Key }).ToList(),
    };

    void ApplyLayout(LayoutPreset l)
    {
        CloseAllPopped();
        SidePanel.Width = Math.Clamp(l.SideWidth, 300, 900); _settings.SideWidth = SidePanel.Width;
        SetPanelOpen(l.SideOpen);
        if (l.SideTab >= 0 && l.SideTab < SideTabs.ItemCount) SideTabs.SelectedIndex = l.SideTab;
        foreach (var w in l.Windows)
        {
            _settings.Windows[w.Tab.ToString()] = w;
            PopOutTab(w.Tab);
        }
        SaveSettings();
    }

    static readonly string[] BuiltInLayouts = ["Lesen", "Studium", "Zwei Monitore"];

    LayoutPreset? BuiltIn(string name) => name switch
    {
        "Lesen" => new() { SideOpen = false },
        "Studium" => new() { SideOpen = true, SideWidth = 460, SideTab = GuideTab },
        "Zwei Monitore" => new() { SideOpen = true, SideWidth = 420, SideTab = GuideTab, Windows = [new(80, 80, 720, 900, false) { Tab = KnowledgeTab }] },
        _ => null,
    };

    void InitWindows()
    {
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
            RefreshLayoutBox(name); LayoutInfo.Text = $"Layout „{name}“ gespeichert (Seitenleiste, Breite, offener Reiter, ausgelagerte Fenster mit Position und Größe).";
        };
        LayoutDeleteBtn.Click += (_, _) =>
        {
            if (LayoutBox.SelectedItem is string n && _settings.Layouts.Remove(n)) { SaveSettings(); RefreshLayoutBox(); LayoutInfo.Text = $"Layout „{n}“ gelöscht."; }
            else LayoutInfo.Text = "Eingebaute Layouts lassen sich nicht löschen.";
        };
        PopOutDocsBtn.Click += (_, _) => PopOutTab(KnowledgeTab);
        Closing += (_, _) => CloseAllPopped();
    }

    void RefreshLayoutBox(string? select = null)
    {
        var names = BuiltInLayouts.Concat(_settings.Layouts.Keys.Order()).ToList();
        LayoutBox.ItemsSource = names;
        LayoutBox.SelectedItem = select ?? names.First();
    }
}
