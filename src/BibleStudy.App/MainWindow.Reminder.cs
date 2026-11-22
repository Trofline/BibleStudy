using Avalonia;
using Avalonia.Controls;
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

    static readonly (string Id, string Label, uint Modifiers, uint Key)[] Hotkeys =
    [
        ("CtrlAltB", K("Strg + Alt + B"), GlobalHotkey.Control | GlobalHotkey.Alt, 0x42),
        ("CtrlShiftB", K("Strg + Umschalt + B"), GlobalHotkey.Control | GlobalHotkey.Shift, 0x42),
        ("CtrlAltL", K("Strg + Alt + L"), GlobalHotkey.Control | GlobalHotkey.Alt, 0x4C),
        ("CtrlAltSpace", K("Strg + Alt + Leertaste"), GlobalHotkey.Control | GlobalHotkey.Alt, 0x20),
    ];

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

        // the key combination: Windows only
        HotkeyRow.IsVisible = OperatingSystem.IsWindows();
        HotkeyBox.ItemsSource = Hotkeys.Select(h => L(h.Label)).ToList();
        HotkeyBox.SelectedIndex = Math.Max(0, Array.FindIndex(Hotkeys, h => h.Id == _settings.GlobalHotkeyKeys));
        HotkeyCheck.IsChecked = _settings.GlobalHotkeyOn;
        HotkeyBox.IsEnabled = _settings.GlobalHotkeyOn;
        HotkeyCheck.IsCheckedChanged += (_, _) => { _settings.GlobalHotkeyOn = HotkeyCheck.IsChecked == true; HotkeyBox.IsEnabled = _settings.GlobalHotkeyOn; SaveSettings(); ApplyHotkey(true); };
        HotkeyBox.SelectionChanged += (_, _) =>
        {
            if (HotkeyBox.SelectedIndex < 0) return;
            _settings.GlobalHotkeyKeys = Hotkeys[HotkeyBox.SelectedIndex].Id; SaveSettings(); ApplyHotkey(true);
        };
        ApplyHotkey(false);
    }

    void ApplyHotkey(bool report)
    {
        _hotkey?.Dispose(); _hotkey = null;
        if (!OperatingSystem.IsWindows() || !_settings.GlobalHotkeyOn) return;
        var h = Hotkeys.FirstOrDefault(x => x.Id == _settings.GlobalHotkeyKeys, Hotkeys[0]);
        var made = new GlobalHotkey(h.Modifiers, h.Key, () => Dispatcher.UIThread.Post(BringForward));
        if (made.Registered) { _hotkey = made; if (report) StatusText.Text = L("Tastenkürzel {0}: BibleStudy kommt jetzt von überall nach vorn.", L(h.Label)); }
        else StatusText.Text = L("Das Tastenkürzel {0} ist schon von einem anderen Programm belegt. Wähle ein anderes.", L(h.Label));
    }

    /// <summary>Out of the tray, out of the taskbar, from behind other windows.</summary>
    void BringForward()
    {
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
