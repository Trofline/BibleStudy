# 13. Einstellungen

Öffnen: Zahnrad oben rechts oder rechte Leiste → **Mehr → Einstellungen**. Die Einstellungen sind in Abschnitte gegliedert, die sich einklappen lassen; eine **Sprungliste** mit den vollen Namen bringt dich direkt zu einem Abschnitt.

Alle Einstellungen werden automatisch gespeichert.

## 13.1 Sprache

Sprache der Oberfläche: Deutsch, Englisch, Spanisch, Französisch, Russisch.

### Eigene Sprache hinzufügen

Deine Sprache fehlt? Speichere die **Vorlage mit der KI-Anweisung**, lass sie von einer KI übersetzen und **importiere die Sprachdatei**. Fehlende Texte bleiben Deutsch. *Nur fehlende Texte exportieren* spart bei einer unvollständigen Sprache Arbeit. Die Datei ist ein JSON, in dem der deutsche Text der Schlüssel ist.

## 13.2 Darstellung

- **Design:** System, Hell, Dunkel.
- **Akzentfarbe** des Programms: sechs fertige Farben oder mit **+** eine eigene (Farbwähler mit Vorschau; Hell und Dunkel werden so angepasst, dass Schrift lesbar bleibt). Eigene Farben entfernst du per Rechtsklick.
- **Schrift:** Größe, Zeilenabstand, Blocksatz, leichter lesbare Schrift (zum Beispiel bei Legasthenie), hoher Kontrast.
- **Versnummern:** klein hochgestellt, normal im Text, ausblenden.
- **Worte Jesu** farbig hervorheben, mit eigener Farbe (unabhängig vom Farbschema).
- **Hebräisch:** Vokalzeichen und Akzentzeichen (Kantillation) ein-/ausblenden.
- **Wortbedeutungen in meine Sprache übersetzen** (KI).
- **Kommentare anzeigen** (Menschenwerk, nicht der Bibeltext; gilt nach Neustart).
- **Animationen:** Seitenleiste, Reiter und Kapitel gleiten sanft ein; Stärke *Dezent* bis *Groß*.
- **Schließ-Animation:** wie sich das Programm verabschiedet: *Weich* (wegzoomen), *Unscharf* (verschwimmen), *Dezent* (nur ausblenden), *Röhrenfernseher*, *Partikel-Auflösung* (das Fenster zerfällt zu Sand und weht weg) oder *Keine*.
- **Großes Kapitel-Kopfstück** (sonst schlicht).
- **Programmsymbol** für Fenster, Taskleiste, Titelleiste und Startbild.
- **Darstellung zurücksetzen.**

## 13.3 Verhalten

- Beim Start die **Startseite** zeigen.
- Kapitel beim Weiterblättern (▶) als **gelesen** markieren.
- Kapitel als gelesen markieren, wenn du bis zum Ende **gescrollt** hast.
- Beim Speichern den **Buchnamen als Tag** vergeben.
- **Standard-Markierfarbe.**
- **Vorlesen: Geschwindigkeit.**
- **Vorlesen: Stimme.** Windows-Stimme oder natürliche Stimme (Piper, wird beim ersten Mal geladen).
- **Suchverlauf löschen.**

## 13.4 Fenster und Layouts

Jeden Bereich der Seitenleiste kannst du in ein **eigenes Fenster** auslagern (Knopf ⧉ neben den Reitern oder Strg+Umschalt+O) und frei verschieben, auch auf einen zweiten Monitor. Die Anordnung speicherst du als **Layout** (Knopf *Layouts* oben, zum Beispiel „Dashboard“) und lädst sie jederzeit wieder.

Weitere Optionen:

- Mit Windows starten (nur mit der fertig installierten Programmdatei, nicht bei `dotnet run`),
- Startzustand des Fensters: Normal, Maximiert, Minimiert,
- Symbol im Infobereich (System-Tray) anzeigen,
- Beim Schließen in den Infobereich minimieren (Beenden über Rechtsklick auf das Symbol),
- Fensterposition und -größe merken,
- Geöffnete Fenster (Dokumente, Bücher …) beim Start wiederherstellen.

## 13.5 Tastenkürzel

