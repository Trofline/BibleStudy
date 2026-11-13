# 6. Documents and Knowledge

The **Documents** area (right sidebar, Ctrl+Shift+D) is your personal knowledge archive, similar to Obsidian: your own texts, linked to each other and to Bible passages.

## 6.1 A New Document

Right sidebar → **Documents → Write** → **＋ New**. The New menu offers:

- an empty document,
- a document from a **template**,
- a document from the verse you clicked (also via right-click on the verse).

### Templates

Templates are ready-made frameworks for studies: one heading per question, so a document begins with structure instead of an empty page. Examples are a **Section study** with the parts *Observe – Interpret – Apply*, a **Person study** (Who is this person? Family and origin, Character and relationship with God, Key passages, What do I learn from this?), a **Word study** (Original text and basic meaning, Occurrences in the Bible, Meaning in context, Connections) and a **Parable interpretation** (Background and audience, The image and its meaning, The main message, Application for me).

## 6.2 The Editor

The editor works like a word processor:

- at the top the **paragraph format** (Headings, Quote, lists),
- **Bold** (Ctrl+B) and **Italic** (Ctrl+I),
- Horizontal rule,
- **＋ Verse:** type a Bible passage and insert it as a quote or link, or pick one of your saved verses (marks and bookmarks) from the list below; "From all vaults" also shows those of the other vaults. You can also drag a mark from the side panel (Marks tab) into the document with the mouse; it is inserted as a quote where you let go,
- **[[ ]]:** link to another document. If you select a word first, or type a text in the box at the top (for example "click here"), that text appears as the link in the document and a click still leads to the other document (written as `[[Title|click here]]`). With the cursor inside a link you change that very link: its text, or where it leads,
- Undo.

Enter in a list continues it. Every document has a fixed number, and a link leads by that number. So you can rename a document as often as you like, its links stay valid.

**Reading** (Ctrl+E) shows the finished page without the editing bar, in which verses and links are clickable. Existing documents open in this view, new ones in writing mode; **Write** takes you back. At the top you switch **Auto-save** on or off (off: saved with Save, when you leave the document and when the program closes). The document needs a title for this; Ctrl+S saves immediately.

The **Export** button offers **Print / PDF**, **Save as HTML file …** and **Save as Markdown …**. Everything the document refers to comes along, so that its links work in the file: linked documents (and their links) and the text of the verses it mentions. In print and the HTML file these are further sections the links jump to; in the Markdown export, separate files in a folder that the links point to, with the verses in "Bible passages.md".

**The "Export" window:** each of the three entries under **Export** opens the same window. At the top you choose the format: **Print / PDF**, **HTML file** or **Markdown**.

**Print as a handout (and as an HTML file):** you choose one of eight looks. They differ not only in color but also in their build:
- **Classic:** serif type and gold, a centered head with an ornament line
- **Modern:** clear and airy, color bars at the head and the cards
- **Plain:** saves ink, no colored areas at all
- **Elegant:** wine red, headings in small capitals, a large initial at the start, double rules
- **Church newsletter:** a colored head across the page, round shapes, headings as little labels
- **Newspaper:** a big headline between thick rules, the text in two columns like an article
- **Large print:** large, clear type with wide line spacing, all in one column, good for older eyes
- **Nature:** shades of green, a leaf before every heading

Also:
- the name of the church or group for the head, and the font size,
- the Bible references as an appendix in boxes, in two columns if you like,
- small numbers at the references in the text, so they are found in the appendix at once on paper,
- questions set apart: a section "Questions" becomes a box with numbered questions, single lines with "?" are marked,
- lined space for notes at the end.

The sheet has A4 pages with page numbers and no lonely lines at a page edge. **Preview** shows it in the browser, **Print / PDF …** opens the print dialog at once; there choose "Save as PDF" and turn off "Headers and footers". The HTML file gets the same look; for it too, **Preview** shows the sheet in the browser first. Your choice is kept.

**Markdown:** here you choose how the Bible references come along: as **a file of their own, with links** (as before), **as a quote under the text** (all in one file, each verse right under the paragraph that names it) or **not at all**. Also whether linked documents come along as files of their own, and whether every file gets a **head with title, date and tags** (YAML, as Obsidian reads it). The **preview** shows the text of the main file and which files will be made in the folder, before you save.

## 6.3 Linking: Verses and [[Links]]

