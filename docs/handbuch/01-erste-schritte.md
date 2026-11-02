# 1. Erste Schritte

Dieses Kapitel führt dich vom Download bis zur ersten gelesenen Bibelstelle. Wenn du das Programm schon kennst, springe zu [Kapitel 2](02-lesen.md).

## 1.1 Was BibleStudy ist

BibleStudy ist ein kostenloses, quelloffenes Programm zum Bibelstudium für Windows, Linux und macOS. Es ist in C# mit .NET 10 und Avalonia geschrieben und steht unter der MIT-Lizenz. Du kannst damit

- die Bibel in vielen freien Übersetzungen lesen, vergleichen und durchsuchen,
- den Grundtext (Hebräisch und Griechisch) Wort für Wort ansehen, mit Strong-Nummern, Morphologie und Syntax,
- eigene Notizen, Markierungen, Lesezeichen, Dokumente, To-dos und Gebete führen,
- Predigten vorbereiten,
- Querverweise als Graphen, auf der Karte, in einer Zeitleiste und als Stammbaum erkunden,
- mit einer anderen Person direkt verbunden gemeinsam studieren,
- auf Wunsch einen KI-Assistenten (Claude oder ein lokales Ollama-Modell) nutzen.

Das Leitprinzip heißt **Scripture alone**: Der Bibeltext steht an erster Stelle. Kommentare sind Menschenwerk und deshalb standardmäßig ausgeblendet (siehe [Kapitel 13](13-einstellungen.md)).

**Das Programm enthält keine Bibeltexte.** Übersetzungen, Wörterbücher und Daten werden erst von ihrer Originalquelle geladen, wenn du sie brauchst, und liegen dann im Zwischenspeicher auf deinem Computer. Lizenz und Herkunft siehst du in der Fußzeile und unter *Einstellungen → Quellen und Lizenzen*.

## 1.2 Installation

