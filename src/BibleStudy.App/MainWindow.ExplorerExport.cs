using System.Diagnostics;
using Avalonia.Controls;
using System.Text;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// A folder of the explorer as a hand-out: every mark and note in it (and in its sub-folders, each as a section of its own) with the
/// text of the verse in the translation that is open. As a print page (from there "Save as PDF") or as a Markdown file.
/// </summary>
public partial class MainWindow
{
    sealed record ExportEntry(VerseRef Ref, int LastVerse, string Place, string VerseText, string Marked, string Note, bool IsNote);
    sealed record ExportSection(string Title, int Depth, List<ExportEntry> Entries);

    async Task<List<ExportSection>> CollectFolderAsync(long? folder)
    {
        var t = TranslationBox.SelectedItem as TranslationInfo;
        var cache = new Dictionary<(string, int), Chapter?>();
        async Task<string> Text(VerseRef r, int last)
        {
            if (t is null) return "";
            if (!cache.TryGetValue((r.Book, r.Chapter), out var ch))
                try { cache[(r.Book, r.Chapter)] = ch = await _provider.GetChapterAsync(t.Id, r.Book, r.Chapter); } catch { cache[(r.Book, r.Chapter)] = ch = null; }
            if (ch is null) return "";
            return string.Join(" ", ch.Verses.Where(v => v.Number >= r.Verse && v.Number <= last)
                .Select(v => string.Concat(v.Segments.Where(x => x.NoteId is null).Select(x => x.Text)).Trim()));
        }

        var marks = _vault.GetMarks();
        var notes = _vault.GetNotes();
        var sections = new List<ExportSection>();
        async Task Add(long? id, string title, int depth)
        {
            var entries = new List<ExportEntry>();
            foreach (var m in marks.Where(m => Known(m.FolderId) == id))
                entries.Add(new ExportEntry(m.Ref, m.LastVerse, Label(m.Ref) + (m.MultiVerse ? "–" + m.LastVerse : ""), await Text(m.Ref, m.LastVerse), m.WholeVerse ? "" : m.Text.Trim(), m.Note.Trim(), false));
            foreach (var n in notes.Where(n => Known(n.FolderId) == id))
                entries.Add(new ExportEntry(n.Ref, n.Ref.Verse, Label(n.Ref), await Text(n.Ref, n.Ref.Verse), "", n.Text.Trim(), true));
            entries = [.. entries.OrderBy(e => BookOrder(e.Ref.Book)).ThenBy(e => e.Ref.Chapter).ThenBy(e => e.Ref.Verse).ThenBy(e => e.IsNote)];
            sections.Add(new ExportSection(title, depth, entries));
            foreach (var f in ChildFolders(id)) await Add(f.Id, f.Name, depth + 1);
        }
        await Add(folder, folder is long fid && FolderById(fid) is { } fo ? fo.Name : L("Alle Markierungen"), 0);
        // a sub-folder without anything in it (or below it) would only be an empty heading
        bool Filled(int i)
        {
            for (var j = i; j < sections.Count && (j == i || sections[j].Depth > sections[i].Depth); j++) if (sections[j].Entries.Count > 0) return true;
            return false;
        }
        return [.. sections.Where((s, i) => i == 0 || Filled(i))];
    }

    MenuItem ExportMenu(long? folder)
    {
        var menu = new MenuItem { Header = L("Exportieren") };
        menu.Items.Add(MenuAction(L("Als PDF / Drucken …"), () => _ = ExportFolderAsync(folder, markdown: false)));
        menu.Items.Add(MenuAction(L("Als Markdown-Datei"), () => _ = ExportFolderAsync(folder, markdown: true)));
        return menu;
    }

