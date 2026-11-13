using System.Text;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Exporting a document takes along what it refers to, so that its links can still be clicked in the file: the documents it links to
/// (and those they link to) and the text of the verses it mentions. In one HTML page they are sections further down; in Markdown, files next to each other.
/// </summary>
public partial class MainWindow
{
    /// <summary>A verse reference as it stands in a text, with its text from the translation in use, and the place it gets in the export.</summary>
    sealed record ExportVerse(string Span, string Quote, string Anchor);

    /// <summary>The documents the text links to, and those they link to, each once (not the document itself).</summary>
    List<Document> LinkedDocuments(string body, long selfId)
    {
        var result = new List<Document>();
        var seen = new HashSet<long> { selfId };
        var all = _vault.GetDocuments();
        var queue = new Queue<DocumentLinks.DocLink>(DocumentLinks.Links(body));
        while (queue.Count > 0)
        {
            if (VaultStore.Resolve(all, queue.Dequeue()) is not { } d || !seen.Add(d.Id)) continue;
            result.Add(d);
            foreach (var l in DocumentLinks.Links(d.Body)) queue.Enqueue(l);
        }
        return result;
    }

    /// <summary>The verses the texts mention, each once (at most 200; one that cannot be loaded stays plain text).</summary>
    async Task<List<ExportVerse>> ExportVersesAsync(IEnumerable<string> bodies)
    {
        var result = new List<ExportVerse>();
        var seen = new HashSet<string>();
        foreach (var body in bodies)
            foreach (var (index, length, r) in DocumentLinks.VerseSpans(body))
            {
                var span = body.Substring(index, length);
                if (result.Count >= 200 || !seen.Add(span)) continue;
                IReadOnlyList<PassageRef> passages = PassageParser.Parse(span, out _) is { Count: > 0 } parsed ? parsed : [new PassageRef(r.Book, r.Chapter, r.Verse, r.Verse)];
                if (await QuotePassagesAsync(passages) is { } quote) result.Add(new ExportVerse(span, quote, $"vers-{result.Count + 1}"));
            }
        return result;
    }

    (string Title, string Body, string Tags, long Id, List<Document> Linked) CurrentDocumentForExport()
    {
        var body = DocBodyBox.Text ?? "";
        return (DocTitleBox.Text?.Trim() ?? "", body, DocTagsBox.Text?.Trim() ?? "", _doc?.Id ?? 0, LinkedDocuments(body, _doc?.Id ?? 0));
    }

    async Task SaveDocumentAsHtmlAsync()
    {
        var (title, body, tags, id, linked) = CurrentDocumentForExport();
        if (title.Length == 0 && body.Trim().Length == 0) { DocInfo.Text = L("Nichts zu exportieren."); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L("Dokument als HTML speichern"), SuggestedFileName = SafeName(title, L("Dokument")) + ".html", DefaultExtension = "html",
            FileTypeChoices = [new FilePickerFileType("HTML") { Patterns = ["*.html"] }],
        });
        await using var target = await file.SaveTargetAsync();
        if (target?.Path is not { } path) return;
        var html = await HandoutHtmlAsync(title, body, tags, id, linked, false);   // the same designed sheet as for printing, with its links
        try
        {
            await File.WriteAllTextAsync(path, html, new UTF8Encoding(false));
            StatusText.Text = linked.Count == 0 ? L("Gespeichert: {0}", path) : L("Gespeichert: {0} (mit {1} verlinkten Dokumenten)", path, linked.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = L("Speichern nicht möglich: ") + ex.Message; }
    }

