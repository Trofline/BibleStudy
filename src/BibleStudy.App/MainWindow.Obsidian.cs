using System.Diagnostics;
using System.Text;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The whole vault as a folder of Markdown files that Obsidian (or any other notes program) opens directly, and that stays readable without
/// this program: every folder becomes a folder, every note and every document a file of its own, the marks of a folder one list in it.
/// Verse references in notes and documents become [[links]] to the note of that verse, and every note lists the notes that mention it.
/// </summary>
public partial class MainWindow
{
    static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '#', '^', '[', ']', '|' }).ToHashSet();
        var s = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim(' ', '.');
        return s.Length == 0 ? L("Ohne Titel") : s.Length > 120 ? s[..120] : s;
    }

    /// <summary>A tag as Obsidian takes it: "1. Mose" → "#1-Mose" (blanks become "-", other signs go; never empty).</summary>
    static string ObsidianTag(string tag)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(string.Concat(tag.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '/' ? c : char.IsWhiteSpace(c) ? '-' : '\0').Where(c => c != '\0')), "-{2,}", "-").Trim('-');
        return "#" + (s.Length > 0 ? s : "Tag");
    }

    async Task ExportObsidianAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L("Ordner für den Obsidian-Export wählen"), AllowMultiple = false });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } parent) return;
        // a new folder every time: files of an older export (with other names) must not mix with this one
        var root = Path.Combine(parent, SafeFileName($"BibleStudy – {_vault.Name}"));
        if (Directory.Exists(root)) root += " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
        StatusText.Text = L("Export wird vorbereitet …");
        if (_exBuilt) ExNote(L("Export wird vorbereitet …"), false);
        try
        {
            var count = await WriteVaultMarkdownAsync(root);
            var done = L("{0} Dateien geschrieben nach {1}. In Obsidian: „Ordner als Vault öffnen“.", count, root);
            BackupInfo.Text = StatusText.Text = done;
            if (_exBuilt) ExNote(done, false);
            try { Process.Start(new ProcessStartInfo(root) { UseShellExecute = true }); } catch { /* the path is in the status line */ }
        }
        catch (Exception ex)
        {
            BackupInfo.Text = StatusText.Text = L("Export fehlgeschlagen: ") + ex.Message;
            if (_exBuilt) ExNote(L("Export fehlgeschlagen: ") + ex.Message, false);
        }
    }

    /// <summary>Writes the files; returns how many.</summary>
    async Task<int> WriteVaultMarkdownAsync(string root)
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

        var files = 0;
        void Write(string file, string content) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, content, new UTF8Encoding(false)); files++; }
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // two things with the same name (or names that only differ in case or in characters a file name cannot have) get their own file
        string Unique(string dir, string name)
        {
            var candidate = name;
            for (var n = 2; !used.Add(Path.Combine(dir, candidate)); n++) candidate = $"{name} ({n})";
            return candidate;
        }

        // every folder of the vault is a folder on the disk (a folder whose parent is gone counts as one at the top)
        var folders = _vault.GetFolders();
        var byId = folders.ToDictionary(f => f.Id);
        var paths = new Dictionary<long, string>();
        string PathOf(long id)
        {
            if (paths.TryGetValue(id, out var p)) return p;
            var parts = new List<string>();
            for (long? at = id; at is long a && byId.TryGetValue(a, out var f) && parts.Count < 50; at = f.ParentId) parts.Insert(0, SafeFileName(f.Name));
            return paths[id] = Path.Combine([root, .. parts]);
        }
        string Dir(long? folder) => folder is long f && byId.ContainsKey(f) ? PathOf(f) : root;
        Directory.CreateDirectory(root);
        foreach (var f in folders) Directory.CreateDirectory(PathOf(f.Id));

        var notes = _vault.GetNotes();
        var marks = _vault.GetMarks();
        var docs = _vault.GetDocuments();
        var noteAt = notes.ToDictionary(n => n.Ref);
        // the file name of a note is its verse ("Römer 8,28"); first come, first named
        var noteFiles = notes.ToDictionary(n => n.Ref, n => Unique(Dir(n.FolderId), SafeFileName($"{BookName(n.Ref.Book)} {n.Ref.Chapter},{n.Ref.Verse}")));
        var docFiles = docs.OrderBy(d => d.Id).ToDictionary(d => d.Id, d => Unique(Dir(d.FolderId), SafeFileName(d.Title)));

        // "Joh 3,16" in a text: a link to the note of that verse, when there is one
        string Linked(string text)
        {
            var sb = new StringBuilder();
            var at = 0;
            foreach (var (index, length, r) in DocumentLinks.VerseSpans(text))
            {
                if (index < at || !noteFiles.TryGetValue(r, out var file)) continue;
                sb.Append(text, at, index - at).Append("[[").Append(file).Append('|').Append(text, index, length).Append("]]");
                at = index + length;
            }
            return sb.Append(text, at, text.Length - at).ToString();
        }
        static string Yaml(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        static string Front(params (string Key, string Value)[] lines) =>
            lines.All(l => l.Value.Length == 0) ? "" : "---\n" + string.Concat(lines.Where(l => l.Value.Length > 0).Select(l => $"{l.Key}: {l.Value}\n")) + "---\n\n";
        static string TagList(IEnumerable<string> tags) => tags.Any() ? "[" + string.Join(", ", tags.Select(x => Yaml(ObsidianTag(x)[1..]))) + "]" : "";

        // notes: one file each, with the verse, the note and the notes that mention it
        var i = 0;
        foreach (var n in notes)
        {
            if (++i % 25 == 0) StatusText.Text = L("Export: {0} von {1} Notizen …", i, notes.Count);
            var md = new StringBuilder(Front(("verse", Yaml(Label(n.Ref)))));
            md.Append("# ").Append(Label(n.Ref)).Append("\n\n");
            if (await Text(n.Ref, n.Ref.Verse) is { Length: > 0 } verse) md.Append("> ").Append(verse).Append("\n\n");
            md.Append(Linked(n.Text.Trim())).Append('\n');
            var from = notes.Where(o => o.Ref != n.Ref && DocumentLinks.VerseRanges(o.Text).Any(x => x.First.Book == n.Ref.Book && x.First.Chapter == n.Ref.Chapter && n.Ref.Verse >= x.First.Verse && n.Ref.Verse <= x.EndVerse)).ToList();
            if (from.Count > 0)
            {
                md.Append("\n## ").Append(L("Erwähnt in Notizen zu:").TrimEnd(':')).Append("\n\n");
                foreach (var o in from) md.Append("- [[").Append(noteFiles[o.Ref]).Append("]]\n");
            }
            Write(Path.Combine(Dir(n.FolderId), noteFiles[n.Ref] + ".md"), md.ToString());
        }

        // documents: one file each, named after the title (so the [[Title]] links between them keep working)
        foreach (var d in docs)
        {
            var md = new StringBuilder(Front(("tags", TagList(d.Tags))));
            if (d.Path is not null) md.Append(L("Verknüpfte Datei:")).Append($" [{Path.GetFileName(d.Path)}](<file:///{d.Path.Replace('\\', '/')}>)\n\n");
            md.Append(Linked(d.Body));
            Write(Path.Combine(Dir(d.FolderId), docFiles[d.Id] + ".md"), md.ToString());
        }

        // marks: one list per folder, in Bible order, the marked words highlighted in the verse
        var markFiles = new List<(string Dir, string File)>();
        foreach (var group in marks.GroupBy(m => m.FolderId is long f && byId.ContainsKey(f) ? f : (long?)null))
        {
            var title = group.Key is long g ? byId[g].Name : L("Ohne Ordner");
            var file = Unique(Dir(group.Key), L("Markierungen"));
            markFiles.Add((Dir(group.Key), file));
            var md = new StringBuilder("# ").Append(L("Markierungen")).Append(" – ").Append(title).Append("\n\n");
            foreach (var m in group.OrderBy(m => BookOrder(m.Ref.Book)).ThenBy(m => m.Ref.Chapter).ThenBy(m => m.Ref.Verse))
            {
                var place = Label(m.Ref) + (m.MultiVerse ? "–" + m.LastVerse : "");
                md.Append("## ").Append(noteFiles.TryGetValue(m.Ref, out var nf) ? $"[[{nf}|{place}]]" : place).Append("\n\n");
                var verse = await Text(m.Ref, m.LastVerse);
                if (verse.Length > 0) md.Append("> ").Append(m.WholeVerse ? verse : Emphasized(verse, m.Text.Trim(), "==", "==")).Append("\n\n");
                else if (m.Text.Trim().Length > 0) md.Append("> ").Append(Loc.Quote(m.Text.Trim())).Append("\n\n");
                var info = new List<string> { L("Farbe:") + " " + (ColorName(m.Color) is { Length: > 0 } c ? c + $" (`{m.Color}`)" : $"`{m.Color}`") };
                if (m.Tags.Count > 0) info.Add(string.Join(" ", m.Tags.Select(ObsidianTag)));
                md.Append(string.Join(" · ", info)).Append("\n\n");
                if (m.Note.Trim().Length > 0) md.Append(Linked(m.Note.Trim())).Append("\n\n");
            }
            Write(Path.Combine(Dir(group.Key), file + ".md"), md.ToString());
        }

        // what has no folder of its own: own headings, reading list, bookmarks, to-dos
        var headings = _vault.GetHeadings();
        if (headings.Count > 0)
        {
            var hs = new StringBuilder("# " + L("Überschrift") + "\n\n");
            foreach (var h in headings.OrderBy(h => BookOrder(h.Ref.Book)).ThenBy(h => h.Ref.Chapter).ThenBy(h => h.Ref.Verse))
                hs.Append("- ").Append(Label(h.Ref)).Append(": ").Append(h.Text).Append('\n');
            Write(Path.Combine(root, Unique(root, L("Überschrift")) + ".md"), hs.ToString());
        }
        var queue = new StringBuilder("# " + L("Leseliste") + "\n\n");
        foreach (var q in _vault.GetQueue()) queue.Append("- [ ] ").Append(q.Label).Append('\n');
        Write(Path.Combine(root, Unique(root, L("Leseliste")) + ".md"), queue.ToString());
        var bookmarks = new StringBuilder("# " + L("Lesezeichen") + "\n\n");
        foreach (var b in _vault.GetBookmarks().OrderBy(b => BookOrder(b.Ref.Book)).ThenBy(b => b.Ref.Chapter).ThenBy(b => b.Ref.Verse))
            bookmarks.Append("- ").Append(noteFiles.TryGetValue(b.Ref, out var nf) ? $"[[{nf}|{Label(b.Ref)}]]" : Label(b.Ref)).Append('\n');
        Write(Path.Combine(root, Unique(root, L("Lesezeichen")) + ".md"), bookmarks.ToString());
        var todos = new StringBuilder("# " + L("To-dos") + "\n\n");
        foreach (var td in _vault.GetTodos()) todos.Append($"- [{(td.Done ? "x" : " ")}] {td.Text}{(td.Ref is { } r ? $" ({Label(r)})" : "")}\n");
        Write(Path.Combine(root, Unique(root, L("To-dos")) + ".md"), todos.ToString());

        // the start page: what is where
        string Rel(string dir, string file) => Path.GetRelativePath(root, Path.Combine(dir, file)).Replace('\\', '/');
        var exported = L("Export aus BibleStudy vom {0:dd.MM.yyyy HH:mm}.", DateTime.Now);
        var start = new StringBuilder("# " + _vault.Name + "\n\n" + exported);
        if (t is not null) start.Append(' ').Append(L("Bibeltext: {0}", t.ShortName));
        start.Append("\n\n");
        if (notes.Count > 0)
        {
            start.Append("## ").Append(L("Notizen zu Versen")).Append('\n');
            foreach (var n in notes.OrderBy(n => BookOrder(n.Ref.Book)).ThenBy(n => n.Ref.Chapter).ThenBy(n => n.Ref.Verse))
                start.Append("- [[").Append(Rel(Dir(n.FolderId), noteFiles[n.Ref])).Append('|').Append(Label(n.Ref)).Append("]]\n");
            start.Append('\n');
        }
        if (docs.Count > 0)
        {
            start.Append("## ").Append(L("Dokumente")).Append('\n');
            foreach (var d in docs.OrderBy(d => d.Title, StringComparer.CurrentCultureIgnoreCase))
                start.Append("- [[").Append(Rel(Dir(d.FolderId), docFiles[d.Id])).Append('|').Append(d.Title).Append("]]\n");
            start.Append('\n');
        }
        start.Append("## ").Append(L("Weitere")).Append('\n');
        foreach (var (dir, file) in markFiles.OrderBy(x => x.Dir)) start.Append("- [[").Append(Rel(dir, file)).Append("]]\n");
        start.Append($"- [[{L("Leseliste")}]]\n- [[{L("Lesezeichen")}]]\n- [[{L("To-dos")}]]\n");
        if (headings.Count > 0) start.Append($"- [[{L("Überschrift")}]]\n");
        Write(Path.Combine(root, Unique(root, "Start") + ".md"), start.ToString());
        return files;
    }
}
