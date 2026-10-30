using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// A short tour on the first start: one bubble per important button, pointing at the real button, with Next / Back / Skip.
/// It can be started again any time (help page, command field). For people who had the program before, a single question about backups.
/// </summary>
public partial class MainWindow
{
    sealed record TourStep(Func<Control> Anchor, PlacementMode Placement, string Title, string Text, Func<Control?>? Extra = null);

    bool _touring;

    void InitTour()
    {
        TourBtn.Click += (_, _) => StartTour();
        // a while after the start, when the splash screen and the notice about sources are gone
        DispatcherTimer.RunOnce(() => OfferTour(), TimeSpan.FromSeconds(6));
    }

    void OfferTour()
    {
        if (!IsVisible || _touring) return;
        // another window (notice, update question) is open: try again a little later
        if (OwnedWindows.Count > 0 || SplashWindow.IsShowing) { DispatcherTimer.RunOnce(() => OfferTour(), TimeSpan.FromSeconds(5)); return; }
        if (_settings.TourPending) { StartTour(); return; }
        if (!_settings.BackupAsked && _settings.BackupSchedule == "off") _ = AskForBackupAsync();
    }

    /// <summary>Asked once: whoever had the program before and has no backup yet. The answer is kept either way.</summary>
    async Task AskForBackupAsync()
    {
        _settings.BackupAsked = true; SaveSettings();
        var answer = await AskAsync(L("Sicherung einschalten?"),
            L("Deine Arbeit (Vaults, Notizen, Markierungen, Einstellungen) liegt nur auf diesem Computer. Soll das Programm sie einmal pro Woche in den Ordner „{0}“ sichern? Die zehn neuesten Sicherungen bleiben; den Ordner kannst du in den Einstellungen ändern, z. B. in einen Cloud-Ordner.", BackupFolder),
            L("Ja, wöchentlich"), L("Nein, danke"));
        if (answer != 0) return;
        foreach (var item in BackupScheduleBox.Items.OfType<ComboBoxItem>())
            if ((string?)item.Tag == "weekly") BackupScheduleBox.SelectedItem = item;   // its handler saves it and makes the first backup
    }

    void StartTour()
    {
        if (_touring) return;
        _palette.IsOpen = false; Focus();   // the command list must be gone: two popups at the same time confuse the renderer
        ShowReader();
        _touring = true;
        var steps = new TourStep[]
        {
            new(() => TransBtn, PlacementMode.Bottom, L("Sprache und Übersetzung"), L("Hier wählst du die Sprache und die Übersetzung. Viele sind frei und ohne Anmeldung dabei; eigene Dateien importierst du in den Einstellungen.")),
            new(() => BookBtn, PlacementMode.Bottom, L("Buch und Kapitel"), L("Buch anklicken: Danach öffnet sich gleich die Kapitelliste. Oder tippe „Joh“ und Enter. Mit den Pfeilen blätterst du Kapitel.")),
            new(() => MarkBtn, PlacementMode.Bottom, L("Verse anklicken und markieren"), L("Klick auf einen Vers: rechts siehst du Querverweise und kannst eine Notiz schreiben. Rechtsklick öffnet das Menü. Text markieren und hier einfärben.")),
            new(() => StudyBtn, PlacementMode.Bottom, L("Wortstudie"), L("Einschalten, dann ein unterstrichenes Wort antippen: Bedeutung im Hebräischen oder Griechischen, und alle Stellen, an denen es vorkommt.")),
            new(() => ParallelBtn, PlacementMode.Bottom, L("Parallel"), L("Weitere Übersetzungen als Spalten daneben. Jede Spalte kann auch eine andere Bibelstelle zeigen, z. B. 1. Mose neben Daniel.")),
            new(() => BookmarkBtn, PlacementMode.Bottom, L("Merken"), L("Setzt den Vers auf deine Merkliste (Strg+D), zum späteren Zurückspringen. Unter „Notizen“ rechts findest du sie wieder.")),
            new(() => Rail, PlacementMode.Left, L("Die Leiste rechts"), L("Suchen, Vers (Querverweise, Grundtext), Notizen, Dokumente, Predigt, Planen (Lesepläne, Aufgaben, Gebete, Auswendiglernen), Zusammen und KI. Ziehst du ein Symbol aus der Leiste, wird es ein eigenes Fenster.")),
            new(() => QuickJumpBox, PlacementMode.Bottom, L("Gehe zu … (Strg+K)"), L("Ein Feld für alles: eine Bibelstelle („Röm 8,28“), ein Wort, ein Befehl, ein Dokument. Tippen, mit den Pfeilen wählen, Enter.")),
            new(() => TodoTopBtn, PlacementMode.Bottom, L("To-Dos"), L("Eine freie Liste zum Abhaken, von überall erreichbar.")),
            new(() => VaultTopBtn, PlacementMode.Bottom, L("Vault"), L("Ein Vault ist deine Mappe für ein Studienprojekt (z. B. „Predigt“ und „Privat“). Notizen, Markierungen und Dokumente gehören immer zu einem Vault.")),
            new(() => SettingsTopBtn, PlacementMode.Bottom, L("Einstellungen, Hilfe und Sicherung"),
                L("Hier stellst du Aussehen, Sicherung und Updates ein. Dieser Rundgang lässt sich unter Hilfe jederzeit neu starten. Das Programm installiert nie ohne deine Zustimmung."),
                () => BackupExtra()),
        };
        ShowTourStep(steps, 0);
    }

