# 17. Fehlersuche und häufige Fragen

## 17.1 Probleme beim Start

**Windows warnt vor dem Installer (SmartScreen).** Der Installer ist nicht signiert, weil das für kostenlose Software ohne Budget nicht vorgesehen ist. Vergleiche die Prüfsumme der Datei mit `SHA256SUMS.txt` aus dem Release. Bei „Weitere Informationen → Trotzdem ausführen“ startet er.

**Linux/macOS startet nicht.** Die Pakete sind unter Windows gebaut und kaum getestet. Lies `README-Linux.txt` bzw. `README-macOS.txt` im Paket; auf dem Mac startest du `Start-BibleStudy.command`. Bitte melde Probleme als Ticket (Feedback-Knopf).

**Das Programm zeigt nur ein weißes Fenster oder stürzt ab.** *Einstellungen → Feedback → Letzten Fehler anzeigen* nennt, was protokolliert wurde. Hänge das einem Ticket an.

## 17.2 Texte fehlen oder laden nicht

**Eine Übersetzung lädt nicht.** Du brauchst beim ersten Öffnen eine Internetverbindung. Danach liegt sie im Zwischenspeicher. Zum Arbeiten ohne Netz: *Offline verfügbar machen*.

**Keine Strong-Nummern.** Nur manche Übersetzungen bringen sie mit (zum Beispiel KJV). Wähle eine davon oder nutze das Plugin *Strong-Nummern ergänzen* (geschätzt, ohne Gewähr).

**Wörterbuch nicht verfügbar.** Die Wörterbücher werden bei Bedarf geladen; ohne Verbindung erscheint dort eine Meldung mit dem Grund.

## 17.3 Bedienung

**Beim Rechtsklick erscheint ein „Kopieren Strg+C“-Knopf statt des Menüs.** Das war ein Fehler früherer Versionen und ist behoben. Das Vers-Menü bleibt jetzt oben, *Markierten Text kopieren* steht in ihm.

**„Wort erklären“ ist grau.** Markiere erst ein einzelnes Wort (oder zwei) im Text, dann Rechtsklick.

**Ein Kürzel tut nichts.** Prüfe in den Einstellungen, ob es anders belegt wurde. Strg+B im Editor macht Text fett, nicht die Leiste.

**Meine Parallel-Spalten haben zu ähnliche Farben.** Ändere sie über den Farb-Knopf in der Parallel-Leiste ([Kapitel 2.5](02-lesen.md)).

**Die Leiste ist zu voll.** Wechsle auf *Easy* oder lagere selten gebrauchte Ansichten in eigene Fenster aus.

## 17.4 Daten

**Ich habe etwas gelöscht.** Suche in den automatischen Sicherungen (*Einstellungen → Sicherung*) und importiere die ZIP als neuen Vault.

**Ich möchte Daten auf einen anderen Computer mitnehmen.** *Exportieren …* auf dem alten, *Importieren …* auf dem neuen Gerät; oder kopiere den Ordner `Data` der portablen Version.

**Wo sind meine Daten?** Siehe [Kapitel 1.3](01-erste-schritte.md).

## 17.5 Zusammen studieren

Siehe [Kapitel 10.4](10-zusammen-studieren.md).

## 17.6 Plugins

**Das Plugin erscheint nicht.** Ordner liegt falsch oder `plugin.json` fehlt: *Plugin-Ordner öffnen*, Ordner prüfen, *Neu einlesen*.

**Das Plugin fragt erneut um Erlaubnis.** Das ist Absicht: Hat sich seine `plugin.json` geändert, musst du es neu erlauben.

## 17.7 Fragen und Antworten

**Ist das Programm wirklich kostenlos?** Ja, MIT-Lizenz, ohne Werbung und ohne Konto.

**Wer steckt dahinter?** Ein einzelner Entwickler; Beiträge sind willkommen (Pull Request, mit Zustimmung zur MIT-Lizenz). Bitte nur Daten mit klarer, freier Lizenz hinzufügen.

**Warum fehlen moderne deutsche Übersetzungen wie Luther 2017, Elberfelder oder Schlachter?** Sie sind urheberrechtlich geschützt. Das Programm lädt nur Texte, die ihre Quelle zur freien Nutzung anbietet. Wenn du eine Datei besitzt, kannst du sie für den privaten Gebrauch selbst importieren (siehe [Kapitel 13.7](13-einstellungen.md)).

**Kann ich mehrere Fenster nebeneinander nutzen?** Ja: Ansichten aus der Leiste ziehen, auch auf einen zweiten Monitor. Als Layout speichern.

**Wo melde ich einen Fehler?** Feedback-Knopf oben, oder direkt auf GitHub als Ticket.

**Wo sehe ich, was neu ist?** Auf die Versionsnummer in der Fußzeile klicken (Patch Notes).
