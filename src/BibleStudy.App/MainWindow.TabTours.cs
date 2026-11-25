using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// A short tour for every tab: the first time a tab is opened, a small card explains that tab (and only that one) in two or three steps.
/// It comes once per tab; "Alle aus" turns them all off, the help page brings them back. While the short tour or the deep tour runs, tabs that
/// they open do not start their tour and are not counted as seen: the deep tour neither starts nor uses them up.
/// </summary>
public partial class MainWindow
{
    sealed record TabTourStep(string Title, string Text);

    Border? _tabTourCard;
    int? _tabTourIndex;
    /// <summary>Set a while after the start: tabs that come back from the last session do not all start their tour at once.</summary>
    bool _tabToursReady;

    /// <summary>The steps for a tab (by its index in the side panel), or null for a tab without a tour.</summary>
    static TabTourStep[]? TabTourSteps(int index)
    {
        TabTourStep S(string title, string text) => new(title, text);
        return index switch
        {
            0 => [S(K("Die Suche"), K("Tippe ein Wort oder eine Wendung und drück Enter: Die Treffer stehen nach Büchern gruppiert darunter, ein Klick öffnet die Stelle. „In Anführungszeichen“ sucht genau diese Wendung, „G25“ das Grundwort.")),
                  S(K("Optionen und Hilfe"), K("„Mehr Optionen“ grenzt den Bereich ein (z. B. nur NT) und sucht auf Wunsch im Grundtext. „Suche bauen“ stellt eine Suche aus Listen zusammen, „Suchhilfe“ zeigt alle Beispiele zum Anklicken. Mit ☆ merkst du dir eine Suche."))],
            GuideTab => [S(K("Die Vers-Übersicht"), K("Hier steht alles zum Vers, den du im Text angeklickt hast: deine Notizen, Markierungen und Dokumente, andere Übersetzungen, Querverweise und wo er in deinen Materialien vorkommt.")),
                  S(K("Mehr zum Vers"), K("Rechtsklick auf einen Vers öffnet das Menü mit Kopieren, Bild, Auswendiglernen, Dokument und den Studien-Werkzeugen. Die Pillen oben wechseln zu Grundtext, Wort, Querverweisen und Vorkommen."))],
            GrundtextTab => [S(K("Grundtext Wort für Wort"), K("Der angeklickte Vers im Griechischen oder Hebräischen: jedes Wort mit Umschrift, Grammatik und Bedeutung. Ein Klick auf ein Wort öffnet es im Wörterbuch.")),
                  S(K("Mit dem Text verbunden"), K("Klickst du im Bibeltext einen anderen Vers an, wechselt der Grundtext mit."))],
            3 => [S(K("Ein Wort im Urtext"), K("Schalte oben „Wortstudie“ ein und tippe ein unterstrichenes Wort an: Hier erscheinen Grundwort, Strong-Nummer, Bedeutung, Aussprache und wie es übersetzt wird.")),
                  S(K("Weiterforschen"), K("„Alle Vorkommen“ zeigt jede Stelle mit diesem Grundwort, „Vorkommen als Diagramm“ die Verteilung über die Bibel."))],
            4 => [S(K("Querverweise"), K("Die Verse, die mit dem angeklickten Vers zusammenhängen, die stärksten zuerst. Ein Klick führt hin, der Pfeil oben links zurück.")),
                  S(K("Richtung und eigene"), K("Die Knöpfe oben wählen, ob Verweise vom Vers weg, zu ihm hin oder beides gezeigt werden. Eigene Querverweise legst du per Rechtsklick auf einen Vers an, sie erscheinen als goldene Chips."))],
            5 => [S(K("Vorkommen"), K("Alle Stellen, an denen ein Grundwort (Strong-Nummer) steht, jeweils mit dem Verstext. Du kannst nach Büchern und nach der Übersetzung des Wortes filtern.")),
                  S(K("Woher die Liste kommt"), K("Die Liste füllt sich, wenn du in der Wortstudie „Alle Vorkommen“ wählst oder in der Suche eine Strong-Nummer suchst."))],
            CommentaryTab => [S(K("Kommentare"), K("Freie Bibelkommentare zum aktuellen Kapitel; der angeklickte Vers wird hervorgehoben. Kommentare sind Menschenwort: Prüfe sie an der Schrift."))],
            LexiconTab => [S(K("Das Lexikon"), K("Ein Bibellexikon zu Personen und Orten. Oben stehen die Namen, die im aktuellen Kapitel vorkommen; ein Klick zeigt den Eintrag.")),
                  S(K("Suchen"), K("Über das Suchfeld findest du jede Person und jeden Ort, auch über Strg+K."))],
            2 => [S(K("Notizen"), K("Oben schreibst du eine Notiz zum angeklickten Vers. Sie wird beim Wechseln von selbst gespeichert; Bibelstellen darin werden zu Links.")),
                  S(K("Alle Notizen"), K("Darunter stehen alle deine Notizen, nach Ordnern; ein Klick springt zur Stelle."))],
            1 => [S(K("Markierungen"), K("Alle farbig markierten Verse und Wörter des Vaults, nach Farben, Ordnern und Tags. Ein Klick springt zur Stelle.")),
                  S(K("Notiz und Ordner"), K("Wählst du eine Markierung, kannst du ihr eine Notiz, einen Ordner und Tags geben oder sie löschen."))],
            ExplorerTab => [S(K("Der Explorer"), K("Deine Markierungen und Notizen wie im Datei-Explorer: links die Ordner, rechts der Inhalt, oben der Pfad.")),
                  S(K("Ordnen"), K("Lege Ordner und Unterordner an und zieh Verse oder Ordner auf einen Ordner, um sie einzuordnen. Ein Doppelklick öffnet die Stelle."))],
            BookmarksTab => [S(K("Lesezeichen"), K("Deine gemerkten Verse als einfache Liste, zum späteren Zurückspringen. Ein Lesezeichen setzt du mit „Merken“ über dem Text oder Strg+D."))],
            KnowledgeTab => [S(K("Dokumente"), K("Hier schreibst du eigene Texte: Studien, Andachten, Predigten. „＋ Neu“ legt ein Dokument an; es wird von selbst gespeichert.")),
                  S(K("Verknüpfen"), K("„Joh 3,16“ im Text wird zum Link auf den Vers, [[Titel]] zum Link auf ein anderes Dokument. Mit „＋ Vers“ fügst du einen Vers als Zitat ein.")),
                  S(K("Ordnen und teilen"), K("Ordner und Kategorien halten Ordnung. Über „⋯“ druckst du ein Dokument oder speicherst es als PDF; der Wissensgraph zeigt, was womit verknüpft ist."))],
            MaterialsTab => [S(K("Materialien"), K("Wähle einen Ordner mit den Unterlagen einer Bibelstunde: Word, PDF, PowerPoint, Bilder und mehr öffnen sich hier, ohne ein anderes Programm.")),
                  S(K("Lesen und markieren"), K("Bibelstellen im Text sind Links. Text auswählen und rechts klicken markiert ihn, ohne die Datei zu ändern. Tab wechselt zwischen den offenen Dateien."))],
            LiveTab => [S(K("Live"), K("Die Bibelstunde auf den Handys der Gruppe: Die anderen scannen den QR-Code und lesen im Browser mit, ohne App und ohne Konto. Alle müssen im selben WLAN sein.")),
                  S(K("Fragen und Abstimmungen"), K("Die Teilnehmer können Fragen schicken und abstimmen; du siehst beides hier."))],
            ArchiveTab => [S(K("Archiv"), K("Welche Stelle an welchem Abend in der Bibelstunde dran war, mit Notizen und den Fragen des Abends. Eine Live-Bibelstunde trägt sich am Ende selbst ein."))],
            SermonTab => [S(K("Predigt vorbereiten"), K("Gib Titel, Bibeltext und Kernaussage ein und gliedere die Predigt in Abschnitte, jeder mit Notizen, Versen und Minuten.")),
                  S(K("Fertig machen"), K("Die Predigt ist ein normales Dokument und taucht auch unter „Dokumente“ auf. Daraus machst du ein Handout zum Drucken."))],
            6 => [S(K("Leseliste"), K("Verse, die du der Reihe nach durchgehen willst. Füge Verse hinzu (auch per Rechtsklick oder aus der Suche) und starte die Liste: Über dem Text gehst du dann Vers für Vers weiter."))],
            7 => [S(K("To-dos"), K("Deine Aufgaben fürs Studium, auf Wunsch mit Unteraufgaben, Datum und Vers. Erledigtes hakst du ab.")),
                  S(K("Von überall"), K("Der To-do-Knopf oben öffnet die Liste jederzeit, von überall im Programm."))],
            PrayerTab => [S(K("Gebete"), K("Deine Gebetsanliegen mit Datum und auf Wunsch einem Vers. Ist ein Gebet erhört, hakst du es ab und hältst fest, wie."))],
            PlanTab => [S(K("Lesepläne"), K("Wähle einen Plan, z. B. die Bibel in einem Jahr: Jeden Tag stehen hier ein paar Kapitel.")),
                  S(K("Fortschritt"), K("Was du als gelesen markierst, zählt für den Plan, egal wo du gelesen hast. Die Serie zeigt, wie viele Tage du dranbleibst."))],
            MemoryTab => [S(K("Auswendig lernen"), K("Füge Verse hinzu (auch per Rechtsklick auf einen Vers) und übe sie mit Lücken. Je besser du einen Vers kannst, desto mehr Lücken und desto seltener kommt er."))],
            StatsTab => [S(K("Statistik"), K("Wie viel der Bibel du gelesen hast, deine Serie, die letzten Wochen und was dein Vault enthält. Alles wird auf deinem Gerät berechnet."))],
            CollabTab => [S(K("Zusammen studieren"), K("Verbinde dich direkt mit einer anderen Person: Im selben WLAN seht ihr euch mit Namen, ein Tippen genügt. Sonst tauscht ihr einen Einladungscode aus.")),
                  S(K("Weit voneinander entfernt"), K("Tretet beide demselben kostenlosen ZeroTier- oder Tailscale-Netzwerk bei, dann klappt es wie im selben WLAN. Verbunden könnt ihr chatten, Verse und Dokumente teilen und der Stelle der anderen Person folgen."))],
            AiTab => [S(K("KI-Assistent"), K("Stell deinem lokalen Claude eine Frage zur Bibel; er schlägt mit den Werkzeugen dieses Programms nach. „＋ Neues Gespräch“ öffnet ein weiteres Gespräch."))],
            PericopeTab => [S(K("Perikope"), K("Ein Abschnitt in Bildern: Wortarten, Verbformen, Schlüsselwörter und wo sie vorkommen, der Rhythmus der Verse und wie die Sätze verbunden sind.")),
                  S(K("Abschnitt wählen"), K("Oben wählst du den Abschnitt; ein paar Sätze darunter sagen, was auffällt."))],
            SentenceTab => [S(K("Satzdiagramm"), K("Der Satz des angeklickten Verses als Satzdiagramm: Subjekt, Verb und Objekt auf der Linie, alles andere darunter. Für lange Sätze gibt es ein eigenes Fenster."))],
            AnalysisTab => [S(K("Analyse"), K("Die Wörter des angeklickten Verses, Schlüsselwörter und seltene Wörter eines Kapitels.")),
                  S(K("Ein Wort untersuchen"), K("Zu jedem Wort siehst du verwandte Wörter, seine üblichen Begleiter und die Verteilung über die Bibel. Im NT gibt es dazu die Grammatiksuche."))],
            SynopsisTab => [S(K("Synopse"), K("Eine Begebenheit, wie Matthäus, Markus, Lukas und Johannes sie erzählen, nebeneinander. Was nur ein Evangelium hat, ist farbig."))],
            StrongTab => [S(K("Strong-Wörterbuch"), K("Schlag ein Wort des Urtextes nach: Strong-Nummer (G3056), griechisches oder hebräisches Wort, Umschrift oder Bedeutung. „α א“ öffnet eine Tastatur für die Buchstaben."))],
            LearnTab => [S(K("Sprachen lernen"), K("Lerne das griechische und hebräische Alphabet mit einem Buchstaben-Quiz und dann die wichtigsten Vokabeln, mit Wiederholung nach deinem Können."))],
            SettingsTab => [S(K("Einstellungen"), K("Die Kacheln führen zu den Bereichen, das Suchfeld oben findet jede Einstellung, z. B. „Schrift“ oder „Sicherung“.")),
                  S(K("Wichtig am Anfang"), K("Unter „Bibeln und Daten“ lädst du alles auf einmal, unter „Meine Daten“ stellst du die Sicherung ein, unter „Synchronisieren“ verbindest du PC und Handy."))],
            13 => [S(K("Hilfe"), K("Hier startest du den Rundgang, den Tiefen-Rundgang und das Quiz. Darunter ist jede Funktion kurz erklärt.")),
                  S(K("Mehr Hilfe"), K("„Dokumentation“ ist das ausführliche Handbuch mit Suche. Die Rundgänge für einzelne Reiter holst du hier mit „Reiter-Rundgänge wieder zeigen“ zurück."))],
            HandbookTab => [S(K("Dokumentation"), K("Das ausführliche Handbuch: links das Inhaltsverzeichnis, oben die Suche. Über den Knopf daneben öffnest du es als eine Datei."))],
            14 => [S(K("Maße & Info"), K("Biblische Längen, Gewichte, Geld, Hohlmaße und Zeiten in heutigen Einheiten, dazu die Abkürzungen im Programm."))],
            _ => null,
        };
    }

