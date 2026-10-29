using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace BibleStudy.App;

/// <summary>Keyboard shortcuts as a table of actions; every binding can be changed in the settings.</summary>
public partial class MainWindow
{
    sealed record KeyAction(string Id, string Label, string Default, Func<Task> Run, bool WhenTyping = false);

    List<KeyAction> _keyActions = [];
    string? _capturing;
    readonly Dictionary<string, Button> _keyButtons = [];

    void InitKeys()
    {
        static Task Sync(Action a) { a(); return Task.CompletedTask; }
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        _keyActions =
        [
            new("quickjump", L("Gehe zu Stelle oder suchen"), "Ctrl+K", () => Sync(() => { QuickJumpBox.Focus(); QuickJumpBox.SelectAll(); }), true),
            new("search", L("Suche öffnen"), "Ctrl+F", () => Sync(() => { SetPanelOpen(true); SideTabs.SelectedIndex = 0; SearchBox.Focus(); SearchBox.SelectAll(); }), true),
            new("back", L("Zurück zur vorherigen Stelle"), "Alt+Left", GoBackAsync, true),
            new("home", L("Startseite"), "Alt+Home", () => Sync(ShowHome), true),
            new("prev", L("Voriges Kapitel"), "Ctrl+Left", () => Sync(() => { if (ChapterBox.Value > ChapterBox.Minimum) ChapterBox.Value -= 1; })),
            new("next", L("Nächstes Kapitel"), "Ctrl+Right", () => Sync(() => ChapterBox.Value += 1)),
            new("read", L("Kapitel als gelesen markieren"), "Ctrl+R", () => Sync(() => { if (_curBook != "") SetRead(_curBook, [_curChapter], !_read.Contains((_curBook, _curChapter))); })),
            new("bookmark", L("Vers merken"), "Ctrl+D", () => Sync(ToggleBookmark)),
            new("copy", L("Vers mit Stelle kopieren"), "Ctrl+Shift+C", CopyVerseAsync, true),
            new("fontplus", L("Schrift größer"), "Ctrl+Plus", () => Sync(() => Click(FontPlusBtn)), true),
            new("fontminus", L("Schrift kleiner"), "Ctrl+Minus", () => Sync(() => Click(FontMinusBtn)), true),
            new("fontreset", L("Schriftgröße zurücksetzen"), "Ctrl+D0", () => Sync(() => FontSlider.Value = 22), true),
            new("panel", L("Seitenleiste ein/aus"), "Ctrl+B", () => Sync(() => SetPanelOpen(!SidePanel.IsVisible)), true),
            new("theme", L("Design wechseln (hell/dunkel)"), "Ctrl+T", () => Sync(() => Click(ThemeBtn)), true),
            new("focus", L("Fokus-Modus (nur Text)"), "F11", () => Sync(ToggleFocusMode), true),
            new("guide", L("Vers-Übersicht öffnen"), "Ctrl+G", () => Sync(() => { SetPanelOpen(true); SideTabs.SelectedIndex = GuideTab; }), true),
            new("docs", L("Dokumente öffnen"), "Ctrl+Shift+D", () => Sync(() => OpenWork(NavGroups.First(g => g.Name == "Dokumente"))), true),
            new("popout", L("Aktuelles Seitenfenster auslagern"), "Ctrl+Shift+O", () => Sync(() => PopOutTab(SideTabs.SelectedIndex)), true),
        ];
        BuildKeysPanel();
        ResetKeysBtn.Click += (_, _) => { _settings.KeyBindings.Clear(); SaveSettings(); BuildKeysPanel(); };
        KeysTopBtn.Click += (_, _) => ShowKeysWindow();
    }

    string GestureOf(KeyAction a) => _settings.KeyBindings.TryGetValue(a.Id, out var g) ? g : a.Default;

    static string Pretty(string gesture) => gesture.Replace("Ctrl", L("Strg")).Replace("Shift", L("Umschalt")).Replace("Left", "←").Replace("Right", "→").Replace("Home", L("Pos1"))
        .Replace("Plus", "+").Replace("Minus", "−").Replace("D0", "0").Replace("Up", "↑").Replace("Down", "↓");

