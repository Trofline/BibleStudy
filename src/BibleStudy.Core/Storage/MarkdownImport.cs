using System.Text;
using System.Text.RegularExpressions;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Storage;

/// <summary>
/// Reads a folder of Markdown files (an Obsidian vault, or what BibleStudy itself exported) into a vault: every file becomes a document (the folders of the
/// disk become folders of the vault), a file of the export with a "verse:" head becomes the note of that verse. What the vault already has is never replaced:
/// a document of the same title is merged line by line with the file (what both have stays once), so a file edited elsewhere comes back without a copy.
/// </summary>
public static partial class MarkdownImport
{
    public enum PageKind { Document, Note }

    public sealed record Page(PageKind Kind, string Title, string Body, IReadOnlyList<string> Tags, VerseRef? Verse);

    public sealed record Report(int Added, int Merged, int Unchanged, int Notes, int Skipped, int Folders, IReadOnlyList<string> Problems);

    // ---- one file ---------------------------------------------------------------------------------------------------------

    /// <summary>The page a file stands for; <paramref name="fileName"/> without its ending is the title when the file does not say otherwise.</summary>
    public static Page Parse(string text, string fileName)
    {
        text = text.TrimStart('﻿').ReplaceLineEndings("\n");
        var tags = new List<string>();
        string? title = null, verse = null;
        if (text.StartsWith("---\n", StringComparison.Ordinal) && FrontEnd(text) is int end)
        {
            var lines = text[4..end].Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var m = YamlKey().Match(lines[i]);
                if (!m.Success) continue;
                var key = m.Groups["k"].Value.ToLowerInvariant(); var value = m.Groups["v"].Value.Trim();
                if (key is "tags" or "tag")
                {
                    if (value.Length == 0) { while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith("- ", StringComparison.Ordinal)) tags.Add(lines[++i].TrimStart()[2..]); }
                    else tags.AddRange(value.Trim('[', ']').Split(',').SelectMany(t => t.Contains(',') || t.Trim().StartsWith('"') ? [t] : t.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)));
                }
                else if (key == "title") title = Unquote(value);
                else if (key == "verse") verse = Unquote(value);
            }
            text = text[(end + 4)..].TrimStart('\n');
        }
        // a first heading that only gives the title
        var body = text;
        var first = body.TrimStart('\n');
        var eol = first.IndexOf('\n');
        var firstLine = (eol < 0 ? first : first[..eol]).Trim();
        var heading = firstLine.StartsWith("# ", StringComparison.Ordinal) ? firstLine[2..].Trim() : null;
        string fallback = Path.GetFileNameWithoutExtension(fileName).Trim();

        VerseRef? at = null;
        if (verse is { Length: > 0 } && DocumentLinks.VerseSpans(verse) is { Count: > 0 } spans) at = spans[0].Ref;
        if (at is not null && heading is not null)
        {
            // a note of the export: "# Römer 8,28", the verse as a quote, the note, and a list of the notes that mention it
            body = eol < 0 ? "" : first[(eol + 1)..].TrimStart('\n');
            while (body.StartsWith('>')) { var e = body.IndexOf('\n'); body = e < 0 ? "" : body[(e + 1)..]; }
            body = MentionedIn().Replace(body.TrimStart('\n'), "");
        }
        else if (heading is not null && (title is null || string.Equals(heading, title, StringComparison.OrdinalIgnoreCase)))
        {
            title ??= heading;
            body = eol < 0 ? "" : first[(eol + 1)..].TrimStart('\n');
        }
        body = FromMarkdownLinks(body).TrimEnd();
        tags.AddRange(InlineTags(body));
        var clean = tags.Select(t => t.Trim().Trim('"', '\'', '#').Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return at is { } v
            ? new Page(PageKind.Note, Path.GetFileNameWithoutExtension(fileName), body.Trim(), clean, v)
            : new Page(PageKind.Document, string.IsNullOrWhiteSpace(title) ? fallback : title.Trim(), body, clean, null);
    }

    static int? FrontEnd(string text)
    {
        var at = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        while (at >= 0)
        {
            var after = at + 4;
            if (after >= text.Length || text[after] == '\n') return at;
            at = text.IndexOf("\n---", after, StringComparison.Ordinal);
        }
        return null;
    }

    static string Unquote(string s) => s.Length >= 2 && (s[0] == '"' && s[^1] == '"' || s[0] == '\'' && s[^1] == '\'') ? s[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\") : s;

    /// <summary>Links of Markdown ([text](File.md)) become links between documents ([[File|text]]); verses that were links go back to plain text, the program links them itself.</summary>
    public static string FromMarkdownLinks(string body)
    {
        body = MarkdownLink().Replace(body, m =>
        {
            var target = m.Groups["u"].Value.Trim('<', '>');
            var hash = target.IndexOf('#'); if (hash >= 0) target = target[..hash];
            if (!target.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || target.Contains("://", StringComparison.Ordinal)) return m.Value;
            string name;
            try { name = Uri.UnescapeDataString(target); } catch (UriFormatException) { name = target; }
            var title = Path.GetFileNameWithoutExtension(name.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)).Trim();
            var shown = m.Groups["t"].Value.Trim();
            if (title.Length == 0) return m.Value;
            if (shown.Length == 0 || string.Equals(shown, title, StringComparison.OrdinalIgnoreCase)) return $"[[{title}]]";
            if (DocumentLinks.VerseSpans(shown) is { Count: 1 } one && one[0].Index == 0 && one[0].Length == shown.Length) return shown;
            return $"[[{title}|{shown}]]";
        });
        // Obsidian names a document with its folder: [[Folder/Title]]
        return FolderLink().Replace(body, m => "[[" + m.Groups["n"].Value + "]]");
    }

    static IEnumerable<string> InlineTags(string body)
    {
        var inCode = false;
        foreach (var line in body.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { inCode = !inCode; continue; }
            if (inCode) continue;
            foreach (Match m in InlineTag().Matches(line)) yield return m.Groups["t"].Value;
        }
    }

    [GeneratedRegex(@"^(?<k>[A-Za-z_][\w\-]*)\s*:\s*(?<v>.*)$")] private static partial Regex YamlKey();
    [GeneratedRegex(@"(?<!!)\[(?<t>[^\]\n]*)\]\((?<u><[^>\n]+>|[^)\s\n]+)(?:\s+""[^""]*"")?\)")] private static partial Regex MarkdownLink();
    [GeneratedRegex(@"\[\[(?:[^\]|#\n]*/)(?<n>[^\]\n]+)\]\]")] private static partial Regex FolderLink();
    [GeneratedRegex(@"(?<![\w&/#\[(])#(?<t>[A-Za-zÀ-ÿ][\w\-/À-ÿ]*)")] private static partial Regex InlineTag();
    [GeneratedRegex(@"\n*## [^\n]*\n\n(?:- \[\[[^\n]*(?:\n|$))+\s*$")] private static partial Regex MentionedIn();

    // ---- a folder ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads all .md files below <paramref name="dir"/> (folders that start with a dot, like .obsidian, are left alone). Lists that BibleStudy writes into
    /// its export (marks, reading list, bookmarks, to-dos, headings, start page) are not read back: their content lives in the vault already.
    /// </summary>
    public static Report ImportFolder(VaultStore store, string dir, IReadOnlySet<string>? listNames = null, Action<string>? progress = null)
    {
        int added = 0, merged = 0, same = 0, notes = 0, skipped = 0, madeFolders = 0;
        var problems = new List<string>();
        var folders = store.GetFolders().ToList();
        long? FolderFor(string relativeDir)
        {
            if (relativeDir is "" or ".") return null;
            long? parent = null;
            foreach (var part in relativeDir.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
            {
                var found = folders.FirstOrDefault(f => f.ParentId == parent && string.Equals(f.Name, part, StringComparison.OrdinalIgnoreCase));
                if (found is null)
                {
                    var id = store.AddFolder(part);
                    if (parent is long p) store.SetFolderParent(id, p);
                    found = new Folder(id, part, parent); folders.Add(found); madeFolders++;
                }
                parent = found.Id;
            }
            return parent;
        }

        var files = EnumerateMarkdown(dir).ToList();
        var docs = store.GetDocuments();
        var byTitle = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in docs) byTitle.TryAdd(d.Title.Trim(), d);
        var done = 0;
        foreach (var file in files)
        {
            if (++done % 20 == 0) progress?.Invoke($"{done} / {files.Count}");
            try
            {
                var text = File.ReadAllText(file);
                var name = Path.GetFileNameWithoutExtension(file);
                if (IsGeneratedList(text, name, listNames)) { skipped++; continue; }
                var page = Parse(text, name);
                if (page.Body.Length == 0 && page.Kind == PageKind.Document && page.Tags.Count == 0 && text.Trim().Length == 0) { skipped++; continue; }
                if (page.Kind == PageKind.Note)
                {
                    var mine = store.GetNote(page.Verse!.Value);
                    if (mine is null) { if (page.Body.Length > 0) { store.SetNote(page.Verse.Value, page.Body); notes++; } else skipped++; continue; }
                    var have = DocumentLinks.WithoutNumbers(mine.Text).Trim();
                    if (have == page.Body.Trim()) { same++; continue; }
                    if (VaultStore.MergeLines(have, page.Body, out _) is { } both && both.Trim() != have) { store.SetNote(page.Verse.Value, both.Trim()); merged++; } else same++;
                    continue;
                }
                var relative = Path.GetRelativePath(dir, Path.GetDirectoryName(file)!);
                if (byTitle.TryGetValue(page.Title, out var existing))
                {
                    var have = DocumentLinks.WithoutNumbers(existing.Body).TrimEnd();
                    var tagsNew = page.Tags.Where(t => !existing.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
                    if (have == page.Body.TrimEnd() && tagsNew.Count == 0) { same++; continue; }
                    var union = VaultStore.MergeLines(have, page.Body, out _);
                    if (union is null) { problems.Add(L(Path.GetRelativePath(dir, file), "zu groß zum Zusammenführen")); skipped++; continue; }
                    if (union.TrimEnd() == have && tagsNew.Count == 0) { same++; continue; }
                    store.UpdateDocument(existing.Id, existing.Title, union.TrimEnd(), existing.Path, existing.IsTemplate, existing.Tags.Concat(tagsNew), existing.FolderId);
                    merged++;
                    continue;
                }
                var id = store.AddDocument(page.Title, page.Body, null, false, page.Tags, FolderFor(relative));
                byTitle[page.Title] = store.GetDocuments().First(d => d.Id == id);
                added++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add(L(Path.GetRelativePath(dir, file), ex.Message)); skipped++; }
        }
        return new Report(added, merged, same, notes, skipped, madeFolders, problems);
    }

    static string L(string file, string reason) => $"{file}: {reason}";

    static IEnumerable<string> EnumerateMarkdown(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) yield return f;
        foreach (var d in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(d).StartsWith('.')) continue;
            foreach (var f in EnumerateMarkdown(d)) yield return f;
        }
    }

    /// <summary>The lists of an export of BibleStudy: the start page (it says where it comes from) and the files named like a list of the program.</summary>
    static bool IsGeneratedList(string text, string name, IReadOnlySet<string>? listNames)
    {
        if (text.Contains("<a id=\"", StringComparison.Ordinal) && text.Contains("\"></a>", StringComparison.Ordinal)) return true;   // the file with the verses of a document export
        if (listNames is not null && listNames.Contains(Regex.Replace(name, @" \(\d+\)$", "")) && text.TrimStart('﻿', '\n', '\r').StartsWith("# ", StringComparison.Ordinal)) return true;
        return false;
    }
}
