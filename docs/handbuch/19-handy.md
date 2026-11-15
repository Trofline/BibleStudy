# 19. Auf dem Handy (Android und iPhone)

BibleStudy läuft auch auf dem Handy, mit denselben Vaults, Markierungen, Notizen und Dokumenten wie am PC. Beide Handy-Versionen sind **Beta**: bitte meldet, was nicht funktioniert.

## 19.1 iPhone installieren (ohne bezahlten Apple-Account)

Das geht mit **SideStore**. Es signiert die App mit deiner eigenen, kostenlosen Apple-ID und erneuert sie danach von selbst. Du brauchst **einmalig** einen PC oder Mac mit Kabel. Insgesamt dauert es etwa 10 Minuten.

1. **SideStore aufs iPhone bringen (ca. 4 Min.).** Am PC das Programm *iloader* öffnen, das iPhone per Kabel anschließen und SideStore installieren (Anleitung auf docs.sidestore.io). Meldet SideStore, dass die Pairing-Datei fehlt: in iloader unten auf **Manage Pairing File** klicken und sie für SideStore einsetzen.
2. **Dem Profil vertrauen und den Entwicklermodus einschalten (ca. 2 Min.).** iPhone: Einstellungen → Allgemein → VPN und Geräteverwaltung → deiner Apple-ID vertrauen. Einstellungen → Datenschutz & Sicherheit → Entwicklermodus → einschalten (das iPhone startet neu).
3. **BibleStudy hinzufügen (ca. 2 Min.).** In SideStore anmelden, dann diesen Link auf dem iPhone antippen: `sidestore://source?url=https://trofline.github.io/BibleStudy/sidestore.json`. Oder in SideStore → Quellen → **+** → `https://trofline.github.io/BibleStudy/sidestore.json`. Dann BibleStudy → **Installieren**.
4. **Automatisch erneuern (ca. 2 Min.).** Eine kostenlose Apple-ID signiert Apps nur für 7 Tage. Mit einer Automation passiert das jede Nacht von selbst; Schritt für Schritt in **19.4**.

**Falls die App doch einmal abgelaufen ist:** SideStore öffnen und bei BibleStudy auf **Erneuern** tippen. Deine Daten bleiben erhalten.

**Updates** kommen über SideStore: BibleStudy dort aktualisieren.

### Grenzen, ehrlich gesagt

- Eine kostenlose Apple-ID erlaubt höchstens **3 selbst signierte Apps** gleichzeitig; SideStore zählt mit.
- Nach langem Ausschalten oder großen iOS-Updates kann einmal manuelles Erneuern nötig sein.
- Apple könnte diese Methode irgendwann einschränken.
- Die Live-Bibelstunde läuft nur, solange die App im Vordergrund ist.
- Claude Code, Ollama und der MCP-Server gibt es nur am PC.

## 19.2 Bedienung auf dem Handy

- **Antippen** wählt einen Vers, **langes Drücken** öffnet das Menü (am PC: Rechtsklick).
- Unter der Kopfzeile klappt ein **Pfeil** Reiter und Werkzeugleiste ein, damit mehr Platz für den Text bleibt.
- **Geteilte Ansicht** und mehrere Übersetzungen nebeneinander drehen das Handy automatisch quer (Android).
- **Exportieren** öffnet das Teilen-Menü: in Dateien sichern, per Messenger senden, in einer anderen App öffnen.

## 19.3 Geräte abgleichen (PC, Android, iPhone)

Einstellungen → **Geräte abgleichen**: Deine Vaults gleichen sich zwischen deinen eigenen Geräten ab, **ohne Konto und ohne Server**.

1. Auf dem ersten Gerät (am besten dem PC) **Abgleich einrichten**. Es zeigt einen **QR-Code** und einen Code mit 26 Zeichen.
2. Auf dem zweiten Gerät BibleStudy einmal öffnen, dann den QR-Code mit der **Kamera-App** scannen (BibleStudy öffnet sich und koppelt). Oder am PC **Code kopieren**, den Link zum Handy schicken und dort unter **Code von einem anderen Gerät** einfügen.
3. Sobald sich die Geräte erreichen, erscheint eine grüne Meldung **„Verbunden mit …“**. Ab dann musst du nie wieder koppeln.

**Wie es danach läuft:** Sind beide Apps im **selben WLAN** offen, gleichen sie sich von selbst ab: beim Start, wenn die App wieder nach vorne kommt, und wenige Sekunden nach jeder Änderung. Der PC schickt beim Schließen noch seine letzten Änderungen. **Jetzt abgleichen** startet es sofort.

**Es geht nichts verloren:** Hast du auf beiden Geräten getrennt markiert, haben danach beide alle Markierungen. Hat sich etwas nur auf einer Seite geändert, gilt diese Seite. Wurde dieselbe Notiz auf beiden Seiten verschieden geändert, bleiben beide Fassungen. Gelöschtes wandert mit, auf dem anderen Gerät in den Papierkorb. Ein neues oder leeres Gerät löscht nie etwas. Einstellungen und Materialien werden noch nicht abgeglichen.

**iPhone:** iOS lässt Apps im Hintergrund nicht arbeiten. Das iPhone gleicht deshalb nur ab, **während BibleStudy offen ist**. Bis dahin bleiben die Änderungen auf beiden Seiten einfach liegen. Gab es 3 Tage keinen Abgleich, erinnert die App daran. Am bequemsten ist die Automation unten.

Auf dem PC fragt die Windows-Firewall beim ersten Mal nach: nur **Private Netzwerke** erlauben. Auf dem iPhone fragt iOS nach der Erlaubnis für das **lokale Netzwerk**: erlauben (sonst Einstellungen → Datenschutz & Sicherheit → Lokales Netzwerk → BibleStudy).

## 19.4 iPhone-Automationen (Kurzbefehle)

Zwei Automationen, damit du an nichts denken musst. Beide legst du in der App **Kurzbefehle** an, unten im Reiter **Automation**.

**A) Abgleich, sobald du nach Hause kommst**

1. **Kurzbefehle → Automation → +** (oben rechts) → **WLAN**.
2. **Netzwerk** → dein WLAN zu Hause wählen, **Verbindet** ankreuzen.
3. **Sofort ausführen** wählen (nicht „Nach Bestätigung“), **Weiter**.
4. **Neue leere Automation** → Aktion **App öffnen** → **BibleStudy** wählen → **Fertig**.

Kommst du heim, öffnet sich BibleStudy kurz und gleicht mit dem PC ab (der PC muss dafür an sein und BibleStudy offen haben, auch minimiert im Infobereich).

**B) App jeden Tag erneuern (gegen die 7-Tage-Grenze)**

1. **Kurzbefehle → Kurzbefehle → +** und diese Aktionen in dieser Reihenfolge: das VPN von **StosVPN** (oder LocalDevVPN) **einschalten**, **Alle Apps erneuern** von **SideStore**, das VPN wieder **ausschalten**. Name: **BibleStudy erneuern**.
2. **Kurzbefehle → Automation → +** → **Tageszeit** → zum Beispiel **3:00**, **Täglich** → **Sofort ausführen** → **Weiter** → den Kurzbefehl **BibleStudy erneuern** wählen.
3. Das Handy nachts am Strom und im WLAN lassen.

Die genauen Namen der Aktionen hängen von der Version von SideStore und der VPN-App ab. Tippe in der Aktionssuche einfach „SideStore“ oder „VPN“ ein.
