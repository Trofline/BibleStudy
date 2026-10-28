using Avalonia.Controls;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Ready-made frames for studies: a heading per question, so a document starts with structure instead of a blank page.</summary>
public partial class MainWindow
{
    sealed record StudyTemplate(string Title, string[] Sections);

    static StudyTemplate[] StudyTemplates =>
    [
        new(L("Wortstudie"), [L("Wort / Strong-Nummer"), L("Grundtext und Grundbedeutung"), L("Vorkommen in der Bibel"), L("Bedeutung im Zusammenhang"), L("Zusammenfassung")]),
        new(L("Personenstudie"), [L("Wer ist die Person?"), L("Familie und Herkunft"), L("Wichtige Stellen"), L("Charakter und Beziehung zu Gott"), L("Was lerne ich daraus?")]),
        new(L("Themenstudie"), [L("Fragestellung"), L("Wichtige Verse"), L("Beobachtungen"), L("Zusammenhänge (Querverweise)"), L("Anwendung")]),
        new(L("Gleichnis-Auslegung"), [L("Der Text"), L("Hintergrund und Zuhörer"), L("Das Bild und seine Bedeutung"), L("Die Hauptaussage"), L("Anwendung für mich")]),
        new(L("Abschnittsstudie"), [L("Beobachten: Was steht da?"), L("Auslegen: Was bedeutet es?"), L("Anwenden: Was heißt das für mich?"), L("Gebet")]),
    ];

    void BuildTemplateMenu()
    {
        var menu = new MenuFlyout();
        foreach (var t in StudyTemplates)
        {
            var item = new MenuItem { Header = t.Title };
            item.Click += (_, _) => NewDocumentFromTemplate(t);
            menu.Items.Add(item);
        }
        DocTemplatesBtn.Flyout = menu;
    }

    void NewDocumentFromTemplate(StudyTemplate t)
    {
        VerseRef? r = _selected is int v && _curChapterData is not null ? new VerseRef(_curBook, _curChapter, v) : null;
        var title = r is { } rr ? $"{t.Title}: {Label(rr)}" : t.Title;
        var body = $"## {title}\n\n";
        if (r is { } q && _selected is int sv)
            body += $"> {VerseTextOf(sv)}\n> ({(TranslationBox.SelectedItem as TranslationInfo)?.ShortName})\n\n";
        foreach (var s in t.Sections) body += $"### {s}\n\n\n";
        OpenDocument(new Document(0, title, body, null, false, DateTime.Now, r is { } tr ? AutoTags(tr).Append(t.Title).ToList() : [t.Title]));
    }
}
