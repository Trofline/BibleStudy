using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The program of a Bible study for the timer: parts with their own time (song 5 min, prayer 5 min, Bible study 40 min …) one after the
/// other. The chip beside the clock names the part that is on and its time left; when a part is over, the next one starts by itself.
/// </summary>
public partial class MainWindow
{
    List<TimerStep>? _program;   // while a program runs: its parts
    int _programAt;

    string ProgramPrefix() => _program is { } p && _programAt < p.Count ? p[_programAt].Name + " · " : "";

    void StartProgram()
    {
        var steps = _settings.TimerProgram.Where(s => s.Minutes > 0).Select(s => new TimerStep { Name = s.Name.Trim().Length > 0 ? s.Name.Trim() : L("Teil"), Minutes = s.Minutes }).ToList();
        if (steps.Count == 0) return;
        _program = steps; _programAt = 0;
        StartTimer(TimeSpan.FromMinutes(steps[0].Minutes), false);
        StatusText.Text = L("Ablauf gestartet: {0} Teile, zusammen {1} min. Jetzt: {2}.", steps.Count, steps.Sum(s => s.Minutes), steps[0].Name);
    }

    /// <summary>The next part (when the time of one is over, or by hand); after the last the program ends.</summary>
    void NextProgramStep(bool timeUp)
    {
        if (_program is not { } p) return;
        if (_programAt + 1 < p.Count)
        {
            _programAt++;
            var step = p[_programAt];
            StartTimer(TimeSpan.FromMinutes(step.Minutes), false);
            var msg = L("Jetzt: {0} ({1} min).", step.Name, step.Minutes);
            StatusText.Text = msg;
            if (timeUp) { ShowUndoToast(msg, () => { NextProgramStep(false); return Task.CompletedTask; }, L("Weiter")); Signal(); }
            return;
        }
        _program = null; _timerEnd = null; _timerPaused = null;
        TickClock();
        var done = L("Der Ablauf ist zu Ende.");
        StatusText.Text = done;
        ShowUndoToast(done, () => { StartProgram(); return Task.CompletedTask; }, L("Noch einmal"));
        Signal();
    }

    /// <summary>The end of a time is seen and heard: the window comes to the front, with a short tone.</summary>
    void Signal()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        try { if (OperatingSystem.IsWindows()) Console.Beep(880, 300); } catch { }
    }

    /// <summary>The part of the timer menu for the program: while it runs its parts with the one that is on; otherwise the parts to write.</summary>
    void AddProgramSection(StackPanel panel, Flyout flyout)
    {
        panel.Children.Add(new TextBlock { Text = L("ABLAUF"), Classes = { "eyebrow" } });
        if (_program is { } running)
        {
            for (var i = 0; i < running.Count; i++)
            {
                var mark = i < _programAt ? "✓" : i == _programAt ? "▸" : "·";
                panel.Children.Add(new TextBlock { Text = $"{mark}  {running[i].Name} · {L("{0} min", running[i].Minutes)}", FontWeight = i == _programAt ? FontWeight.SemiBold : FontWeight.Normal, Opacity = i < _programAt ? 0.55 : 1 });
            }
            var next = new Button { Content = _programAt + 1 < running.Count ? L("Nächster Teil") : L("Ablauf beenden"), Classes = { "primary" } };
            next.Click += (_, _) => { flyout.Hide(); if (_programAt + 1 < running.Count) NextProgramStep(false); else { StopTimer(); StatusText.Text = L("Der Ablauf ist zu Ende."); } };
            panel.Children.Add(next);
            return;
        }
        panel.Children.Add(new TextBlock { Text = L("Die Teile der Bibelstunde mit ihrer Zeit; sie laufen nacheinander ab."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        var rows = new StackPanel { Spacing = 6 };
        var start = new Button { Content = L("Ablauf starten"), Classes = { "primary" } };
        void Fill()
        {
            rows.Children.Clear();
            foreach (var step in _settings.TimerProgram)
            {
                var s = step;
                var name = new TextBox { Text = s.Name, PlaceholderText = L("z. B. Gebet"), Width = 180 };
                name.TextChanged += (_, _) => { s.Name = name.Text ?? ""; SaveSettings(); };
                var minutes = new NumericUpDown { Minimum = 1, Maximum = 300, Value = s.Minutes, FormatString = "0", Width = 128 };
                minutes.ValueChanged += (_, _) => { s.Minutes = (int)(minutes.Value ?? 5); SaveSettings(); };
                var remove = new Button { Classes = { "tool" }, Width = 28, Height = 28, Content = "✕", Padding = new(0) };
                ToolTip.SetTip(remove, L("Teil entfernen"));
                remove.Click += (_, _) => { _settings.TimerProgram.Remove(s); SaveSettings(); Fill(); };
                rows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { name, minutes, remove } });
            }
            start.IsEnabled = _settings.TimerProgram.Count > 0;
            var total = _settings.TimerProgram.Sum(s => s.Minutes);
            ToolTip.SetTip(start, total > 0 ? L("Zusammen {0} min", total) : null);
        }
        Fill();
        panel.Children.Add(rows);
        var add = new Button { Content = L("+ Teil") };
        add.Click += (_, _) => { _settings.TimerProgram.Add(new TimerStep { Name = "", Minutes = 5 }); SaveSettings(); Fill(); };
        var sample = new Button { Content = L("Beispiel einfügen"), IsVisible = _settings.TimerProgram.Count == 0 };
        sample.Click += (_, _) =>
        {
            _settings.TimerProgram = [new() { Name = L("Lied"), Minutes = 5 }, new() { Name = L("Gebet"), Minutes = 5 }, new() { Name = L("Bibelarbeit"), Minutes = 40 }, new() { Name = L("Austausch"), Minutes = 15 }, new() { Name = L("Gebetsgemeinschaft"), Minutes = 10 }];
            SaveSettings(); sample.IsVisible = false; Fill();
        };
        start.Click += (_, _) => { flyout.Hide(); StartProgram(); };
        panel.Children.Add(new WrapPanel { Children = { add, sample, start }, ItemSpacing = 6, LineSpacing = 6 });
    }
}
