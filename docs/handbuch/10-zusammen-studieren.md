# 10. Zusammen studieren

Mit anderen Personen **verbunden** studieren: Chat, Verse und Dokumente teilen, die Bibelstelle mitlesen. Die Verbindung ist **verschlüsselt, braucht kein Konto und ist kostenlos** (Technik: im selben Netzwerk eine direkte, mit ECDH und AES-GCM verschlüsselte Verbindung ohne Server; über verschiedene Netzwerke ein Raum auf einem kleinen Vermittler-Server, Ende-zu-Ende verschlüsselt).

## 10.1 Verbindung aufbauen

Rechte Leiste → **Zusammen → Zusammen studieren**. Gib zuerst deinen Namen ein. Am einfachsten ist ein **Raum mit Raum-Code**: Das klappt von überall, egal in welchem Netzwerk ihr seid.

### Über das Internet mit Raum-Code (empfohlen)

Egal wo ihr seid (zum Beispiel jeder bei sich zu Hause, oder einer im Mobilfunk): Ihr trefft euch in einem **Raum**. Bis zu **fünf Personen** passen in einen Raum.

Unter **Über das Internet (Raum-Code)**:

1. Eine Person wählt **Raum erstellen**. Es erscheint ein kurzer **Raum-Code** wie `K7F2-QXM9`.
2. Sie schickt den Code per Messenger an die anderen (oder liest ihn am Telefon vor).
3. Die anderen wählen **Raum beitreten**, tippen den Code ein und tippen auf **Beitreten**.
4. Nach ein, zwei Sekunden seid ihr verbunden. Der Raum-Code bleibt oben auf der Seite stehen, damit noch jemand dazukommen kann.

Alle sehen eine **Prüfzahl**. Ist sie bei allen gleich, sitzt niemand dazwischen.

**Wie das funktioniert:** Die Programme verbinden sich nicht direkt miteinander (das scheitert bei vielen Anschlüssen), sondern jedes für sich mit einem kleinen **Vermittler-Server** im Internet, so wie ein Browser eine Webseite öffnet. Der Vermittler reicht die Nachrichten nur weiter. Alles ist **Ende-zu-Ende verschlüsselt**: Der Schlüssel entsteht auf euren Geräten aus dem Raum-Code; der Vermittler kennt den Code nicht (nur eine Prüfsumme davon als Raumname) und kann nichts lesen. Er speichert nichts. Ihr braucht kein Konto und müsst nichts einrichten.

**Tageslimit:** Der Vermittler läuft im kostenlosen Tarif und hat deshalb ein Tageslimit für alle Nutzer zusammen. Ist es aufgebraucht, verbindet euch bis zum nächsten Tag **im selben Netzwerk** (nächster Abschnitt). Seid ihr an verschiedenen Orten, tretet alle demselben kostenlosen **ZeroTier**-Netzwerk bei (zerotier.com: eine Person erstellt ein Netzwerk, die anderen treten mit dessen Netzwerk-ID bei). Danach seid ihr für BibleStudy „im selben Netzwerk“ und verbindet euch ganz ohne Vermittler.

### Im selben Netzwerk (ohne Codes)

Seid ihr im selben WLAN oder im selben ZeroTier- oder Tailscale-Netzwerk, braucht ihr keine Codes:

1. Beide öffnen die Seite **Zusammen studieren** und geben ihren Namen ein.
2. Unter **Im selben Netzwerk** erscheint nach ein paar Sekunden der Name der anderen Person. Tippe auf **Mit … verbinden**.
3. Die andere Person sieht die Anfrage und tippt auf **Annehmen**.
4. Beide sehen eine **Prüfzahl**. Ist sie bei euch gleich, sitzt niemand dazwischen.

Erscheint niemand, lassen manche Netzwerke das Suchen nicht zu. Dann öffne **Mit Adresse verbinden** und gib die Adresse der anderen Person ein. Die eigene Adresse steht direkt darunter; bei ZeroTier steht sie auch in der ZeroTier-App unter „Managed IPs“.

### Mehrere Personen (Hauskreis)

Ihr könnt beliebig viele sein. Wer schon verbunden ist, holt unter **Weitere Person dazuholen** jemanden dazu. Jedes Programm gibt weiter, was es bekommt: Ist Anna mit Ben verbunden und Ben mit Carla, sehen sich trotzdem alle drei. Jede Person hat ihre eigene Farbe.

