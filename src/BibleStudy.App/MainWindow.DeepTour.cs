using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// The deep tour: after the short tour (or from the help page, the message center, a suggestion) a long walk through the program at one real example,
/// John 3:16. Many steps have a small task that the person does in the real program (the card stays open beside it and ticks the task off as soon as it
/// is done); "Show me" does it for them. Where one stopped is kept, a list jumps to any step, and Back goes one step back.
/// </summary>
public partial class MainWindow
{
    /// <summary>One step: chapter, text, the control it is about (red frame), an optional task with its check, and what "Show me" does.</summary>
    sealed record DeepStep(string Id, string Chapter, string Title, string Text,
        Func<Control?>? Anchor = null, string? Task = null, Func<bool>? Done = null, Func<Task>? Demo = null, Func<Task>? Prepare = null, Func<bool>? Fits = null, string? DemoLabel = null);

    const string DemoBook = "JHN", DemoTarget = "ROM";
    const int DemoChapter = 3, DemoVerse = 16, DemoTargetChapter = 5, DemoTargetVerse = 8;

    DeepStep[]? _deepSteps;
    int _deepIndex;
    Border? _deepCard;
    DispatcherTimer? _deepTimer;
    bool _deepXrefMade;
    string? _deepConcStart;
    int _deepStudyStart;
    /// <summary>How many words were looked up in the word study (the deep tour sees from it that a word was tapped).</summary>
    int _studyCount;

    bool DeepTouring => _deepCard is not null;

    Control? DemoVerseBox() => _curBook == DemoBook && _curChapter == DemoChapter && _verseBoxes.TryGetValue(DemoVerse, out var b) ? b : null;

    async Task EnsureDemoChapterAsync()
    {
        ShowReader();
        if (_curBook != DemoBook || _curChapter != DemoChapter) await GoToAsync(DemoBook, DemoChapter, DemoVerse);
    }