- If you write a Bible passage like "John 3:16", it becomes a link that jumps to the passage.
- `[[Title]]` links to another document. When saved, the link gets the number of the document and then reads `[[#12:Title]]` or `[[#12:Title|your own text]]` in writing mode. In the reading view and in exports you only see the title or your text.
- Below the document, clickable chips show the **verses, links and backlinks**: which other documents point to this one.

## 6.4 Organizing: folders and categories

- **＋ Folder** creates a folder.
- You drag documents with the mouse onto a folder (moves them) or onto a **category** (sets it). Right-clicking a document does the same.
- Right-click on a folder: rename or delete (the documents stay).
- **Categories** are colored chips; a document has at most one. Internally, a category is a tag of the form `kat:Name`. A document whose plain tag has exactly the same name as a category (for example sermons with the tag "Sermon") also counts toward it.
- Rarely used things (tags, folders, template, connections) are collapsed.

## 6.5 The Knowledge Graph

The **Knowledge graph** shows the connections between your documents, verses and notes as a network. It helps you see which thoughts belong together and which documents stand alone.

## 6.6 Linked Files

Besides your own texts, you can **link files** that are on your computer. They appear in the knowledge area and can be connected with verses and documents.

## 6.7 Inserting Verses and Saving Hits

- Right-click on a verse → *Insert into open document* inserts the verse as a quote with its passage reference.
- In Search, *Hits as document* and *Hits as table (CSV)* save whole lists of hits.

## 6.8 Exporting

Documents can be printed or saved as PDF. For Obsidian there is an export as a Markdown folder (*Settings → Export as Obsidian vault …*): documents, highlights, notes, bookmarks, reading list and to-dos.

## 6.9 Working Together on Documents

In the *Together* tab you can send a document as a **copy** to the connected person (see [Chapter 10](10-studying-together.md)).

## 6.10 Strategies for a Good Archive

- **One thought per document.** Short documents link better.
- **Titles as statements** ("Grace is not earned" instead of "Grace").
- **Always name the passage.** That way the document shows up in the verse overview.
- **Link rather than copy.** `[[Title]]` instead of writing the text again.
- **Few categories.** Five to seven are enough (for example Sermon, Study, Devotion, Person, Word).
- **Tidy up for 10 minutes weekly:** look for unlinked documents in the graph and connect them.

## 6.11 Materials of a Bible study

The **Materials** tab (on the rail "Material") shows papers you were given right inside the program, so you need not switch to Word with Alt+Tab.

1. **Choose folder …** and, if you like, **Include sub-folders** (on by default).
2. On the left the files appear as a tree like in the Explorer. A click opens a file on the right.
3. **Tab** and **Shift+Tab** switch between the open files; each has a small tab with ✕ at the top.

What opens:

- **Word (.docx and the old .doc)**, **OpenDocument (.odt)**, **RTF**, **PowerPoint (.pptx)**: text with headings, lists, table rows, bold and italic, and the pictures in the file (for the old .doc the text only). Even big folders of hundreds of pages open in a second or two.
- **PDF**: as **page images**, so it looks as printed, scanned sheets included. "Show text only" switches to the text, which can be selected and copied.
- **Text, Markdown, web pages, pictures** (PNG, JPG, GIF, BMP, WebP).
- Everything else, such as spreadsheets or audio, opens **in its own program**.

New, renamed or deleted files in the folder appear by themselves; an open file that was just changed is shown anew. Bible references in the text ("Rom 8:28") are colored and underlined; a click opens the passage in the Read tab, for "Rom 8:28-30" with all three verses chosen. The same references also stand as buttons above the text. The button left of the file tabs hides the folder list and shows it again, so the file gets the whole width, above all in the split view. Together with the **split view** (chapter 2.12) it then stands right beside the file.

**Table of contents:** if a file has a table of contents of its own, it stands in the text; every line can be clicked and jumps to its section. The **Contents** button above the file shows it at any time as a list with a search field, even far down in the document. A file without one lists its headings under "Contents"; a PDF its bookmarks (a click jumps to the page).

**Opening and closing folders:** the two buttons above the tree open or close all folders. Which folders are open is kept for the next start.

**Bible references:** the first references in the file stand as buttons above the text; "All … Bible references" shows the whole list.