    /// <summary>Only when backups are off: one button that turns the weekly backup on.</summary>
    Control? BackupExtra()
    {
        if (_settings.BackupSchedule != "off") return new TextBlock { Text = L("Die Sicherung ist an: einmal pro Woche in den Ordner „{0}“.", BackupFolder), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        var button = new Button { Content = L("Wöchentliche Sicherung einschalten"), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) =>
        {
            foreach (var item in BackupScheduleBox.Items.OfType<ComboBoxItem>())
                if ((string?)item.Tag == "weekly") BackupScheduleBox.SelectedItem = item;
            _settings.BackupAsked = true; SaveSettings();
            button.Content = L("Eingeschaltet ✓"); button.IsEnabled = false;
        };
        return button;
    }

    void ShowTourStep(TourStep[] steps, int i)
    {
        _palette.IsOpen = false;   // closing a bubble hands the focus back to the quick-jump field, which would open its list under the next bubble
        while (i < steps.Length && steps[i].Anchor() is { IsVisible: false } or { Bounds.Width: 0 }) i++;   // a button that is not there (hidden panel, other layout) is skipped
        if (i >= steps.Length) { FinishTour(); return; }
        var step = steps[i]; var last = i == steps.Length - 1;
        var moved = false;

        var panel = new StackPanel { Spacing = 8, Width = 330 };
        panel.Children.Add(new TextBlock { Text = step.Title, FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = step.Text, TextWrapping = TextWrapping.Wrap });
        if (step.Extra?.Invoke() is { } extra) panel.Children.Add(extra);
        var skip = new Button { Content = L("Überspringen"), Classes = { "ghost" }, IsVisible = !last };
        var back = new Button { Content = L("Zurück"), IsVisible = i > 0 };
        var next = new Button { Content = last ? L("Fertig") : L("Weiter"), Classes = { "primary" } };
        var buttons = new DockPanel { Margin = new(0, 6, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { back, next } };
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = $"{i + 1} / {steps.Length}", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center }, skip } });
        panel.Children.Add(buttons);

        var flyout = new Flyout { Content = panel, Placement = step.Placement };
        void Go(Action then) { moved = true; flyout.Hide(); DispatcherTimer.RunOnce(then, TimeSpan.FromMilliseconds(150)); }   // the old bubble must be gone first
        next.Click += (_, _) => Go(() => ShowTourStep(steps, i + 1));
        back.Click += (_, _) => Go(() => ShowTourStep(steps, i - 1));
        skip.Click += (_, _) => Go(FinishTour);
        flyout.Closed += (_, _) => { if (!moved) FinishTour(); };   // a click somewhere else ends the tour
        flyout.ShowAt(step.Anchor());
    }

    void FinishTour()
    {
        _touring = false;
        DispatcherTimer.RunOnce(() => { _palette.IsOpen = false; if (QuickJumpBox.IsFocused) Focus(); }, TimeSpan.FromMilliseconds(250));
        if (!_settings.TourPending) return;
        _settings.TourPending = false; SaveSettings();
    }
}
