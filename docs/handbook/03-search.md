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

Below the hit list:

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

In the New Testament there is the **Grammar search** (under *Verse → Analysis*), for example all aorist passive participles. The **Clause role search** finds Greek clauses by role, such as subject Jesus and object Peter (see [Chapter 4](04-original-text-word-study.md)).

## 3.11 Ctrl+K Finds Even More

The *Go to …* field also searches Bible passages, books, commands, documents, notes, prayers, Strong's numbers, as well as people and places (a hit opens the Dictionary). Anything that doesn't match starts the text search.

## 3.12 The Order in Which You Should Search

1. Start with a single word and look at the number of hits.
2. Too many hits? Add a second word, a range or "Whole word".
3. Too few? Use wildcards (`*`), the base form (`L=`) or the original-text search.
4. A topic instead of a word? Search the Strong's numbers of the important terms together.
5. Save the good search with ☆.
