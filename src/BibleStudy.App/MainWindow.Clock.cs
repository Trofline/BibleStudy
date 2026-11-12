using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// The clock at the bottom right of the status bar, and a timer (e.g. for a Bible study or a time of prayer): a click on the clock sets it,
/// the remaining time shows beside the clock, and when it has run out a bar says so and the window comes to the front.
/// </summary>
public partial class MainWindow
{
    DispatcherTimer? _clockTimer;
    DateTime? _timerEnd;            // while running
    TimeSpan? _timerPaused;         // while paused: the time that was left
    TimeSpan _timerLength;

    static readonly int[] TimerPresets = [5, 10, 15, 20, 30, 45, 60, 90];

    void InitClock()
    {
        ClockChip.IsVisible = _settings.ShowClock;
        ShowClockCheck.IsChecked = _settings.ShowClock;
        ShowClockCheck.IsCheckedChanged += (_, _) => { _settings.ShowClock = ShowClockCheck.IsChecked == true; SaveSettings(); ClockChip.IsVisible = _settings.ShowClock; };
        ClockChip.Click += (_, _) => ShowTimerFlyout(ClockChip);
        TimerChip.Click += (_, _) => ShowTimerFlyout(TimerChip);
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clockTimer.Tick += (_, _) => TickClock();
        _clockTimer.Start();
        TickClock();
    }

    void TickClock()
    {
        ClockText.Text = DateTime.Now.ToString("t", Loc.Culture);
        if (_timerEnd is { } end)
        {
            var left = end - DateTime.Now;
            if (left <= TimeSpan.Zero) { TimerDone(); return; }
            TimerChipText.Text = "⏱ " + TimerText(left);
        }
        else if (_timerPaused is { } p) TimerChipText.Text = "⏸ " + TimerText(p);
        TimerChip.IsVisible = _timerEnd is not null || _timerPaused is not null;
    }

    /// <summary>"4:05" or, from an hour on, "1:04:05"; seconds are rounded up, so the timer never shows 0:00 while it still runs.</summary>
    public static string TimerText(TimeSpan t)
    {
        var s = (long)Math.Ceiling(Math.Max(0, t.TotalSeconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    void StartTimer(TimeSpan length)
    {
        if (length <= TimeSpan.Zero) return;
        _timerLength = length; _timerPaused = null;
        _timerEnd = DateTime.Now + length;
        _settings.TimerMinutes = Math.Max(1, (int)Math.Round(length.TotalMinutes)); SaveSettings();
        StatusText.Text = L("Timer gestellt: {0} min, endet um {1}.", (int)Math.Round(length.TotalMinutes), _timerEnd.Value.ToString("t", Loc.Culture));
        TickClock();
    }

    void PauseTimer()
    {
        if (_timerEnd is { } end) { _timerPaused = end - DateTime.Now; _timerEnd = null; }
        else if (_timerPaused is { } p) { _timerEnd = DateTime.Now + p; _timerPaused = null; }
        TickClock();
    }

    void StopTimer() { _timerEnd = null; _timerPaused = null; TickClock(); }

    void TimerDone()
    {
        var length = _timerLength;
        _timerEnd = null; _timerPaused = null;
        TickClock();
        StatusText.Text = L("Der Timer ist abgelaufen ({0} min).", (int)Math.Round(length.TotalMinutes));
        ShowUndoToast(L("Der Timer ist abgelaufen ({0} min).", (int)Math.Round(length.TotalMinutes)), () => { StartTimer(length); return Task.CompletedTask; }, L("Noch einmal"));
        // the window comes to the front, so the end is seen even when another program is in front
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        try { if (OperatingSystem.IsWindows()) Console.Beep(880, 300); } catch { }
    }

    void ShowTimerFlyout(Control anchor)
    {
        var flyout = new Flyout { Placement = PlacementMode.TopEdgeAlignedRight };
        var panel = new StackPanel { Spacing = 8, Width = 270 };
        panel.Children.Add(new TextBlock { Text = L("TIMER"), Classes = { "eyebrow" } });
        if (_timerEnd is not null || _timerPaused is not null)
        {
            var left = _timerEnd is { } e ? e - DateTime.Now : _timerPaused ?? TimeSpan.Zero;
            panel.Children.Add(new TextBlock { Text = L("Noch {0}", TimerText(left)), FontSize = 20, FontWeight = FontWeight.SemiBold });
            var pause = new Button { Content = _timerEnd is null ? L("Fortsetzen") : L("Anhalten"), Classes = { "primary" } };
            pause.Click += (_, _) => { PauseTimer(); flyout.Hide(); };
            var stop = new Button { Content = L("Beenden") };
            stop.Click += (_, _) => { StopTimer(); flyout.Hide(); StatusText.Text = L("Timer beendet."); };
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { pause, stop } });
            panel.Children.Add(new Separator());
            panel.Children.Add(new TextBlock { Text = L("Neu stellen:"), Classes = { "caption" } });
        }
        else panel.Children.Add(new TextBlock { Text = L("Wähle eine Dauer. Wenn sie um ist, meldet sich das Programm."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        var presets = new WrapPanel();
        foreach (var m in TimerPresets)
        {
            var b = new Button { Classes = { "seg" }, Content = L("{0} min", m), Margin = new(0, 0, 6, 6) };
            b.Classes.Set("active", m == _settings.TimerMinutes);
            b.Click += (_, _) => { StartTimer(TimeSpan.FromMinutes(m)); flyout.Hide(); };
            presets.Children.Add(b);
        }
        panel.Children.Add(presets);
        var box = new NumericUpDown { Minimum = 1, Maximum = 600, Value = _settings.TimerMinutes, FormatString = "0", Width = 120, VerticalAlignment = VerticalAlignment.Center };
        var start = new Button { Content = L("Starten"), Classes = { "primary" }, VerticalAlignment = VerticalAlignment.Center };
        start.Click += (_, _) => { StartTimer(TimeSpan.FromMinutes((double)(box.Value ?? 10))); flyout.Hide(); };
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; StartTimer(TimeSpan.FromMinutes((double)(box.Value ?? 10))); flyout.Hide(); } };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, new TextBlock { Text = L("Minuten"), VerticalAlignment = VerticalAlignment.Center }, start } });
        flyout.Content = panel;
        flyout.ShowAt(anchor);
    }
}
