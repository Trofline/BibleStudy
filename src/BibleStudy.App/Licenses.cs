namespace BibleStudy.App;

/// <summary>Where the program's data comes from and under which license (mirrors THIRD-PARTY-NOTICES.md).</summary>
static class Licenses
{
    public static readonly (string Name, string Use, string License)[] Sources =
    [
        (L("Free Use Bible API (HelloAO)"), L("Übersetzungen, Kommentare, Datensätze"), L("Freie Nutzung; jede Übersetzung nennt ihre eigene Lizenz in der Fußzeile")),
        (L("Berean Standard Bible und Interlinear"), L("Wort für Wort Hebräisch/Griechisch"), L("Gemeinfrei")),
        (L("Open Cross Reference (OpenBible.info)"), L("Querverweise"), L("CC BY 4.0")),
        (L("Theographic Bible Metadata"), L("Personen, Orte, Ereignisse, Zeitleiste"), L("CC BY-SA 4.0")),
        (L("Easton's Bible Dictionary"), L("Bibellexikon"), L("Gemeinfrei (1897)")),
        (L("STEPBible Lexikon (Tyndale House)"), L("Strong-Wörterbuch"), L("CC BY 4.0")),
        (L("Kommentare (Matthew Henry, JFB, Keil-Delitzsch, Calvin, Tyndale u. a.)"), L("Kommentare"), L("je Kommentar angezeigt; meist gemeinfrei, Tyndale CC BY-SA 4.0")),
        ("OpenThesaurus", L("Deutsche Synonyme"), L("CC BY-SA 4.0 / LGPL")),
        (L("Natural Earth"), L("Weltkarte"), L("Gemeinfrei")),
        (L("Avalonia UI, Microsoft.Data.Sqlite, ModelContextProtocol"), L("Programmbibliotheken"), L("MIT / Apache-2.0")),
        (L("Inter, Crimson Text"), L("Schriften"), L("SIL Open Font License 1.1")),
    ];

    public static string Own => L("BibleStudy selbst steht unter der MIT-Lizenz. Das Programm enthält keine Bibeltexte: Alles wird auf deinen Wunsch von der Originalquelle geladen. Eigene Dateien, die du importierst (z. B. eine Elberfelder-CSV), bleiben auf deinem Rechner, sind als „nur private Nutzung“ markiert und werden nie weitergegeben.");
}
