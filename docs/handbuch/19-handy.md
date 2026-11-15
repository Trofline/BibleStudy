# 19. Auf dem Handy (Android und iPhone)

BibleStudy läuft auch auf dem Handy, mit denselben Vaults, Markierungen, Notizen und Dokumenten wie am PC. Beide Handy-Versionen sind **Beta**: bitte meldet, was nicht funktioniert.

## 19.1 iPhone installieren (ohne bezahlten Apple-Account)

Das geht mit **SideStore**. Es signiert die App mit deiner eigenen, kostenlosen Apple-ID und erneuert sie danach von selbst. Du brauchst **einmalig** einen PC oder Mac mit Kabel. Insgesamt dauert es etwa 10 Minuten.

1. **SideStore aufs iPhone bringen (ca. 4 Min.).** Am PC das Programm *iloader* öffnen, das iPhone per Kabel anschließen und SideStore installieren (Anleitung auf docs.sidestore.io).
2. **Dem Profil vertrauen und den Entwicklermodus einschalten (ca. 2 Min.).** iPhone: Einstellungen → Allgemein → VPN und Geräteverwaltung → deiner Apple-ID vertrauen. Einstellungen → Datenschutz & Sicherheit → Entwicklermodus → einschalten (das iPhone startet neu).
3. **BibleStudy hinzufügen (ca. 2 Min.).** In SideStore anmelden, dann diesen Link auf dem iPhone antippen: `sidestore://source?url=https://trofline.github.io/BibleStudy/sidestore.json`. Oder in SideStore → Quellen → **+** → `https://trofline.github.io/BibleStudy/sidestore.json`. Dann BibleStudy → **Installieren**.
4. **Automatisch erneuern (ca. 1 Min.).** Eine kostenlose Apple-ID signiert Apps nur für 7 Tage. Mit dem Kurzbefehl **„BibleStudy erneuern“** passiert das von selbst: Kurzbefehl laden, dann Kurzbefehle → Automation → Tageszeit (zum Beispiel täglich 3:00) → **Sofort ausführen** → den Kurzbefehl wählen.

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

1. Auf dem ersten Gerät **Abgleich einrichten**. Es zeigt einen **QR-Code** und einen Code mit 26 Zeichen.
2. Auf dem zweiten Gerät den QR-Code mit der **Kamera-App** scannen (BibleStudy öffnet sich und koppelt) oder den Code unter **Code von einem anderen Gerät** eintippen.
3. Sind beide Geräte im **selben WLAN**, finden sie sich von selbst und gleichen beim Start und danach alle paar Minuten ab. **Jetzt abgleichen** startet es sofort.

Die Verbindung ist verschlüsselt; ohne den Code kann kein anderes Gerät mitmachen. Es wird nichts überschrieben und nichts gelöscht: Bei Unterschieden bleiben beide Fassungen. **Gelöschtes wandert nicht mit** (es bleibt auf dem anderen Gerät), Einstellungen und Materialien werden noch nicht abgeglichen.

Auf dem PC fragt die Windows-Firewall beim ersten Mal nach: nur **Private Netzwerke** erlauben. Auf dem iPhone fragt iOS nach der Erlaubnis für das **lokale Netzwerk**: erlauben.
