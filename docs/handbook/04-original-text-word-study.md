# 4. Original Text, Word Study and Analysis

This chapter is one of the core features: you don't just read a translation, you get close to the original, even without any knowledge of Greek or Hebrew.

## 4.1 The three ways to the original text

| Way | Where | What you get |
|---|---|---|
| **Word study** | "Word study" toggle above the text, then click an underlined word | Original-text word, meaning, all occurrences |
| **Original text** | Right sidebar → Verse → Original text | the whole clicked verse in the original, word by word |
| **Strong's dictionary** | Right sidebar → Search → Strong's dictionary | Look up by number, word, transliteration or English meaning |

## 4.2 Strong's numbers

In the 19th century, James Strong gave a number to every Hebrew and Greek word of the English Bible. Hebrew numbers begin with **H** (H8392), Greek ones with **G** (G3056). With the number you find the same original word everywhere, no matter how it was translated.

Not every translation comes with Strong's numbers. The program has them, for example, for the KJV. For translations without numbers there is the bundled plugin *Strong-Nummern ergänzen* (add Strong's numbers); it adds an **estimated** numbering, without guarantee, so please double-check (see [Chapter 12](12-plugins.md)).

## 4.3 Word study step by step

1. Choose a translation with Strong's numbers (for example KJV).
2. Turn on **Word study**. Words with a number are underlined.
3. Click a word. The **Word** tab opens on the right.
4. You see the original-text word with its transliteration and its meaning. If you like, you can have the meaning translated into your language (AI with your Claude; the result is saved).
5. **All occurrences** shows every passage where this original word appears, with the verse text.
6. Below the entry there are further cards, for example detailed dictionaries (see 4.5).

A word can have several numbers; in that case a card appears for each one.

## 4.4 The "Original text" tab

The tab shows the clicked verse in the original, word by word with its meaning. Words can be clicked to learn more. For Hebrew you can show or hide vowel points and accent marks (cantillation) in the settings.

## 4.5 Dictionaries and lexicons

Depending on the word, the program shows the following sources (all freely licensed, loaded on demand):

- **Strong's dictionary** (brief).
- **STEPBible brief lexicon** below the Strong's entries (can be turned off in the settings).
- **Brown-Driver-Briggs** (BDB) for Hebrew, detailed. BDB dates from 1906 and is in the public domain, digitized by the Open Scriptures Hebrew Lexicon Project (CC BY 4.0).
- **Liddell-Scott-Jones** (LSJ) for Greek, detailed, edited by Tyndale House Cambridge (STEPBible, CC BY 4.0).
- **Bible dictionary (Dictionary tab)** for people and places: Easton's Bible Dictionary via Theographic. The names in the current chapter are listed at the top.

## 4.6 How else is the word translated?

For an original-text number, the program shows **how the word is rendered in other passages**. One click shows all passages with that rendering. This is one of the best ways to understand the range of a word.

There are also **German synonyms** (source: openthesaurus.de, CC BY-SA 4.0). One click searches for the word in the Bible text; this way you can see whether another word makes the passage clearer.

## 4.7 Morphology and syntax

In the New Testament (Greek) and in the Old Testament (Hebrew), the program knows the **morphology** of every word: part of speech, time, mood, voice, person, number, gender, case. The **syntax** shows the sentence structure with subject, verb, object and so on.

### Grammar search

Under *Verse → Analysis → Grammar* you search for grammatical features. You choose part of speech, time, mood, voice, person, number, gender and case, and can add a base form or Strong's number (for example G26). This way you can find, for instance, all aorist passive participles or all imperatives of a particular verb.

At the top you choose the **language**. With *Hebrew (Old Testament)* the fields change: instead of time, mood and voice there are **Verb form** (qatal, yiqtol, wayyiqtol, weqatal, Imperative, Jussive, Infinitive, Participle), **Stem** (Qal, Niphal, Piel, Pual, Hiphil, Hophal, Hithpael) and **State** (Absolute, Construct). This way you can find, for example, all Hiphil verbs in the wayyiqtol or all construct forms of a noun. The Hebrew data (about 930 chapter files) is loaded once on first use and then read from the cache. A base form may also be typed without vowel points.

### Clause role search