    static string TabTourName(int index) => NavGroups.SelectMany(g => g.Tabs).FirstOrDefault(t => t.Index == index)?.Label ?? "";

    /// <summary>A tab was shown: its tour comes, the first time and when nothing else is going on.</summary>
    void MaybeTabTour(int index)
    {
        if (!_tabToursReady || _settings.TabToursOff || _settings.TabToursSeen.Contains(index.ToString()) || TabTourSteps(index) is null) return;
        // a moment later: the tab has its place, and a tour that opened it has its card (the deep tour opens tabs in its steps)
        DispatcherTimer.RunOnce(() =>
        {
            if (_settings.TabToursOff || _settings.TabToursSeen.Contains(index.ToString())) return;
            if (_touring || DeepTouring || _tabTourCard is not null || OwnedWindows.Count > 0 || !IsActive) return;
            if (!TabIsShown(index)) return;
            ShowTabTour(index, 0);
        }, TimeSpan.FromMilliseconds(700));
    }

    /// <summary>Is the tab on screen: the side panel shows it, or a work tab (or an area of the split view) is on it.</summary>
    bool TabIsShown(int index)
    {
        if (SidePanel.IsVisible && SideTabs.SelectedIndex == index && GroupOf(index) is not { Windowed: true }) return true;
        return _windows.Values.Any(d => d.Current == index && d.Win is null && d.Panel.IsEffectivelyVisible);
    }

