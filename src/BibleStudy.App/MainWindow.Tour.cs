using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// A short tour on the first start: one bubble per important button, pointing at the real button, with Next / Back / Skip.
/// It can be started again any time (help page, command field). For people who had the program before, a single question about backups.
/// </summary>
public partial class MainWindow
{
    sealed record TourStep(Func<Control> Anchor, PlacementMode Placement, string Title, string Text, Func<Control?>? Extra = null, Action? Prepare = null);

    bool _touring;

    void InitTour()
    {
        TourBtn.Click += (_, _) => StartTour();
        InitHelpPage();
        // a while after the start, when the splash screen and the notice about sources are gone
        DispatcherTimer.RunOnce(() => OfferTour(), TimeSpan.FromSeconds(6));
    }

    async Task RunSetupThenTourAsync()
    {
        await RunSetupWizardAsync();
        if (_exiting) return;
        OfferTour();
    }

    void OfferTour()
    {
        if (!IsVisible || _touring) return;
        // another window (notice, update question) is open: try again a little later
        if (OwnedWindows.Count > 0 || SplashWindow.IsShowing) { DispatcherTimer.RunOnce(() => OfferTour(), TimeSpan.FromSeconds(5)); return; }
        if (_settings.TourPending && !_settings.SetupDone) { _ = RunSetupThenTourAsync(); return; }
        if (_settings.TourPending) { _settings.LastSeenVersion = AppPaths.Version; StartTour(); return; }   // a new installation has nothing "new" to read
        if (!_settings.BackupAsked && _settings.BackupSchedule == "off") { _ = AskForBackupAsync(); return; }
        _ = ShowPatchNotesIfNewAsync();
        _ = OfferReimportAsync();
        DispatcherTimer.RunOnce(ShowNewKeysTip, TimeSpan.FromSeconds(4));
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
            new(() => BookmarkBtn, PlacementMode.Bottom, L("Merken"), L("Setzt ein Lesezeichen auf den Vers (Strg+D), zum späteren Zurückspringen. Unter „Merken“ rechts in der Leiste findest du sie wieder.")),
            new(() => Rail, PlacementMode.Left, L("Die Leiste rechts"), L("Suchen, Vers (Querverweise, Grundtext), Notizen, Dokumente, Predigt, Planen (Lesepläne, To-dos, Gebete, Auswendiglernen), Zusammen und KI. Ziehst du ein Symbol aus der Leiste, wird es ein eigenes Fenster.")),
            new(() => QuickJumpBox, PlacementMode.Bottom, L("Gehe zu … (Strg+K)"), L("Ein Feld für alles: eine Bibelstelle („Röm 8,28“), ein Wort, ein Befehl, ein Dokument. Tippen, mit den Pfeilen wählen, Enter.")),
            new(() => TodoTopBtn, PlacementMode.Bottom, L("To-dos"), L("Eine freie Liste zum Abhaken, von überall erreichbar.")),
            new(() => VaultTopBtn, PlacementMode.Bottom, L("Vault"), L("Ein Vault ist deine Mappe für ein Studienprojekt (z. B. „Predigt“ und „Privat“). Notizen, Markierungen und Dokumente gehören immer zu einem Vault.")),
            new(() => SettingsTopBtn, PlacementMode.Bottom, L("Einstellungen, Hilfe und Sicherung"),
                L("Hier stellst du Aussehen, Sicherung und Updates ein. Dieser Rundgang lässt sich unter Hilfe jederzeit neu starten. Das Programm installiert nie ohne deine Zustimmung."),
                () => BackupExtra()),
            // the tour goes on inside the settings (tab 12), the help page (13) and ends at the documentation
            new(() => DownloadAllBtn, PlacementMode.Bottom, L("Alles auf einmal laden"),
                L("Hier lädst du mit einem Klick alles, was sonst erst beim Gebrauch kommt: Übersetzungen, Wörterbücher, Grundtext-Daten, Querverweise und Stimmen. Am besten einmal am Anfang, dann wartest du später nie."),
                null, () => OpenWork(NavGroups.First(g => g.Name == "Mehr"), SettingsTab)),
            new(() => BackupScheduleBox, PlacementMode.Top, L("Automatische Sicherung"),
                L("Deine Arbeit liegt nur auf diesem Computer. Hier stellst du ein, dass sie täglich, wöchentlich oder monatlich in einen Ordner deiner Wahl gesichert wird, zum Beispiel in einen Cloud-Ordner."),
                null, () => OpenWork(NavGroups.First(g => g.Name == "Mehr"), SettingsTab)),
            new(() => TourBtn, PlacementMode.Bottom, L("Hilfe"),
                L("Die Hilfe erklärt kurz jede Funktion, vom Lesen über die Suche bis zum Zusammen-Studieren. Hier startest du diesen Rundgang auch wieder."),
                null, () => OpenWork(NavGroups.First(g => g.Name == "Hilfe"), 13)),
            new(() => (Control?)_hbSearch ?? Rail, PlacementMode.Bottom, L("Dokumentation"),
                L("Das ausführliche Handbuch: jede Funktion, jedes Tastenkürzel, Anwendung und Strategien. Mit Inhaltsverzeichnis und Suche. Von hier aus kannst du alles Weitere selbst entdecken."),
                null, () => OpenWork(NavGroups.First(g => g.Name == "Hilfe"), HandbookTab)),
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

    void ShowTourStep(TourStep[] steps, int i, bool prepared = false)
    {
        _palette.IsOpen = false;   // closing a bubble hands the focus back to the quick-jump field, which would open its list under the next bubble
        // a step inside the settings or the help needs that page open; the steps at the top need the reading page back
        if (!prepared && i >= 0 && i < steps.Length && (steps[i].Prepare is not null || _activeWork is not null))
        {
            if (steps[i].Prepare is { } prepare) prepare(); else ShowReader();
            DispatcherTimer.RunOnce(() =>
            {
                try { steps[i].Anchor().BringIntoView(); } catch { /* the anchor may not exist yet; the step is skipped below */ }
                DispatcherTimer.RunOnce(() => ShowTourStep(steps, i, true), TimeSpan.FromMilliseconds(180));
            }, TimeSpan.FromMilliseconds(350));
            return;
        }
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
        flyout.Closed += (_, _) => { HideTourRing(); if (!moved) FinishTour(); };   // a click somewhere else ends the tour
        ShowTourRing(step.Anchor());
        flyout.ShowAt(step.Anchor());
    }

    Border? _tourRing;

    /// <summary>A red frame around the button (or area) the bubble talks about, so it is clear what the text refers to.</summary>
    void ShowTourRing(Control target)
    {
        HideTourRing();
        if (AdornerLayer.GetAdornerLayer(target) is not { } layer) return;
        _tourRing = new Border { BorderBrush = Brushes.Red, BorderThickness = new(2.5), CornerRadius = new(6), IsHitTestVisible = false };
        AdornerLayer.SetAdornedElement(_tourRing, target);
        layer.Children.Add(_tourRing);
    }

    void HideTourRing()
    {
        if (_tourRing?.Parent is Panel p) p.Children.Remove(_tourRing);
        _tourRing = null;
    }

    void FinishTour()
    {
        _touring = false; HideTourRing();
        if (_activeWork is not null) ShowReader();   // the tour may have ended inside the settings
        DispatcherTimer.RunOnce(() => { _palette.IsOpen = false; if (QuickJumpBox.IsFocused) Focus(); }, TimeSpan.FromMilliseconds(250));
        if (!_settings.TourPending) return;
        _settings.TourPending = false; SaveSettings();
        if (_settings.AutoUpdate) DispatcherTimer.RunOnce(async () => await CheckForUpdateAsync(manual: false), TimeSpan.FromSeconds(2));   // it waited for the first start to be over
    }
}
