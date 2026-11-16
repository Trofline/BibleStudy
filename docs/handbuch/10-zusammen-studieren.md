# 10. Zusammen studieren

Mit einer anderen Person **direkt verbunden** studieren: Chat, Verse und Dokumente teilen, die Bibelstelle mitlesen. Die Verbindung ist **verschlüsselt, braucht keinen Server, kein Konto und ist kostenlos** (Technik: im selben Netzwerk eine direkte, mit ECDH und AES-GCM verschlüsselte Verbindung; über verschiedene Netzwerke WebRTC).

## 10.1 Verbindung aufbauen

Rechte Leiste → **Zusammen → Zusammen studieren**. Gib zuerst deinen Namen ein.

### Im selben Netzwerk (ohne Codes)

Seid ihr im selben WLAN oder im selben ZeroTier- oder Tailscale-Netzwerk, braucht ihr keine Codes:

1. Beide öffnen die Seite **Zusammen studieren** und geben ihren Namen ein.
2. Unter **Im selben Netzwerk** erscheint nach ein paar Sekunden der Name der anderen Person. Tippe auf **Mit … verbinden**.
3. Die andere Person sieht die Anfrage und tippt auf **Annehmen**.
4. Beide sehen eine **Prüfzahl**. Ist sie bei euch gleich, sitzt niemand dazwischen.

Erscheint niemand, lassen manche Netzwerke das Suchen nicht zu. Dann öffne **Mit Adresse verbinden** und gib die Adresse der anderen Person ein. Die eigene Adresse steht direkt darunter; bei ZeroTier steht sie auch in der ZeroTier-App unter „Managed IPs“.

### In verschiedenen Netzwerken (mit Codes)

Unter **Mit Codes (andere Netze)**:

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

- **Ein privates Netzwerk wie ZeroTier oder Tailscale** (beide haben ein kostenloses Angebot für Privatpersonen, brauchen aber ein Konto). Beide installieren das Programm und treten demselben Netzwerk bei. Danach verhalten sich die Computer, als wären sie im selben WLAN, und ihr verbindet euch wie oben unter *Im selben Netzwerk*, ohne Codes. Diese Programme sind eigenständige Produkte mit eigenen Lizenzen und Bedingungen; BibleStudy liefert sie nicht mit und ist nicht damit verbunden.
- **Ein eigener Relay-Server (TURN)**, der den Verkehr weiterleitet. Das ist für Fortgeschrittene und kostet in der Regel Geld oder eigenen Aufwand. Die Daten bleiben dabei verschlüsselt. In BibleStudy trägst du ihn im Reiter *Zusammen studieren* unter **Eigener Relay-Server (optional)** ein: Adresse (zum Beispiel `turn:server.example:3478`), Benutzername und Passwort. Am besten tragen beide dasselbe ein, und ihr erstellt danach neue Codes. Das Passwort wird unverschlüsselt in den Einstellungen gespeichert. Mit einem Relay-Server haben wir die Verbindung noch nicht getestet.

## 10.5 Strategien für Gruppen

- **Hauskreis online:** Eine Person liest den Text vor und teilt ihre Stelle; die andere folgt.
- **Zu zweit lernen:** Gemeinsam ein Dokument nach der Vorlage *Abschnittsstudie* ausfüllen und die Kopien vergleichen.
- **Mentoring:** Fragen als Dokument senden, Antworten als Dokument zurück.
