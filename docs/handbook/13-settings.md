# 13. Settings

Open: gear icon at the top right, or the right-hand bar → **More → Settings**. The settings are organized into sections that you can collapse; a **jump list** with the full names takes you straight to a section.

All settings are saved automatically.

## 13.1 Language

Language of the interface: German, English, Spanish, French, Russian.

### Add your own language

Your language is missing? Save the **template with the AI instruction**, have an AI translate it, and **import the language file**. Missing texts stay German. *Export only missing texts* saves work for an incomplete language. The file is a JSON in which the German text is the key.

## 13.2 Appearance

- **Theme:** System, Light, Dark.
- **Accent color** of the program: six ready-made colors or your own with **+** (color picker with preview; light and dark are adjusted so text stays readable). Remove your own colors with a right click.
- **Font:** size, line spacing, justified text, an easier-to-read font (for example for dyslexia), high contrast.
- **Verse numbers:** small superscript, normal in the text, hidden.
- **Words of Jesus** highlighted in color, with their own color (independent of the color scheme).
- **Hebrew:** show/hide vowel points and accent marks (cantillation).
- **Translate word meanings into my language** (AI).
- **Show commentaries** (human work, not the Bible text; takes effect after a restart).
- **Animations:** sidebar, tabs, and chapters slide in smoothly; strength from *Subtle* to *Big*.
- **Large chapter header** (otherwise plain).
- **Program icon** for window, taskbar, title bar, and splash screen.
- **Reset appearance.**

## 13.3 Behavior

- Show the **Home** page at start.
- Mark chapters as **read** when you page forward (▶).
- Mark chapters as read when you have **scrolled** to the end.
- Assign the **book name as a tag** when saving.
- **Default highlight color.**
- **Read aloud: speed.**
- **Read aloud: voice.** Windows voice or natural voice (Piper, downloaded the first time).
- **Clear search history.**

## 13.4 Windows and layouts

You can move any area of the sidebar into its **own window** (the ⧉ button next to the tabs or Ctrl+Shift+O) and place it freely, even on a second monitor. You save the arrangement as a **layout** (the *Layouts* button at the top, for example "Dashboard") and load it again at any time.

More options:

- Start with Windows (only with the fully installed program file, not with `dotnet run`),
- Starting state of the window: Normal, Maximized, Minimized,
- Show icon in the notification area (system tray),
- Minimize to the notification area when closing (quit via right-click on the icon),
- Remember window position and size,
- Restore open windows (documents, books …) at start.

## 13.5 Keyboard shortcuts

Click a combination and press the new key. **Delete** removes an assignment, **Esc** cancels. *Reset all to default* restores the original assignments. The default assignments are listed in [Chapter 14](14-shortcuts.md).

## 13.6 Vault

A **vault** is like a separate folder for a study project: highlights, notes, documents, reading list, to-dos, and your reading progress always belong to exactly one vault. You can create several (for example "Sermon" and "Private") and switch between them here. Each vault is a single file.

- **Create** makes a new vault.
- **Delete this vault …** deletes it permanently, along with everything in it. The program creates a backup first; afterwards it can only be restored from that backup.

## 13.7 Import your own translation

*Choose file …* imports a Bible from a file. Imported translations stay on your computer, for your private use only, and can be **removed** again under "Imported translations".

## 13.8 Offline

*Make the chosen translation available offline* downloads it completely. Only texts that the source offers for free use are downloaded; the license and attribution are shown in the footer. The data is stored locally and belongs to you.

## 13.9 Sources and licenses

Shows **where each piece of information comes from** and what you may do with it. None of it is built into the program: it is only downloaded when you need it, and is then kept in the cache. Buttons: *Copy list*, *Open cache folder*, *Read the note about the origin again*.

## 13.10 Cross-references and dictionary

- **Hidden cross references …:** the list of what you have hidden, with the option to restore items.
- Show the **STEPBible brief lexicon** below the Strong entries.

## 13.11 Plugins

Open plugin folder, **Reload**, allow plugins, guide for authors (see [Chapter 12](12-plugins.md)).

## 13.12 Feedback and Windows search

*Report a bug …*, *Suggest an idea …*, and *Give feedback …* open a prepared **public ticket** on GitHub (you need an account there for this). Version and system are already in the text, your data never is, and nothing is sent before you submit it there yourself. *View ideas and bugs …* shows all tickets. *Show last error* shows what the program logged most recently.

*Add to the Start menu (Windows search)* makes sure Windows can find BibleStudy through its search.

## 13.13 Backup and sharing

- **Export …** writes the active vault (highlights, folders, tags, notes, documents, reading list, to-dos, reading progress) and your settings as a ZIP. **Import …** creates a new vault from it.
- **Export as Obsidian vault …** (see [Chapter 6](06-documents-knowledge.md)).
- **Automatic backup** of all data (ZIP): Off, Daily, Weekly, Monthly; **Choose backup folder …**; **Back up now**.
- **Use a Syncthing folder …:** To share between several devices, you put the backups in a Syncthing folder. **Never sync the live data folder itself**; that can corrupt files.

More in [Chapter 15](15-backup-privacy.md).

## 13.14 AI

Claude Code (with Bible tools) or Ollama; the instruction for Claude; the MCP server (see [Chapter 11](11-ai.md)).

## 13.15 Updates and program

- **Check for updates now.**
- **Patch notes** (what's new).
- **Project on GitHub.**
- **Restart program.**
- **Remove program …** (asks whether your data should be kept).
