using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// Two small helpers to get to the reading: the reminder (once a day from a chosen time, when nothing was read that day) and, on Windows, a
/// key combination that brings the program forward from anywhere.
/// </summary>
public partial class MainWindow
{
    DispatcherTimer? _reminderTimer;
    DateTime? _reminderPause;
    bool _reminderShowing;
    GlobalHotkey? _hotkey;

    void InitReminder()
    {
        // every half hour from 05:00 to 23:30
        var times = Enumerable.Range(10, 38).Select(i => $"{i / 2:00}:{(i % 2 == 0 ? "00" : "30")}").ToList();
        if (!times.Contains(_settings.ReminderTime)) times.Add(_settings.ReminderTime);
        ReminderTimeBox.ItemsSource = times;
        ReminderTimeBox.SelectedItem = _settings.ReminderTime;
        ReminderCheck.IsChecked = _settings.ReadingReminder;
        ReminderTimeBox.IsEnabled = _settings.ReadingReminder;
        ReminderCheck.IsCheckedChanged += (_, _) =>
        {
            _settings.ReadingReminder = ReminderCheck.IsChecked == true; ReminderTimeBox.IsEnabled = _settings.ReadingReminder;
            _reminderPause = null; _settings.ReminderShownOn = ""; SaveSettings(); CheckReminder();
        };
        ReminderTimeBox.SelectionChanged += (_, _) =>
        {
            if (ReminderTimeBox.SelectedItem is not string t) return;
            _settings.ReminderTime = t; _settings.ReminderShownOn = ""; _reminderPause = null; SaveSettings();
        };
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _reminderTimer.Tick += (_, _) => CheckReminder();
        _reminderTimer.Start();
        DispatcherTimer.RunOnce(CheckReminder, TimeSpan.FromSeconds(25));   // an app opened after the time: reminded a little after the start page is there


        // the key combination: Windows only; the combination itself is in the list of shortcuts ("bringforward")
        HotkeyRow.IsVisible = OperatingSystem.IsWindows();
        HotkeyCheck.IsChecked = _settings.GlobalHotkeyEnabled;
        HotkeyCheck.IsCheckedChanged += (_, _) => { _settings.GlobalHotkeyEnabled = HotkeyCheck.IsChecked == true; SaveSettings(); ApplyHotkey(true); };
        // brought forward and minimised again within a moment: that was not the user, the window comes back (a stray key release, the system menu of Alt)
        PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "WindowState" && WindowState == WindowState.Minimized && (DateTime.Now - _broughtForward).TotalMilliseconds < 1500)
                Dispatcher.UIThread.Post(() => { WindowState = WindowState.Normal; Activate(); });
        };
        ApplyHotkey(false);
    }

    DateTime _broughtForward = DateTime.MinValue;

    /// <summary>"Ctrl+Alt+B" → the modifiers and the key code of Windows; null when that cannot be a shortcut for the whole system (it needs Ctrl or Alt and a letter, digit, F, arrow or similar key).</summary>
    static (uint Modifiers, uint Key)? HotkeyOf(string gesture)
    {
        uint mods = 0; Key? key = null;
        foreach (var part in gesture.Split('+'))
        {
            switch (part)
            {
                case "Ctrl": mods |= GlobalHotkey.Control; break;
                case "Alt": mods |= GlobalHotkey.Alt; break;
                case "Shift": mods |= GlobalHotkey.Shift; break;
                default: key = Enum.TryParse<Key>(part switch { "Plus" => "OemPlus", "Minus" => "OemMinus", _ => part }, out var k) ? k : null; break;
            }
        }
        if (key is not { } kk || (mods & (GlobalHotkey.Control | GlobalHotkey.Alt)) == 0) return null;
        uint? vk = kk switch
        {
            >= Key.A and <= Key.Z => 0x41u + (uint)(kk - Key.A),
            >= Key.D0 and <= Key.D9 => 0x30u + (uint)(kk - Key.D0),
            >= Key.F1 and <= Key.F12 => 0x70u + (uint)(kk - Key.F1),
            Key.Space => 0x20, Key.Home => 0x24, Key.End => 0x23, Key.PageUp => 0x21, Key.PageDown => 0x22,
            Key.Left => 0x25, Key.Up => 0x26, Key.Right => 0x27, Key.Down => 0x28, Key.Insert => 0x2D, Key.Delete => 0x2E,
            Key.OemPlus => 0xBB, Key.OemMinus => 0xBD,
            _ => null,
        };
        return vk is { } v ? (mods, v) : null;
    }

    void ApplyHotkey(bool report)
    {
        _hotkey?.Dispose(); _hotkey = null;
        if (!OperatingSystem.IsWindows() || !_settings.GlobalHotkeyEnabled) return;
        if (_keyActions.FirstOrDefault(a => a.Id == "bringforward") is not { } action) return;
        var gesture = GestureOf(action);
        if (gesture.Length == 0) { if (report) StatusText.Text = L("Für „BibleStudy von überall nach vorn holen“ ist keine Tastenkombination gewählt (Liste der Tastenkürzel)."); return; }
        if (HotkeyOf(gesture) is not { } h)
        {
            StatusText.Text = L("Als Kürzel für ganz Windows geht nur eine Kombination mit Strg oder Alt und einer Buchstaben-, Zahlen-, F- oder Pfeiltaste.");
            return;
        }
        var made = new GlobalHotkey(h.Modifiers, h.Key, () => Dispatcher.UIThread.Post(BringForward));
        if (made.Registered) { _hotkey = made; if (report) StatusText.Text = L("Tastenkürzel {0}: BibleStudy kommt jetzt von überall nach vorn.", Pretty(gesture)); }
        else StatusText.Text = L("Das Tastenkürzel {0} ist schon von einem anderen Programm belegt. Wähle ein anderes.", Pretty(gesture));
    }

    /// <summary>Out of the tray, out of the taskbar, from behind other windows.</summary>
    void BringForward()
    {
        _broughtForward = DateTime.Now;
        RestoreFromTray();
        Topmost = true; Topmost = false;   // a window that is only "activated" may stay behind others; this puts it on top once
        Activate();
    }

    void CheckReminder()
    {
        if (!_settings.ReadingReminder || _reminderShowing || _vault is null) return;
        var now = DateTime.Now;
        DateOnly? shown = DateOnly.TryParseExact(_settings.ReminderShownOn, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
        List<DateTime> dates;
        try { dates = _vault.GetReadDates().Select(x => x.At).ToList(); } catch (Exception ex) when (ex is not OutOfMemoryException) { return; }
        if (!ReadingReminder.Due(now, _settings.ReminderTime, dates, shown, _reminderPause)) return;
        _reminderPause = null;
        _settings.ReminderShownOn = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); SaveSettings();
        ShowReminder();
    }

    void ShowReminder()
    {
        var text = L("Du hast heute noch nichts gelesen.");
        if (AppPlatform.IsMobile || (IsVisible && IsActive && WindowState != WindowState.Minimized)) { ShowUndoToast(text, ReadNowAsync, L("Jetzt lesen")); return; }
        // the program is behind something, minimised or in the tray: a small window in the corner of the screen that takes no focus
        _reminderShowing = true;
        var close = new Button { Content = L("Schließen"), Classes = { "ghost" } };
        var later = new Button { Content = L("In einer Stunde"), Margin = new(8, 0, 0, 0) };
        var go = new Button { Content = L("Jetzt lesen"), Classes = { "primary" } };
        var win = new Window
        {
            Width = 380, Height = 150, CanResize = false, WindowDecorations = Avalonia.Controls.WindowDecorations.None, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
            Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.Manual,
            Content = new Border
            {
                BorderThickness = new(1), BorderBrush = Res("BsBorder"), Padding = new(18, 14),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = "BibleStudy", FontWeight = FontWeight.SemiBold, FontSize = 16 },
                        new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                        new WrapPanel { Margin = new(0, 8, 0, 0), Children = { go, later, close } },
                    },
                },
            },
        };
        if (Screens.Primary is { } screen)
        {
            var area = screen.WorkingArea; var scale = screen.Scaling;
            win.Position = new PixelPoint((int)(area.Right - (win.Width + 16) * scale), (int)(area.Bottom - (win.Height + 16) * scale));
        }
        void End() { _reminderShowing = false; if (win.IsVisible) win.Close(); }
        go.Click += async (_, _) => { End(); await ReadNowAsync(); };
        later.Click += (_, _) => { _reminderPause = DateTime.Now.AddHours(1); End(); };
        close.Click += (_, _) => End();
        win.Closed += (_, _) => _reminderShowing = false;
        win.Show();
        DispatcherTimer.RunOnce(End, TimeSpan.FromSeconds(60));
    }

    /// <summary>"Read now": today's chapters of a running plan, else the place where one stopped, else the start page.</summary>
    async Task ReadNowAsync()
    {
        BringForward();
        if (PlanTodayCard() is { } plan) { plan.Go(); return; }
        if (_vault.GetLastPosition() is { } last && _books.Any(b => b.Id == last.Book)) { await GoToAsync(last.Book, last.Chapter); return; }
        ShowHome(true);
    }
}
