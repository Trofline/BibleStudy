using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// The message center: now and then a small suggestion in the corner (in the style of the other notices, in violet) shows a part of the
/// program one may not know yet. At most one a day and one per start, never during the tour or while another window is open.
/// A window (settings, help, command field) lists every suggestion: show it again, try it, turn it off; and what is new in this version,
/// and the notices of this run.
/// </summary>
public partial class MainWindow
{
    /// <summary>One suggestion: what it is about, what one can do with it (optional), and whether it fits here (phone, computer, the state of the settings).</summary>
    sealed record Suggestion(string Id, string Title, string Text, string? ActionLabel = null, Action? Run = null, Func<bool>? Fits = null);

    Suggestion[] AllSuggestions() =>
    [
        new("deep-tour", K("Tiefen-Rundgang"),
            K("Lerne das Programm an einem echten Beispiel gründlich kennen: Wortstudie, eigene Querverweise, Suche, Notizen und mehr. Du machst jeden Schritt selbst und kannst jederzeit pausieren."),
            K("Starten"), () => StartDeepTour(), () => !_settings.DeepTourDone),
        new("quiz", K("Quiz: Kennst du BibleStudy?"),
            K("Ein paar Fragen und kleine Aufgaben, die du direkt im Programm nachschlägst, zum Beispiel wie oft ein griechisches Wort im Johannesevangelium vorkommt."),
            K("Starten"), ShowQuiz, () => _settings.DeepTourDone),
        new("quickjump", K("Gehe zu … (Strg+K)"),
            K("Ein Feld für alles: Bibelstellen („Röm 8,28“), Wörter, Befehle, Dokumente und Strong-Nummern. Tippen, mit den Pfeilen wählen, Enter."),
            K("Ausprobieren"), () => { ShowReader(); QuickJumpBox.Focus(); }, () => !_phone),
        new("word-study", K("Wortstudie"),
            K("Schalte die Wortstudie ein und tippe ein unterstrichenes Wort an: Du siehst das hebräische oder griechische Grundwort, seine Bedeutung und alle Stellen, an denen es vorkommt."),
            K("Einschalten"), () => { ShowReader(); StudyBtn.IsChecked = true; }, () => !_phone),
        new("my-xref", K("Eigene Querverweise"),
            K("Rechtsklick auf einen Vers (am Handy lange tippen), dann „Studium“ › „Eigenen Querverweis setzen …“. Er erscheint als goldener Chip unter dem Vers, und am Ziel führt einer zurück. Ein Rechtsklick auf den Chip löscht ihn wieder.")),
        new("parallel", K("Übersetzungen nebeneinander"),
            K("Mit „Parallel“ oben im Lesebereich kommen weitere Übersetzungen als Spalten dazu. Jede Spalte kann auch eine andere Stelle zeigen."),
            Fits: () => !_phone),
        new("split", K("Geteilte Ansicht"),
            K("Bis zu vier Bereiche nebeneinander, zum Beispiel links die Bibel und rechts deine Notizen oder ein Dokument."),
            K("Einschalten"), () => SplitTopBtn.IsChecked = true, () => !_phone && SplitTopBtn.IsChecked != true),
        new("keys", K("Tastenkürzel"),
            K("Fast alles geht auch mit der Tastatur: Kapitel blättern, Verse wählen und einfärben, zwischen Lesen und dem letzten Reiter wechseln."),
            K("Alle Tastenkürzel"), ShowKeysWindow, () => !AppPlatform.IsMobile),
        new("download-all", K("Alles auf einmal laden"),
            K("Im Download-Manager lädst du Übersetzungen, Wörterbücher, Grundtext-Daten und Stimmen in einem Rutsch. Danach geht alles ohne Internet und ohne Warten."),
            K("Öffnen"), () => ShowSetting(DownloadAllBtn)),
        new("backup", K("Automatische Sicherung"),
            K("Deine Arbeit liegt nur auf diesem Gerät. Stell ein, dass sie regelmäßig in einen Ordner deiner Wahl gesichert wird, zum Beispiel in einen Cloud-Ordner."),
            K("Einstellen"), () => ShowSetting(BackupScheduleBox), () => _settings.BackupSchedule == "off"),
        new("sync", K("PC und Handy synchronisieren"),
            K("Gibt es BibleStudy auch auf deinem Handy? Einmal koppeln, dann gleichen sich Notizen, Markierungen und Lesefortschritt im selben WLAN von selbst ab."),
            K("Einrichten"), () => ShowSetting(SyncStatus), () => _syncCfg.Key is null),
        new("plans", K("Lesepläne"),
            K("Unter „Planen“ findest du Lesepläne, To-dos, Gebete und Verse zum Auswendiglernen. Ein Plan zeigt dir jeden Tag, was dran ist."),
            K("Öffnen"), () => OpenWork(GroupOf(PlanTab)!, PlanTab), () => GroupOf(PlanTab) is not null),
        new("handbook", K("Das Handbuch"),
            K("Jede Funktion ausführlich erklärt, mit Beispielen, Strategien fürs Bibelstudium und einer Suche."),
            K("Öffnen"), () => OpenWork(NavGroups.First(g => g.Name == "Hilfe"), HandbookTab)),
        new("tour", K("Rundgang"),
            K("Der kurze Rundgang zeigt die wichtigsten Knöpfe an Ort und Stelle. Du kannst ihn jederzeit wiederholen."),
            K("Starten"), () => DispatcherTimer.RunOnce(StartTour, TimeSpan.FromMilliseconds(350))),
    ];

