namespace BibleStudy.App;

/// <summary>Where the program's data comes from and under which license (mirrors THIRD-PARTY-NOTICES.md).</summary>
static class Licenses
{
    public static readonly (string Name, string Use, string License)[] Sources =
    [
        ("Free Use Bible API (HelloAO)", "Übersetzungen, Kommentare, Datensätze", "Freie Nutzung; jede Übersetzung nennt ihre eigene Lizenz in der Fußzeile"),
        ("Berean Standard Bible und Interlinear", "Wort für Wort Hebräisch/Griechisch", "Gemeinfrei"),
        ("Open Cross Reference (OpenBible.info)", "Querverweise", "CC BY 4.0"),
        ("Theographic Bible Metadata", "Personen, Orte, Ereignisse, Zeitleiste", "CC BY-SA 4.0"),
        ("Easton's Bible Dictionary", "Bibellexikon", "Gemeinfrei (1897)"),
        ("STEPBible Lexikon (Tyndale House)", "Strong-Wörterbuch", "CC BY 4.0"),
        ("Kommentare (Matthew Henry, JFB, Keil-Delitzsch, Calvin, Tyndale u. a.)", "Kommentare", "je Kommentar angezeigt; meist gemeinfrei, Tyndale CC BY-SA 4.0"),
        ("OpenThesaurus", "Deutsche Synonyme", "CC BY-SA 4.0 / LGPL"),
        ("Natural Earth", "Weltkarte", "Gemeinfrei"),
        ("Avalonia UI, Microsoft.Data.Sqlite, ModelContextProtocol", "Programmbibliotheken", "MIT / Apache-2.0"),
        ("Inter, Crimson Text", "Schriften", "SIL Open Font License 1.1"),
    ];

    public const string Own = "BibleStudy selbst steht unter der MIT-Lizenz. Das Programm enthält keine Bibeltexte: Alles wird auf deinen Wunsch von der Originalquelle geladen. Eigene Dateien, die du importierst (z. B. eine Elberfelder-CSV), bleiben auf deinem Rechner, sind als „nur private Nutzung“ markiert und werden nie weitergegeben.";
}