Klicke auf eine Kombination und drücke die neue Taste. **Entf** löscht eine Belegung, **Esc** bricht ab. *Alle auf Standard zurücksetzen* stellt die Ausgangsbelegung wieder her. Die Standardbelegung steht in [Kapitel 14](14-tastenkuerzel.md).

## 13.6 Vault

Ein **Vault** ist wie eine eigene Mappe für ein Studienprojekt: Markierungen, Notizen, Dokumente, Leseliste, To-dos und dein Lesefortschritt gehören immer zu genau einem Vault. Du kannst mehrere anlegen (zum Beispiel „Predigt“ und „Privat“) und hier wechseln. Jeder Vault ist eine einzelne Datei.

- **Anlegen** erzeugt einen neuen Vault.
- **Diesen Vault löschen …** löscht ihn endgültig mit allem, was darin ist. Vorher legt das Programm eine Sicherung an; danach lässt er sich nur noch daraus zurückholen.

## 13.7 Eigene Übersetzung importieren

*Datei wählen …* importiert eine Bibel aus einer Datei. Importierte Übersetzungen bleiben auf deinem Computer, nur für deinen privaten Gebrauch, und lassen sich unter „Importierte Übersetzungen“ wieder **entfernen**.

## 13.8 Offline

*Gewählte Übersetzung offline verfügbar machen* lädt sie komplett. Es werden nur Texte geladen, die die Quelle zur freien Nutzung anbietet; Lizenz und Namensnennung stehen in der Fußzeile. Die Daten liegen lokal und gehören dir.

## 13.9 Quellen und Lizenzen

Zeigt, **woher jede Information stammt** und was du damit tun darfst. Nichts davon ist im Programm eingebaut: Es wird erst geladen, wenn du es brauchst, und liegt dann im Zwischenspeicher. Knöpfe: *Liste kopieren*, *Zwischenspeicher öffnen*, *Hinweis zur Herkunft noch einmal lesen*.

## 13.10 Querverweise und Wörterbuch

- **Ausgeblendete Querverweise …:** die Liste dessen, was du ausgeblendet hast, mit Rückholen.
- **STEPBible-Kurzlexikon** unter den Strong-Einträgen anzeigen.

## 13.11 Plugins

Plugin-Ordner öffnen, **Neu einlesen**, Plugins erlauben, Anleitung für Autoren (siehe [Kapitel 12](12-plugins.md)).

## 13.12 Feedback und Windows-Suche

*Fehler melden …*, *Idee vorschlagen …*, *Feedback geben …* öffnen ein vorbereitetes **öffentliches Ticket** auf GitHub (dafür brauchst du dort ein Konto). Version und System stehen schon im Text, deine Daten nie, und es wird nichts gesendet, bevor du es dort selbst abschickst. *Ideen und Fehler ansehen …* zeigt alle Tickets. *Letzten Fehler anzeigen* zeigt, was das Programm zuletzt protokolliert hat.

*Im Startmenü eintragen (Windows-Suche)* sorgt dafür, dass Windows BibleStudy über seine Suche findet.

## 13.13 Sicherung und Austausch

- **Exportieren …** schreibt den aktiven Vault (Markierungen, Ordner, Tags, Notizen, Dokumente, Leseliste, To-dos, Lesefortschritt) und deine Einstellungen als ZIP. **Importieren …** legt daraus einen neuen Vault an.
- **Als Obsidian-Vault exportieren …** (siehe [Kapitel 6](06-dokumente-wissen.md)).
- **Automatische Sicherung** aller Daten (ZIP): Aus, Täglich, Wöchentlich, Monatlich; **Sicherungsordner wählen …**; **Jetzt sichern**.
- **Syncthing-Ordner nutzen …:** Zwischen mehreren Geräten legst du die Sicherungen in einen Syncthing-Ordner. **Synchronisiere nie den laufenden Datenordner selbst**, das kann Dateien beschädigen.

Mehr in [Kapitel 15](15-sicherung-datenschutz.md).

## 13.14 KI

Claude Code (mit Bibel-Werkzeugen) oder Ollama; die Anweisung für Claude; der MCP-Server (siehe [Kapitel 11](11-ki.md)).

## 13.15 Updates und Programm

- **Jetzt nach Updates suchen.**
- **Patch Notes** (was ist neu).
- **Projekt auf GitHub.**
- **Programm neu starten.**
- **Programm entfernen …** (fragt, ob deine Daten bleiben sollen).
