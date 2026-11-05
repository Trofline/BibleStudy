# 15. Vaults, Backup and Privacy

## 15.1 What a vault is

A **vault** is a single file (SQLite) holding everything personal in a study project:

- Highlights (with folders, tags, categories),
- Notes and bookmarks,
- Documents (including sermons),
- Reading list, to-dos, prayers,
- Reading progress, memory verses, your own cross-references.

You can keep several vaults, for example "Sermon" and "Private", and switch between them with the vault button at the top. This is the best way to keep things separate.

## 15.2 Backup

### Automatic

*Settings → Backup*: **Daily, Weekly or Monthly**; the destination is a folder of your choice. Before every update and before deleting a vault, the program also creates a backup on its own.

**Cleaning up:** every update creates a backup, which can take a lot of space. *Settings → Backup → Clean up backups …* lists all backups with date, kind (for example “Before the update to 1.5.2”) and size. Check what can go, or choose “All but the 3 newest”; deleting asks first and is permanent.

### Manual

- **Back up now** creates a ZIP with all your data.
- **Export …** writes only the active vault, along with its settings, as a ZIP.

### Bringing it back

**Import …** creates a **new vault** from an export ZIP; your existing one stays untouched.

### Multiple devices

Put the backups in a **Syncthing folder**. **Never sync the live data folder**, as this can corrupt files.

## 15.3 Safe storage

The program writes files **atomically**: first a temporary file is created, which then takes the place of the old one. A crash in the middle of saving therefore never leaves a half-written file behind. If the settings ever become unreadable or locked, the old file is kept instead of being overwritten.

## 15.4 Privacy

- **No account, no sign-in, no telemetry.** The program only contacts GitHub to check for updates.
- The program contains no Bible texts; it downloads them from their original sources when you need them. The same goes for the audio Bible (eBible.org): a chapter is only downloaded when you want to hear it.
- Your notes, documents and highlights never leave your computer unless you deliberately send them (Study together, export).
- **AI:** With Claude Code, your question goes to Anthropic (through your own sign-in). With **Ollama**, everything stays local.
- **Feedback:** A ticket is public on GitHub. Nothing is sent until you submit it there yourself.
- **Study together:** The other person sees your internet address. Only what you both send is transmitted.

## 15.5 Uninstalling

When you remove the program, it always asks whether **your data should stay**. Without the checkmark, vaults and settings are kept.

## 15.6 Liability

BibleStudy is free software provided **without warranty** (see the MIT license). Third-party content (translations, commentaries, data), your own imports, and anything you share are your responsibility.
