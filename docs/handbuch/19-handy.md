# 19. Auf dem Handy (Android und iPhone)

BibleStudy läuft auch auf dem Handy, mit denselben Vaults, Markierungen, Notizen und Dokumenten wie am PC. Beide Handy-Versionen sind **Beta**: bitte meldet, was nicht funktioniert.

## 19.1 iPhone installieren (ohne bezahlten Apple-Account)

Das geht mit **SideStore**. Es signiert die App mit deiner eigenen, kostenlosen Apple-ID und erneuert sie danach von selbst. Du brauchst **einmalig** einen Computer (Windows oder Mac) und ein Kabel. Insgesamt dauert es etwa 15 Minuten.

> **Keine Sorge:** Der **Entwicklermodus** ist ein offizieller Schalter von Apple. Er erlaubt nur, dass Apps laufen, die du selbst installiert hast. Code-Sperre, Face ID, „Wo ist?“ und iOS-Updates bleiben an, das iPhone wird nicht „gejailbreakt“, und du kannst ihn jederzeit wieder ausschalten. „Der eigenen Apple-ID vertrauen“ heißt, du vertraust dir selbst. LocalDevVPN läuft nur auf dem iPhone und schickt keine Daten ins Internet.

1. **SideStore aufs iPhone bringen (ca. 4 Min.).** Unter Windows *Apple-Geräte* (Microsoft Store) oder iTunes installieren (am Mac nicht nötig), dann das Programm *iloader* öffnen und das iPhone per Kabel anschließen. Fragt das iPhone **„Diesem Computer vertrauen?“**: **Vertrauen** tippen und den Code eingeben. In iloader mit der Apple-ID anmelden, in der Liste etwas nach unten scrollen und **SideStore** in der **normalen Version** (nicht Nightly oder Beta) installieren. Meldet SideStore später, dass die Pairing-Datei fehlt: in iloader unten auf **Manage Pairing File** klicken und sie für SideStore einsetzen.
2. **Der eigenen Apple-ID vertrauen (1 Min.).** Sonst startet SideStore nicht („Nicht vertrauenswürdiger Entwickler“). **Einstellungen → Allgemein → VPN und Geräteverwaltung** → unter *Entwickler-App* deine Apple-ID (E-Mail-Adresse) antippen → **„… vertrauen“** → **Vertrauen**.
3. **Entwicklermodus einschalten (2 Min.).** Der Schalter erscheint erst, nachdem SideStore installiert ist.
   1. **Einstellungen → Datenschutz & Sicherheit** → ganz nach unten → **Entwicklermodus** → Schalter ein.
   2. **Neustart** tippen.
   3. Nach dem Neustart erscheint **„Entwicklermodus aktivieren?“**: **Aktivieren** und den iPhone-Code eingeben.

   Fehlt der Punkt: SideStore einmal antippen, dann erscheint er. Sonst war Schritt 1 nicht fertig.
4. **LocalDevVPN installieren (1 Min.).** Aus dem App Store laden, einmal öffnen und die VPN-Konfiguration **erlauben**. Es ist ein VPN auf dem Handy selbst, über das SideStore mit dem Handy spricht; es schickt keine Daten irgendwohin.
5. **BibleStudy installieren (2 Min.).** In SideStore anmelden, dann diesen Link auf dem iPhone antippen: `sidestore://source?url=https://trofline.github.io/BibleStudy/sidestore.json`. Oder in SideStore → **Sources → +** → `https://trofline.github.io/BibleStudy/sidestore.json`. Dann BibleStudy → **Installieren**. Die `.ipa`-Datei musst du nicht selbst laden, SideStore holt sie über die Quelle aus dem neuesten Release. Nur falls die Quelle nicht klappt: die `.ipa` aus dem Release laden und in SideStore unter **My Apps** → **+** wählen.
   **Anmeldung in SideStore klappt nicht?** Das ist im Moment ein bekannter Fehler von SideStore. In SideStore → **Settings** → bei **User Customization** den Schalter **On-Device Anisette** ausschalten → **Anisette Client Configuration** → **Raw JSON**. Im Textfeld **nichts löschen und nichts anderes ändern**, nur `macos;` durch `Mac OS X;` ersetzen. Speichern und neu anmelden.
