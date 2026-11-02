# 1. Getting Started

This chapter takes you from the download to the first Bible passage you read. If you already know the program, skip ahead to [Chapter 2](02-reading.md).

## 1.1 What BibleStudy Is

BibleStudy is a free, open-source Bible study program for Windows, Linux, and macOS. It is written in C# with .NET 10 and Avalonia and is released under the MIT license. With it you can

- read, compare, and search the Bible in many free translations,
- view the original text (Hebrew and Greek) word by word, with Strong's numbers, morphology, and syntax,
- keep your own notes, highlights, bookmarks, documents, to-dos, and prayers,
- prepare sermons,
- explore cross-references as graphs, on the map, in a timeline, and as a family tree,
- study together with another person, connected directly,
- use an AI assistant (Claude or a local Ollama model) if you like.

The guiding principle is **Scripture alone**: the Bible text comes first. Commentaries are the work of people and are therefore hidden by default (see [Chapter 13](13-settings.md)).

**The program contains no Bible texts.** Translations, dictionaries, and data are downloaded from their original source only when you need them, and are then kept in the cache on your computer. You can see the license and origin in the footer and under *Settings → Sources and licenses*.

## 1.2 Installation

Download the right file from [Releases](https://github.com/Trofline/BibleStudy/releases):

| File | Purpose |
|---|---|
| `BibleStudy-Setup-x.y.z.exe` | Windows installer without administrator rights, with uninstaller and automatic updates |
| `BibleStudy-Portable-x.y.z.zip` | Windows portable: extract and start. All data is stored in the `Data` folder next to it |
| `BibleStudy-Linux-x64-x.y.z.tar.gz` | Linux |
| `BibleStudy-macOS-arm64-x.y.z.zip` / `BibleStudy-macOS-x64-x.y.z.zip` | macOS (Apple chip / Intel) |
| `SHA256SUMS.txt` | Checksums to verify your download |

Notes:

- The Linux and macOS packages are built on Windows. So far the program has only been started on an Intel Mac; other systems are untested. The packages include instructions (`README-macOS.txt`, `README-Linux.txt`). On the Mac, you start `Start-BibleStudy.command`.
- The Windows installer is not signed, so Windows (SmartScreen) may show a warning. This is normal for free software without a certificate; you can compare the checksum from `SHA256SUMS.txt` with that of your file.

## 1.3 Where Your Data Lives

Your data is kept separate from the program:

- **Installed:** `%LOCALAPPDATA%\BibleStudy`
- **Portable:** the `Data` folder next to the program

Updates and reinstallations never touch this folder. Before every update, the program creates a backup. When you uninstall, you are always asked whether the data should be kept.

The data folder contains, among other things:

- the **vaults** (your folders with highlights, notes, documents …), each a single file,
- the **settings** (a JSON file),
- the **cache** with downloaded translations and dictionaries,
- backups and plugins.

## 1.4 The First Start

On first start, the program asks you to agree to short terms of use. After that you see the **Home** page with all the books of the Bible. Click a book to see its chapters, and click a chapter to open it.

The first time you open a translation, it is downloaded. If you want to work without internet, turn on offline availability for the selected translation under *Settings* (see [Chapter 13](13-settings.md)).

### The Tour

On the **Help** tab (right sidebar, "More" group), the *Start tour* button starts a guided tour through the most important buttons. The button or area currently being explained is **outlined in red**, so it is clear what the text refers to.

## 1.5 The Interface at a Glance

The window consists of four areas.

### Top Bar

From left to right (some buttons only appear in Advanced mode):

- **House icon:** Home page with all books (Alt+Home).
- **Vault:** shows the active folder; click to switch.
- **Easy / Advanced:** switches between the full feature set and the streamlined interface (see 1.7).
- **To-dos:** your free-form checklist, reachable from anywhere.
- **Feedback:** report bugs, suggest ideas.
- **Keyboard shortcuts:** view and change them.
- **Layouts:** save and load window arrangements.
- **Settings:** Settings, help, info.
- **Window buttons:** minimize, maximize, close.

### Selection Row

- **Go to …** (Ctrl+K): a field for Bible passages, search, and commands.
- **Language, Translation, Book, Chapter** and the ◀ ▶ arrows for paging.

### Toolbar Above the Text

**Read**, **A− / A+** (Font size), **View**, **Graph**, **Map**, **Timeline**, **Family tree**, **Parallel**, **Word study**, **Read aloud**, **Highlight**, **Bookmark**.

### Reading Area and Right Sidebar

The Bible text is in the middle. On the right is the **sidebar** with the groups *Search, Verse, Books, Notes, Bookmarks, Documents, Sermon, Planning, Together, AI*, and *More*. Clicking an icon shows the group's views as pills; below them, a sentence explains what the selected view is for. With Ctrl+B you hide the whole sidebar, and you drag its edge to change its width.

You can drag any view out of the sidebar or move it into its **own window** with the ⧉ button (Ctrl+Shift+O), even onto a second monitor.

### Footer

On the left is the status message; on the right are the update status and the version number (clicking it shows the patch notes). The license of the selected translation is also shown here.

## 1.6 Quick Access to Everything: Ctrl+K

The **Go to …** field understands:

- Bible passages ("Rom 8:28", "John 15:11; 16:24", "Matt 14:13-21"),
- books,
- commands (for example "Switch theme"),
- your documents, notes, prayers,
- Strong's numbers, people, and places,
- free words: then the text search starts.

Results appear as you type. Arrow keys select, Enter opens.

## 1.7 Easy and Advanced

The *Easy / Advanced* button at the top switches between two interfaces:

- **Advanced:** everything is visible.
- **Easy:** only Reading, Search, Notes, Settings, Help, and Documentation. Buttons such as Parallel, Graph, Word study, To-dos, Feedback, and Keyboard shortcuts disappear, as does the "Verse" group.

Nothing is lost in the process, and your choice is remembered.

## 1.8 The Program's Language

The interface is available in German, English, Spanish, French, and Russian. The translations were made by AI, and corrections are welcome. You can add another language yourself (see [Chapter 13](13-settings.md), section "Custom language").

## 1.9 When Something Doesn't Work

See [Chapter 17](17-troubleshooting.md). You can report a bug via the feedback icon at the top; nothing is sent until you submit the ticket on GitHub yourself.
