# Roadmap

Arbeitsplan für die nächsten Versionen. Reihenfolge = Reihenfolge der Abarbeitung. Pro Phase: bauen, Tests (inkl. Übersetzungen) grün, im echten Programm prüfen, ein Release auf GitHub.

Regeln für die Abarbeitung:
- Nur vertrauenswürdige Quellen: gemeinfrei oder klar frei lizenziert (CC BY, CC BY-SA, CC0), von bekannten Trägern (z. B. Open Scriptures, STEPBible, Perseus, Clear Bible, eBible.org). Keine Quellen mit „nicht-kommerziell“, Nutzererklärung oder unklarem Ursprung (z. B. CCAT-Septuaginta, NC-Fassungen des LSJ). Lizenz vor dem Einbau prüfen, im Programm unter „Quellen und Lizenzen“ nennen und im Test/der Roadmap festhalten.
- Nie den Namen des kommerziellen Mitbewerbers in Programm, Repo oder Commits.
- Alles Sichtbare mit `L("…")` und in en/es/fr/ru übersetzt (`tools/BibleStudy.L10n`).
- Nach außen Sichtbares (Pull Requests bei winget/Flathub/Scoop, Anträge, Konten) nicht allein tun, sondern vorbereiten und Elias Bescheid geben.
- Was nicht auf echtem Mac/Linux geprüft werden kann, im Release-Text so benennen.

## Phase 1 · v1.0.21 – Fehler und Kleinkram
- [x] Fenster zu klein: Toolbar oben (Strg+K-Feld usw.) verliert Elemente → Umbruch bzw. Überlauf-Menü „⋯“, nichts verschwindet
- [x] Zusammenarbeit: Einladung erstellen → Antwortcode einfügen, Fenster setzt sich nach ~15 s zurück. Ursache suchen (Timer/Neuaufbau des Tabs, Fokus, Fehler im Log), beheben, Test
- [x] Versionsnummer unten rechts in der Statusleiste
- [x] GitHub-Button (öffnet das Open-Source-Projekt): oben in der Leiste und zusätzlich in den Einstellungen
- [x] „Satzbau“ überall in „Syntax“ umbenennen (Oberfläche, Hilfe, Übersetzungen)
- [x] Verse-Warteschlange (Queue) verständlicher: Erklärtext, bessere Beschriftung, Leer-Zustand
- [x] Strg+K: alle Befehle anzeigen (Liste scrollbar bzw. „Alle Befehle“-Eintrag), nicht nur 8
- [x] Jesu Worte in eigener Farbe (Einstellung an/aus; Quelle nur, wo Daten legal vorhanden)
- [x] Kopieren mit Quelle: Übersetzung, Stelle und Lizenzhinweis automatisch anfügen

## Phase 2 · v1.0.22 – Updates, Verlauf, Verteilung im Repo
- [x] Patch Notes: nach einem Update anzeigen, danach jederzeit unter Hilfe nachlesbar (aus den Release-Texten, offline zwischengespeichert)
- [x] Rollback auf frühere Versionen (Liste der Releases, Download + Prüfsumme, Daten werden vorher gesichert, nie ohne Zustimmung)
- [x] Lizenz-Cache: Lizenzdaten/Quellen als Cache im Repo bzw. Release hochladen, falls Download bei jemandem scheitert (Ausweichadresse im Programm) – Kopie der Übersetzungsliste im Programm und im Repo (src/BibleStudy.Core/Data), wird nur bei Serverfehler genutzt
- [x] GitHub Actions: Build + Tests bei jedem Push, Release-Build für alle Systeme
- [x] GitHub Pages: Projektseite im Design der App (hell/dunkel, Programm-Grafiken als Illustrationen) – docs/index.html; in GitHub unter Settings → Pages → Ordner /docs einschalten. Echte Screenshots später in docs/img ergänzen

## Phase 3 · v1.1.0 – Lesen und Studieren
- [x] Vers-Seite rechts: zusätzlich Personen, Orte und Schlüsselwörter zum angeklickten Vers
- [x] Parallelansicht: Spalte wählen und per Ziehen umsortieren; Presets speichern/laden (welche Übersetzungen/Stellen, wo)
- [x] Wort antippen, das man nicht kennt → kurze Erklärung (Wörterbuch, Grundtext falls vorhanden)
  - Stand: Wort im Text markieren, Rechtsklick auf den Vers → „Wort erklären“ (Wiktionary, CC BY-SA); Wörterbuch/Grundtext-Anzeige weiter über „Wortstudie“
- [x] Lesehilfen: Kontrastmodus, Schrift für Legasthenie, Schriftzoom, komplette Tastaturbedienung
  - Stand: Kontrastmodus, lesbare Schrift und Schriftzoom sind da; die komplette Tastaturbedienung ist noch offen (kommt mit Screenreader-Phase)
