<p align="center"><img src="src/BibleStudy.App/Assets/app.png" width="128" alt="BibleStudy logo"></p>

<p align="center">
  <a href="https://github.com/Trofline/BibleStudy/releases/latest"><img alt="Release" src="https://img.shields.io/github/v/release/Trofline/BibleStudy?color=c9a227"></a>
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-blue"></a>
  <a href="https://github.com/Trofline/BibleStudy/actions/workflows/build.yml"><img alt="Build" src="https://github.com/Trofline/BibleStudy/actions/workflows/build.yml/badge.svg"></a>
  <a href="https://github.com/Trofline/BibleStudy/actions/workflows/codeql.yml"><img alt="CodeQL" src="https://github.com/Trofline/BibleStudy/actions/workflows/codeql.yml/badge.svg"></a>
  <img alt="Platforms" src="https://img.shields.io/badge/Windows%20%C2%B7%20Linux%20%C2%B7%20macOS-555">
</p>
<p align="center"><a href="https://trofline.github.io/BibleStudy/">Website</a> · <a href="https://github.com/Trofline/BibleStudy/releases/latest">Download</a> · <a href="https://github.com/Trofline/BibleStudy/wiki">Wiki</a> · <a href="docs/handbuch/README.md">Handbuch</a></p>

# BibleStudy

