# 12. Plugins

Ein Plugin ist ein kleines Programm, das BibleStudy **erweitert**. Es kann Übersetzungen liefern, Befehle für das Suchfeld (Strg+K) mitbringen und über die lokale Schnittstelle mit dem Programm arbeiten.

> **Wichtig:** Ein Plugin läuft **mit deinen Rechten** auf deinem Computer und kann **deine Daten in BibleStudy lesen und ändern**. Erlaube nur Plugins von Menschen, denen du vertraust. Das Projekt prüft Plugins nicht und übernimmt keine Verantwortung für sie oder die Texte, die sie liefern.

## 12.1 Plugin installieren

1. *Einstellungen → Plugins → Plugin-Ordner öffnen*.
2. Den Ordner des Plugins (mit einer `plugin.json`) dort hineinlegen.
3. **Neu einlesen** klicken, lesen, was das Plugin über sich sagt, und **Dieses Plugin erlauben** ankreuzen.
4. Die Übersetzungen des Plugins erscheinen in der Übersetzungsliste wie alle anderen.

Ändert sich die `plugin.json` eines erlaubten Plugins, fragt das Programm erneut um Erlaubnis.

## 12.2 Beiliegende Plugins

Im Ordner `plugins` des Projekts und in jedem Release liegen fertige Plugins, zum Beispiel **Strong-Nummern ergänzen**. Es fügt Übersetzungen ohne Strong-Nummern eine **geschätzte** Nummerierung hinzu. Ohne Gewähr; bitte prüfen.

## 12.3 Plugin-Texte

Texte von Plugin-Übersetzungen liegen nur im Arbeitsspeicher, werden nie auf die Festplatte geschrieben und nie an andere gesendet (auch nicht in „Zusammen studieren“).

## 12.4 Eigene Plugins schreiben

Die Anleitung steht in [PLUGINS.md](../../PLUGINS.md) (auch im Programm unter *Einstellungen → Anleitung für Plugin-Autoren*). Kurz:

- Ein Plugin ist ein Ordner mit einer `plugin.json` (id, Name, Version, Autor, Beschreibung, **contentNotice**, `provides`, `command`, `args`).
- Anfragen und Antworten sind **je eine Zeile JSON** über Standardein- und -ausgabe. Eine Anfrage, die länger als 30 Sekunden dauert, wird abgebrochen und das Plugin neu gestartet.
- Methode `translations` ist Pflicht und liefert die Übersetzungen.
- Beispiele liegen im Ordner `examples`.

Bitte schreibe keine Plugins, die Nutzungsbedingungen einer Webseite verletzen oder ein Urheberrecht ignorieren.
