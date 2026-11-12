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

New, renamed or deleted files in the folder appear by themselves; an open file that was just changed is shown anew. Bible references in the text ("Rom 8:28") are colored and underlined; a click opens the passage in the Read tab, for "Rom 8:28-30" with all three verses chosen. The same references also stand as buttons above the text. Together with the **split view** (chapter 2.12) it then stands right beside the file.

**Table of contents:** if a file has a table of contents of its own, it stands in the text; every line can be clicked and jumps to its section. The **Contents** button above the file shows it at any time as a list with a search field, even far down in the document. A file without one lists its headings under "Contents"; a PDF its bookmarks (a click jumps to the page).

**Opening and closing folders:** the two buttons above the tree open or close all folders. Which folders are open is kept for the next start.

**Bible references:** the first references in the file stand as buttons above the text; "All … Bible references" shows the whole list.