The clause role search finds clauses in which all the chosen roles occur together, for example *Subject Jesus* and *Object Peter*. The word can be a Strong's number, the base form (Greek or Hebrew) or the English meaning; empty means: any word in this role. It applies to the language you chose above it, so it also covers the Hebrew sentence structure of the Old Testament.

## 4.8 The "Analysis" tab

The tab bundles tools for understanding words and chapters better. It is based on the Strong's numbers of the KJV, which are loaded once. You will find:

- **Words of the clicked verse** with their original-text numbers,
- **Key words** of the chapter,
- **unique words** (occurring only here in the Bible),
- **related words** and companion words,
- **frequencies** and the **distribution** of a word across the books of the Bible.

The first time you use it, the concordance is built; wait a moment.

## 4.9 Concordance: "Occurrences"

The tab *Verse → Occurrences* shows all passages where a word (Strong's number) occurs, with the verse text. The verse texts appear as soon as the search has run once for the chosen translation.

## 4.10 Variant readings of the Greek editions

In the New Testament, the major Greek editions differ in some places. Right-click a verse → *Variant readings of the Greek editions* shows the differences. If the editions agree, or the differences don't change the translation, it says so there as well.

## 4.11 Cross-references

The tab *Verse → Cross-references* shows verses that are related to the clicked verse, with vote count, strongest first. You can

- **hide a cross reference permanently** (and show it again under *Settings → Hidden cross references …*),
- add an **own cross reference** (right-click the verse → *Add own cross reference …*): enter a passage and a short note. It appears as a golden chip with ★ below the verse and as a way back below the target verse; right-clicking the chip deletes it. Own cross references belong to the vault and are included in the backup.

Cross-references can also be viewed as a graph (see [Chapter 9](09-graphs-map.md)).

## 4.12 The Verse overview

Right-click a verse → Verse overview (or Ctrl+G): everything about the clicked verse at a glance, namely your notes and documents on it, other translations, cross-references, as well as people, places and key words of the verse.

### Chapter report

Right-click a verse → **Chapter report** (or Ctrl+K → "Chapter report"): one page with everything the program knows about the open chapter.

- **In numbers:** verses, words and distinct words.
- **Typical words:** what sets this chapter apart from the whole Bible (log-likelihood test), with a click to the search.
- **Words that occur nowhere else:** the unique words of the translation.
- **People and places** with a click into the dictionary.
- **Original text in numbers:** parts of speech, verb forms and the most frequent words (Greek and Hebrew).
- **What this chapter points to:** the chapters that the cross-references of the verses point to most strongly.
- **Your work:** number of your notes and highlights.

*Copy as Markdown* puts the whole report on the clipboard, for example for a document or sermon preparation. Parts without data (no internet, translation not yet loaded for search) are missing, the rest remains.

### Highlighting identical words

With **Word study** switched on (button at the top of the bar), **hovering** the mouse makes all words in the chapter that belong to the word under the pointer light up:

- **purple:** same Strong's number **and** same word,
- **green:** same Strong's number, but a different word (the original-text term is translated differently here),
- **blue:** same word, but a different Strong's number (not recognizable from the English alone, but a different word in the original). Blue is off by default because it adds a lot of color for frequent words.

The pointer also shows the Strong's number, the base form, the pronunciation, a short meaning and, once the analysis data is loaded, how often the word occurs in the Bible.

**Right-clicking a word** offers, among other things:

- **Pin highlight:** the colors stay in place even when you move the mouse away or change the chapter. Several words can be pinned.
- **Next / Previous marked word:** jumps one by one through all marked passages of the chapter.
- **Remove all pinned highlights.**
- The **purple / green / blue** toggles, which colors are shown at all.

This only works for translations with Strong's numbers (for example KJV); for the others there is nothing to link.

### Occurrences as a chart

In the word study (click a word) there is the **Occurrences as a chart** button. The window shows:

- a **bar for each book** of the matching testament (Hebrew numbers: Old, Greek: New Testament) with the number of occurrences; clicking a bar jumps to the first passage in that book,
- a **ring** with the words the KJV uses to translate the term, including counts.

Counting is based on the Strong's numbers of the KJV. The first time, the program builds the index once (as in the Analysis tab).

## 4.13 Learning Greek and Hebrew

If you want to go deeper, learn the scripts yourself: *Planning → Languages* (see [Chapter 7](07-planning.md)).
