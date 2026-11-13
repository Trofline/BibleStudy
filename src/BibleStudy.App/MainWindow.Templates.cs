using Avalonia.Controls;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Ready-made frames for studies: a heading per question, so a document starts with structure instead of a blank page. The methods for a
/// group add questions for the conversation; in the handout they stand in a box of their own, numbered.
/// </summary>
public partial class MainWindow
{
    sealed record StudyTemplate(string Title, string[] Sections, string[]? Questions = null, string? About = null);

    static StudyTemplate[] StudyTemplates =>
    [
        new(L("Wortstudie"), [L("Wort / Strong-Nummer"), L("Grundtext und Grundbedeutung"), L("Vorkommen in der Bibel"), L("Bedeutung im Zusammenhang"), L("Zusammenfassung")]),
        new(L("Personenstudie"), [L("Wer ist die Person?"), L("Familie und Herkunft"), L("Wichtige Stellen"), L("Charakter und Beziehung zu Gott"), L("Was lerne ich daraus?")]),
        new(L("Themenstudie"), [L("Fragestellung"), L("Wichtige Verse"), L("Beobachtungen"), L("Zusammenhänge (Querverweise)"), L("Anwendung")]),
        new(L("Gleichnis-Auslegung"), [L("Der Text"), L("Hintergrund und Zuhörer"), L("Das Bild und seine Bedeutung"), L("Die Hauptaussage"), L("Anwendung für mich")]),
        new(L("Abschnittsstudie"), [L("Beobachten: Was steht da?"), L("Auslegen: Was bedeutet es?"), L("Anwenden: Was heißt das für mich?"), L("Gebet")]),
    ];

    /// <summary>Methods for a Bible study in a group: each brings its questions for the conversation ({0} is the passage).</summary>
    static StudyTemplate[] GroupMethods =>
    [
        new(L("Beobachten – Deuten – Anwenden"), [L("Notizen")],
        [
            L("Wer kommt in {0} vor, wo und wann spielt es?"), L("Welche Wörter oder Gedanken wiederholen sich?"), L("Was geschieht unmittelbar davor und danach?"),
            L("Was bedeutete der Text für die ersten Hörer?"), L("Was zeigt der Text über Gott?"), L("Was zeigt er über den Menschen?"),
            L("Was willst du nach diesem Text anders machen?"),
        ], L("Die klassische induktive Methode: erst genau hinsehen, dann verstehen, dann umsetzen.")),
        new(L("Entdeckerbibelstudium (DBS)"), [L("Notizen")],
        [
            L("Wofür bist du diese Woche dankbar? Was belastet dich?"), L("Lest {0} zweimal. Wer erzählt den Abschnitt mit eigenen Worten nach?"),
            L("Was lernen wir aus {0} über Gott?"), L("Was lernen wir über Menschen?"), L("Wenn das wahr ist: Was willst du diese Woche tun?"),
            L("Wem erzählst du von dem, was du heute entdeckt hast?"),
        ], L("Discovery Bible Study: der Text steht im Mittelpunkt, jede Person entdeckt selbst; endet mit Gehorsam und Weitersagen.")),
        new(L("Schwedische Methode (Licht, Fragezeichen, Pfeil)"), [L("Notizen")],
        [
            L("💡 Was leuchtet dir in {0} ein, was ist dir neu aufgegangen?"), L("? Was verstehst du nicht, worüber stolperst du?"), L("→ Was trifft dich persönlich?"),
        ], L("Jede Person liest still und setzt drei Zeichen an den Rand; danach erzählt jede Runde.")),
        new(L("SOAP (Schrift, Beobachtung, Anwendung, Gebet)"), [L("Schrift: Der Vers, der mich anspricht"), L("Beobachtung"), L("Anwendung"), L("Gebet")],
        [
            L("Welcher Vers aus {0} spricht dich an?"), L("Was beobachtest du darin?"), L("Wie wendest du ihn heute an?"), L("Was sagst du Gott dazu?"),
        ], L("Scripture, Observation, Application, Prayer: ein Vers, ganz persönlich.")),
        new(L("Erzählung entdecken"), [L("Notizen")],
        [
            L("Welche Personen kommen in {0} vor? Was wollen sie?"), L("Wo ist der Wendepunkt der Geschichte?"), L("Wie reagieren die Personen auf Gott oder Jesus?"),
            L("Mit wem in der Geschichte fühlst du dich verbunden, und warum?"), L("Was überrascht dich?"), L("Was sagt die Geschichte uns heute?"),
        ], L("Für Erzählungen und Gleichnisse: die Geschichte von innen erleben.")),
        new(L("Lectio Divina"), [L("Notizen")],
        [
            L("Lesen: Welches Wort oder welcher Satz aus {0} bleibt hängen?"), L("Nachdenken: Warum gerade dieses Wort?"), L("Beten: Was antwortest du Gott?"),
            L("Ruhen: Eine Minute Stille mit diesem Wort."),
        ], L("Das alte langsame Lesen: viermal, mit Stille dazwischen.")),
    ];

    void NewDocumentFromTemplate(StudyTemplate t)
    {
        VerseRef? r = _selected is int v && _curChapterData is not null ? new VerseRef(_curBook, _curChapter, _selVerses.Count > 1 ? _selVerses.Min() : v) : null;
        var end = _selVerses.Count > 1 ? _selVerses.Max() : r?.Verse ?? 0;
        var label = r is { } lr ? Label(lr) + (end > lr.Verse ? "–" + end : "") : "";
        var title = label.Length > 0 ? $"{t.Title}: {label}" : t.Title;
        var body = $"## {title}\n\n";
        if (r is { } q)
        {
            var text = string.Join(" ", Enumerable.Range(q.Verse, end - q.Verse + 1).Select(n => (end > q.Verse ? Superscript(n) + " " : "") + VerseTextOf(n)).Where(s => s.Trim().Length > 0));
            body += $"> {text}\n> ({label}, {(TranslationBox.SelectedItem as TranslationInfo)?.ShortName})\n\n";
        }
        if (t.About is { } about) body += $"*{about}*\n\n";
        if (t.Questions is { } questions)
        {
            body += "### " + L("Fragen zum Gespräch") + "\n\n";
            foreach (var question in questions) body += "- " + string.Format(question, label.Length > 0 ? label : L("dem Text")) + "\n";
            body += "\n";
        }
        foreach (var s in t.Sections) body += $"### {s}\n\n\n";
        OpenDocument(new Document(0, title, body, null, false, DateTime.Now, r is { } tr ? AutoTags(tr).Append(t.Title).ToList() : [t.Title]));
    }
}
