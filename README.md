# BibleStudy

Freie, quelloffene Bibelstudien-Software für Windows (C#, .NET 10, Avalonia). Lesen, vergleichen, markieren, Notizen und Dokumente
verknüpfen, Predigten vorbereiten – alles offline nutzbar und ohne Konto.

*Free and open-source Bible study software for Windows. Read, compare, highlight, link notes and documents, prepare sermons – works offline, no account needed.*

## Was kann es?

- **Lesen und vergleichen:** viele freie Übersetzungen, Parallelspalten, Unterschiede zwischen Übersetzungen farbig markiert
- **Suche:** Wörter, Wendungen, Grundformen, Strong-Nummern, Bibelstellen, **Themensuche** mit verwandten Wörtern
- **Studium:** Wort-für-Wort-Grundtext, Strong-Wörterbuch, Querverweise (Graph, Netzwerk, Bogen), Kommentare, Bibellexikon
- **Zeitleiste mit Karte**, **Stammbaum** von Personen der Bibel
- **Eigenes Wissen:** Vaults (wie bei Obsidian) mit Markierungen, Notizen, Dokumenten, Ordnern, Tags, Wiki-Links
- **Planen:** Lesepläne mit Fortschritt, Leseliste, Aufgaben, **Gebetsliste**
- **Predigt-Editor** mit Gliederung, Versen, Zeitplan und Handout-Export
- **Reiter wie im Explorer**, Layouts (Desktops), frei belegbare Tastenkürzel
- **KI-Anbindung (optional):** MCP-Server, damit Claude das Programm bedienen kann

## Installation

Lade unter [Releases](https://github.com/trofline/BibleStudy/releases) eine der beiden Dateien:

| Datei | Wofür |
|---|---|
| `BibleStudy-Setup-x.y.z.exe` | Installer (ohne Administratorrechte), mit Deinstallation und automatischen Updates |
| `BibleStudy-Portable-x.y.z.zip` | Portable Version: entpacken und starten; alle Daten liegen im Ordner `Data` daneben |

**Deine Daten sind geschützt:** Installiert liegen sie in `%LOCALAPPDATA%\BibleStudy`, also getrennt vom Programm. Updates und
Neuinstallationen berühren sie nicht, vor jedem Update wird zusätzlich eine Sicherung in `Dokumente\BibleStudy-Backup` angelegt.
Beim Deinstallieren (und beim Entfernen der portablen Version) wirst du immer gefragt, ob deine Daten erhalten bleiben sollen.

## Selbst bauen

```powershell
dotnet run --project src/BibleStudy.App      # Entwicklung
.\release.ps1 -Version 1.0.0                 # Installer + Portable-Paket in .\release
```

## Lizenzen und Quellen

Der Programmcode steht unter der [MIT-Lizenz](LICENSE). **Das Programm enthält keine Bibeltexte.** Übersetzungen, Kommentare und Daten
werden auf deinen Wunsch von ihren Originalquellen geladen; die Lizenz steht jeweils in der Fußzeile und im Programm unter
*Einstellungen → Quellen und Lizenzen*. Alle Quellen: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
Eigene Dateien, die du importierst (z. B. eine Elberfelder-CSV), bleiben auf deinem Rechner und sind nur für deinen privaten Gebrauch.

## Mitmachen

Fehler und Wünsche bitte als [Issue](https://github.com/trofline/BibleStudy/issues) melden. Beiträge sind willkommen; mit einem
Pull Request stimmst du zu, dass dein Beitrag unter der MIT-Lizenz steht. Bitte nur Daten und Texte mit klarer, freier Lizenz einbauen.