Freie, quelloffene Bibelstudien-Software für Windows, Linux und macOS (C#, .NET 10, Avalonia). Lesen, vergleichen, markieren, Notizen und Dokumente
verknüpfen, Predigten vorbereiten – alles offline nutzbar und ohne Konto.

*[English version](README.md)* · *[Das Handbuch](docs/handbuch/README.md): jede Funktion, jedes Tastenkürzel, Strategien*

<p align="center"><a href="docs/img/tour.gif"><img src="docs/img/tour.gif" alt="Kurze Tour: Lesen mit Wortstudie, Grundtext, Bogen-Diagramm, Netzwerk, Stammbaum" width="900"></a></p>

<p align="center">
  <a href="docs/img/occurrences.png"><img src="docs/img/occurrences.png" alt="Wo ein Wort im Alten Testament vorkommt" height="300"></a>
  <a href="docs/img/original-text.png"><img src="docs/img/original-text.png" alt="Grundtext Wort für Wort mit Wörterbuch" height="300"></a>
</p>

## Bibelstunde halten

Eine Bibelstunde vorbereiten und halten, alles in einem Programm: Deine Unterlagen öffnen sich direkt darin, die Bibelstelle steht daneben, das Handout kommt gestaltet heraus und die Verse gehen an den Beamer.

<p align="center"><a href="docs/img/split-view.png"><img src="docs/img/split-view.png" alt="Geteilte Ansicht: links Johannes 10, rechts die Unterlagen mit anklickbaren Bibelstellen und einer Markierung mit Notiz" width="900"></a></p>

- **Deine Materialien im Programm:** Word, PDF, PowerPoint, OpenDocument, Markdown und mehr öffnen sich im Tab *Materialien*. Bibelstellen darin sind anklickbar, mit der Maus darauf siehst du den Vers, und du kannst alle Dateien auf einmal durchsuchen.
- **Markierungen und Notizen in deinen Unterlagen**, ohne dass die Dateien selbst verändert werden.
- **Geteilte Ansicht:** die Bibel auf der einen Seite, deine Materialien oder Dokumente auf der anderen.
- **Handouts in acht Designs:** jedes Dokument als gestaltetes Blatt drucken, mit den Versen als Anhang, hervorgehobenen Fragen und Platz für Notizen. Dazu Export als HTML und Markdown.
- **Beamer-Ansicht:** Verse oder Abschnitte deiner Materialien groß und ruhig auf einem zweiten Bildschirm.
- **Ablauf für den Timer:** Lied, Gebet, Bibelarbeit … jeder Teil mit seiner Zeit, ein kurzer Ton, wenn der nächste beginnt.

<p align="center">
  <a href="docs/img/materials.png"><img src="docs/img/materials.png" alt="Der Tab Materialien mit Vers-Vorschau" height="260"></a>
  <a href="docs/img/handout-looks.png"><img src="docs/img/handout-looks.png" alt="Acht Designs für das Handout" height="260"></a>
  <a href="docs/img/projector.png"><img src="docs/img/projector.png" alt="Ein Vers auf dem Beamer" height="260"></a>
</p>

<sub>Die Bilder zeigen die englische Oberfläche; das Programm gibt es auch auf Deutsch.</sub>

## Was kann es?

- **Lesen und vergleichen:** viele freie Übersetzungen, Parallelspalten, Unterschiede zwischen Übersetzungen farbig markiert, dazu eine **Hörbibel** (gesprochene World English Bible, gemeinfrei, spielt Kapitel für Kapitel und läuft nach dem Laden auch offline)
- **Suche:** Wörter, Wendungen, Grundformen, Strong-Nummern, Bibelstellen, **Themensuche** mit verwandten Wörtern
- **Studium:** Wort-für-Wort-Grundtext, Strong-Wörterbuch, Querverweise (Graph, Netzwerk, Bogen), Kommentare, Bibellexikon
- **Zeitleiste mit Karte**, **Stammbaum** von Personen der Bibel
- **Eigenes Wissen:** Vaults (wie bei Obsidian) mit Markierungen (freie Textauswahl über Verse, Untermarkierungen, Farbwechsel mit einem Klick), Notizen, Dokumenten, Ordnern, Tags, Wiki-Links
- **Planen:** Lesepläne mit Fortschritt, Leseliste, Aufgaben, **Gebetsliste**
- **Predigt-Editor** mit Gliederung, Versen, Zeitplan und Handout-Export
- **Bibelstunde:** Materialordner (Word, PDF, PowerPoint …) mit anklickbaren Bibelstellen, Suche, Markierungen und Notizen; geteilte Ansicht; Handouts in acht Designs; Beamer-Ansicht; Uhr, Timer und Ablauf für den Abend
- **Morphologie und Syntax** des Urtextes (Griechisch und Hebräisch): Zeit, Fall, Person …, Syntax mit Subjekt, Verb, Objekt …
- **Analyse-Werkzeuge:** Schlüsselwörter und einmalige Wörter je Kapitel, verwandte Wörter, Verteilung eines Wortes über die Bibel
- **Satzdiagramm und Perikopen-Analyse:** der Satz eines Verses als klassisches Satzdiagramm (Reed–Kellogg, Griechisch und Hebräisch, Export als PNG/SVG) und ein Abschnitt in Bildern: Wortarten, Verbformen, Schlüsselwörter und wo sie vorkommen, Rhythmus der Verse, Satzbau und Verbindungen
- **Dokumente mit Kategorien**, Studienvorlagen und Wissensgraph
- **Zusammen studieren:** direkt mit einer anderen Person verbunden (verschlüsselt, kein Server, kein Konto, kostenlos): Chat, Verse und Dokumente teilen, der Stelle der anderen folgen; eingerichtet durch den Austausch von zwei kurzen Codes. Es ist eine direkte Verbindung ohne Relay-Server und klappt am besten im selben Netzwerk; über verschiedene Netzwerke hängt es von euren Anschlüssen ab (ein privates Netzwerk wie ZeroTier oder Tailscale oder ein eigener Relay-Server kann helfen, siehe Handbuch)
- **Linux und macOS:** Pakete werden mit gebaut (auf Windows gebaut; bisher nur auf einem Intel-Mac gestartet, sonst ungetestet), mit Schritt-für-Schritt-Anleitung im Paket (`README-macOS.txt`, `README-Linux.txt`), siehe Releases
- **Griechisch und Hebräisch lernen:** Alphabet, Buchstaben-Quiz, Vokabeltrainer mit Wiederholung
- **Drucken / PDF** für Dokumente, Lesepläne, Gebete und Predigt; Predigt-Folien als Vollbild-Show
- **Reiter wie im Explorer**, Layouts (Desktops), frei belegbare Tastenkürzel
- **Mehrsprachig:** Oberfläche auf Englisch, Deutsch, Spanisch, Französisch und Russisch (KI-erstellte Übersetzungen, Korrekturen willkommen; weitere Sprachen als JSON-Datei im Ordner `lang` ergänzbar)
- **KI-Anbindung (optional):** MCP-Server, damit Claude das Programm bedienen kann
- **Plugins:** zusätzliche Übersetzungen, Befehle und Zugriff auf das Programm (siehe [PLUGINS.md](PLUGINS.md)). Fertige Plugins liegen im Ordner [`plugins`](plugins) und in jedem Release, zum Beispiel **Strong-Nummern ergänzen** (fügt Übersetzungen ohne Strong-Nummern eine *geschätzte* Nummerierung hinzu; keine Garantie, bitte prüfen)

## Installation

Lade unter [Releases](https://github.com/Trofline/BibleStudy/releases) eine der beiden Dateien:

| Datei | Wofür |
|---|---|
| `BibleStudy-Setup-x.y.z.exe` | Installer (ohne Administratorrechte), mit Deinstallation und automatischen Updates |
| `BibleStudy-Portable-x.y.z.zip` | Portable Version: entpacken und starten; alle Daten liegen im Ordner `Data` daneben |

**Deine Daten sind geschützt:** Installiert liegen sie in `%LOCALAPPDATA%\BibleStudy`, also getrennt vom Programm. Updates und
Neuinstallationen berühren sie nicht, vor jedem Update wird zusätzlich eine Sicherung in `Dokumente\BibleStudy-Backup` angelegt.
Beim Deinstallieren (und beim Entfernen der portablen Version) wirst du immer gefragt, ob deine Daten erhalten bleiben sollen.

## Auf dem Handy

**Android:** Lade `BibleStudy-Android-…apk` aus dem [neuesten Release](https://github.com/Trofline/BibleStudy/releases/latest) herunter (Beta) und öffne sie. Danach aktualisiert sich die App selbst.

**iPhone (Beta), kostenlos und ohne Mac:** Apple erlaubt nur dem App Store, Apps zu installieren. Deshalb wird BibleStudy mit [SideStore](https://sidestore.io) installiert, das die App mit deiner eigenen, kostenlosen Apple-ID signiert. Du brauchst **einmalig einen Windows-PC (oder Mac) mit Kabel**; danach erneuert das Handy die App von selbst. Zusammen etwa 10 Minuten.

1. **SideStore aufs iPhone bringen (ca. 4 Min.).** Am PC *Apple-Geräte* (Microsoft Store) oder iTunes installieren, dann [iloader](https://github.com/nab138/iloader) öffnen, das iPhone per Kabel anschließen, mit der Apple-ID anmelden und SideStore installieren. iloader erzeugt auch die *Pairing-Datei*, die SideStore braucht. Meldet SideStore später, dass sie fehlt: iloader mit angeschlossenem iPhone erneut öffnen, unten auf **Manage Pairing File** klicken und sie für SideStore einsetzen.
2. **Vertrauen und Entwicklermodus (ca. 2 Min.).** iPhone: Einstellungen → Allgemein → VPN und Geräteverwaltung → deiner Apple-ID vertrauen. Einstellungen → Datenschutz & Sicherheit → Entwicklermodus → ein (das iPhone startet neu).
3. **Loopback-VPN installieren (ca. 1 Min.).** SideStore spricht über ein winziges VPN auf dem Handy selbst mit dem Handy. Es schickt keine Daten irgendwohin. Installiere **StosVPN** (oder *LocalDevVPN*) aus dem App Store und schalte es einmal ein.
4. **BibleStudy hinzufügen (ca. 2 Min.).** SideStore öffnen, anmelden, dann auf dem iPhone [diesen Link](sidestore://source?url=https://trofline.github.io/BibleStudy/sidestore.json) antippen (oder Quellen → **+** → `https://trofline.github.io/BibleStudy/sidestore.json`). BibleStudy in der Liste öffnen und **Installieren** tippen.
5. **Jeden Tag automatisch erneuern (ca. 2 Min.).** Eine kostenlose Apple-ID signiert eine App nur 7 Tage. Mit einer Automation musst du nie daran denken:
   1. **Kurzbefehle → Kurzbefehle → +** und diese Aktionen in dieser Reihenfolge anlegen: das VPN von StosVPN **einschalten** (Aktion der VPN-App), **Alle Apps erneuern** (Aktion von SideStore), das VPN wieder **ausschalten**. Name: **BibleStudy erneuern**.
   2. **Kurzbefehle → Automation → +** → **Tageszeit** (zum Beispiel 3:00, täglich) → **Sofort ausführen** → den Kurzbefehl wählen. „Beim Ausführen benachrichtigen“ kannst du ausschalten.
   3. Lass das Handy nachts am Strom und mit WLAN, dann läuft es.
6. **Abgleich mit dem PC, sobald du heimkommst (ca. 1 Min.).** Das iPhone gleicht nur ab, während BibleStudy offen ist. Damit das von selbst passiert: **Kurzbefehle → Automation → +** → **WLAN** → dein WLAN wählen, **Verbindet** ankreuzen → **Sofort ausführen** → **Weiter** → **Neue leere Automation** → Aktion **App öffnen** → **BibleStudy**. Kommst du heim, öffnet sich die App kurz und gleicht ab. Einrichten des Abgleichs: Handbuch Kapitel 19.3.

Ist eine App trotzdem abgelaufen: SideStore öffnen und bei BibleStudy **Erneuern** tippen; deine Daten bleiben. Updates kommen über SideStore.

**Ehrliche Grenzen:** Eine kostenlose Apple-ID erlaubt höchstens 3 selbst signierte Apps gleichzeitig (SideStore zählt mit). Nach langem Ausschalten oder einem großen iOS-Update kann einmal manuelles Erneuern nötig sein. Apple könnte die Methode irgendwann einschränken. Claude Code, Ollama und der MCP-Server gibt es nur am PC, die Live-Bibelstunde läuft nur bei geöffneter App. SideStore ändert sich ab und zu: sieht ein Schritt anders aus, gilt [docs.sidestore.io](https://docs.sidestore.io).

## Selbst bauen

```powershell
dotnet run --project src/BibleStudy.App      # Entwicklung
powershell -ExecutionPolicy Bypass -File .\release.ps1 -Version 1.0.0   # Installer + Portable-Paket in .\release
```

## Lizenzen und Quellen

Der Programmcode steht unter der [MIT-Lizenz](LICENSE). **Das Programm enthält keine Bibeltexte.** Übersetzungen, Kommentare und Daten
werden auf deinen Wunsch von ihren Originalquellen geladen; die Lizenz steht jeweils in der Fußzeile und im Programm unter
*Einstellungen → Quellen und Lizenzen*. Alle Quellen: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
Eigene Dateien, die du importierst (z. B. eine Elberfelder-CSV), bleiben auf deinem Rechner und sind nur für deinen privaten Gebrauch.

**Keine Gewähr, keine Haftung.** BibleStudy ist freie Software und wird „wie besehen“ ohne jede Gewähr bereitgestellt (siehe Lizenz). Soweit gesetzlich zulässig, haften die Entwickler nicht für Schäden aus der Nutzung; die Haftung für Vorsatz, grobe Fahrlässigkeit, Schäden an Leben, Körper und Gesundheit sowie jede gesetzlich nicht ausschließbare Haftung bleibt unberührt. Inhalte Dritter (Übersetzungen, Kommentare, Daten), eigene Importe und alles, was du teilst, liegen in deiner Verantwortung. Beim ersten Start fragt das Programm die Zustimmung zu kurzen Nutzungsbedingungen ab.

## Mitmachen

Fehler und Wünsche bitte als [Issue](https://github.com/Trofline/BibleStudy/issues) melden. Beiträge sind willkommen; mit einem
Pull Request stimmst du zu, dass dein Beitrag unter der MIT-Lizenz steht. Bitte nur Daten und Texte mit klarer, freier Lizenz einbauen.


