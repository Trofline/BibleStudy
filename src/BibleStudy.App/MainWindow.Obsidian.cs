using System.Text;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Writes the active vault as a folder of Markdown files that Obsidian (or any editor) opens directly.</summary>
public partial class MainWindow
{
    static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '#', '^', '[', ']', '|' }).ToHashSet();
        var s = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim(' ', '.');
        return s.Length == 0 ? "Ohne Titel" : s.Length > 120 ? s[..120] : s;
    }

    static string ObsidianTag(string tag) => "#" + string.Concat(tag.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '/' ? c : '_'));

    async Task ExportObsidianAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L("Ordner für den Obsidian-Export wählen"), AllowMultiple = false });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } parent) return;
        try
        {
            var root = Path.Combine(parent, SafeFileName($"BibleStudy – {_vault.Name}"));
            var count = await Task.Run(() => WriteObsidian(root));
            BackupInfo.Text = L("{0} Dateien geschrieben nach {1}. In Obsidian: „Ordner als Vault öffnen“.", count, root);
        }
        catch (Exception ex) { BackupInfo.Text = L("Export fehlgeschlagen: ") + ex.Message; }
    }

    int WriteObsidian(string root)
    {
        var files = 0;
        void Write(string relative, string content) { var p = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, content, new UTF8Encoding(false)); files++; }

        // documents: one note per document, [[links]] keep working because file name = title
        var docs = _vault.GetDocuments();
        foreach (var d in docs)
        {
            var sb = new StringBuilder();
            if (d.Tags.Count > 0) sb.AppendLine("---").AppendLine($"tags: [{string.Join(", ", d.Tags.Select(t => '"' + t.Replace("\"", "'") + '"'))}]").AppendLine("---");
            if (d.Path is not null) sb.AppendLine($"Verknüpfte Datei: [{Path.GetFileName(d.Path)}](<file:///{d.Path.Replace('\\', '/')}>)").AppendLine();
            sb.AppendLine(d.Body);
            Write(Path.Combine("Dokumente", SafeFileName(d.Title) + ".md"), sb.ToString());
        }

        // marks: one note per folder, consecutive verses as one range
        var folders = _vault.GetFolders().ToDictionary(f => f.Id, f => f.Name);
        foreach (var group in _vault.GetMarks().GroupBy(m => m.FolderId))
        {
            var name = group.Key is long id && folders.TryGetValue(id, out var n) ? n : "Ohne Ordner";
            var sb = new StringBuilder($"# {name}\n\n");
            var list = group.OrderBy(m => BookOrder(m.Ref.Book)).ThenBy(m => m.Ref.Chapter).ThenBy(m => m.Ref.Verse).ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var first = list[i]; var last = first; var texts = new List<string> { first.Text };
                while (i + 1 < list.Count && list[i + 1] is var nx && nx.WholeVerse && last.WholeVerse && nx.Ref.Book == last.Ref.Book && nx.Ref.Chapter == last.Ref.Chapter && nx.Ref.Verse == last.Ref.Verse + 1 && nx.Note == first.Note)
                { texts.Add(nx.Text); last = nx; i++; }
                var label = last == first ? Label(first.Ref) : $"{BookName(first.Ref.Book)} {first.Ref.Chapter},{first.Ref.Verse}–{last.Ref.Verse}";
                sb.AppendLine($"## {label}");
                sb.AppendLine($"> {(first.WholeVerse ? string.Join(" ", texts) : "„" + first.Text + "“")}").AppendLine();
                if (first.Note.Length > 0) sb.AppendLine(first.Note).AppendLine();
                if (first.Tags.Count > 0) sb.AppendLine(string.Join(" ", first.Tags.Select(ObsidianTag)));
                sb.AppendLine($"Farbe: `{first.Color}`").AppendLine();
            }
            Write(Path.Combine("Markierungen", SafeFileName(name) + ".md"), sb.ToString());
        }

        var notes = new StringBuilder("# Notizen zu Versen\n\n");
        foreach (var n in _vault.GetNotes().OrderBy(n => BookOrder(n.Ref.Book)).ThenBy(n => n.Ref.Chapter).ThenBy(n => n.Ref.Verse))
            notes.AppendLine($"## {Label(n.Ref)}").AppendLine(n.Text).AppendLine();
        Write("Notizen.md", notes.ToString());

        var queue = new StringBuilder("# Leseliste\n\n");
        foreach (var q in _vault.GetQueue()) queue.AppendLine($"- [ ] {q.Label}");
        Write("Leseliste.md", queue.ToString());

        var todos = new StringBuilder("# Aufgaben\n\n");
        foreach (var t in _vault.GetTodos()) todos.AppendLine($"- [{(t.Done ? "x" : " ")}] {t.Text}{(t.Ref is { } r ? $" ({Label(r)})" : "")}");
        Write("Aufgaben.md", todos.ToString());

        var start = new StringBuilder($"# {_vault.Name}\n\nExport aus BibleStudy vom {DateTime.Now:dd.MM.yyyy HH:mm}.\n\n## Dokumente\n");
        foreach (var d in docs.OrderBy(d => d.Title)) start.AppendLine($"- [[{SafeFileName(d.Title)}]]");
        start.AppendLine("\n## Weitere\n- [[Notizen]]\n- [[Leseliste]]\n- [[Aufgaben]]");
        foreach (var f in folders.Values.Order()) start.AppendLine($"- [[{SafeFileName(f)}]] (Markierungen)");
        Write("Start.md", start.ToString());
        return files;
    }
}
