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
    bool _deepDemoUsed;
    /// <summary>
    /// Counters of what the person did (chapters shown, verses chosen, searches, words looked up): the deep tour takes them at the start of a step
    /// and ticks a task off only when something happened after that, not because the state was already there.
    /// </summary>
    int _studyCount, _chapterShows, _selectCount, _searchCount, _typedJumps, _tabSwitches;
    sealed record DeepBase(int Chapters, int Selects, int Searches, int Studies, string Conc, string? Mark, HashSet<long> Xrefs,
        int Typed, int TabSwitches, int Extras, string Translation, HashSet<long> MarkIds);
    DeepBase _deepBase = new(0, 0, 0, 0, "", null, [], 0, 0, 0, "", []);

    bool OnDemoChapter => _curBook == DemoBook && _curChapter == DemoChapter;

    bool DeepTouring => _deepCard is not null;

    Control? DemoVerseBox() => _curBook == DemoBook && _curChapter == DemoChapter && _verseBoxes.TryGetValue(DemoVerse, out var b) ? b : null;

    async Task EnsureDemoChapterAsync()
    {
        ShowReader();
        if (_curBook != DemoBook || _curChapter != DemoChapter) await GoToAsync(DemoBook, DemoChapter, DemoVerse);
    }

    /// <summary>The ids of the cross references John 3:16 → Rom 5:8 in the vault (the one of the tour, and any the person had before).</summary>
    HashSet<long> DemoXrefIds()
    {
        try
        {
            return _vault.GetMyXrefs(DemoBook, DemoChapter).Where(x => x.From.Book == DemoBook && x.From.Chapter == DemoChapter && x.From.Verse == DemoVerse
                && x.To.Book == DemoTarget && x.To.Chapter == DemoTargetChapter && x.To.Verse == DemoTargetVerse).Select(x => (long)x.Id).ToHashSet();
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { return []; }
    }

    string? DemoMark() => OnDemoChapter && _wholeMarks.TryGetValue(DemoVerse, out var c) ? c : null;

    /// <summary>What was there when the step began: a task counts only for what changes afterwards.</summary>
    DeepBase TakeDeepBase() => new(_chapterShows, _selectCount, _searchCount, _studyCount, ConcTitle.Text ?? "", DemoMark(), DemoXrefIds(),
        _typedJumps, _tabSwitches, _extras.Count, CurrentTranslationId, _chapterMarks.Select(m => m.Id).ToHashSet());

    DeepStep[] DeepTourSteps()
    {
        var read = K("Lesen"); var verse = K("Vers und Markieren"); var words = K("Wortstudie"); var refs = K("Querverweise"); var study = K("Studium");
        var views = K("Ansichten"); var search = K("Suchen"); var tabs = K("Reiter und Fenster"); var order = K("Ordnen und Planen");
        var together = K("Zusammen und Geräte"); var help = K("Hilfe");
        var desk = () => !_phone;
        async Task OpenSettingAt(Control target) { OpenWork(GroupOf(SettingsTab)!, SettingsTab); await Task.Delay(50); RevealSetting(target); }
        DeepStep[] all =
        [
            // ---- reading ----
            new("intro", read, K("Willkommen zum Tiefen-Rundgang"),
                K("Wir lernen das Programm an einem echten Beispiel kennen: Johannes 3,16. Bei vielen Schritten gibt es eine kleine Aufgabe, die du direkt im Programm erledigst; dieses Kärtchen bleibt dabei offen und hakt sie ab. „Zeig’s mir“ erledigt sie für dich. Unter „Schritte“ springst du zu jedem Schritt, und das Programm merkt sich, wo du aufgehört hast.")),
            new("goto", read, K("Zu einer Bibelstelle springen"),
                K("Klick oben in das Feld „Gehe zu …“ (oder drück Strg+K), tippe „Joh 3,16“ und Enter. Dasselbe Feld findet auch Bücher, Wörter, Befehle und deine Dokumente. Am Handy tippst du dafür auf den Titel oben."),
                () => _phone ? _pTitle : QuickJumpBox, K("Öffne Johannes 3."),
                () => _chapterShows > _deepBase.Chapters && OnDemoChapter, EnsureDemoChapterAsync,
                // already at John 3: the tour starts somewhere else, so there is a way to go
                async () => { ShowReader(); if (OnDemoChapter) await GoToAsync("PSA", 23, 1); }),
            new("book", read, K("Buch und Kapitel"),
                K("Hier siehst du alle Bücher auf einen Blick; nach dem Buch kommt gleich die Kapitelliste. Mit ◀ ▶ oder den Pfeiltasten blätterst du Kapitel, der Pfeil ganz links bringt dich zur vorigen Stelle zurück."),
                () => BookBtn, Fits: desk),
            new("arrows", read, K("Mit den Pfeiltasten blättern"),
                K("← und → blättern ein Kapitel zurück und weiter. ↑ und ↓ scrollen den Text; ist ein Vers gewählt, springen sie zum Vers darüber oder darunter. Bild↑ und Bild↓ blättern eine ganze Seite."),
                null, K("Geh mit → ein Kapitel weiter und mit ← wieder zurück zu Johannes 3."),
                () => _chapterShows >= _deepBase.Chapters + 2 && OnDemoChapter,
                async () => { await GoToAsync(DemoBook, DemoChapter + 1); await Task.Delay(500); await GoToAsync(DemoBook, DemoChapter); },
                async () => { await EnsureDemoChapterAsync(); Focus(); }, desk),
            new("typed", read, K("Versnummer tippen"),
                K("Tipp einfach die Nummer eines Verses auf der Tastatur, ohne vorher in ein Feld zu klicken: 1 und gleich danach 6 springt zu Vers 16 und wählt ihn. Nach einer kurzen Pause beginnt eine neue Zahl."),
                null, K("Spring mit der Tastatur zu Vers 16."),
                () => _typedJumps > _deepBase.Typed && OnDemoChapter && _selected == DemoVerse,
                () => { JumpToTypedVerse(1); JumpToTypedVerse(6); return Task.CompletedTask; },
                async () => { await EnsureDemoChapterAsync(); SelectVerse(1); ScrollToVerse(1); Focus(); }, desk),
            new("translation", read, K("Übersetzung wechseln"),
                K("Viele Übersetzungen sind frei dabei, in vielen Sprachen. Für die Wortstudie brauchst du eine mit Strong-Nummern (z. B. KJV); hat die gewählte keine, bietet dir das Programm eine passende an."),
                () => _phone ? _pTrans : TransBtn, K("Wähle eine andere Übersetzung."),
                () => CurrentTranslationId != _deepBase.Translation, Prepare: EnsureDemoChapterAsync),
            new("parallel", read, K("Übersetzungen vergleichen"),
                K("„Parallel“ stellt eine weitere Übersetzung als Spalte daneben, bis zu drei. Vergleiche Joh 3,16 in zwei Übersetzungen: Wo unterscheiden sie sich?"),
                () => ParallelBtn, K("Füge eine Spalte hinzu."),
                () => _extras.Count > _deepBase.Extras, () => { AddColumn(); return Task.CompletedTask; }, EnsureDemoChapterAsync, desk),
            new("parallel-sync", read, K("Spalten synchron oder für sich"),
                K("Über den Spalten steht „Synchron scrollen“: An, laufen alle Spalten zusammen; aus, scrollt jede für sich. Mit dem Symbol daneben wählst du, welche Spalten zusammen laufen. Der Knopf mit der Bibelstelle im Kopf einer Spalte lässt sie eine ganz andere Stelle zeigen, z. B. 1. Mose 1 neben Johannes 1. Mit ✕ entfernst du eine Spalte wieder."),
                Prepare: EnsureDemoChapterAsync, Fits: desk),

            // ---- verses and marking ----
            new("verse", verse, K("Einen Vers wählen"),
                K("Klick auf Vers 16 (am Handy antippen). Notiz, Lesezeichen und die Querverweise rechts beziehen sich dann auf diesen Vers."),
                DemoVerseBox, K("Wähle Johannes 3,16."),
                () => _selectCount > _deepBase.Selects && OnDemoChapter && _selected == DemoVerse,
                async () => { await EnsureDemoChapterAsync(); SelectVerse(DemoVerse); }, EnsureDemoChapterAsync),
            new("multi", verse, K("Mehrere Verse wählen"),
                K("Halte Umschalt gedrückt und klick auf einen anderen Vers: Alle Verse dazwischen sind gewählt. Mit Strg+Klick nimmst du einzelne Verse dazu oder weg, mit Umschalt+↑/↓ geht es per Tastatur. Am Handy drückst du lange auf eine Versnummer und ziehst den Finger nach unten. Färben, Kopieren und „In Dokument einfügen“ gelten dann für alle."),
                DemoVerseBox, K("Wähle die Verse 16 bis 18."),
                () => OnDemoChapter && _selVerses.Count >= 2,
                () => { ChooseVerseRange(DemoVerse, DemoVerse + 2); return Task.CompletedTask; },
                async () => { await EnsureDemoChapterAsync(); SelectVerse(DemoVerse); }),
            new("mark", verse, K("Einen Vers markieren"),
                K("Mit dem Stift färbst du die gewählten Verse ein. Schneller geht es mit einem Klick auf die Versnummer: Er färbt den ganzen Vers in der Schnellfarbe. Per Tastatur öffnet die Leertaste die Farben, die Pfeiltasten wählen eine und Enter übernimmt sie."),
                () => _phone ? DemoVerseBox() : MarkBtn, K("Färbe Vers 16 in einer Farbe deiner Wahl ein."),
                () => DemoMark() is { } now && now != _deepBase.Mark, Prepare: EnsureDemoChapterAsync),
            new("word-mark", verse, K("Nur ein Wort markieren"),
                K("Du musst nicht immer den ganzen Vers färben. Zieh mit der Maus über ein einzelnes Wort, zum Beispiel „Welt“, und wähl dann mit dem Stift oben eine Farbe (oder drück die Leertaste). Am Handy drückst du lange auf das Wort. Auch so eine Markierung kann eine eigene Notiz bekommen und in einen Ordner kommen."),
                DemoVerseBox, K("Markiere ein einzelnes Wort."),
                () => OnDemoChapter && _chapterMarks.Any(m => !m.WholeVerse && !_deepBase.MarkIds.Contains(m.Id)), Prepare: EnsureDemoChapterAsync),
            new("bookmark", verse, K("Lesezeichen"),
                K("„Merken“ (Strg+D) setzt ein Lesezeichen auf den Vers, zum späteren Zurückspringen. Unter „Merken“ in der Leiste rechts findest du alle wieder."),
                () => BookmarkBtn, Fits: desk),
            new("note", verse, K("Eine Notiz zum Vers"),
                K("Rechts unter „Notizen“ schreibst du zum gewählten Vers, was dir auffällt. Bibelstellen wie „Röm 5,8“ werden darunter von selbst zu Links. Notizen werden beim Wechseln automatisch gespeichert. Unter „Markierungen“ daneben stehen alle deine Farben, nach Farben und Ordnern."),
                () => NoteEditor, Prepare: async () => { await EnsureDemoChapterAsync(); if (_selected != DemoVerse) SelectVerse(DemoVerse); RevealSideTab(2); }),
            new("side-verse", verse, K("Alles zum Vers auf einen Blick"),
                K("Rechts in der Leiste unter „Vers“ › „Übersicht“ steht alles zum gewählten Vers: deine Notizen und Dokumente, der Vers in anderen Übersetzungen, die Querverweise und wo er in deinen Materialien vorkommt. Daneben liegen „Grundtext“, „Wort“, „Querverweise“ und „Vorkommen“."),
                null, Prepare: async () => { await EnsureDemoChapterAsync(); if (_selected != DemoVerse) SelectVerse(DemoVerse); RevealSideTab(GuideTab); }),

            // ---- word study ----
            new("study-on", words, K("Wortstudie einschalten"),
                K("Mit diesem Schalter werden die Wörter unterstrichen, hinter denen ein hebräisches oder griechisches Grundwort mit Strong-Nummer steht."),
                () => StudyBtn, K("Schalte die Wortstudie ein."),
                () => StudyBtn.IsChecked == true, async () => { await EnsureDemoChapterAsync(); StudyBtn.IsChecked = true; },
                // it is on already: off first, so the step shows the switch at work
                async () => { await EnsureDemoChapterAsync(); if (StudyBtn.IsChecked == true) StudyBtn.IsChecked = false; }, desk),
            new("study-word", words, K("Ein Wort antippen"),
                K("Tippe in Vers 16 auf „geliebt“ (je nach Übersetzung „liebte“). Rechts erscheint das griechische Grundwort agapaō (G25): Bedeutung, Aussprache und wie es übersetzt wird."),
                DemoVerseBox, K("Tippe ein unterstrichenes Wort an."),
                () => _studyCount > _deepBase.Studies,
                async () => { await EnsureDemoChapterAsync(); RevealSideTab(StrongTab); StrongSearchBox.Text = "G25"; await SearchStrongAsync(); },
                EnsureDemoChapterAsync),
            new("occurrences", words, K("Alle Vorkommen"),
                K("Unter dem Wort führt „Alle Vorkommen“ zur Konkordanz: jede Stelle, an der agapaō steht, nach Büchern geordnet. So siehst du, wie Johannes das Wort sonst gebraucht, und „Vorkommen als Diagramm“ zeigt die Verteilung über die Bibel."),
                null, K("Öffne die Vorkommen eines Wortes."),
                () => (ConcTitle.Text ?? "") is { Length: > 0 } t && t != _deepBase.Conc,
                async () => await ShowConcordanceAsync("G25", "G25  ἀγαπάω")),
            new("grundtext", words, K("Grundtext Wort für Wort"),
                K("Rechtsklick auf den Vers › „Studium“ › „Grundtext Wort für Wort“ zeigt den ganzen Vers im Griechischen: Umschrift, Grammatik und Bedeutung jedes Wortes. Ein Klick auf ein Wort öffnet das Wörterbuch."),
                () => DemoVerseBox(), null, null,
                async () => { await EnsureDemoChapterAsync(); RevealSideTab(GrundtextTab); await ShowGrundtextAsync(new VerseRef(DemoBook, DemoChapter, DemoVerse)); },
                EnsureDemoChapterAsync),
            new("lexicon", words, K("Strong-Wörterbuch und Sprachen"),
                K("Unter „Lernen“ schlägst du jedes Wort des Grundtextes direkt nach: Nummer (G26), griechisches oder hebräisches Wort, Umschrift oder deutsche Bedeutung. „Sprachen“ daneben bringt dir das griechische und hebräische Alphabet und die wichtigsten Vokabeln bei, mit Wiederholung."),
                () => StrongSearchBox, null, null,
                async () => { StrongSearchBox.Text = "G26"; await SearchStrongAsync(); },
                () => { RevealSideTab(StrongTab); return Task.CompletedTask; }),

            // ---- cross references ----
            new("xrefs", refs, K("Querverweise"),
                K("Rechts unter „Querverweise“ stehen die gedruckten Querverweise zum gewählten Vers. Ein Klick führt hin, der Pfeil oben links bringt dich zurück."),
                null, Prepare: async () => { await EnsureDemoChapterAsync(); if (_selected != DemoVerse) SelectVerse(DemoVerse); RevealSideTab(4); }),
            new("xref-intext", refs, K("Querverweise im Text"),
                K("Unter „Ansicht“ über dem Text schaltest du „Querverweise im Text“ ein: Dann stehen die Verweise direkt bei jedem Vers, ein Klick führt hin. „Mindestens Stimmen“ zeigt nur die wichtigsten, und „Graph neben dem Text“ zeichnet sie als Linien in einer Spalte daneben."),
                () => ViewBtn, K("Schalte „Querverweise im Text“ ein."),
                () => _settings.XrefInText,
                async () => { _xrefInText = true; _settings.XrefInText = true; SaveSettings(); await RedrawChapterAsync(); },
                async () => { await EnsureDemoChapterAsync(); if (_settings.XrefInText) { _xrefInText = false; _settings.XrefInText = false; SaveSettings(); await RedrawChapterAsync(); } }, desk),
            new("myxref-add", refs, K("Eigenen Querverweis anlegen"),
                K("Röm 5,8 sagt etwas sehr Ähnliches: „Gott aber erweist seine Liebe zu uns darin, dass Christus für uns gestorben ist.“ Verknüpfe die beiden: Rechtsklick auf Vers 16 (am Handy lange tippen) › „Studium“ › „Eigenen Querverweis setzen …“, als Ziel „Röm 5,8“ eintippen und speichern."),
                DemoVerseBox, K("Lege den Querverweis Joh 3,16 → Röm 5,8 an."),
                () => DemoXrefIds().Any(id => !_deepBase.Xrefs.Contains(id)),
                async () => { await EnsureDemoChapterAsync(); if (DemoVerseBox() is { } box) ShowMyXrefEditor(new VerseRef(DemoBook, DemoChapter, DemoVerse), box, "Röm 5,8"); },
                EnsureDemoChapterAsync),
            new("myxref-chip", refs, K("Der goldene Chip"),
                K("Unter Vers 16 steht jetzt ein goldener Chip „★ Röm 5,8“. Ein Klick führt hin; dort steht unter Vers 8 „↩ Joh 3,16“ und führt zurück. Eigene Querverweise gehören zu deinem Vault und kommen mit in die Sicherung."),
                DemoVerseBox, Prepare: EnsureDemoChapterAsync),
            new("myxref-del", refs, K("Querverweis wieder löschen"),
                K("Rechtsklick auf den goldenen Chip (am Handy lange tippen) › „Eigenen Querverweis löschen“. Danach ist er auf beiden Seiten verschwunden."),
                DemoVerseBox, K("Lösche den Querverweis Joh 3,16 → Röm 5,8 wieder."),
                () => _deepBase.Xrefs.Count > 0 && _deepBase.Xrefs.Any(id => !DemoXrefIds().Contains(id)),
                async () =>
                {
                    foreach (var x in _vault.GetMyXrefs(DemoBook, DemoChapter).Where(x => x.From.Verse == DemoVerse && x.To.Book == DemoTarget && x.To.Chapter == DemoTargetChapter && x.To.Verse == DemoTargetVerse))
                        _vault.DeleteMyXref(x.Id);
                    _myXrefKey = null; await RedrawChapterAsync();
                },
                EnsureDemoChapterAsync),

            // ---- the study tab ----
            new("pericope", study, K("Perikope"),
                K("Der Reiter „Studium“ hat Werkzeuge für einen ganzen Abschnitt. „Perikope“ zeigt Johannes 3 in Bildern: Wortarten, Verbformen, Schlüsselwörter und wo sie vorkommen, den Rhythmus der Verse und wie die Sätze verbunden sind."),
                null, Prepare: async () => { await EnsureDemoChapterAsync(); if (_selected != DemoVerse) SelectVerse(DemoVerse); RevealSideTab(PericopeTab); }),
            new("sentence", study, K("Satzdiagramm"),
                K("Das Satzdiagramm zeigt den Satz des gewählten Verses wie im Grammatikunterricht: Subjekt, Verb und Objekt auf der Linie, alles andere darunter. Es entsteht aus den Syntaxdaten des griechischen und hebräischen Textes."),
                null, Prepare: async () => { await EnsureDemoChapterAsync(); if (_selected != DemoVerse) SelectVerse(DemoVerse); RevealSideTab(SentenceTab); }),
            new("analysis", study, K("Analyse"),
                K("„Analyse“ findet Schlüsselwörter, Wörter, die nur einmal vorkommen, verwandte Wörter und die Verteilung eines Wortes über die Bibel. Im Neuen Testament gibt es dazu eine Grammatiksuche, z. B. nach allen Aorist-Passiv-Partizipien."),
                null, Prepare: async () => { await EnsureDemoChapterAsync(); RevealSideTab(AnalysisTab); }),
            new("synopsis", study, K("Synopse"),
                K("Die Synopse stellt die Evangelien nebeneinander: dieselbe Begebenheit bei Matthäus, Markus, Lukas und Johannes, die Unterschiede farbig. Probier es mit der Speisung der Fünftausend (Johannes 6)."),
                null, Prepare: () => { RevealSideTab(SynopsisTab); return Task.CompletedTask; }),

            // ---- views ----
            new("graph", views, K("Graph"),
                K("Unter „Ansichten“ über dem Text liegen große Darstellungen. „Graph“ zeigt das Kapitel mit seinen Querverweisen als Netz (Doppelklick auf einen Knoten lädt mehr), die ganze Bibel als Bogen, AT und NT, Wort- und Personen-Netze. Knoten lassen sich ziehen, das Mausrad zoomt."),
                () => ViewsBtn, K("Öffne den Graph."),
                () => GraphPane.IsVisible, async () => await ShowGraphAsync(), EnsureDemoChapterAsync, () => !_settings.EasyMode),
            new("map", views, K("Karte"),
                K("„Karte“ zeigt die Orte des Kapitels oder Buches. Ein Klick auf einen Ort zeigt seine Verse; bei einer Person zeigt „Reiseweg zeigen“ die Orte, die bei ihr genannt werden, in der Reihenfolge der Bibel."),
                () => ViewsBtn, K("Öffne die Karte."),
                () => MapPane.IsVisible, async () => await ShowMapAsync(), EnsureDemoChapterAsync),
            new("timeline", views, K("Zeitleiste"),
                K("Die Zeitleiste stellt die Ereignisse der Bibel auf einer Zeitachse dar, mit Karte. Ein Klick auf ein Ereignis zeigt, wo es steht, und öffnet die Stelle."),
                () => ViewsBtn, null, null,
                () => { TimelineBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); return Task.CompletedTask; }, EnsureDemoChapterAsync),
            new("family", views, K("Stammbaum"),
                K("Der Stammbaum zeigt eine Person mit Eltern, Geschwistern und Kindern, zum Beispiel die Linie von Abraham bis David. Ein Klick auf einen Namen geht im Baum weiter."),
                () => ViewsBtn, K("Öffne den Stammbaum."),
                () => FamilyPane.IsVisible,
                () => { FamilyBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); return Task.CompletedTask; }, EnsureDemoChapterAsync),

            // ---- search ----
            new("search", search, K("Suchen"),
                K("Rechts unter „Suche“ findest du Wörter und Wendungen in der ganzen Bibel, nach Büchern gruppiert. „In Anführungszeichen“ sucht genau diese Wendung, „G25“ sucht nach der Strong-Nummer, also nach dem Grundwort unabhängig von der Übersetzung."),
                () => SearchBox, K("Suche nach „Liebe“."),
                () => _searchCount > _deepBase.Searches && (SearchBox.Text ?? "").Contains(L("Liebe"), StringComparison.OrdinalIgnoreCase),
                async () => { RevealSideTab(0); SearchBox.Text = L("Liebe"); await SearchAsync(); },
                () => { ShowReader(); RevealSideTab(0); return Task.CompletedTask; }),
            new("search-more", search, K("Suche verfeinern"),
                K("„Mehr Optionen“ grenzt ein: nur das Alte Testament oder die Paulusbriefe, Wortanfang oder ganzes Wort, im Grundtext suchen. „Suche bauen“ setzt eine Suche Zeile für Zeile aus einer Liste zusammen, „Suchhilfe“ zeigt alle Beispiele. Person:Abraham findet jeden Vers über Abraham, auch wo er noch Abram heißt; Ort:Jerusalem dasselbe für Orte."),
                () => SearchOptionsBtn, Prepare: () => { ShowReader(); RevealSideTab(0); return Task.CompletedTask; }),

            // ---- tabs and windows ----
            new("tabs", tabs, K("Reiter und die Tab-Taste"),
                K("Was du öffnest (Dokumente, Explorer, Studium …), bekommt oben einen Reiter wie im Browser; „Lesen“ ist immer der erste. Die Tab-Taste springt zwischen „Lesen“ und dem zuletzt offenen Reiter hin und her, Strg+Tab geht der Reihe nach durch alle. Klickst du den kleinen Kreis in einem Reiter an, bekommt er einen Punkt: Haben zwei Reiter einen Punkt, wechselt Tab immer genau zwischen diesen beiden."),
                () => TabStrip, K("Drück die Tab-Taste zweimal."),
                () => _tabSwitches >= _deepBase.TabSwitches + 2,
                async () => { SwitchReadTab(); await Task.Delay(700); SwitchReadTab(); },
                async () => { RevealSideTab(ExplorerTab); await Task.Delay(100); ShowReader(); Focus(); }, desk),
            new("split", tabs, K("Geteilte Ansicht"),
                K("Dieser Knopf zeigt bis zu vier Bereiche nebeneinander, z. B. links den Bibeltext und rechts den Explorer oder ein Dokument. Zieh einen Reiter auf die Mitte eines Bereichs, ersetzt er ihn; auf seinen Rand, kommt er als neuer Bereich daneben; aus dem Fenster heraus, wird er ein eigenes Fenster. Die Linien dazwischen lassen sich ziehen. Derselbe Knopf schaltet wieder zurück."),
                () => SplitTopBtn, K("Schalte die geteilte Ansicht ein, mit dem Bibeltext und einem zweiten Reiter."),
                () => _settings.SplitView && Panes.Count >= 2,
                async () => { RevealSideTab(ExplorerTab); await Task.Delay(100); SplitTopBtn.IsChecked = true; },
                () => { if (SplitTopBtn.IsChecked == true) SplitTopBtn.IsChecked = false; ShowReader(); return Task.CompletedTask; }, desk),
            new("sidebar", tabs, K("Die Leiste rechts"),
                K("Rechts liegt die Seitenleiste: Suchen, Vers, Notizen, Merken, Zusammen und die großen Bereiche. Strg+B blendet sie ein und aus, ihren Rand ziehst du breiter. Ziehst du ein Symbol aus der Leiste heraus, wird es ein eigenes Fenster, z. B. für einen zweiten Bildschirm."),
                () => Rail, Prepare: () => { ShowReader(); SetPanelOpen(true); return Task.CompletedTask; }, Fits: desk),

            // ---- order and plan ----
            new("vault", order, K("Vaults"),
                K("Alles, was du anlegst (Markierungen, Notizen, Querverweise, Dokumente), gehört zu einem Vault. Lege zum Beispiel einen für eine Predigtreihe an und einen für dich privat; oben wechselst du zwischen ihnen."),
                () => VaultTopBtn, Fits: desk),
            new("explorer", order, K("Explorer"),
                K("Der Explorer zeigt alle Markierungen und Notizen wie im Datei-Explorer: Ordner und Unterordner anlegen, Verse per Ziehen einordnen, ein Doppelklick öffnet die Stelle. Hier findest du auch deine Markierung von Johannes 3,16 wieder."),
                null, Prepare: () => { RevealSideTab(ExplorerTab); return Task.CompletedTask; }),
            new("docs", order, K("Dokumente"),
                K("Unter „Dokumente“ schreibst du längere Texte: Studien, Andachten, Predigten. Einen Vers fügst du per Rechtsklick › „In offenes Dokument einfügen“ ein, Bibelstellen werden zu Links."),
                null, Prepare: () => { OpenWork(NavGroups.First(g => g.Name == "Dokumente"), KnowledgeTab); return Task.CompletedTask; }),
            new("material", order, K("Materialien und Live"),
                K("Unter „Material“ öffnest du die Unterlagen einer Bibelstunde direkt im Programm: Word, PDF, PowerPoint und Bilder. Bibelstellen darin werden zu Links, und Text lässt sich farbig markieren, ohne die Datei zu ändern. „Live“ zeigt die Bibelstunde auf den Handys der Gruppe (QR-Code scannen, ohne App), „Archiv“ hält fest, welche Stelle wann dran war."),
                null, Prepare: () => { RevealSideTab(MaterialsTab); return Task.CompletedTask; }),
            new("sermon", order, K("Predigt"),
                K("Der Predigt-Editor hilft beim Vorbereiten: Text, Kernaussage, Gliederung mit Versen, Notizen und Zeitplan; daraus wird ein Handout zum Drucken."),
                null, Prepare: () => { RevealSideTab(SermonTab); return Task.CompletedTask; }),
            new("plans", order, K("Planen"),
                K("Unter „Planen“ liegen Lesepläne mit Fortschritt (z. B. die Bibel in einem Jahr), die Leseliste, To-dos, Gebete mit Datum und Vers, das Auswendiglernen mit wachsenden Lücken und eine Statistik, wie viel du gelesen hast."),
                null, Prepare: () => { if (GroupOf(PlanTab) is { } g) OpenWork(g, PlanTab); return Task.CompletedTask; }),

            // ---- together and devices ----
            new("collab", together, K("Zusammen studieren"),
                K("Unter „Zusammen“ verbindest du dich direkt mit einer anderen Person: chatten, Verse und Dokumente teilen und der Bibelstelle der anderen folgen. Am einfachsten im selben WLAN, dort seht ihr euch mit Namen. Seid ihr weit voneinander entfernt, tretet beide demselben kostenlosen ZeroTier- oder Tailscale-Netzwerk bei; dann klappt es wie im selben WLAN. Sonst tauscht ihr einen Einladungscode per Messenger aus."),
                null, Prepare: () => { ShowReader(); RevealSideTab(CollabTab); return Task.CompletedTask; }),
            new("sync", together, K("Handy und PC synchronisieren"),
                K("Deine Vaults gleichen sich zwischen deinen eigenen Geräten ab, ohne Konto und ohne Server. Unter Einstellungen › „Synchronisieren“ zeigt „Synchronisation einrichten“ einen QR-Code, den du mit dem Handy scannst. Danach finden sich die Geräte im selben WLAN von selbst, und eine Änderung ist nach wenigen Sekunden auf dem anderen."),
                () => SyncSetupBtn, Prepare: () => OpenSettingAt(SyncSetupBtn)),
            new("ai", together, K("KI-Assistent"),
                K("Unter „KI“ beantwortet dein lokales Claude Fragen zur Bibel und schlägt dabei mit den Werkzeugen dieses Programms nach. Mehrere Gespräche laufen nebeneinander."),
                null, Prepare: () => { RevealSideTab(AiTab); return Task.CompletedTask; }, Fits: () => !_phone && NavGroups.Any(g => g.Name == "KI")),

            // ---- help ----
            new("help", help, K("Der Hilfe-Bereich"),
                K("Unter „Hilfe“ (das Fragezeichen in der Leiste rechts, oder Einstellungen › „Hilfe, Feedback, Updates“ › „Hilfe öffnen“) findest du beide Rundgänge, das Quiz und eine kurze Erklärung jeder Funktion. „Dokumentation“ ist das ausführliche Handbuch mit Suche, „Maße & Info“ erklärt biblische Maße, Gewichte und Zeiten."),
                () => TourBtn, Prepare: () => { RevealSideTab(13); return Task.CompletedTask; }),
            new("center", help, K("Mitteilungen und Vorschläge"),
                K("Ab und zu zeigt dir das Programm unten in der Ecke einen Vorschlag. Alle findest du unter Einstellungen › Kachel „Hilfe, Feedback, Updates“ › Karte „Updates und Programm“ › „Mitteilungen und Vorschläge“, und ebenso im Hilfe-Bereich. Dort schaltest du jeden Vorschlag an oder aus, zeigst ihn noch einmal und liest, was neu ist."),
                () => MessagesBtn, null, null, () => { ShowMessageCenter(); return Task.CompletedTask; }, () => OpenSettingAt(MessagesBtn)),
            new("end", help, K("Geschafft!"),
                K("Du kennst jetzt das ganze Programm. Teste dich mit dem kleinen Quiz: ein paar Fragen und zwei Aufgaben, die du im Programm nachschlägst. Den Tiefen-Rundgang findest du jederzeit unter Hilfe wieder."),
                null, null, null, () => { ShowQuiz(); return Task.CompletedTask; }, () => { ShowReader(); return Task.CompletedTask; }, DemoLabel: K("Quiz starten")),
        ];
        return all.Where(s => s.Fits?.Invoke() ?? true).ToArray();
    }

    /// <summary>Starts the deep tour. Was it left in the middle, the person chooses: go on there, or start again from the beginning.</summary>
    void StartDeepTour(int? at = null) => _ = StartDeepTourAsync(at);

    async Task StartDeepTourAsync(int? at)
    {
        if (_touring) return;
        _palette.IsOpen = false; Focus();
        CloseTabTour(seen: false);
        var steps = DeepTourSteps();
        var start = at ?? _settings.DeepTourStep;
        if (start < 0 || start >= steps.Length) start = 0;
        if (at is null && start > 0 && !DeepTouring)
        {
            var answer = await AskAsync(L("Tiefen-Rundgang"),
                L("Du warst bei Schritt {0} von {1} („{2}“). Möchtest du dort weitermachen oder noch einmal von vorn beginnen?", start + 1, steps.Length, L(steps[start].Title)),
                L("Weitermachen"), L("Von vorn beginnen"));
            if (answer < 0) return;
            if (answer == 1) { start = 0; _settings.DeepTourSeen.Clear(); SaveSettings(); }
        }
        _deepSteps = steps;
        await ShowDeepStepAsync(start);
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
        _deepBase = TakeDeepBase(); _deepDemoUsed = false;
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
                _deepDemoUsed = true;
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
                taskState.Text = ok ? "✓ " + (_deepDemoUsed ? L("Gezeigt. Probier es ruhig auch selbst.") : L("Geschafft! Weiter geht’s."))
                    : step.Id == "myxref-del" && _deepBase.Xrefs.Count == 0 ? L("Noch offen. Leg den Querverweis erst im Schritt davor an.") : L("Noch offen.");
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
        var restart = new Button { Content = L("Von vorn beginnen"), Margin = new(0, 10, 0, 0) };
        restart.Click += (_, _) => { fly.Hide(); _settings.DeepTourSeen.Clear(); SaveSettings(); _ = ShowDeepStepAsync(0); };
        list.Children.Add(restart);
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
