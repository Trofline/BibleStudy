# 3. Search

Search is the most powerful tool in the program. You'll find it in the bar under **Search → Text** (or with Ctrl+F). Below the search field you'll see the range, options and hits.

## 3.1 The Basic Rules

| You type | What is found |
|---|---|
| `grace` | Verses with the word grace |
| `Moses Elijah` | Verses in which **both** words occur (AND) |
| `"I am"` | the exact phrase |
| `grace / truth` | Verses with one **or** the other (OR) |
| `grace -truth` | grace, but **without** truth |
| `(grace / peace) Jesus` | Parentheses group: (grace or peace) and Jesus |
| `house*` | Wildcard at the end: house, houses, household … (Word beginning) |
| `*ness` | Wildcard at the start: Word ending |
| `*spirit*` | Word part |
| `Grace NAH5 truth` | both at most **5 words** apart, in any order |
| `Lord DANN3 God` | God comes **after** Lord, at most 3 words behind it |
| `L=go` | all forms of the **base form** (went, goes, gone) |
| `L~house` | base form, capitalization doesn't matter |
| `m?n` | `?` stands for **exactly one** letter (man, men …) |
| `"angel * Lord"` | inside a phrase `*` stands for exactly one word |
| `~graces` | **fuzzy**: similar spellings too (one letter more, less or different) |
| `grace in:"Mt 5-7"` | search only in this passage or book (`in:John`, `in:"Rom 1-8; Gal"`) |

**Operators as in Logos** (in capitals): `A BEFORE B` and `A AFTER B` (order, also with a distance: `BEFORE 5 WORDS`), `A NEAR B` (about 8 words), `A WITHIN 3 WORDS B`, `A WITHIN 2 VERSES B`, `A WITHIN 1 CHAPTER B`, `A WITHIN 1 SENTENCE B` (in the same sentence, also across verses), and with NOT in front the opposite (`grace NOT WITHIN 3 WORDS truth`). `AND`, `OR`, `NOT` work like a space, `/` and `-`.

**Word distance:** `NAH` and `DANN` always need a number (`NAH1` to `NAH999`); a word next to it counts as distance 1. Both sides may be phrases, groups, base forms or Strong's numbers, for example `"kingdom of God" NAH10 (parable / mustard seed)`. English works too: `NEAR5`, `THEN3`. Without a number, "then" remains an ordinary search word.

Punctuation on search words (comma, hyphen) is ignored: `grace,` finds the same as `grace`. A single surplus `)` no longer knocks the words after it out of the search.

## 3.2 Strong's Numbers and Original Text

- `G3056` finds all verses with this Greek Strong's number, `H8392` with a Hebrew one.
- `G3056=truth` combines number and word.
- `O=Grace` searches for the **original-text word behind "grace"**: so it also finds passages where the translation uses a different English word.
- You can type Greek and Hebrew words directly. The **α א** button next to the search field opens a keyboard for both scripts; you can also type in Latin letters ("logos" → λογος, "shalom" → שלם).

Strong's numbers are only available for translations that include them. If they're missing, a search by number can't find anything.

## 3.3 Range

Next to the search field you choose **where** to search: Whole Bible, Old Testament, New Testament, Prophets, Pauline Epistles, Gospels and other ranges. There is also **Words of Jesus only**: then only the red-letter text is searched (for translations that mark it, for example KJV).

## 3.4 How Words Match

The selection next to the range determines how a word matches:

- **Word beginning** (love → loves, loved, lovely),
- **Whole word**,
- **Word ending** (… → -ness),
- **Word part**.

## 3.5 More Options

- **Search in the original text:** finds a word even where the translation uses a different word. This works through the Strong's numbers; they appear as buttons below the field.
- **Match capitals:** LORD ≠ Lord.
- **Search only in the last hits:** narrows a hit list further.
- **Proximity:** "Search words in the same verse / within 2 or 5 verses / in the same chapter".

## 3.6 People, Places, References

| You type | What is found |
|---|---|
| `Person:Abraham` | every verse in which Abraham is mentioned, also as Abram |
| `Ort:Jerusalem` | the same for places |
| `Person:Abraham promise` | person and word combined |
| `Bezug:"Jer 31"` | New Testament verses that refer to this passage |
| `Bezug:"Heb 10"` | the other way round: which OT verses a NT section refers to (cross-references with vote counts, strongest first; not just quotations) |

## 3.7 Build a Search

If you don't want to learn the syntax, click **Build a search**. For each row you choose from a list:

- all words,
- one of the words,
- exact phrase,
- without,
- begins with,
- ends with,
- Word part,
- base form,
- original-text word,
- Strong's number,
- (also person, place, reference).

The search is assembled from this and then appears in the search field. That way you learn the syntax along the way.

## 3.8 Search Help, History, Saved Searches

- **Search help** shows all examples, which you can click.
- With **☆** you save a search along with its options; **Saved searches** brings it back up.
- The search history can be cleared in the Settings.