    bool HasDemoXref()
    {
        try
        {
            return _vault.GetMyXrefs(DemoBook, DemoChapter).Any(x => x.From.Book == DemoBook && x.From.Chapter == DemoChapter && x.From.Verse == DemoVerse
                && x.To.Book == DemoTarget && x.To.Chapter == DemoTargetChapter && x.To.Verse == DemoTargetVerse);
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { return false; }
    }

    DeepStep[] DeepTourSteps()
    {
        var read = K("Lesen"); var verse = K("Vers und Markieren"); var words = K("Wortstudie"); var refs = K("Querverweise"); var search = K("Suchen"); var order = K("Ordnen und Planen");
        DeepStep[] all =
        [
            new("intro", read, K("Willkommen zum Tiefen-Rundgang"),
                K("Wir lernen das Programm an einem echten Beispiel kennen: Johannes 3,16. Bei vielen Schritten gibt es eine kleine Aufgabe, die du direkt im Programm erledigst; dieses Kärtchen bleibt dabei offen und hakt sie ab. „Zeig’s mir“ erledigt sie für dich. Unter „Schritte“ springst du zu jedem Schritt, und das Programm merkt sich, wo du aufgehört hast.")),
            new("goto", read, K("Zu einer Bibelstelle springen"),
                K("Klick oben in das Feld „Gehe zu …“ (oder drück Strg+K), tippe „Joh 3,16“ und Enter. Dasselbe Feld findet auch Bücher, Wörter, Befehle und deine Dokumente. Am Handy tippst du dafür auf den Titel oben."),
                () => _phone ? _pTitle : QuickJumpBox, K("Öffne Johannes 3."),
                () => _curBook == DemoBook && _curChapter == DemoChapter, EnsureDemoChapterAsync),
            new("book", read, K("Buch und Kapitel"),
                K("Hier siehst du alle Bücher auf einen Blick; nach dem Buch kommt gleich die Kapitelliste. Mit ◀ ▶ oder den Pfeiltasten blätterst du Kapitel, der Pfeil ganz links bringt dich zur vorigen Stelle zurück."),
                () => BookBtn, Fits: () => !_phone),
            new("translation", read, K("Übersetzung wechseln"),
                K("Viele Übersetzungen sind frei dabei. Für die Wortstudie brauchst du eine mit Strong-Nummern; hat die gewählte keine, bietet dir das Programm eine passende an."),
                () => _phone ? _pTrans : TransBtn),
            new("parallel", read, K("Übersetzungen vergleichen"),
                K("„Parallel“ stellt eine weitere Übersetzung als Spalte daneben. Vergleiche Joh 3,16 in zwei Übersetzungen: Wo unterscheiden sie sich? Jede Spalte kann auch eine andere Stelle zeigen."),
                () => ParallelBtn, Fits: () => !_phone),

            new("verse", verse, K("Einen Vers wählen"),
                K("Klick auf Vers 16 (am Handy antippen). Notiz, Lesezeichen und die Querverweise rechts beziehen sich dann auf diesen Vers. Mit Umschalt+Klick wählst du mehrere Verse."),
                DemoVerseBox, K("Wähle Johannes 3,16."),
                () => _curBook == DemoBook && _curChapter == DemoChapter && _selected == DemoVerse,
                async () => { await EnsureDemoChapterAsync(); SelectVerse(DemoVerse); }, EnsureDemoChapterAsync),
            new("mark", verse, K("Markieren"),
                K("Mit dem Stift färbst du den gewählten Vers oder einen markierten Teil des Textes ein. Per Tastatur geht es mit der Leertaste. Alle Markierungen findest du unter „Sammlung“ wieder, nach Farben und Ordnern."),
                () => _phone ? DemoVerseBox() : MarkBtn, K("Färbe Vers 16 in einer Farbe deiner Wahl ein."),
                () => _curBook == DemoBook && _curChapter == DemoChapter && _wholeMarks.ContainsKey(DemoVerse), Prepare: EnsureDemoChapterAsync),
            new("bookmark", verse, K("Lesezeichen"),
                K("„Merken“ (Strg+D) setzt ein Lesezeichen auf den Vers, zum späteren Zurückspringen. Unter „Merken“ findest du alle wieder."),
                () => BookmarkBtn, Fits: () => !_phone),
            new("note", verse, K("Eine Notiz zum Vers"),
                K("Rechts unter „Notizen“ schreibst du zum gewählten Vers, was dir auffällt. Bibelstellen wie „Röm 5,8“ werden darunter von selbst zu Links. Notizen werden beim Wechseln automatisch gespeichert."),
                () => NoteEditor, Prepare: async () => { await EnsureDemoChapterAsync(); if (_selected != DemoVerse) SelectVerse(DemoVerse); RevealSideTab(2); }),

            new("study-on", words, K("Wortstudie einschalten"),
                K("Mit diesem Schalter werden die Wörter unterstrichen, hinter denen ein hebräisches oder griechisches Grundwort mit Strong-Nummer steht."),
                () => StudyBtn, K("Schalte die Wortstudie ein."),
                () => StudyBtn.IsChecked == true, async () => { await EnsureDemoChapterAsync(); StudyBtn.IsChecked = true; }, EnsureDemoChapterAsync, () => !_phone),
            new("study-word", words, K("Ein Wort antippen"),
                K("Tippe in Vers 16 auf „geliebt“ (je nach Übersetzung „liebte“). Rechts erscheint das griechische Grundwort agapaō (G25): Bedeutung, Aussprache und wie es übersetzt wird."),
                DemoVerseBox, K("Tippe ein unterstrichenes Wort an."),
                () => _studyCount > _deepStudyStart,
                async () => { await EnsureDemoChapterAsync(); RevealSideTab(StrongTab); StrongSearchBox.Text = "G25"; await SearchStrongAsync(); },
                async () => { await EnsureDemoChapterAsync(); _deepStudyStart = _studyCount; }),
            new("occurrences", words, K("Alle Vorkommen"),
                K("Unter dem Wort führt „Alle Vorkommen“ zur Konkordanz: jede Stelle, an der agapaō steht, nach Büchern geordnet. So siehst du, wie Johannes das Wort sonst gebraucht, und „Vorkommen als Diagramm“ zeigt die Verteilung über die Bibel."),
                null, K("Öffne die Vorkommen eines Wortes."),
                () => (ConcTitle.Text ?? "") is { Length: > 0 } t && t != _deepConcStart,
                async () => await ShowConcordanceAsync("G25", "G25  ἀγαπάω"),
                () => { _deepConcStart = ConcTitle.Text ?? ""; return Task.CompletedTask; }),
            new("grundtext", words, K("Grundtext Wort für Wort"),
                K("Rechtsklick auf den Vers › „Studium“ › „Grundtext Wort für Wort“ zeigt den ganzen Vers im Griechischen: Umschrift, Grammatik und Bedeutung jedes Wortes. Ein Klick auf ein Wort öffnet das Wörterbuch."),
                () => DemoVerseBox(), null, null,
                async () => { await EnsureDemoChapterAsync(); RevealSideTab(GrundtextTab); await ShowGrundtextAsync(new VerseRef(DemoBook, DemoChapter, DemoVerse)); },
                EnsureDemoChapterAsync),

            new("xrefs", refs, K("Querverweise"),
                K("Rechts unter „Querverweise“ stehen die gedruckten Querverweise zum gewählten Vers. Ein Klick führt hin, der Pfeil oben links bringt dich zurück."),
                null, Prepare: async () => { await EnsureDemoChapterAsync(); if (_selected != DemoVerse) SelectVerse(DemoVerse); RevealSideTab(4); }),
            new("myxref-add", refs, K("Eigenen Querverweis anlegen"),
                K("Röm 5,8 sagt etwas sehr Ähnliches: „Gott aber erweist seine Liebe zu uns darin, dass Christus für uns gestorben ist.“ Verknüpfe die beiden: Rechtsklick auf Vers 16 (am Handy lange tippen) › „Studium“ › „Eigenen Querverweis setzen …“, als Ziel „Röm 5,8“ eintippen und speichern."),
                DemoVerseBox, K("Lege den Querverweis Joh 3,16 → Röm 5,8 an."),
                () => { var has = HasDemoXref(); if (has) _deepXrefMade = true; return has; },
                async () => { await EnsureDemoChapterAsync(); if (DemoVerseBox() is { } box) ShowMyXrefEditor(new VerseRef(DemoBook, DemoChapter, DemoVerse), box, "Röm 5,8"); },
                EnsureDemoChapterAsync),
            new("myxref-chip", refs, K("Der goldene Chip"),
                K("Unter Vers 16 steht jetzt ein goldener Chip „★ Röm 5,8“. Ein Klick führt hin; dort steht unter Vers 8 „↩ Joh 3,16“ und führt zurück. Eigene Querverweise gehören zu deinem Vault und kommen mit in die Sicherung."),
                DemoVerseBox, Prepare: EnsureDemoChapterAsync),
            new("myxref-del", refs, K("Querverweis wieder löschen"),
                K("Rechtsklick auf den goldenen Chip (am Handy lange tippen) › „Eigenen Querverweis löschen“. Danach ist er auf beiden Seiten verschwunden."),
                DemoVerseBox, K("Lösche den Querverweis Joh 3,16 → Röm 5,8 wieder."),
                () => _deepXrefMade && !HasDemoXref(),
                async () =>
                {
                    foreach (var x in _vault.GetMyXrefs(DemoBook, DemoChapter).Where(x => x.From.Verse == DemoVerse && x.To.Book == DemoTarget && x.To.Chapter == DemoTargetChapter && x.To.Verse == DemoTargetVerse))
                        _vault.DeleteMyXref(x.Id);
                    _myXrefKey = null; await RedrawChapterAsync();
                },
                async () => { await EnsureDemoChapterAsync(); if (HasDemoXref()) _deepXrefMade = true; }),

            new("search", search, K("Suchen"),
                K("Rechts unter „Suche“ findest du Wörter und Wendungen in der ganzen Bibel, nach Büchern gruppiert. „In Anführungszeichen“ sucht genau diese Wendung, „G25“ sucht nach der Strong-Nummer, also nach dem Grundwort unabhängig von der Übersetzung."),
                () => SearchBox, K("Suche nach „Liebe“."),
                () => (SearchBox.Text ?? "").Contains(L("Liebe"), StringComparison.OrdinalIgnoreCase),
                async () => { RevealSideTab(0); SearchBox.Text = L("Liebe"); await SearchAsync(); },
                () => { ShowReader(); RevealSideTab(0); return Task.CompletedTask; }),

            new("vault", order, K("Vaults"),
                K("Alles, was du anlegst (Markierungen, Notizen, Querverweise, Dokumente), gehört zu einem Vault. Lege zum Beispiel einen für eine Predigtreihe an und einen für dich privat; oben wechselst du zwischen ihnen."),
                () => VaultTopBtn, Fits: () => !_phone),
            new("docs", order, K("Dokumente"),
                K("Unter „Dokumente“ schreibst du längere Texte: Studien, Andachten, Predigten. Einen Vers fügst du per Rechtsklick › „In offenes Dokument einfügen“ ein, Bibelstellen werden zu Links."),
                null, Prepare: () => { OpenWork(NavGroups.First(g => g.Name == "Dokumente"), KnowledgeTab); return Task.CompletedTask; }),
            new("plans", order, K("Lesepläne und mehr"),
                K("Unter „Planen“ liegen Lesepläne mit Fortschritt, To-dos, Gebete und das Auswendiglernen von Versen."),
                null, Prepare: () => { if (GroupOf(PlanTab) is { } g) OpenWork(g, PlanTab); return Task.CompletedTask; }),
            new("center", order, K("Mitteilungen und Vorschläge"),
                K("Ab und zu zeigt dir das Programm unten in der Ecke einen Vorschlag. Unter Einstellungen › „Mitteilungen und Vorschläge“ siehst du alle, schaltest sie an oder aus und findest, was neu ist."),
                null, null, null, () => { ShowMessageCenter(); return Task.CompletedTask; }, () => { ShowReader(); return Task.CompletedTask; }),
            new("end", order, K("Geschafft!"),
                K("Du kennst jetzt die wichtigsten Werkzeuge. Teste dich mit dem kleinen Quiz: ein paar Fragen und zwei Aufgaben, die du im Programm nachschlägst. Den Tiefen-Rundgang findest du jederzeit unter Hilfe wieder."),
                null, null, null, () => { ShowQuiz(); return Task.CompletedTask; }, () => { ShowReader(); return Task.CompletedTask; }, DemoLabel: K("Quiz starten")),
        ];
        return all.Where(s => s.Fits?.Invoke() ?? true).ToArray();
    }

    /// <summary>Starts the deep tour where it was left (or at the beginning).</summary>
    void StartDeepTour(int? at = null)
    {
        if (_touring) return;
        _palette.IsOpen = false; Focus();
        _deepSteps = DeepTourSteps();
        var start = at ?? _settings.DeepTourStep;
        if (start < 0 || start >= _deepSteps.Length) start = 0;
        _ = ShowDeepStepAsync(start);
    }

    async Task ShowDeepStepAsync(int i)
    {
        if (_deepSteps is not { } steps) return;
        i = Math.Clamp(i, 0, steps.Length - 1);
        _deepIndex = i;
        var step = steps[i];
        _settings.DeepTourStep = i;
        if (!_settings.DeepTourSeen.Contains(step.Id)) _settings.DeepTourSeen.Add(step.Id);
        SaveSettings();

        try
        {
            if (step.Prepare is { } prepare) await prepare();
            else if (_activeWork is not null) ShowReader();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
        BuildDeepCard(step, i, steps.Length);
        // the frame goes on the control once the page has its place (a tab that just opened, a chapter that just loaded)
        DispatcherTimer.RunOnce(() => { if (_deepIndex == i && DeepTouring) RingDeepAnchor(step); }, TimeSpan.FromMilliseconds(400));
    }

    void RingDeepAnchor(DeepStep step)
    {
        HideTourRing();
        Control? anchor = null;
        try { anchor = step.Anchor?.Invoke(); } catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or KeyNotFoundException) { }
        if (anchor is null || !anchor.IsEffectivelyVisible || anchor.Bounds.Width == 0) return;
        if (TopLevel.GetTopLevel(anchor) is null) return;
        try { RevealSetting(anchor); anchor.BringIntoView(); } catch (InvalidOperationException) { }
        ShowTourRing(anchor);
    }

    void BuildDeepCard(DeepStep step, int i, int count)
    {
        if (!EnsureNoticeHost()) return;
        if (_deepCard is null)
        {
            _deepCard = new Border
            {
                CornerRadius = new(14), Padding = new(16, 12), BorderThickness = new(2), ZIndex = 1001,
                BoxShadow = BoxShadows.Parse("0 6 24 0 #40000000"),
            };
            _deepCard.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
            _deepCard.BorderBrush = new SolidColorBrush(Color.Parse(NoticeMessage));
            _noticeHost!.Children.Add(_deepCard);
        }
        var card = _deepCard;
        card.Width = PW(400);
        card.HorizontalAlignment = _phone ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        card.VerticalAlignment = _phone ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        card.Margin = _phone ? new Thickness(12, 56, 12, 0) : new Thickness(16, 0, 0, 36);

        var panel = new StackPanel { Spacing = 8 };
        var crumb = new TextBlock { Text = L("Tiefen-Rundgang · {0} · Schritt {1} von {2}", L(step.Chapter), i + 1, count), FontSize = 11, TextWrapping = TextWrapping.Wrap };
        crumb.Foreground = new SolidColorBrush(Color.Parse(NoticeMessage));
        panel.Children.Add(crumb);
        panel.Children.Add(new ProgressBar { Minimum = 0, Maximum = count, Value = i + 1, Height = 4, MinHeight = 4 });
        panel.Children.Add(new TextBlock { Text = L(step.Title), FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = L(step.Text), TextWrapping = TextWrapping.Wrap });

        TextBlock? taskState = null;
        if (step.Task is { } task)
        {
            taskState = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
            var box = new Border { CornerRadius = new(8), Padding = new(10, 6), Child = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = L("Deine Aufgabe: {0}", L(task)), TextWrapping = TextWrapping.Wrap }, taskState } } };
            box.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsAccentSoft"));
            panel.Children.Add(box);
        }

        var stepsBtn = new Button { Content = L("Schritte"), Classes = { "ghost" } };
        stepsBtn.Click += (_, _) => ShowDeepStepList(stepsBtn);
        var pause = new Button { Content = L("Pause"), Classes = { "ghost" } };
        ToolTip.SetTip(pause, L("Schließt den Rundgang. Er geht beim nächsten Mal hier weiter (Hilfe › Tiefen-Rundgang)."));
        pause.Click += (_, _) => CloseDeepTour(finished: false);
        var back = new Button { Content = L("Zurück"), IsVisible = i > 0 };
        back.Click += (_, _) => _ = ShowDeepStepAsync(i - 1);
        var last = i == count - 1;
        var next = new Button { Content = last ? L("Fertig") : L("Weiter"), Classes = { "primary" } };
        next.Click += (_, _) => { if (last) CloseDeepTour(finished: true); else _ = ShowDeepStepAsync(i + 1); };
        Button? demo = null;
        if (step.Demo is { } run)
        {
            demo = new Button { Content = step.DemoLabel is { } label ? L(label) : step.Task is null ? L("Zeigen") : L("Zeig’s mir") };
            demo.Click += async (_, _) =>
            {
                try { await run(); } catch (Exception ex) { ErrorLog.Write(ex); StatusText.Text = ex.Message; }
                DispatcherTimer.RunOnce(() => { if (_deepIndex == i && DeepTouring) RingDeepAnchor(step); }, TimeSpan.FromMilliseconds(400));
            };
        }
        var left = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Children = { stepsBtn, pause } };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        if (demo is not null) right.Children.Add(demo);
        right.Children.Add(back); right.Children.Add(next);
        panel.Children.Add(new StackPanel { Spacing = 6, Margin = new(0, 4, 0, 0), Children = { right, left } });
        card.Child = panel;

        // the task ticks itself off as soon as it is done in the program
        _deepTimer?.Stop();
        if (step.Done is { } done && taskState is not null)
        {
            void Check()
            {
                bool ok;
                try { ok = done(); } catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or Microsoft.Data.Sqlite.SqliteException) { ok = false; }
                taskState.Text = ok ? "✓ " + L("Geschafft! Weiter geht’s.") : L("Noch offen.");
                taskState.Foreground = new SolidColorBrush(Color.Parse(ok ? NoticeGood : "#8A8A8A"));
            }
            Check();
            _deepTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _deepTimer.Tick += (_, _) => { if (_deepIndex == i && DeepTouring) Check(); else _deepTimer?.Stop(); };
            _deepTimer.Start();
        }
    }

    /// <summary>All steps by chapter: done ones ticked, the current one marked; a click jumps there.</summary>
    void ShowDeepStepList(Control anchor)
    {
        if (_deepSteps is not { } steps) return;
        var list = new StackPanel { Spacing = 2 };
        var fly = new Flyout { Placement = Avalonia.Controls.PlacementMode.Top };
        string? chapter = null;
        for (var n = 0; n < steps.Length; n++)
        {
            var s = steps[n]; var index = n;
            if (s.Chapter != chapter)
            {
                chapter = s.Chapter;
                list.Children.Add(new TextBlock { Text = L(chapter), FontWeight = FontWeight.SemiBold, Margin = new(0, n == 0 ? 0 : 8, 0, 2) });
            }
            var mark = n == _deepIndex ? "▶ " : _settings.DeepTourSeen.Contains(s.Id) ? "✓ " : "○ ";
            var b = new Button { Content = mark + L(s.Title), Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, FontWeight = n == _deepIndex ? FontWeight.SemiBold : FontWeight.Normal };
            b.Click += (_, _) => { fly.Hide(); _ = ShowDeepStepAsync(index); };
            list.Children.Add(b);
        }
        fly.Content = new ScrollViewer { MaxHeight = 420, Width = PW(300), Content = list };
        fly.ShowAt(anchor);
    }

    void CloseDeepTour(bool finished)
    {
        _deepTimer?.Stop();
        HideTourRing();
        if (_deepCard?.Parent is Panel p) p.Children.Remove(_deepCard);
        _deepCard = null;
        if (finished)
        {
            _settings.DeepTourDone = true;
            _settings.DeepTourStep = 0;
            SaveSettings();
            Notify(L("Tiefen-Rundgang abgeschlossen"), L("Du findest ihn jederzeit unter Hilfe wieder."), good: true);
        }
        else Notify(L("Rundgang pausiert"), L("Unter Hilfe › Tiefen-Rundgang geht es bei Schritt {0} weiter.", _deepIndex + 1));
    }

    /// <summary>After the short tour: the offer of the deep tour (asked once; later it comes as a suggestion in the message center).</summary>
    async Task OfferDeepTourAsync()
    {
        if (_settings.DeepTourOffered || _settings.DeepTourDone) return;
        _settings.DeepTourOffered = true; SaveSettings();
        var answer = await AskAsync(L("Tiefer einsteigen?"),
            L("Der Tiefen-Rundgang geht an einem echten Beispiel (Johannes 3,16) Schritt für Schritt durch das Programm: Wortstudie, eigene Querverweise anlegen und löschen, Suche, Notizen und mehr. Du machst jeden Schritt selbst, kannst jederzeit pausieren und später weitermachen."),
            L("Ja, los geht’s"), L("Später"));
        if (answer == 0) DispatcherTimer.RunOnce(() => StartDeepTour(0), TimeSpan.FromMilliseconds(300));
    }
}