- [x] Hebräisch: Vokale und Akzente einzeln ausblenden, richtige Rechts-nach-links-Darstellung (in den Einstellungen wählbar)
  - Stand: Vokale/Akzente einzeln ausblendbar; Rechts-nach-links gilt automatisch im Grundtext (kein eigener Schalter)
- [x] Kommentare (nur frei lizenzierte) + Volltextsuche in den Kommentaren
  - Stand: Kommentare gab es schon (Liste mit Lizenz je Kommentar); neu: „Im ganzen Kommentar suchen“ (liest den Kommentar einmal ein, danach offline)

## Phase 4 · v1.2.0 – Bücher und Grundtext
- [x] BDB-Lexikon (Hebräisch): Open Scriptures Hebrew Lexicon, Text gemeinfrei, Datei CC BY 4.0; erscheint unter dem Strong-Eintrag hebräischer Wörter, mit Quellenangabe
- [ ] LSJ (Griechisch): nur aus dem Original der Perseus Digital Library (PerseusDL/lexica, CC BY-SA 4.0, Beta-Code-Umwandlung selbst schreiben). NICHT die Celano-Fassung (CC BY-NC-SA). Vorher prüfen, dass die LSJ-Dateien selbst unter CC BY-SA stehen; Größe beachten (nur bei Bedarf laden)
- [ ] Fachlexika: Auswahl nur nach Lizenzprüfung
- [ ] Septuaginta: griechischer Text nur, wenn eine klar freie Quelle gefunden wird (CCAT/Rahlfs-Dateien sind NICHT geeignet: Nutzererklärung + NC). Bis dahin: die gemeinfreien englischen Fassungen (Brenton, LXX2012, Updated Brenton) sind über die Bibelliste schon lesbar; Knopf „Septuaginta neben Hebräisch/Hauptübersetzung“ als Parallel-Preset
- [ ] Textkritik: Lesarten/Varianten anzeigen, wo freie Daten existieren

## Phase 5 · v1.3.0 – Einrichtung, KI, Sync, Barrierefreiheit
- [x] Erststart-Assistent: Sprache → empfohlene Übersetzung → Kommentare und Datensätze mit einem Klick laden (mit Fortschritt, abbrechbar)
  - Stand: Fenster beim ersten Start (Sprache, Übersetzung, Offline-Laden und Querverweise). Kommentare/weitere Datensätze kommen mit ihren Phasen dazu
- [x] Ollama-Einbindung (lokale KI, keine Daten nach außen)
  - Stand: Einstellungen → KI-Motor „Ollama“; einfacher Chat mit dem angeklickten Vers, ohne die Bibel-Werkzeuge (die gibt es nur mit Claude Code). Werkzeuge für Ollama wären ein späterer Schritt
- [x] Syncthing-Anbindung wie bei OneDrive/Google Drive (Datenordner/Sicherungsordner synchronisieren, Anleitung + Erkennung)
  - Stand: Einstellungen → Sicherung → „Syncthing-Ordner nutzen“ (erkennt Syncthing-Ordner, legt die Sicherungen dort ab; der laufende Datenordner wird bewusst nicht synchronisiert)
- [x] Screenreader-Unterstützung (AutomationProperties, Fokusreihenfolge, Test mit NVDA)
  - Stand: Icon-Knöpfe und Eingabefelder bekommen ihren Hinweistext als Namen. Fokusreihenfolge und ein echter NVDA-Test sind noch offen (nur Elias kann mit NVDA prüfen)

## Phase 6 · v1.4.0 – Gemeinsam studieren
- [ ] Gemeinsame Markierungen und gemeinsame Vaults (Cursor/Auswahl der anderen sichtbar)
- [ ] Live-Stunde: alle haben dieselbe Ansicht, sichtbare Cursor und Markierungen

## Phase 7 · v1.5.0 – Große Ansichten
- [x] Wort-Reise durch die Bibel (Grundtextwort anklicken → animierte Karte von Buch zu Buch)
  - Stand: Knopf „Reise durch die Bibel“ in der Konkordanz (Strong-Wort): ein Licht wandert durch alle Bücher, Bücher mit Treffern leuchten auf; Klick filtert auf das Buch
- [ ] Gedanken-Landkarte: Syntaxbäume → Satzdiagramm / Argumentationsbaum, farbig
- [ ] Zeitreise-Karte: Zeitregler, Orte/Wege/Reiche erscheinen und verschwinden, gleichzeitig lebende Personen
  - Stand: Zeitregler und „Zeitreise“-Abspielen in der Zeitleiste (Ereignisse nacheinander, Karte folgt). Offen: Orte/Reiche erscheinen und verschwinden, gleichzeitig lebende Personen