### Freunde

Bei einer Person im selben Netzwerk erscheint **Als Freund merken**, ebenso in einem Raum, in dem nur ihr zwei seid. Danach steht sie unter **Freunde**, und ein Tipp verbindet euch, auch wenn sie gerade nicht in der Liste auftaucht (dann über die zuletzt benutzte Adresse). Seid ihr an verschiedenen Orten, tippt ihr beide auf **Übers Internet**: Ihr trefft euch in eurem eigenen Raum beim Vermittler, ohne Raum-Code. Das verbraucht vom Tageslimit genau so viel wie ein Raum mit Code und nichts, solange keiner von euch ihn offen hat. Mit **Automatisch annehmen** wirst du bei diesem Freund nicht mehr gefragt. Erkannt wird ein Freund an einem gemeinsamen Geheimnis, das beim Merken ausgetauscht wird, nicht nur am Namen.

## 10.2 Was ihr tun könnt

- **Chatten.** Der Chat ist eine eigene Kachel mit fester Höhe: Neue Nachrichten erscheinen unten und bleiben im Blick, die Knöpfe drumherum verrutschen nicht. Hast du nach oben gescrollt, bringt dich **Neue Nachrichten ↓** wieder nach unten. Du siehst, wenn die andere Person schreibt. Ist die Seite zu, meldet ein kleiner Hinweis neue Nachrichten.
- **Chat gespeichert.** Chat und geteilte Verse werden nur auf deinem Gerät gespeichert, für jede Person getrennt. Bist du wieder mit derselben Person im Raum (erkannt an ihrer festen Kennung, nicht am Namen), steht euer bisheriger Chat oben; mit jemand anderem beginnt ein neuer. Ausschalten oder leeren: **Einstellungen → Meine Daten → Chats**.
- **Leiten:** Wer auf **Ich leite** tippt, gibt die Stelle vor. Alle anderen springen automatisch mit, bis sie selbst blättern; dann erscheint **Wieder folgen**.
- **Fragen zum Text:** eine gemeinsame Liste. Jeder kann Fragen hinzufügen und abhaken, wenn sie besprochen sind.
- **Gemeinsames Notizblatt:** eine Seite, an der alle schreiben (schreiben zwei gleichzeitig, gilt die zuletzt gesendete Fassung). Lässt sich als Dokument speichern.
- **Reagieren:** auf eine Chatnachricht tippen oder für einen Vers **Reagieren** wählen (♥, Amen, ✓, !, ?). Eine Reaktion auf einen Vers erscheint kurz über dem Vers.
- **Treffen als Dokument speichern:** Gespräch, geteilte Verse, Fragen und Notizblatt kommen als ein Dokument in deinen Vault.
- **Live mitsehen:** Lest ihr dasselbe Kapitel, siehst du die Verse, die die andere Person anklickt (lila umrahmt, mit Namen), und ihren Mauszeiger. Hat sie Wörter markiert, stehen sie am Rahmen. Ihre Markierungen erscheinen als Balken neben den Versen (übernehmen mit einem Knopf), und liest sie vor, ist der gerade gelesene Vers unterstrichen. Abschalten mit **Meinen Mauszeiger zeigen**.
- **Hierher zeigen:** Die andere Person bekommt einen Hinweis mit einem Knopf zu deiner Stelle.
- **Markierungen teilen:** der gewählten Verse, dieses Kapitels, dieses Buchs oder alle. Beim Übernehmen kommen nur neue dazu.
- **Notizen teilen:** dieses Kapitel, dieses Buch oder alle. Wo schon eine Notiz ist, wird die andere darunter angehängt.
- **Vault senden:** eine Kopie eines ganzen Vaults. Die andere Person kann ihn **als Gast ansehen** (verschwindet beim Schließen) oder **als neuen Vault speichern**.
- **Verse teilen.**
- **Dokumente teilen** (als Kopie; Gesendetes ist in der Größe begrenzt, lange Dokumente werden gekürzt).
- **Die eigene Bibelstelle mitsenden** und **der Stelle der anderen Person folgen.**
- Markierungen mitsehen (Zusammen-Markierungen).

### Sprechen und Kamera (am Computer)

Unter **Sprechen und Kamera**:

- **Gedrückt halten zum Sprechen:** Solange du den Knopf drückst, hören dich die anderen (wie ein Funkgerät).
- **Mikrofon an (Gespräch):** wie ein Anruf, bis du es wieder ausschaltest. Mit Kopfhörern gibt es kein Echo.
- **Sprachnachricht:** antippen, sprechen, noch einmal antippen; höchstens 2 Minuten. Die anderen spielen sie mit **▶ Abspielen** ab.
- **Kamera an:** Dein Bild erscheint bei allen als kleine Kachel; **Videos groß** zeigt alle Bilder in einem eigenen Fenster.
- **Bildqualität:** unter **Mikrofon und Kamera wählen**: niedrig, mittel oder **HD (720p)** (Standard). Ruckelt das Bild oder kommt es spät an, wähle eine niedrigere Stufe: HD braucht für jede Person mit Kamera etwa 5 Mbit/s. Kommt das Bild nicht hinterher, lässt BibleStudy alte Bilder weg und zeigt immer das neueste, damit es nicht immer weiter verzögert.
- **Die anderen hören** schaltet den Ton der anderen aus und an. Unter **Mikrofon und Kamera wählen** suchst du die Geräte aus.

Unter Windows geht Sprechen ohne Zusatz. Für die Kamera (und unter Mac und Linux auch für den Ton) braucht BibleStudy das freie Programm **FFmpeg**: unter Windows im Download-Manager unter **Kamera und Ton** (rund 110 MB), am Mac mit `brew install ffmpeg`, unter Linux mit der Paketverwaltung (`sudo apt install ffmpeg`). Ist FFmpeg schon installiert, wird es benutzt. Auf dem Handy kommen Sprechen und Kamera später.

Nichts wird aufgezeichnet, außer einer Sprachnachricht, die du selbst sendest. Ton und Bild laufen über dieselbe verschlüsselte Verbindung wie alles andere.

## 10.3 Datenschutz

- Übertragen wird nur, was ihr sendet.
- Im selben Netzwerk sieht die andere Person **deine Internetadresse** (das liegt an der direkten Verbindung).
- Über das Internet (Raum-Code) sieht der Vermittler-Server deine Internetadresse, wie jede Webseite, die du öffnest. Inhalte kann er nicht lesen, und er speichert nichts. Er läuft bei Cloudflare; sein Programmcode liegt offen im Ordner `relay` des Quelltexts.
- Texte von Plugin-Übersetzungen werden **nie** an andere gesendet.
- Vor dem ersten Gebrauch fragt das Programm um Einverständnis.

## 10.4 Wenn es nicht klappt

**Im selben Netzwerk:**

- Beide müssen **gleichzeitig online** sein.
- Ein **VPN** oder eine **strenge Firewall** kann die Verbindung verhindern. Unter Windows beim ersten Start „Zugriff zulassen“ wählen; am Mac die Freigabe „Lokales Netzwerk“ erteilen.
- Klappt es im selben Netzwerk nicht, nehmt einfach den Weg mit Raum-Code.

**Mit Raum-Code:**

- Alle müssen den Raum **gleichzeitig offen** haben. Wer den Raum erstellt hat, lässt die Seite offen, bis die anderen da sind.
- „Es ist niemand gekommen“: Prüft den Code. Er hat 8 Zeichen; Groß- und Kleinschreibung und der Bindestrich sind egal.
- „In diesem Raum sind schon fünf Personen“: Lasst euch einen neuen Code geben.
- „Der Vermittler-Server ist gerade nicht erreichbar“: Prüfe die Internetverbindung. Im kostenlosen Tarif hat der Vermittler ein Tageslimit; ist es erreicht, verbindet euch bis zum nächsten Tag im selben Netzwerk, an verschiedenen Orten über ein gemeinsames ZeroTier-Netzwerk (siehe oben).

### Eigener Vermittler-Server

Wer möchte, kann den Vermittler selbst betreiben (kostenlos bei Cloudflare, Anleitung in `relay/README.md` im Quelltext) und ihn im Reiter *Zusammen studieren* unter **Eigener Vermittler-Server (optional)** eintragen. Alle im Raum müssen dann denselben eintragen.

## 10.5 Strategien für Gruppen

- **Hauskreis online:** Eine Person erstellt einen Raum, leitet (**Ich leite**) und liest den Text vor; die anderen folgen.
- **Zu zweit lernen:** Gemeinsam ein Dokument nach der Vorlage *Abschnittsstudie* ausfüllen und die Kopien vergleichen.
- **Mentoring:** Fragen als Dokument senden, Antworten als Dokument zurück.