6. **Den fertigen Kurzbefehl übernehmen (3 Min.).** Er schaltet das VPN für SideStore an und aus und signiert BibleStudy alle 6 Tage neu. Siehe **19.4**, auch für die kurze Prüfung danach.

**Falls die App doch einmal abgelaufen ist:** SideStore öffnen und bei BibleStudy auf **Erneuern** tippen. Deine Daten bleiben erhalten.

**Updates:** BibleStudy sagt dir Bescheid, wenn eine neue Version da ist. Das Update selbst machst du von Hand in SideStore: SideStore öffnen → **Sources** nach unten ziehen → **My Apps** → bei BibleStudy **Update**.

### Grenzen, ehrlich gesagt

- Eine kostenlose Apple-ID erlaubt höchstens **3 selbst signierte Apps** gleichzeitig; SideStore zählt mit.
- Nach langem Ausschalten oder großen iOS-Updates kann einmal manuelles Erneuern nötig sein.
- Apple könnte diese Methode irgendwann einschränken.
- Die Live-Bibelstunde läuft nur, solange die App im Vordergrund ist.
- Claude Code, Ollama und der MCP-Server gibt es nur am PC.

## 19.2 Bedienung auf dem Handy

- **Antippen** wählt einen Vers, **langes Drücken** öffnet das Menü (am PC: Rechtsklick).
- **Mehrere ganze Verse:** Finger auf eine Versnummer halten und nach unten ziehen; beim Loslassen erscheinen die Farben für alle gewählten Verse.
- **Mehrere Einträge im Explorer:** lange auf einen Eintrag drücken (oder **Auswählen**), die anderen antippen und dann über die Leiste unten für alle etwas tun.
- Unter der Kopfzeile klappt ein **Pfeil** Reiter und Werkzeugleiste ein, damit mehr Platz für den Text bleibt.
- **Geteilte Ansicht** und mehrere Übersetzungen nebeneinander drehen das Handy automatisch quer (Android).
- **Exportieren** öffnet das Teilen-Menü: in Dateien sichern, per Messenger senden, in einer anderen App öffnen.

## 19.3 Geräte synchronisieren (PC, Android, iPhone)

Einstellungen → **Geräte synchronisieren**: Deine Vaults synchronisieren sich zwischen deinen eigenen Geräten, **ohne Konto und ohne Server**.

1. Auf dem ersten Gerät (am besten dem PC) **Synchronisation einrichten**. Es zeigt einen **QR-Code** und einen Code mit 26 Zeichen.
2. Auf dem zweiten Gerät BibleStudy einmal öffnen, dann den QR-Code mit der **Kamera-App** scannen (BibleStudy öffnet sich und koppelt). Oder am PC **Code kopieren**, den Link zum Handy schicken und dort unter **Code von einem anderen Gerät** einfügen.
3. Sobald sich die Geräte erreichen, erscheint unten eine kleine grüne Meldung **„Verbunden mit …“**, und sobald alles übertragen ist **„Synchronisiert mit …“**: Dann kannst du die App auf dem Handy schließen. Ab dann musst du nie wieder koppeln. Die Meldungen verschwinden von selbst; antippen oder zur Seite wischen schließt sie sofort.

**Wie es danach läuft:** Sind beide Apps im **selben WLAN** offen, synchronisieren sie sich von selbst: beim Start, wenn die App wieder nach vorne kommt, wenige Sekunden nach jeder Änderung und sonst alle 20 Sekunden. Der PC schickt beim Schließen noch seine letzten Änderungen, aber nur an ein Handy, auf dem die App gerade offen ist. **Deshalb: Bevor du am PC aufhörst, öffne kurz BibleStudy auf dem Handy** und warte auf „Synchronisiert mit …“. Sofort geht es mit **Synchronisieren**: am PC unten neben der Versionsnummer, am Handy oben im Menü **Werkzeuge**.