- [ ] Liquid-Glass-Design und Animationen (abschaltbar, respektiert „Bewegung reduzieren“)
- [x] Minimalistischer Stil („Einfach-Modus“ / Easy Mode): aufgeräumte Oberfläche mit nur dem Wichtigsten (Lesen, Suchen, Notizen), alles andere ausgeblendet und mit einem Schalter wieder einblendbar. Oben in der Leiste ein Button, der zwischen „Advanced“ und „Easy“ umschaltet (Wahl wird gemerkt)
  - Stand: Knopf „Advanced/Easy“ oben in der Leiste; Easy zeigt nur Suche, Notizen, Vers-Übersicht, Einstellungen und Hilfe und blendet einige Knöpfe aus (wird gemerkt)

## Phase 8 · Verteilung (braucht Elias)
- [x] winget-Manifest vorbereiten (Pull Request erst nach Rückfrage) (packaging/winget, siehe packaging/VERTEILUNG.md)
- [x] Scoop-Bucket und Flathub-Entwurf vorbereitet (packaging/scoop, packaging/flatpak; Flatpak ungetestet)
- [ ] Code-Signierung über SignPath Foundation (Antrag für Open Source – Elias muss ihn stellen) – Vorbereitung in packaging/VERTEILUNG.md, Antrag stellt Elias
- [ ] Microsoft-Store-Signatur/-Paket (Entwicklerkonto nötig)

## Zusatz (Wünsche von Elias, nach Phase 8 einsortieren oder früher ziehen)
- [x] Eigene Sprache importieren: Vorlage + KI-Anweisung speichern, übersetzte Datei importieren (Platzhalter-Prüfung, Neustart-Frage); Einstellungen → Sprache
- [x] Syntax als Baum-Diagramm (Reiter „Baum“ in der Syntax-Ansicht) zusätzlich zu Liste und Morphologie
- [x] Querverweis-Stern (Vers in der Mitte, stärkste Querverweise außen; Knopf „Als Stern zeigen“ in der Vers-Übersicht) zusätzlich zu den bestehenden Graphen
- [x] Neue Beenden-Animation (hochgleiten, verkleinern, verwischen, auflösen)
- [ ] Beenden-Animation und Einstiegs-Animation in den Einstellungen wählbar (aus / dezent / groß)
- [ ] Stern-Ansicht: zweite Ebene (Verbindungen zwischen den Nachbarn), Ziehen der Knoten, Export als Bild
- [ ] Syntaxbaum: Export als Bild/SVG, Farben je Satzrolle, Zoomen
- [ ] Sprachpakete teilen: Ordner `lang/` im Repo für Community-Sprachen (Pull Requests), Auswahl „Sprachen aus dem Internet laden“ in den Einstellungen
- [ ] Sprachdateien: Prüfung auf Vollständigkeit mit Fortschrittsanzeige („87 % übersetzt“), fehlende Texte als neue Vorlage exportieren (nur das Fehlende an die KI geben)
- [ ] Pages: echte Screenshots statt Zeichnungen, Deutsch/Englisch-Umschalter, Seite „Neuigkeiten“ aus den Release-Texten
- [ ] Pages: Anleitung „Eigene Sprache hinzufügen“ mit fertigem KI-Text zum Kopieren
- [ ] Wort-Reise und Zeitreise-Karte auch als kleine animierte Vorschau auf der Startseite der App
- [ ] Startseite der App: „Heute“-Karte mit Tageslesung, Vers des Tages als schönes Bild (teilen mit Quelle und Lizenz)
- [ ] Gespeicherte Parallel-Anordnungen auch mit Hauptübersetzung und Sprache, Tastenkürzel zum Wechseln
- [ ] Tastaturbedienung komplett (Tab-Reihenfolge, Pfeiltasten in Listen), gemeinsam mit der Screenreader-Phase
- [ ] Automatischer Test, der Screenshots der wichtigsten Ansichten (hell/dunkel, klein/groß) erzeugt, damit Layout-Fehler früher auffallen
- [x] PDF-Import für gedruckte Bibeln (Spalten, Kopfzeile, Verszahlen, Überschriften, Parallelstellen, Fußnoten; getestet mit der Schlachter 2000: 31.007 von rund 31.100 Versen)
- [ ] PDF-Import: Kapitelanfänge ohne Überschrift sicherer erkennen (z. B. Josua 20 geht noch verloren), erste Kapitel der Bücher mit Seitenspalte, Kapitel-/Verszählung der Übersetzung (Joel/Maleachi) auf die Standardzählung abbilden