Lade unter [Releases](https://github.com/Trofline/BibleStudy/releases) die passende Datei herunter:

| Datei | Wofür |
|---|---|
| `BibleStudy-Setup-x.y.z.exe` | Windows-Installer ohne Administratorrechte, mit Deinstallation und automatischen Updates |
| `BibleStudy-Portable-x.y.z.zip` | Windows portabel: entpacken und starten. Alle Daten liegen im Ordner `Data` daneben |
| `BibleStudy-Linux-x64-x.y.z.tar.gz` | Linux |
| `BibleStudy-macOS-arm64-x.y.z.zip` / `BibleStudy-macOS-x64-x.y.z.zip` | macOS (Apple-Chip / Intel) |
| `SHA256SUMS.txt` | Prüfsummen, um den Download zu kontrollieren |

Hinweise:

- Die Linux- und macOS-Pakete werden unter Windows gebaut. Das Programm wurde bisher nur auf einem Intel-Mac gestartet, andere Systeme sind ungetestet. In den Paketen liegt eine Anleitung (`README-macOS.txt`, `README-Linux.txt`). Auf dem Mac startest du `Start-BibleStudy.command`.
- Der Windows-Installer ist nicht signiert. Windows (SmartScreen) kann deshalb warnen. Das ist bei kostenloser Software ohne Zertifikat normal; du kannst die Prüfsumme aus `SHA256SUMS.txt` mit der deiner Datei vergleichen.

## 1.3 Wo deine Daten liegen

Deine Daten sind vom Programm getrennt:

- **Installiert:** `%LOCALAPPDATA%\BibleStudy`
- **Portabel:** der Ordner `Data` neben dem Programm

Updates und Neuinstallationen berühren diesen Ordner nie. Vor jedem Update legt das Programm eine Sicherung an. Beim Deinstallieren wirst du immer gefragt, ob die Daten bleiben sollen.

Im Datenordner liegen unter anderem:

- die **Vaults** (deine Mappen mit Markierungen, Notizen, Dokumenten …), jeder eine einzelne Datei,
- die **Einstellungen** (eine JSON-Datei),
- der **Zwischenspeicher** mit geladenen Übersetzungen und Wörterbüchern,
- Sicherungen und Plugins.

## 1.4 Der erste Start

Beim ersten Start fragt das Programm nach der Zustimmung zu kurzen Nutzungsbedingungen. Danach siehst du die **Startseite** mit allen Büchern der Bibel. Ein Klick auf ein Buch zeigt die Kapitel, ein Klick auf ein Kapitel öffnet es.

Beim ersten Öffnen einer Übersetzung wird sie heruntergeladen. Willst du ohne Internet arbeiten, schalte unter *Einstellungen* die Offline-Verfügbarkeit der gewählten Übersetzung ein (siehe [Kapitel 13](13-einstellungen.md)).

### Der Rundgang

Auf dem Reiter **Hilfe** (rechte Leiste, Gruppe „Mehr“) startet der Knopf *Rundgang starten* eine geführte Tour durch die wichtigsten Knöpfe. Der Knopf oder Bereich, der gerade erklärt wird, ist **rot umrandet**, damit klar ist, worauf sich der Text bezieht.

## 1.5 Die Oberfläche im Überblick

Das Fenster besteht aus vier Bereichen.

### Obere Leiste

Von links nach rechts (einige Knöpfe erscheinen nur im Advanced-Modus):

- **Haus-Symbol:** Startseite mit allen Büchern (Alt+Pos1).
- **Vault:** zeigt die aktive Mappe; ein Klick wechselt sie.
- **Easy / Advanced:** schaltet zwischen dem vollen Funktionsumfang und der aufgeräumten Oberfläche um (siehe 1.7).
- **To-dos:** deine freie Liste zum Abhaken, von überall erreichbar.
- **Feedback:** Fehler melden, Ideen vorschlagen.
- **Tastenkürzel:** ansehen und ändern.
- **Layouts:** Fensteranordnungen speichern und laden.
- **Einstellungen:** Einstellungen, Hilfe, Info.
- **Fensterknöpfe:** minimieren, maximieren, schließen.

### Auswahlzeile

- **Gehe zu …** (Strg+K): ein Feld für Bibelstellen, Suche und Befehle.
- **Sprache, Übersetzung, Buch, Kapitel** und die Pfeile ◀ ▶ zum Blättern.

### Werkzeugzeile über dem Text

**Gelesen**, **A− / A+** (Schriftgröße), **Ansicht**, **Graph**, **Karte**, **Zeitleiste**, **Stammbaum**, **Parallel**, **Wortstudie**, **Vorlesen**, **Markieren**, **Merken**.

### Lesebereich und rechte Leiste

In der Mitte steht der Bibeltext. Rechts liegt die **Leiste** mit den Gruppen *Suchen, Vers, Bücher, Notizen, Lesezeichen, Dokumente, Predigt, Planen, Zusammen, KI* und *Mehr*. Ein Klick auf ein Symbol zeigt die Ansichten der Gruppe als Pillen; darunter erklärt ein Satz, wofür die gewählte Ansicht da ist. Mit Strg+B blendest du die ganze Leiste aus, am Rand ziehst du ihre Breite.

Du kannst jede Ansicht aus der Leiste ziehen oder mit dem Knopf ⧉ (Strg+Umschalt+O) als **eigenes Fenster** auslagern, auch auf einen zweiten Monitor.

### Fußzeile

Links die Statusmeldung, rechts der Update-Stand und die Versionsnummer (ein Klick darauf zeigt die Patch Notes). Hier steht auch die Lizenz der gewählten Übersetzung.

## 1.6 Schnell zu allem: Strg+K

Das Feld **Gehe zu …** versteht:

- Bibelstellen („Röm 8,28“, „Joh 15,11; 16,24“, „Mt 14,13-21“),
- Bücher,
- Befehle (zum Beispiel „Design wechseln“),
- deine Dokumente, Notizen, Gebete,
- Strong-Nummern, Personen und Orte,
- freie Wörter: Dann startet die Textsuche.

Während du tippst, erscheinen Treffer. Pfeiltasten wählen, Enter öffnet.

## 1.7 Easy und Advanced

Der Knopf *Easy / Advanced* oben wechselt zwischen zwei Oberflächen:

- **Advanced:** alles ist sichtbar.
- **Easy:** nur Lesen, Suchen, Notizen, Einstellungen, Hilfe und Dokumentation. Es verschwinden Knöpfe wie Parallel, Graph, Wortstudie, To-dos, Feedback und Tastenkürzel sowie die Gruppe „Vers“.

Nichts geht dabei verloren, und die Wahl wird gemerkt.

## 1.8 Die Sprache des Programms

Die Oberfläche gibt es auf Deutsch, Englisch, Spanisch, Französisch und Russisch. Die Übersetzungen sind KI-gemacht, Korrekturen sind willkommen. Eine weitere Sprache kannst du selbst hinzufügen (siehe [Kapitel 13](13-einstellungen.md), Abschnitt „Eigene Sprache“).

## 1.9 Wenn etwas nicht geht

Schau in [Kapitel 17](17-fehlersuche.md). Ein Fehler lässt sich über das Feedback-Symbol oben melden; es wird nichts gesendet, bevor du das Ticket auf GitHub selbst abschickst.