    /// <summary>
    /// The Markdown files of an export, by file name, the document's own first. With the default choices: one file for the document, one for
    /// each linked document and one with the verses, and the links point at the files. The choices can put the verses as quotes right under
    /// the text instead, leave them out, keep the linked documents out, or give the files a head (YAML) with date and tags, as Obsidian reads it.
    /// </summary>
    async Task<List<(string File, string Text)>> MarkdownFilesAsync(string title, string body, string tags, long selfId, List<Document> linked)
    {
        var s = _settings;
        if (!s.MarkdownLinked) linked = [];
        var verses = s.MarkdownVerses == "none" ? [] : await ExportVersesAsync(linked.Select(d => d.Body).Prepend(body));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string FileFor(string t) { var n = SafeName(t, L("Dokument")); var f = n + ".md"; for (var i = 2; !used.Add(f); i++) f = $"{n} ({i}).md"; return f; }
        // in an export names no longer change: each document gets a file named after its title, and the links point at the files
        var pages = linked.Prepend(new Document(selfId, title, body, null, false, DateTime.Now, [])).ToList();
        var files = pages.ToDictionary(p => p, p => FileFor(p.Title));
        (string Title, string File)? Target(DocumentLinks.DocLink l) => VaultStore.Resolve(pages, l) is { } d ? (d.Title, files[d]) : null;
        var inline = s.MarkdownVerses == "inline";
        var verseFile = inline || verses.Count == 0 ? "" : FileFor(L("Bibelstellen"));
        var byVerse = verses.ToDictionary(v => v.Span, v => Uri.EscapeDataString(verseFile) + "#" + v.Anchor);
        string? VerseHref(string span) => verseFile.Length > 0 && byVerse.TryGetValue(span, out var h) ? h : null;
        string Head(string t, string tagText) => !s.MarkdownFrontMatter ? "" :
            "---\ntitle: \"" + t.Replace("\"", "'") + "\"\ndate: " + DateTime.Now.ToString("yyyy-MM-dd") + "\n"
            + (HandoutTags(tagText) is { Length: > 0 } tg ? "tags: [" + string.Join(", ", tg.Split(", ").Select(x => "\"" + x.Replace("\"", "'") + "\"")) + "]\n" : "") + "---\n\n";
        string Page(Document p, string tagText)
        {
            var text = DocumentLinks.ToMarkdownLinks(DocumentLinks.WithoutTitleHeading(p.Body, p.Title), Target, VerseHref);
            if (inline)
            {
                // each verse right under the paragraph that names it, as a quote
                var mine = verses.Where(v => p.Body.Contains(v.Span)).ToList();
                if (mine.Count > 0)
                {
                    var paras = text.ReplaceLineEndings("\n").Split("\n\n").ToList();
                    var done = new HashSet<string>();
                    for (var i = 0; i < paras.Count; i++)
                    {
                        var add = mine.Where(v => paras[i].Contains(v.Span) && done.Add(v.Span)).Select(v => v.Quote.TrimEnd()).ToList();
                        if (add.Count > 0) paras[i] += "\n\n" + string.Join("\n\n", add);
                    }
                    text = string.Join("\n\n", paras);
                }
            }
            return Head(p.Title, tagText) + "# " + p.Title + "\n\n" + text.TrimEnd() + "\n";
        }
        var result = pages.Select((p, i) => (files[p], Page(p, i == 0 ? tags : string.Join(", ", p.Tags)))).ToList();
        if (verseFile.Length > 0)
        {
            var sb = new StringBuilder(Head(L("Bibelstellen"), tags) + "# " + L("Bibelstellen") + "\n\n");
            foreach (var v in verses) sb.Append($"<a id=\"{v.Anchor}\"></a>\n\n## {v.Span}\n\n{v.Quote}");
            result.Add((verseFile, sb.ToString()));
        }
        return result;
    }

    async Task WriteDocumentMarkdownAsync(string dir, string title, string body, string tags, long selfId, List<Document> linked)
    {
        Directory.CreateDirectory(dir);
        var utf8 = new UTF8Encoding(false);
        foreach (var (file, text) in await MarkdownFilesAsync(title, body, tags, selfId, linked)) await File.WriteAllTextAsync(Path.Combine(dir, file), text, utf8);
    }

    async Task SaveDocumentAsMarkdownAsync()
    {
        var (title, body, tags, id, linked) = CurrentDocumentForExport();
        if (title.Length == 0 && body.Trim().Length == 0) { DocInfo.Text = L("Nichts zu exportieren."); return; }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L("Ordner für den Markdown-Export wählen") });
        if (folders.Count == 0) return;
        if (folders[0].LocalPath() is not { } parent) { StatusText.Text = StorageFiles.NoFolderHere; return; }
        try
        {
            // a folder of its own, so the files of the linked documents never land between other files
            var dir = Path.Combine(parent, SafeName(title, L("Dokument")));
            await WriteDocumentMarkdownAsync(dir, title, body, tags, id, linked);
            StatusText.Text = L("{0} Dokument(e) als Markdown gespeichert in {1}", 1 + (_settings.MarkdownLinked ? linked.Count : 0), dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = L("Speichern nicht möglich: ") + ex.Message; }
    }

}
