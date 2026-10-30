# Strong-Nummern ergänzen / Add Strong's numbers

A plugin for BibleStudy that adds Strong's numbers to a translation that has none, so the **word study** works there too. / Ein Plugin für BibleStudy, das Strong-Nummern in eine Übersetzung einfügt, die keine hat, damit auch dort die **Wortstudie** funktioniert.

> ## ⚠ Keine Garantie / No guarantee
> **DE:** Die Nummern werden **geschätzt**. Dazu wird die Übersetzung Wort für Wort mit einer Übersetzung verglichen, die Strong-Nummern hat (Deutsch: Lutherbibel 1912, Englisch: KJV, Spanisch: Reina-Valera 1909, Französisch: Ostervald). **Es gibt keine Garantie, dass die Nummern fehlerfrei sind.** Manche Wörter bekommen keine Nummer, manche eine falsche. Prüfe das Ergebnis, zum Beispiel stichprobenartig mit einer KI, der du vertraust – auch eine KI kann sich irren. Im Zweifel bleibe bei der Lutherbibel 1912 (Deutsch) oder der KJV (Englisch): Dort stammen die Nummern aus der Quelle.
>
> **EN:** The numbers are **estimated** by comparing the translation word by word with one that has Strong's numbers (German: Luther 1912, English: KJV, Spanish: Reina-Valera 1909, French: Ostervald). **There is no guarantee that the numbers are free of errors.** Some words get no number, some a wrong one. Check the result, for example by sampling it with an AI you trust – an AI can be wrong too. When in doubt, stay with Luther 1912 (German) or the KJV (English): there the numbers come from the source.

## Installation

1. Download the file for your system from the [Releases](https://github.com/Trofline/BibleStudy/releases) page: `BibleStudy-Plugin-strongs-adder-<version>-Windows.zip`, `…-Linux-x64.tar.gz`, `…-macOS-arm64.zip` (Apple silicon) or `…-macOS-x64.zip` (Intel).
2. Unpack it into the plugin folder (*Settings → Plugins → Open plugin folder*), so that the folder `strongs-adder` is in there.
3. Click *Reload*, read the notice and tick *Allow this plugin*.
4. Press **Ctrl+K** and run **Strong-Nummern ergänzen** (*Add Strong's numbers*).

**Linux and macOS:** the packages were built on Windows and are **not tested** on these systems (please report problems). If the plugin does not start: in a terminal in the plugin folder run `chmod +x strongs-adder/StrongsAdder` (newer BibleStudy versions do this themselves), and on macOS `xattr -dr com.apple.quarantine strongs-adder` (the program is not signed).

## How it works

1. A window shows the warning above.
2. You choose the language and the translation.
3. The plugin first reads the whole reference translation to learn which of its words carry which number and which words (the, of, and …) carry none. Then it goes through all chapters. For each verse it compares the words with the same verse of the reference translation and gives words of the same kind (equal words or the same stem, in the same order) the numbers of their counterpart. Words without a safe counterpart stay without number.
4. At the end a window tells you how many words got a number and reminds you to check.

Added numbers are underlined with a **dashed yellow line** in the text, so you can tell them from numbers that come from the source. If the text of a verse changes later (for example you import the translation again), the numbers of that verse are not shown any more, because they could sit on wrong words.

The text of your translation is not changed and not uploaded. Only the word positions and numbers are stored, in the folder `strongs-overlay` of your BibleStudy data. *Ergänzte Strong-Nummern entfernen* (*Remove added Strong's numbers*) deletes them again.

In the word study, if the open translation has no numbers, BibleStudy asks which translation with Strong's numbers to open next to it. Translations with added numbers are marked in yellow there as a warning.

## Limits

* Measured with English texts (KJV as reference; WEB and ASV, which have their own numbers, as targets): about three of four numbers the plugin added matched the target's own numbering, and 40–45 % of the words that have a number there got one. Some of the rest are different but equivalent numbers (for example two words for "say"), but some are real mistakes. For German the result will differ.
* Works best for translations close to the reference (Elberfelder, Schlachter and Luther-based texts); a free translation with other wording gets fewer numbers.
* Needs BibleStudy 1.0.15 or newer (the plugin uses its interface for dialogs and for storing the numbers).

## Licence

MIT, like BibleStudy. The reference translations are free texts (Luther 1912, KJV, Reina-Valera 1909, Ostervald); their numbers come from the Free Use Bible API.
