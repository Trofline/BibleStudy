# 17. Troubleshooting and Frequently Asked Questions

## 17.1 Problems at Startup

**Windows warns about the installer (SmartScreen).** The installer isn't signed, because code signing isn't in the budget for free software. Compare the file's checksum with `SHA256SUMS.txt` from the release. Click "More info → Run anyway" and it will start.

**Linux/macOS won't start.** The packages are built on Windows and have barely been tested elsewhere. Read `README-Linux.txt` or `README-macOS.txt` in the package; on a Mac, you start `Start-BibleStudy.command`. Please report problems as a ticket (Feedback button).

**The program shows only a white window or crashes.** *Settings → Feedback → Show last error* tells you what was logged. Attach that to a ticket.

## 17.2 Texts Are Missing or Won't Load

**A translation won't load.** You need an internet connection the first time you open it. After that, it's kept in the cache. To work without a network: *Make available offline*.

**No Strong's numbers.** Only some translations include them (for example KJV). Choose one of those, or use the plugin *Add Strong's numbers* (estimated, no guarantee of accuracy).

**Dictionary unavailable.** The dictionaries are loaded on demand; without a connection, a message appears there giving the reason.

## 17.3 Using the Program

**Right-clicking shows a "Copy Ctrl+C" button instead of the menu.** That was a bug in earlier versions and has been fixed. The Verse menu now stays on top, and *Copy marked text* is inside it.

**"Explain word" is greyed out.** First mark a single word (or two) in the text, then right-click.

**A shortcut does nothing.** Check in Settings whether it has been reassigned. Ctrl+B in the editor makes text bold, not the bar.

**My parallel columns have colors that are too similar.** Change them with the color button in the parallel bar ([Chapter 2.5](02-reading.md)).

**The bar is too crowded.** Switch to *Easy*, or move rarely used views into their own windows.

## 17.4 Data

**I deleted something.** Look in the automatic backups (*Settings → Backup*) and import the ZIP as a new vault.

**I want to take my data to another computer.** Use *Export …* on the old device and *Import …* on the new one; or copy the `Data` folder of the portable version.

**Where is my data?** See [Chapter 1.3](01-getting-started.md).

## 17.5 Studying Together

See [Chapter 10.4](10-studying-together.md).

## 17.6 Plugins

**The plugin doesn't appear.** The folder is in the wrong place or `plugin.json` is missing: *Open plugin folder*, check the folder, *Reload*.

**The plugin asks for permission again.** This is intentional: if its `plugin.json` has changed, you have to allow it again.

## 17.7 Questions and Answers

**Is the program really free?** Yes, MIT license, no ads and no account.

**Who is behind it?** A single developer; contributions are welcome (pull request, agreeing to the MIT license). Please only add data with a clear, free license.

**Why are modern German translations like Luther 2017, Elberfelder or Schlachter missing?** They are protected by copyright. The program only loads texts that their source offers for free use. If you own a file, you can import it yourself for private use (see [Chapter 13.7](13-settings.md)).

**Can I use several windows side by side?** Yes: drag views out of the bar, also onto a second monitor. Save it as a layout.

**Where do I report a bug?** Feedback button at the top, or directly on GitHub as a ticket.

**Where can I see what's new?** Click the version number in the footer (Patch notes).
