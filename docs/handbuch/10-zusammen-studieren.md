# 10. Zusammen studieren

Mit einer anderen Person **direkt verbunden** studieren: Chat, Verse und Dokumente teilen, die Bibelstelle mitlesen. Die Verbindung ist **verschlüsselt, braucht keinen Server, kein Konto und ist kostenlos** (Technik: WebRTC).

## 10.1 Verbindung aufbauen

Rechte Leiste → **Zusammen → Zusammen studieren**.

1. Person A wählt **Einladung erstellen**. Es erscheint ein Code.
2. Person A schickt den Code per Messenger an Person B.
3. Person B wählt **Einladung annehmen**, fügt den Code ein und bekommt einen **Antwortcode**.
4. Person B schickt den Antwortcode zurück; Person A fügt ihn ein.
5. Jetzt seid ihr direkt und verschlüsselt verbunden.

## 10.2 Was ihr tun könnt

- **Chatten.**
- **Verse teilen.**
- **Dokumente teilen** (als Kopie; Gesendetes ist in der Größe begrenzt, lange Dokumente werden gekürzt).
- **Die eigene Bibelstelle mitsenden** und **der Stelle der anderen Person folgen.**
- Markierungen mitsehen (Zusammen-Markierungen).

## 10.3 Datenschutz

- Übertragen wird nur, was ihr sendet.
- Die andere Person sieht **deine Internetadresse** (das liegt an der direkten Verbindung).
- Texte von Plugin-Übersetzungen werden **nie** an andere gesendet.
- Vor dem ersten Gebrauch fragt das Programm um Einverständnis.

## 10.4 Wenn es nicht klappt

- Beide müssen **gleichzeitig online** sein.
- Die Verbindung geht **direkt von Computer zu Computer**, ohne Server dazwischen. Das klappt zuverlässig, wenn ihr **im selben Netzwerk** seid (zum Beispiel im selben WLAN). Über verschiedene Netzwerke hinweg klappt es nur, wenn eure Anschlüsse es zulassen. Bei Mobilfunk, Firmen-, Schul- und Hotelnetzen sowie bei manchen Providern (CGNAT) scheitert es oft.
- Ein **VPN** oder eine **strenge Firewall** kann die Verbindung verhindern. Unter Windows beim ersten Start „Zugriff zulassen“ wählen; am Mac die Freigabe „Lokales Netzwerk“ erteilen.
- Die Codes sind lang; kopiere sie vollständig und ohne Leerzeichen am Rand.
- Versucht es mit einer neuen Einladung.

### Wenn ihr nicht im selben Netzwerk seid

Es gibt zwei Wege, die nicht in BibleStudy eingebaut sind:

- **Ein privates Netzwerk wie ZeroTier oder Tailscale** (beide haben ein kostenloses Angebot für Privatpersonen, brauchen aber ein Konto). Beide installieren das Programm und treten demselben Netzwerk bei. Danach verhalten sich die Computer, als wären sie im selben WLAN. Erstellt dann neue Codes. Diese Programme sind eigenständige Produkte mit eigenen Lizenzen und Bedingungen; BibleStudy liefert sie nicht mit und ist nicht damit verbunden. Wir haben die Verbindung darüber noch nicht getestet.
- **Ein eigener Relay-Server (TURN)**, der den Verkehr weiterleitet. Das ist für Fortgeschrittene und kostet in der Regel Geld oder eigenen Aufwand. Die Daten bleiben dabei verschlüsselt. In BibleStudy trägst du ihn im Reiter *Zusammen studieren* unter **Eigener Relay-Server (optional)** ein: Adresse (zum Beispiel `turn:server.example:3478`), Benutzername und Passwort. Am besten tragen beide dasselbe ein, und ihr erstellt danach neue Codes. Das Passwort wird unverschlüsselt in den Einstellungen gespeichert. Mit einem Relay-Server haben wir die Verbindung noch nicht getestet.

## 10.5 Strategien für Gruppen

- **Hauskreis online:** Eine Person liest den Text vor und teilt ihre Stelle; die andere folgt.
- **Zu zweit lernen:** Gemeinsam ein Dokument nach der Vorlage *Abschnittsstudie* ausfüllen und die Kopien vergleichen.
- **Mentoring:** Fragen als Dokument senden, Antworten als Dokument zurück.