    /// <summary>"Ctrl+Shift+K" style text of a key event; null for modifier keys alone.</summary>
    static string? GestureText(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None => null,
            Key.OemPlus or Key.Add => "Plus", Key.OemMinus or Key.Subtract => "Minus", Key.NumPad0 => "D0",
            var k => k.ToString(),
        };
        if (key is null) return null;
        var m = e.KeyModifiers;
        return (m.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "") + (m.HasFlag(KeyModifiers.Alt) ? "Alt+" : "") + (m.HasFlag(KeyModifiers.Shift) ? "Shift+" : "") + key;
    }

    readonly List<StackPanel> _keyPanels = [];

    /// <summary>Redraws every list of shortcuts that is open (the settings page and the shortcut window).</summary>
    void BuildKeysPanel()
    {
        _keyButtons.Clear();
        _keyPanels.RemoveAll(p => !p.IsAttachedToVisualTree() && p != KeysPanel);
        if (!_keyPanels.Contains(KeysPanel)) _keyPanels.Add(KeysPanel);
        foreach (var panel in _keyPanels) FillKeys(panel);
    }

    void FillKeys(StackPanel panel)
    {
        panel.Children.Clear();
        foreach (var a in _keyActions)
        {
            var action = a;
            var b = new Button { Content = Pretty(GestureOf(a)), MinWidth = 150, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(b, L("Klicken, dann die neue Tastenkombination drücken (Esc bricht ab, Entf löscht)"));
            b.Click += (_, _) => { _capturing = action.Id; b.Content = L("Taste drücken …"); b.Focus(); };
            // clicking somewhere else ends the recording; otherwise the next key typed anywhere (even in a text field) became the new shortcut
            b.LostFocus += (_, _) => { if (_capturing == action.Id) { _capturing = null; b.Content = Pretty(GestureOf(action)); } };
            _keyButtons[a.Id] = b;
            var row = new DockPanel { Margin = new(0, 2) };
            DockPanel.SetDock(b, Dock.Right);
            row.Children.Add(b);
            row.Children.Add(new TextBlock { Text = a.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 8, 0) });
            panel.Children.Add(row);
        }
    }

    async void OnShortcut(object? sender, KeyEventArgs e)
    {
        var gesture = GestureText(e);
        if (gesture is null) return;

        if (_capturing is { } id)   // recording a new binding
        {
            e.Handled = true;
            var action = _keyActions.First(a => a.Id == id);
            _capturing = null;
            if (e.Key == Key.Escape) { BuildKeysPanel(); return; }
            if (e.Key == Key.Delete) _settings.KeyBindings[id] = "";
            else
            {
                // a combination can only belong to one action: take it away from the previous owner
                foreach (var other in _keyActions.Where(o => o.Id != id && GestureOf(o) == gesture)) _settings.KeyBindings[other.Id] = "";
                _settings.KeyBindings[id] = gesture;
            }
            if (_settings.KeyBindings.TryGetValue(id, out var g) && g == action.Default) _settings.KeyBindings.Remove(id);
            SaveSettings(); BuildKeysPanel();
            return;
        }

        var typing = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;
        var hit = _keyActions.FirstOrDefault(a => GestureOf(a) == gesture && GestureOf(a).Length > 0);
        if (hit is null || (typing && !hit.WhenTyping)) return;
        e.Handled = true;
        await hit.Run();
    }

    Window? _keysWindow;

    /// <summary>All keyboard shortcuts in a window of their own (from the keyboard symbol in the top bar): look them up and change them.</summary>
    void ShowKeysWindow()
    {
        if (_keysWindow is not null) { _keysWindow.Activate(); return; }
        var panel = new StackPanel { Spacing = 0 };
        var reset = new Button { Content = L("Alle auf Standard zurücksetzen"), Margin = new(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => { _settings.KeyBindings.Clear(); SaveSettings(); BuildKeysPanel(); };
        var content = new StackPanel
        {
            Margin = new(22, 18),
            Children =
            {
                new TextBlock { Text = L("Tastenkürzel"), FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 4) },
                new TextBlock { Text = L("Klicke auf ein Kürzel, drücke dann die neue Tastenkombination (Esc bricht ab, Entf löscht)."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) },
                panel, reset,
            },
        };
        var win = new Window
        {
            Title = L("BibleStudy – {0}", L("Tastenkürzel")), Width = 560, Height = 640, MinWidth = 380, MinHeight = 300, Background = Res("BsSurface"), Icon = Icon,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = content },
        };
        // the window is its own top level: keys pressed there while recording a shortcut go to the same logic as in the main window
        win.AddHandler(KeyDownEvent, (object? s, KeyEventArgs e) => { if (_capturing is not null) OnShortcut(s, e); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        win.Closed += (_, _) => { _keysWindow = null; _capturing = null; _keyPanels.Remove(panel); };
        _keysWindow = win;
        _keyPanels.Add(panel);
        FillKeys(panel);
        win.Show(this);
    }
}