**Searching:** the field **"Search all materials …"** above the tree searches every file of the folder (case and accents do not matter: "holle" also finds "Hölle"). The hits are listed by file with a piece of text; a click opens the file at exactly that place. **Ctrl+F** searches the open file: Enter goes to the next place, Shift+Enter to the previous one, Esc closes the bar. For a PDF shown as page images, Ctrl+F switches to its text so the words can be marked.

**Preview:** resting the mouse on a Bible reference in the text shows the verse in the chosen translation, without jumping there.

**Marks and notes:** select text and **right-click** → "Mark" (a color) or "Mark with a note …". The note then stands below the paragraph; a click on it changes it. A right-click on a mark offers note, color and remove. **The file itself stays unchanged:** marks and notes are kept in the vault (and so in the backups) together with the marked words. If the file changes or is moved, the program finds the words again. The button **"My marks"** above the file lists all marks of the file to jump to.

**Reading position:** every file opens where you last read it, also after a restart.

**In the verse overview:** when you click a verse in the Bible text, the overview shows under **"In your materials"** which files mention it (also inside a passage like "Rom 8:26-30"), each with a piece of text. A click opens the file at that place.

## 6.12 Live Bible study on the phones

The **Live** tab (in the "Material" rail) brings the Bible study onto the participants' phones, **without an app, an account or the internet**. The program itself becomes a small web page in your Wi-Fi; the phones only have to be in the same Wi-Fi as your computer.

1. **Start live Bible study.** A QR code appears with the address below it (e.g. `http://192.168.1.20:49400/ab3k9x`). If Windows asks whether the program may be reached in the network, choose **Allow** for private networks.
2. The participants scan the code with the camera and tap the link. **QR code to the projector** shows it large on the second screen; it always fits the screen as a whole.
3. On the phone there are four parts:
   - **Now:** what the projector shows (verses or pieces of the materials), large and calm. Without a projector you send the chosen verses with **Chosen verses to the phones**.
   - **Handout:** choose a document and **Send to the phones**; it appears in the same look as printed.
   - **Question:** participants quietly send you a question; the name is optional. Only you see the questions; **To the projector**, **✓ Discussed** or **Delete**.
   - **Poll:** type a question and answers (or **Yes / No**, **Scale 1–5**, **Verses of the passage**) and **Start poll**. Every phone has one vote and can change it. You see the result at once; the phones and the projector only when you show it.

**End …** asks whether the Bible study should go into the **archive**: with the passages shown, the questions and the result of the poll.

**Safety:** the address holds a random code; without it nobody gets in. Nothing leaves your network, and after ending, the room is gone. If the page does not open on the phone, it is often a guest Wi-Fi that keeps devices apart; then a hotspot from the leader's phone, which the computer and the participants join, helps.

## 6.13 Archive of Bible studies

The **Archive** tab keeps which passage was read when: date, title, passages (e.g. `John 10:1-18; Ps 23`), notes and the questions of the evening. The passages can be clicked, and above you search the whole archive. Above a chapter that was already read in a Bible study, the reader shows **"In the Bible study on …"**; a click opens the archive. The archive belongs to the vault and is backed up with it.

## 6.14 Discussion questions by method

Under **Documents → New → Discussion questions by method** you create a document for the verses clicked, with the Bible text and ready questions:

| Method | Idea |
|---|---|
| Observe – Interpret – Apply | the classic inductive method |
| Discovery Bible Study (DBS) | thanks and worries, retell, what we learn about God and people, what I will do, whom I tell |
| Swedish method | 💡 what makes sense, ? what I do not understand, → what touches me |
| SOAP | Scripture, Observation, Application, Prayer |
| Discover the story | for stories and parables |
| Lectio Divina | read, reflect, pray, rest |

The questions stand under "Questions for discussion"; in the **handout** they become a numbered box of their own.

## 6.15 Taking over notes from e-Sword

Under **Settings → Backup and sharing → Import notes from e-Sword …** you choose your e-Sword files (`.notx`, `.cmtx`, `.topx`) or MySword commentaries (`.mybible`). Notes on verses become notes in the active vault (if one is already there, the new one goes below it); topics and notes on whole books or chapters become documents with the tag "e-Sword". Formatting (RTF, HTML) becomes plain text. Importing the same file a second time creates nothing twice. Notes from Logos can be exported there as a Word file and then opened in the **Materials** tab.
