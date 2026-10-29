namespace BibleStudy.Core;

/// <summary>One source of data the program uses: what it is for, who made it, under which licence, where it comes from.</summary>
public sealed record DataSource(string Name, string Purpose, string Author, string License, string Url, string Origin = "");

/// <summary>
/// The list for the page "Sources and data" – so everyone can see where each piece of information comes from and what they may do with it.
/// Nothing here is bundled with the program: it is downloaded when first needed and kept in the cache folder. When a source changes its
/// licence or a new one is added, this list has to be updated together with the code (a test checks that the texts are translated).
/// </summary>
public static class DataSources
{
    public static IReadOnlyList<DataSource> All =>
    [
        new(L("Bibeltexte"), L("Die Übersetzungen, die du liest (eigene Importe ausgenommen). Die Lizenz steht je Übersetzung in der Statusleiste und in der Übersetzungsliste."),
            "Free Use Bible API (AO Lab) mit den Texten der jeweiligen Herausgeber", L("je Übersetzung: gemeinfrei oder freie Lizenz"), "https://bible.helloao.org"),
        new(L("Strong-Wörterbuch"), L("Bedeutung der hebräischen und griechischen Wörter nach Strong-Nummern."),
            "Open Scriptures", "CC BY-SA", "https://github.com/openscriptures/strongs", L("Wörterbuch von James Strong (1890), digitalisiert von Open Scriptures")),
        new(L("Kurzlexikon (STEPBible)"), L("Kurze Bedeutung jedes griechischen und hebräischen Wortes; beim Griechischen mit dem Eintrag von Abbott-Smith (1922, gemeinfrei). Beim Hebräischen werden nur die kurzen Bedeutungen verwendet, weil die langen Definitionen laut der Datei eine Erlaubnis ihres Inhabers brauchen."),
            "STEPBible, Tyndale House Cambridge", "CC BY 4.0", "https://github.com/STEPBible/STEPBible-Data", L("Forschungsinstitut für Bibelwissenschaft; die griechischen Einträge folgen Abbott-Smith (1922)")),
        new(L("Syntax und Morphologie"), L("Satzbau und Grammatik des griechischen Neuen Testaments (Nestle 1904) und der hebräischen Bibel (Westminster-Kodex): Satzrollen-Suche, Grundtext-Ansicht."),
            "MACULA Greek / Hebrew, Biblica und Clear Bible", "CC BY 4.0", "https://github.com/Clear-Bible", L("Gemeinnützige Organisationen; Grundlage sind der griechische Text von Nestle (1904) und der Westminster-Kodex")),
        new(L("Interlinear"), L("Wort für Wort: Grundtext, Umschrift und englische Bedeutung."),
            "Berean Standard Bible Interlinear", L("gemeinfrei"), "https://bereanbible.com"),
        new(L("Querverweise"), L("Die Verse, die zusammengehören, mit Bewertung durch Leser (Graphen, Querverweis-Liste)."),
            "OpenBible.info", "CC BY 4.0", "https://www.openbible.info/labs/cross-references/"),
        new(L("Personen, Orte, Zeitleiste, Lexikon"), L("Personen und Orte der Bibel mit Beziehungen, Koordinaten und Datierungen; Texte des Lexikons aus Easton's Bible Dictionary (1897, gemeinfrei). Datierungen sind Näherungswerte."),
            "Theographic Bible Metadata", "CC BY-SA 4.0", "https://github.com/robertrouse/theographic-bible-metadata", L("Zusammenstellung von Robert Rouse und Mitwirkenden, darunter nach Easton's Bible Dictionary (1897)")),
        new(L("Karte"), L("Länder, Seen und Flüsse der Karte."),
            "Natural Earth", L("gemeinfrei"), "https://www.naturalearthdata.com"),
        new(L("Synonyme"), L("Verwandte Wörter für das Themen-Netz und die Themensuche (deutsch)."),
            "OpenThesaurus", L("Lizenz: siehe die Webseite"), "https://www.openthesaurus.de"),
        new(L("Kommentare"), L("Freie Bibelkommentare, nur wenn du sie in den Einstellungen einschaltest (Kommentare sind Auslegung von Menschen, nicht Schrift)."),
            "Free Use Bible API und die Autoren der Werke", L("je Werk: siehe Lizenz-Link in der Bücherliste"), "https://bible.helloao.org"),
        new(L("Übersetzte Wörterbuchtexte"), L("Die Texte von Strong und Interlinear gibt es nur englisch. Wenn du Claude Code hast, übersetzt es kurze Einträge in deine Sprache. Das ist eine KI-Übersetzung und wird so gekennzeichnet; sie bleibt auf deinem Computer."),
            L("dein eigenes Claude Code"), L("keine fremden Daten"), "https://claude.com/claude-code"),
        new(L("Eigene Importe"), L("Bibeldateien, die du selbst besitzt und einliest, und Texte von Plugins, die du erlaubst. Sie bleiben auf deinem Computer, werden nie weitergegeben und nicht geprüft."),
            L("du"), L("deine eigenen Rechte"), ""),
    ];

    /// <summary>The list as plain text (for copying into a report or a bug ticket).</summary>
    public static string AsText() => string.Join("\n\n", All.Select(s =>
        $"{s.Name}\n  {s.Purpose}\n  {L("Von")}: {s.Author} · {L("Lizenz")}: {s.License}" + (s.Origin.Length > 0 ? $"\n  {L("Herkunft")}: {s.Origin}" : "") + (s.Url.Length > 0 ? $"\n  {s.Url}" : "")));
}