## 3.9 Working with the Hits

Above the hits you choose **how** they are shown:

- **Verses:** the list of hits.
- **Aligned:** every found word under each other, with the text to its left and right (like a concordance).
- **Grid:** books against translations, the number of hits in each cell; the darker, the more.
- **Comparison:** every hit in several translations under each other. Which ones, you choose with **Translations ▾**.
- **Analysis:** spread over the Bible, the word forms found and the chapters with the most hits.

Under **Hits ▾**:

- **Hits as document:** saves the verses as a Document.
- **Hits as table (CSV):** exports them for spreadsheet programs.
- **Places on the map:** shows where the places mentioned are located (bigger dot = more verses).

Clicking a hit jumps to the passage; the hits are highlighted.

### Word statistics

The **Word statistics** button below the hits opens a window with three analyses:

- **Neighbours:** which words appear conspicuously often near the hits (3, 5 or 10 words away). It's calculated with Dunning's log-likelihood test: frequent filler words such as "and" drop out, and only truly striking partners remain. Clicking a word searches for both together, for example `(grace) NAH5 justification`.
- **Key words:** which words characterize the **open book**, compared with the rest of the Bible (Romans, for instance, is full of "law", "faith", "righteous"). Clicking searches for the word.
- **Rare words:** words in the book that occur only **once** in the whole translation. Clicking jumps to the passage.

The numbers apply to the translation currently selected. For hapax legomena in the linguistic sense (only once in the original text), you need the Strong's numbers and the Concordance.

## 3.10 Grammar Search and Clause Roles

The **Grammar search** (under *Verse → Analysis*) covers the Greek New Testament and the Hebrew Old Testament, for example all aorist passive participles or all piel forms. The **Clause role search** finds clauses by role, such as subject Jesus and object Peter, in both testaments too (see [Chapter 4](04-original-text-word-study.md)).

Grammar also works right in the search field and combines with words: `Gram:"verb aorist passive" grace`, `Gram:"noun genitive G26"`, Hebrew `Gram:"piel qatal"`. The Greek or Hebrew words then stand below each hit.

## 3.11 Searching by Meaning in the Original Text

These searches use the MACULA data (Clear Bible, freely licensed) and combine with ordinary words like `Person:`. The data is loaded once the first time.

| You type | What is found |
|---|---|
| `Sense:G863` | every word with **the same meaning**: for ἀφίημι (forgive) also ἄφεσις, χαρίζομαι, ἱλασμός … based on the meaning fields of Louw-Nida (Greek) and SDBH (Hebrew) |
| `Sense:H2617` | for chesed (steadfast love) also emet, emunah, chasid … |
| `Sense:forgive` | an English word chooses the meaning by itself; a word in another language goes through the Strong's numbers of the chosen translation |
| `Sense:G863=forgive` | when a word has **several meanings**, the program names them (leave, forgive, allow) and takes the marked one; with `=` you choose another |
| `Refers:Jesus` | where Jesus is **named or meant**: also "he", "him", "his" and verbs whose subject he is, without his name being written. `Refers:Peter wept` also finds "and he wept bitterly" |
| `LXX:G26` | which **Hebrew words** the Greek Old Testament (Septuagint) renders with ἀγάπη |
| `LXX:H2617` | the other way round: where chesed stands and which Greek word the Septuagint uses for it each time (the count above the list) |
| `Agree:"adjective ~ noun G26"` | two words close together (up to 4 words) that **agree in case, number and gender**, such as an adjective and its noun; Hebrew in number and gender |
| `Event:Baptism` | the passages of an **event** of the timeline (Baptism, Exodus, Crucifixion …) |
| `Root:H6` | every word of a **Hebrew root** after the BDB lexicon: for אבד (perish) also "destruction", "a lost thing", "ruin". Works with the base form or an English word too (`Root:perish`) |

The German spellings work too: `Sinn:`, `Meint:`, `Kongruenz:`, `Ereignis:`, `Wurzel:`. Above the hits you see which words belong to the meaning field, or how often someone is named and meant.

**Good to know:** meaning fields are a classification by scholars, not revelation. In Hebrew many verbs have no field yet, and sometimes a field is wider than one expects (for "shepherd" it also holds "graze" and "eat"). Check the hits.

## 3.12 Ctrl+K Finds Even More

The *Go to …* field also searches Bible passages, books, commands, documents, notes, prayers, Strong's numbers, as well as people and places (a hit opens the Dictionary). Anything that doesn't match starts the text search.

## 3.13 The Order in Which You Should Search

1. Start with a single word and look at the number of hits.
2. Too many hits? Add a second word, a range or "Whole word".
3. Too few? Use wildcards (`*`), the base form (`L=`) or the original-text search.
4. A topic instead of a word? Search the Strong's numbers of the important terms together.
5. Save the good search with ☆.