    /// <summary>The tab that is in front: the open work tab, else the side panel.</summary>
    int? FrontTab()
    {
        if (_activeWork is { } key && _windows.TryGetValue(key, out var d)) return d.Current;
        if (_settings.SplitView && _focusPane is { } f && _windows.TryGetValue(f, out var p)) return p.Current;
        return SidePanel.IsVisible ? SideTabs.SelectedIndex : null;
    }

    void ShowTabTour(int index, int i)
    {
        if (TabTourSteps(index) is not { } steps || !EnsureNoticeHost()) return;
        i = Math.Clamp(i, 0, steps.Length - 1);
        _tabTourIndex = index;
        if (_tabTourCard is null)
        {
            _tabTourCard = new Border
            {
                CornerRadius = new(14), Padding = new(16, 12), BorderThickness = new(2), ZIndex = 1000,
                BoxShadow = BoxShadows.Parse("0 6 24 0 #40000000"),
            };
            _tabTourCard.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
            _tabTourCard.BorderBrush = new SolidColorBrush(Color.Parse(NoticeInfo));
            _noticeHost!.Children.Add(_tabTourCard);
        }
        var card = _tabTourCard;
        card.Width = PW(360);
        card.HorizontalAlignment = _phone ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        card.VerticalAlignment = _phone ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        card.Margin = _phone ? new Thickness(12, 56, 12, 0) : new Thickness(16, 0, 0, 36);

        var step = steps[i];
        var crumb = new TextBlock { Text = steps.Length > 1 ? L("Kurz erklärt · {0} · {1} von {2}", L(TabTourName(index)), i + 1, steps.Length) : L("Kurz erklärt · {0}", L(TabTourName(index))), FontSize = 11, TextWrapping = TextWrapping.Wrap };
        crumb.Foreground = new SolidColorBrush(Color.Parse(NoticeInfo));
        var close = new Button { Content = "✕", Classes = { "ghost" }, Padding = new(6, 0), VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(close, L("Schließen"));
        close.Click += (_, _) => CloseTabTour(seen: true);
        DockPanel.SetDock(close, Dock.Right);
        var head = new DockPanel { Children = { close, crumb } };

        var off = new Button { Content = L("Alle aus"), Classes = { "ghost" } };
        ToolTip.SetTip(off, L("Keine Rundgänge mehr beim ersten Öffnen eines Reiters. Unter Hilfe holst du sie zurück."));
        off.Click += (_, _) => { _settings.TabToursOff = true; SaveSettings(); CloseTabTour(seen: true); Notify(L("Reiter-Rundgänge aus"), L("Unter Hilfe › „Reiter-Rundgänge wieder zeigen“ schaltest du sie wieder ein.")); };
        var back = new Button { Content = L("Zurück"), IsVisible = i > 0 };
        back.Click += (_, _) => ShowTabTour(index, i - 1);
        var last = i == steps.Length - 1;
        var next = new Button { Content = last ? L("Verstanden") : L("Weiter"), Classes = { "primary" } };
        next.Click += (_, _) => { if (last) CloseTabTour(seen: true); else ShowTabTour(index, i + 1); };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { back, next } };
        var bottom = new DockPanel { Margin = new(0, 4, 0, 0) };
        DockPanel.SetDock(right, Dock.Right);
        bottom.Children.Add(right); bottom.Children.Add(off);

        card.Child = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                head,
                new TextBlock { Text = L(step.Title), FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = L(step.Text), TextWrapping = TextWrapping.Wrap },
                bottom,
            },
        };
    }

    /// <summary>Closes the card. Seen: the person closed it or read it to the end, so it does not come again; otherwise (another tour took over) it comes next time.</summary>
    void CloseTabTour(bool seen)
    {
        if (seen && _tabTourIndex is int index && !_settings.TabToursSeen.Contains(index.ToString())) { _settings.TabToursSeen.Add(index.ToString()); SaveSettings(); }
        if (_tabTourCard?.Parent is Panel p) p.Children.Remove(_tabTourCard);
        _tabTourCard = null; _tabTourIndex = null;
    }

    /// <summary>All tab tours come again (each once more, the first time its tab is opened).</summary>
    void ResetTabTours()
    {
        _settings.TabToursSeen.Clear(); _settings.TabToursOff = false; SaveSettings();
        Notify(L("Reiter-Rundgänge an"), L("Jeder Reiter erklärt sich wieder, wenn du ihn das nächste Mal öffnest."), good: true);
    }

    /// <summary>The tour of the tab in front, now (from the command field), even if it was seen.</summary>
    void ShowFrontTabTour()
    {
        if (FrontTab() is int index && TabTourSteps(index) is not null) { CloseTabTour(seen: false); ShowTabTour(index, 0); }
        else StatusText.Text = L("Öffne zuerst einen Reiter, z. B. Dokumente oder Suche.");
    }
}
