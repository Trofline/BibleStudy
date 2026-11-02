# 15. Vaults, Sicherung und Datenschutz

## 15.1 Was ein Vault ist

Ein **Vault** ist eine einzelne Datei (SQLite) mit allem Persönlichen eines Studienprojekts:

- Markierungen (mit Ordnern, Tags, Kategorien),
- Notizen und Lesezeichen,
- Dokumente (auch Predigten),
- Leseliste, To-dos, Gebete,
- Lesefortschritt, Auswendig-Verse, eigene Querverweise.

Du kannst mehrere Vaults führen, zum Beispiel „Predigt“ und „Privat“, und über den Vault-Knopf oben wechseln. Das ist der beste Weg, Dinge zu trennen.

## 15.2 Sicherung

### Automatisch

*Einstellungen → Sicherung*: **Täglich, Wöchentlich oder Monatlich**; Ziel ist ein Ordner deiner Wahl. Vor jedem Update und vor dem Löschen eines Vaults legt das Programm zusätzlich selbst eine Sicherung an.

### Manuell

- **Jetzt sichern** legt eine ZIP mit allen Daten an.
- **Exportieren …** schreibt nur den aktiven Vault samt Einstellungen als ZIP.

### Zurückholen

**Importieren …** legt aus einer Export-ZIP einen **neuen Vault** an; dein bestehender bleibt unberührt.

### Mehrere Geräte

Lege die Sicherungen in einen **Syncthing-Ordner**. **Synchronisiere nie den laufenden Datenordner**, das kann Dateien beschädigen.

## 15.3 Sicherheit der Speicherung

Das Programm schreibt Dateien **atomar**: Erst entsteht eine temporäre Datei, die dann an die Stelle der alten rückt. Ein Absturz mitten im Speichern lässt so keine halbe Datei zurück. Sind die Einstellungen einmal unlesbar oder gesperrt, bleibt die alte Datei erhalten statt überschrieben zu werden.

## 15.4 Datenschutz

- **Kein Konto, keine Anmeldung, keine Telemetrie.** Das Programm fragt nur bei GitHub nach Updates.
- Das Programm enthält keine Bibeltexte; es lädt sie von ihren Originalquellen, wenn du sie brauchst.
- Deine Notizen, Dokumente und Markierungen verlassen den Computer nicht, außer du sendest sie bewusst (Zusammen studieren, Export).
- **KI:** Mit Claude Code geht deine Frage an Anthropic (über deine eigene Anmeldung). Mit **Ollama** bleibt alles lokal.
- **Feedback:** Ein Ticket ist öffentlich auf GitHub. Es wird nichts gesendet, bevor du es dort selbst abschickst.
- **Zusammen studieren:** Die andere Person sieht deine Internetadresse. Übertragen wird nur, was ihr sendet.

## 15.5 Deinstallieren

Beim Entfernen fragt das Programm immer, ob **deine Daten bleiben** sollen. Ohne Haken bleiben Vaults und Einstellungen erhalten.

## 15.6 Haftung

BibleStudy ist freie Software, die **ohne Gewährleistung** bereitgestellt wird (siehe MIT-Lizenz). Inhalte Dritter (Übersetzungen, Kommentare, Daten), eigene Importe und alles, was du teilst, liegen in deiner Verantwortung.