    async Task ExportFolderAsync(long? folder, bool markdown)
    {
        _exInfo.Text = L("Export wird vorbereitet …");
        var sections = await CollectFolderAsync(folder);
        var title = sections[0].Title;
        var total = sections.Sum(s => s.Entries.Count);
        if (total == 0) { ExNote(L("In „{0}“ liegt nichts, das exportiert werden könnte.", title), false); FillExplorerList(); return; }
        var translation = (TranslationBox.SelectedItem as TranslationInfo)?.ShortName ?? "";

        if (markdown)
        {
            var md = new StringBuilder();
            md.Append("# ").Append(title).Append("\n\n");
            if (translation.Length > 0) md.Append('*').Append(L("Bibeltext: {0}", translation)).Append("*\n\n");
            foreach (var s in sections)
            {
                if (s.Depth > 0) md.Append(new string('#', Math.Min(s.Depth + 1, 6))).Append(' ').Append(s.Title).Append("\n\n");
                foreach (var e in s.Entries)
                {
                    md.Append("**").Append(e.Place).Append("**").Append(e.IsNote ? " · " + L("Notiz") : "").Append("\n\n");
                    if (e.VerseText.Length > 0) md.Append("> ").Append(Emphasized(e.VerseText, e.Marked, "**", "**")).Append("\n\n");
                    else if (e.Marked.Length > 0) md.Append("> ").Append(e.Marked).Append("\n\n");
                    if (e.Note.Length > 0) md.Append(e.Note).Append("\n\n");
                }
            }
            var path = Path.Combine(ExportFolder, SafeName(title, L("Markierungen")) + ".md");
            File.WriteAllText(path, md.ToString(), new UTF8Encoding(false));
            StatusText.Text = L("Gespeichert: {0}", path);
            ExNote(L("„{0}“ als Markdown gespeichert ({1} Einträge).", title, total), false);
            try
            {
                if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else Shell.Open(ExportFolder);
            }
            catch { /* the path is in the status line */ }
        }
        else
        {
            var html = new StringBuilder("<style>.place{margin:16px 0 4px;font-weight:bold}.kind{color:#888;font-weight:normal;font-size:.85em}mark{background:#fbe9a6}</style>");
            html.Append("<h1>").Append(H(title)).Append("</h1>");
            html.Append("<p class=\"meta\">").Append(H(L("{0} Einträge", total) + (translation.Length > 0 ? " · " + L("Bibeltext: {0}", translation) : ""))).Append("</p>");
            foreach (var s in sections)
            {
                if (s.Depth > 0) html.Append(s.Depth == 1 ? "<h2>" : "<h3>").Append(H(s.Title)).Append(s.Depth == 1 ? "</h2>" : "</h3>");
                foreach (var e in s.Entries)
                {
                    html.Append("<p class=\"place\">").Append(H(e.Place)).Append(e.IsNote ? " <span class=\"kind\">· " + H(L("Notiz")) + "</span>" : "").Append("</p>");
                    if (e.VerseText.Length > 0) html.Append("<blockquote>").Append(Emphasized(H(e.VerseText), H(e.Marked), "<mark>", "</mark>")).Append("</blockquote>");
                    else if (e.Marked.Length > 0) html.Append("<blockquote>").Append(H(e.Marked)).Append("</blockquote>");
                    if (e.Note.Length > 0) html.Append(e.IsNote ? MarkdownToHtml(e.Note) : "<p>" + H(e.Note).ReplaceLineEndings("<br>") + "</p>");
                }
            }
            ExportPrint(title, html.ToString(), SafeName(title, L("Markierungen")));
            ExNote(L("„{0}“ ist im Browser geöffnet. Dort „Als PDF speichern“ wählen.", title), false);
        }
        FillExplorerList();
    }

    /// <summary>The verse text with the marked words wrapped (the first place they occur); unchanged when they are not found.</summary>
    static string Emphasized(string verse, string marked, string open, string close)
    {
        if (marked.Length == 0) return verse;
        var at = verse.IndexOf(marked, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? verse : verse[..at] + open + verse.Substring(at, marked.Length) + close + verse[(at + marked.Length)..];
    }
}