    /// <summary>Opens the settings at the card that holds this control.</summary>
    void ShowSetting(Control target)
    {
        OpenWork(GroupOf(SettingsTab)!, SettingsTab);
        DispatcherTimer.RunOnce(() => { try { RevealSetting(target); } catch (InvalidOperationException) { /* the page is not built yet */ } }, TimeSpan.FromMilliseconds(350));
    }

    int _suggestTries;

    /// <summary>Some minutes after the start: one suggestion that was not shown yet, if suggestions are on and none came today.</summary>
    void PlanSuggestion() => DispatcherTimer.RunOnce(MaybeSuggest, TimeSpan.FromMinutes(3));

    void MaybeSuggest()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (!_settings.Suggestions || _settings.LastSuggestionDay == today || _settings.TourPending) return;
        // the person is busy (tour, a question, another notice on screen): a little later, a few times at most
        if (_touring || DeepTouring || !IsVisible || OwnedWindows.Count > 0 || _notices?.Children.Count > 0)
        {
            if (++_suggestTries < 10) DispatcherTimer.RunOnce(MaybeSuggest, TimeSpan.FromMinutes(1));
            return;
        }
        var next = AllSuggestions().FirstOrDefault(s => !_settings.SuggestionsSeen.Contains(s.Id) && !_settings.SuggestionsOff.Contains(s.Id) && SuggestionFits(s));
        if (next is null) return;
        _settings.LastSuggestionDay = today;
        ShowSuggestion(next);
    }

    bool SuggestionFits(Suggestion s)
    {
        try { return s.Fits?.Invoke() ?? true; }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException) { return false; }
    }

    void ShowSuggestion(Suggestion s)
    {
        if (!_settings.SuggestionsSeen.Contains(s.Id)) _settings.SuggestionsSeen.Add(s.Id);
        SaveSettings();
        NotifyMessage(L(s.Title), L(s.Text), s.ActionLabel is { } a ? L(a) : null, s.Run, L("Alle Vorschläge"), ShowMessageCenter);
    }

    Window? _messageCenter;

    /// <summary>The message center: suggestions on and off, again or tried at once, what is new, and the notices since the start.</summary>
    void ShowMessageCenter()
    {
        if (_messageCenter is not null) { _messageCenter.Activate(); return; }
        var stack = new StackPanel { Spacing = 10, Margin = new(0, 0, 8, 0) };

        TextBlock Head(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new(0, 8, 0, 0) };
        TextBlock Caption(string text) => new() { Text = text, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

        // what is new: the patch notes of this version (they also open by themselves after an update)
        stack.Children.Add(Head(L("Was ist neu")));
        var news = new Button { Content = L("Was ist neu in Version {0}", AppPaths.Version), HorizontalAlignment = HorizontalAlignment.Left };
        news.Click += async (_, _) => await ShowPatchNotesAsync();
        stack.Children.Add(news);

        // the suggestions
        stack.Children.Add(Head(L("Vorschläge")));
        var on = new CheckBox { Content = L("Gelegentlich Vorschläge zeigen (höchstens einer am Tag)"), IsChecked = _settings.Suggestions };
        stack.Children.Add(on);
        var list = new StackPanel { Spacing = 8 };
        stack.Children.Add(list);
        void Fill()
        {
            list.Children.Clear();
            foreach (var s in AllSuggestions().Where(SuggestionFits))
            {
                var fresh = !_settings.SuggestionsSeen.Contains(s.Id);
                var title = new TextBlock { Text = L(s.Title), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { title } };
                if (fresh)
                    head.Children.Add(new Border
                    {
                        CornerRadius = new(6), Padding = new(6, 1), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.Parse(NoticeMessage)),
                        Child = new TextBlock { Text = L("neu"), FontSize = 11, Foreground = Brushes.White },
                    });
                var active = new CheckBox { Content = L("An"), IsChecked = !_settings.SuggestionsOff.Contains(s.Id), IsEnabled = _settings.Suggestions };
                ToolTip.SetTip(active, L("Aus: Dieser Vorschlag kommt nicht mehr von selbst."));
                active.IsCheckedChanged += (_, _) =>
                {
                    _settings.SuggestionsOff.Remove(s.Id);
                    if (active.IsChecked != true) _settings.SuggestionsOff.Add(s.Id);
                    SaveSettings();
                };
                var again = new Button { Content = L("Nochmal zeigen"), Classes = { "ghost" } };
                again.Click += (_, _) => { ShowSuggestion(s); Fill(); };
                var buttons = new WrapPanel { Children = { active, again } };
                foreach (var c in buttons.Children) c.Margin = new(0, 0, 10, 0);
                if (s.ActionLabel is { } label && s.Run is { } run)
                {
                    var go = new Button { Content = L(label), Classes = { "ghost" } };
                    go.Click += (_, _) =>
                    {
                        if (!_settings.SuggestionsSeen.Contains(s.Id)) { _settings.SuggestionsSeen.Add(s.Id); SaveSettings(); }
                        _messageCenter?.Close();
                        run();
                    };
                    buttons.Children.Add(go);
                }
                var card = new Border
                {
                    CornerRadius = new(10), Padding = new(12, 8), BorderThickness = new(1),
                    Child = new StackPanel { Spacing = 4, Children = { head, Caption(L(s.Text)), buttons } },
                };
                card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
                list.Children.Add(card);
            }
        }
        on.IsCheckedChanged += (_, _) => { _settings.Suggestions = on.IsChecked == true; SaveSettings(); Fill(); };
        Fill();

        // the notices of this run, newest first
        stack.Children.Add(Head(L("Mitteilungen seit dem Start")));
        if (_noticeLog.Count == 0) stack.Children.Add(Caption(L("Noch keine.")));
        foreach (var n in Enumerable.Reverse(_noticeLog))
        {
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new(4), Background = new SolidColorBrush(Color.Parse(n.Color)), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 6, 8, 0) };
            var text = new StackPanel { Spacing = 1 };
            text.Children.Add(new TextBlock { Text = n.At.ToString("t", Loc.Culture) + "  " + n.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (n.Text.Length > 0) text.Children.Add(Caption(n.Text));
            var row = new DockPanel();
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot); row.Children.Add(text);
            stack.Children.Add(row);
        }

        var close = new Button { Content = L("Schließen"), Classes = { "primary" }, VerticalAlignment = VerticalAlignment.Center };
        var win = new Window { Title = L("Mitteilungen und Vorschläge"), Width = 600, Height = 620, MinWidth = 360, MinHeight = 300, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var dock = new DockPanel { Margin = new(20) };
        var title = new TextBlock { Text = L("Mitteilungen und Vorschläge"), FontSize = 20, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(close, Dock.Right);
        var top = new DockPanel { Margin = new(0, 0, 0, 10), Children = { close, title } };
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(new ScrollViewer { Content = stack });
        win.Content = dock;
        close.Click += (_, _) => win.Close();
        win.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) win.Close(); };
        win.Closed += (_, _) => _messageCenter = null;
        _messageCenter = win;
        win.Show(this);
    }
}
