BibleStudy für den Mac / BibleStudy for the Mac
================================================

DEUTSCH
-------
Warum ist das nötig?
BibleStudy ist kostenlos und quelloffen. Um bei Apple "angemeldet" zu sein (Notarisierung), müsste der Entwickler jährlich Geld
bezahlen. Deshalb sperrt macOS das Programm beim ersten Mal. Mit der einmaligen Einrichtung unten läuft es trotzdem.

Welche Datei habe ich? Es gibt zwei Mac-Pakete: "arm64" für Macs mit Apple-Chip (M1, M2, M3 ...) und "x64" für Intel-Macs.
Dein Mac: Apfel-Menü oben links > "Über diesen Mac" > "Chip" (Apple ...) oder "Prozessor" (Intel). Du brauchst das passende Paket.

SCHRITT 1 - Entpacken
Doppelklicke die ZIP-Datei (im Ordner "Downloads"). Es entsteht ein Ordner "BibleStudy". Verschiebe ihn dorthin, wo er bleiben soll,
zum Beispiel in "Dokumente" oder "Programme".

SCHRITT 2 - Einrichten und starten (Variante A, am sichersten)
  1. Öffne das Programm "Terminal" (Cmd + Leertaste drücken, "Terminal" tippen, Enter).
  2. Tippe das Wort  bash  und danach ein Leerzeichen. Noch nicht Enter drücken.
  3. Ziehe die Datei "Start-BibleStudy.command" aus dem Ordner BibleStudy mit der Maus in das Terminal-Fenster.
  4. Drücke Enter.
Beim ersten Mal richtet das Skript alles ein (etwa eine halbe Minute) und startet BibleStudy. Das Terminal-Fenster muss offen bleiben,
solange BibleStudy läuft. Schließt du es, wird BibleStudy beendet.

Variante B (Doppelklick)
Doppelklicke "Start-BibleStudy.command". Sagt macOS, die Datei könne nicht geöffnet werden:
  "Systemeinstellungen" > "Datenschutz & Sicherheit" > nach unten scrollen > bei der Meldung zu "Start-BibleStudy.command"
  auf "Trotzdem öffnen" klicken > Passwort eingeben. (Ältere macOS-Versionen: Rechtsklick auf die Datei > "Öffnen".)

Das nächste Mal startest du genauso, die Einrichtung wird nicht wiederholt.

Was macht das Skript? Es entfernt die Markierung "aus dem Internet geladen" von den Dateien dieses Ordners, macht die Programmdateien
ausführbar und signiert sie mit einer lokalen Signatur (ohne Entwicklerkonto). Es sendet nichts ins Internet und verändert keine
Systemdateien. Du kannst es mit einem Texteditor lesen.

Probleme
  - "zsh: killed": Du hast die Programmdatei direkt gestartet. Starte stattdessen "Start-BibleStudy.command" wie oben beschrieben.
  - "Bad CPU type in executable": Du hast das falsche Paket (arm64 statt x64 oder umgekehrt). Lade das andere herunter.
  - Es passiert nichts / Fehlermeldung: Öffne ein Problem auf https://github.com/Trofline/BibleStudy/issues und schreibe die Meldung
    aus dem Terminal-Fenster dazu.

Deine Daten (Notizen, Markierungen ...) liegen im Ordner ~/.local/share/BibleStudy, getrennt vom Programm.
Aktualisieren: Neues Paket von https://github.com/Trofline/BibleStudy/releases laden, entpacken und den alten Ordner ersetzen.
Deine Daten bleiben erhalten. Der Mac-Teil ist nicht auf jedem System getestet - Rückmeldungen sind willkommen.

ENGLISH
-------
Why is this needed?
BibleStudy is free and open source. Being registered with Apple (notarisation) would cost the developer money every year, so macOS
blocks the program the first time. The one-time setup below makes it run anyway.

Which file do I have? There are two Mac packages: "arm64" for Macs with an Apple chip (M1, M2, M3 ...) and "x64" for Intel Macs.
Your Mac: Apple menu > "About This Mac" > "Chip" (Apple ...) or "Processor" (Intel). Use the matching package.

STEP 1 - Unpack
Double-click the ZIP file (in "Downloads"). A folder "BibleStudy" appears. Move it where it should stay, for example "Documents" or "Applications".

STEP 2 - Set up and start (variant A, the safest)
  1. Open "Terminal" (press Cmd + Space, type "Terminal", Enter).
  2. Type the word  bash  and a space. Do not press Enter yet.
  3. Drag the file "Start-BibleStudy.command" from the BibleStudy folder into the Terminal window.
  4. Press Enter.
The first time, the script sets everything up (about half a minute) and starts BibleStudy. Keep the Terminal window open while BibleStudy
runs. If you close it, BibleStudy ends.

Variant B (double-click)
Double-click "Start-BibleStudy.command". If macOS says the file cannot be opened:
  "System Settings" > "Privacy & Security" > scroll down > at the message about "Start-BibleStudy.command" click "Open Anyway"
  > enter your password. (Older macOS: right-click the file > "Open".)

Next time you start it the same way; the setup is not repeated.

What does the script do? It removes the "downloaded from the internet" mark from the files of this folder, makes the program files
executable and signs them with a local signature (no developer account). It sends nothing to the internet and changes no system
files. You can read it in a text editor.

Problems
  - "zsh: killed": you started the program file directly. Start "Start-BibleStudy.command" instead, as described above.
  - "Bad CPU type in executable": wrong package (arm64 instead of x64 or the other way round). Download the other one.
  - Nothing happens / an error: open an issue at https://github.com/Trofline/BibleStudy/issues and add the message from the Terminal window.

Your data (notes, highlights ...) is stored in ~/.local/share/BibleStudy, separate from the program.
Updating: download the new package from https://github.com/Trofline/BibleStudy/releases, unpack it and replace the old folder.
Your data stays. The Mac part is not tested on every system - feedback is welcome.