**Es geht nichts verloren:** Hast du auf beiden Geräten getrennt markiert, haben danach beide alle Markierungen. Hat sich etwas nur auf einer Seite geändert, gilt diese Seite. Wurde dieselbe Notiz auf beiden Seiten verschieden geändert, bleiben beide Fassungen. Gelöschtes wandert mit, auf dem anderen Gerät in den Papierkorb. Ein neues oder leeres Gerät löscht nie etwas. Synchronisiert werden Markierungen, Notizen, Dokumente, Merkliste, Aufgaben, Gebetsanliegen, Lesefortschritt, Leseplan, Auswendiglernen, Vokabeln sowie Kategorien, Farben und gespeicherte Suchen. Was nur zu einem Gerät gehört (Fenstergröße, Schriftgröße, Design), bleibt auf dem Gerät. Eigene Übersetzungen und Materialien werden noch nicht synchronisiert.

**iPhone:** iOS lässt Apps im Hintergrund nicht arbeiten. Das iPhone synchronisiert deshalb nur, **während BibleStudy offen ist**. Bis dahin bleiben die Änderungen auf beiden Seiten einfach liegen. Gab es 3 Tage keine Synchronisation, erinnert die App daran. Am bequemsten ist die Automation unten.

Auf dem PC fragt die Windows-Firewall beim ersten Mal nach: nur **Private Netzwerke** erlauben. Auf dem iPhone fragt iOS nach der Erlaubnis für das **lokale Netzwerk**: erlauben (sonst Einstellungen → Datenschutz & Sicherheit → Lokales Netzwerk → BibleStudy).

## 19.4 Der fertige Kurzbefehl (iPhone)

Selbst bauen musst du nichts. Ein fertiger Kurzbefehl erledigt alles: Das VPN geht an, sobald SideStore öffnet, und wieder aus, wenn es schließt. Alle 6 Tage signiert das iPhone BibleStudy von selbst neu, damit die 7-Tage-Grenze nie greift.

1. Auf dem iPhone in Safari öffnen: `https://trofline.github.io/BibleStudy/BibleStudy.shortcut` → **Laden**.
2. Oben rechts den Download-Pfeil antippen → die Datei antippen. Sie öffnet sich in **Kurzbefehle**.
3. **Kurzbefehl hinzufügen**.

Fragt iOS beim ersten Lauf, ob der Kurzbefehl das VPN nutzen darf: **Immer erlauben**. Voraussetzung ist **LocalDevVPN** (19.1, Schritt 4).

**Danach einmal prüfen** (Kurzbefehle → BibleStudy lange drücken → **Bearbeiten**):

1. **App neu auswählen:** in den Aktionen (und im ersten Auslöser) auf **SideStore** tippen → nach SideStore suchen → antippen. Nach dem Laden weiß das iPhone nicht sicher, welche App gemeint ist.
2. **Erster Auslöser** „Wenn SideStore geöffnet oder geschlossen ist“: mit dem Pfeil ⌄ aufklappen → **Automation** ein.
3. **Zweiter Auslöser** „Um 18:00“: aufklappen → Ereignis **Tageszeit**, Wiederholen **Alle 6 Tage** → **Automation** ein.
4. **Fehlt ein Auslöser**, oben einen neuen hinzufügen: **App** → **SideStore** → *Geöffnet* und *Geschlossen*; oder **Tageszeit** → 18:00 (eine Zeit, zu der das iPhone meist an ist) → **Alle 6 Tage**. Dann aufklappen und **Automation** einschalten.

Am Ende stehen beide Auslöser mit **Automation** an (Bild in der README).

**Freiwillig, synchronisieren beim Heimkommen:** **Kurzbefehle → Automation → +** → **WLAN** → dein WLAN wählen, **Verbindet** ankreuzen → **Sofort ausführen** → Aktion **App öffnen** → **BibleStudy**. Die App öffnet sich kurz und synchronisiert mit dem PC (19.3).

**Updates:** iOS erlaubt keiner App, eine andere zu installieren, auch BibleStudy nicht. BibleStudy sagt dir deshalb nur Bescheid, wenn eine neue Version da ist; das Update machst du von Hand in SideStore:

1. SideStore selbst öffnen (der Knopf in der Meldung kann es auch öffnen).
2. Unten auf **Sources** tippen und die Liste einmal nach unten ziehen.
3. Unten auf **My Apps** tippen und bei BibleStudy auf **Update** tippen.

Steht noch kein Update da, ein paar Minuten warten und in **Sources** noch einmal nach unten ziehen. Ist eine App trotzdem abgelaufen: SideStore öffnen und bei BibleStudy **Erneuern** tippen; deine Daten bleiben.
