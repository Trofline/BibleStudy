# 3. Suche

Die Suche ist das mächtigste Werkzeug im Programm. Du findest sie in der Leiste unter **Suchen → Text** (oder mit Strg+F). Unter dem Suchfeld stehen Bereich, Optionen und die Treffer.

## 3.1 Die Grundregeln

| Du schreibst | Gefunden wird |
|---|---|
| `Gnade` | Verse mit dem Wort Gnade |
| `Mose Elia` | Verse, in denen **beide** Wörter vorkommen (UND) |
| `"Ich bin"` | die genaue Wendung |
| `Gnade / Wahrheit` | Verse mit dem einen **oder** dem anderen (ODER) |
| `Gnade -Wahrheit` | Gnade, aber **ohne** Wahrheit |
| `(Gnade / Friede) Jesus` | Klammern gruppieren: (Gnade oder Friede) und Jesus |
| `Haus*` | Platzhalter am Ende: Haus, Hauses, Häuser … (Wortanfang) |
| `*keit` | Platzhalter am Anfang: Wortende |
| `*geist*` | Wortteil |
| `L=gehen` | alle Formen der **Grundform** (ging, gingen, gegangen) |
| `L~haus` | Grundform, Groß-/Kleinschreibung egal |

Satzzeichen an Suchwörtern (Komma, Bindestrich) werden ignoriert: `Gnade,` findet dasselbe wie `Gnade`. Ein einzelnes überzähliges `)` wirft die Wörter dahinter nicht mehr aus der Suche.

## 3.2 Strong-Nummern und Grundtext

- `G3056` findet alle Verse mit dieser griechischen Strong-Nummer, `H8392` mit einer hebräischen.
- `G3056=Wahrheit` kombiniert Nummer und Wort.
- `O=Gnade` sucht das **Grundtextwort hinter „Gnade“**: Es findet also auch Stellen, an denen die Übersetzung ein anderes deutsches Wort benutzt.
- Griechische und hebräische Wörter kannst du direkt tippen. Mit dem Knopf **α א** neben dem Suchfeld öffnet sich eine Tastatur für beide Schriften; man kann auch lateinisch tippen („logos“ → λογος, „shalom“ → שלם).

Strong-Nummern gibt es nur bei Übersetzungen, die sie mitbringen. Fehlen sie, kann die Suche nach Nummern nichts finden.

## 3.3 Bereich

Neben dem Suchfeld wählst du, **wo** gesucht wird: Ganze Bibel, Altes Testament, Neues Testament, Propheten, Paulusbriefe, Evangelien und weitere Bereiche. Dazu gibt es **Nur Worte Jesu**: Dann wird nur das Rotgedruckte durchsucht (bei Übersetzungen, die es kennzeichnen, zum Beispiel KJV).

## 3.4 Wie Wörter passen

Die Auswahl neben dem Bereich bestimmt, wie ein Wort passt:

- **Wortanfang** (lieb → liebe, lieben, Liebe),
- **Ganzes Wort**,
- **Wortende** (… → -keit),
- **Wortteil**.

## 3.5 Mehr Optionen

- **Im Grundtext suchen:** findet ein Wort auch dort, wo die Übersetzung ein anderes Wort benutzt. Das geht über die Strong-Nummern; sie erscheinen als Knöpfe unter dem Feld.
- **Groß-/Kleinschreibung beachten:** HERR ≠ Herr.
- **Nur in den letzten Treffern suchen:** engt eine Trefferliste weiter ein.
- **Nähe:** „Suchwörter im selben Vers / innerhalb von 2 oder 5 Versen / im selben Kapitel“.

## 3.6 Personen, Orte, Verweise

| Du schreibst | Gefunden wird |
|---|---|
| `Person:Abraham` | jeder Vers, in dem Abraham erwähnt wird, auch als Abram |
| `Ort:Jerusalem` | dasselbe für Orte |
| `Person:Abraham Verheißung` | Person und Wort verbunden |
| `Bezug:"Jer 31"` | Verse des Neuen Testaments, die auf diese Stelle verweisen |
| `Bezug:"Hebr 10"` | umgekehrt: auf welche AT-Verse ein NT-Abschnitt verweist (Querverweise mit Stimmenzahl, die stärksten zuerst; nicht nur Zitate) |

## 3.7 Suche bauen

Wer die Schreibweise nicht kennen will, klickt auf **Suche bauen**. Pro Zeile wählst du aus einer Liste:

- alle Wörter,
- eines der Wörter,
- genaue Wendung,
- ohne,
- beginnt mit,
- endet auf,
- Wortteil,
- Grundform,
- Grundtextwort,
- Strong-Nummer,
- (auch Person, Ort, Bezug).

Daraus wird die Suche zusammengesetzt und steht danach im Suchfeld. So lernst du die Schreibweise nebenbei.

## 3.8 Suchhilfe, Verlauf, gemerkte Suchen

- **Suchhilfe** zeigt alle Beispiele zum Anklicken.
- Mit **☆** merkst du eine Suche samt ihren Optionen; **Gemerkte Suchen** ruft sie wieder auf.
- Der Suchverlauf lässt sich in den Einstellungen löschen.

## 3.9 Mit den Treffern arbeiten

Unter der Trefferliste:

- **Treffer als Dokument:** legt die Verse als Dokument ab.
- **Treffer als Tabelle (CSV):** exportiert sie für Tabellenprogramme.
- **Orte auf der Karte:** zeigt, wo die genannten Orte liegen (größerer Punkt = mehr Verse).

Ein Klick auf einen Treffer springt zur Stelle; die Treffer sind markiert.

## 3.10 Grammatiksuche und Satzrollen

Im Neuen Testament gibt es die **Grammatiksuche** (unter *Vers → Analyse*), zum Beispiel alle Aorist-Passiv-Partizipien. Die **Satzrollen-Suche** findet griechische Sätze nach Rollen, etwa Subjekt Jesus und Objekt Petrus (siehe [Kapitel 4](04-grundtext-wortstudie.md)).

## 3.11 Strg+K findet noch mehr

Das Feld *Gehe zu …* sucht zusätzlich Bibelstellen, Bücher, Befehle, Dokumente, Notizen, Gebete, Strong-Nummern sowie Personen und Orte (Treffer öffnet das Lexikon). Was nicht passt, startet die Textsuche.

## 3.12 Die Reihenfolge, in der du suchen solltest

1. Mit einem einzelnen Wort anfangen und die Trefferzahl ansehen.
2. Zu viele Treffer? Ein zweites Wort, einen Bereich oder „Ganzes Wort“ hinzufügen.
3. Zu wenige? Platzhalter (`*`), Grundform (`L=`) oder die Grundtext-Suche nutzen.
4. Ein Thema statt eines Wortes? Strong-Nummern der wichtigen Begriffe zusammen suchen.
5. Die gute Suche mit ☆ merken.
