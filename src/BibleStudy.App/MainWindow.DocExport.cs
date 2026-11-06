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

    /// <summary>The page of the document; after it the linked documents and the verses. Links between them jump inside the page.</summary>
    async Task<string> DocumentHtmlAsync(string title, string body, string tags, long selfId, List<Document> linked)
    {
        var verses = await ExportVersesAsync(linked.Select(d => d.Body).Prepend(body));
        // in the file names no longer change: the numbers of the links are only used to find the right place
        var pages = linked.Prepend(new Document(selfId, title, body, null, false, DateTime.Now, [])).ToList();
        string? Href(long? number, string t) => VaultStore.Resolve(pages, new DocumentLinks.DocLink(number, t, "", 0, 0)) is { } d ? $"#doc-{pages.IndexOf(d)}" : null;
        var byVerse = verses.ToDictionary(v => v.Span, v => "#" + v.Anchor);
        string? VerseHref(string span) => byVerse.TryGetValue(span, out var h) ? h : null;

        var back = $"<p class=\"small\"><a href=\"#doc-0\">↑ {H(title)}</a></p>";
        var sb = new StringBuilder($"<section id=\"doc-0\"><h1>{H(title)}</h1>" + (tags.Length > 0 ? $"<p class=\"meta\">{H(tags)}</p>" : "")
            + MarkdownToHtml(DocumentLinks.WithoutTitleHeading(body, title), Href, VerseHref) + "</section>");
        for (var i = 0; i < linked.Count; i++)
            sb.Append($"<hr><section id=\"doc-{i + 1}\"><h1>{H(linked[i].Title)}</h1>{MarkdownToHtml(DocumentLinks.WithoutTitleHeading(linked[i].Body, linked[i].Title), Href, VerseHref)}{back}</section>");
        if (verses.Count > 0)
        {
            sb.Append($"<hr><section><h1>{H(L("Bibelstellen"))}</h1>");
            foreach (var v in verses) sb.Append($"<div id=\"{v.Anchor}\"><h3>{H(v.Span)}</h3>{MarkdownToHtml(v.Quote)}</div>");
            sb.Append(back).Append("</section>");
        }
        return sb.ToString();
    }

    (string Title, string Body, string Tags, long Id, List<Document> Linked) CurrentDocumentForExport()
    {
        var body = DocBodyBox.Text ?? "";
        return (DocTitleBox.Text?.Trim() ?? "", body, DocTagsBox.Text?.Trim() ?? "", _doc?.Id ?? 0, LinkedDocuments(body, _doc?.Id ?? 0));
    }

    async Task PrintDocumentAsync()
    {
        var (title, body, tags, id, linked) = CurrentDocumentForExport();
        if (title.Length == 0 && body.Trim().Length == 0) { DocInfo.Text = L("Nichts zu drucken."); return; }
        ExportPrint(title, await DocumentHtmlAsync(title, body, tags, id, linked), SafeName(title, L("Dokument")));
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
        if (file?.TryGetLocalPath() is not { } path) return;
        var html = "<!doctype html><html lang=\"" + Loc.Code + "\"><meta charset=\"utf-8\"><title>" + H(title) + "</title><style>" + PrintCss + "</style>"
                 + await DocumentHtmlAsync(title, body, tags, id, linked) + "<footer>BibleStudy · " + DateTime.Now.ToString("d") + "</footer></html>";
        try
        {
            await File.WriteAllTextAsync(path, html, new UTF8Encoding(false));
            StatusText.Text = linked.Count == 0 ? L("Gespeichert: {0}", path) : L("Gespeichert: {0} (mit {1} verlinkten Dokumenten)", path, linked.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = L("Speichern nicht möglich: ") + ex.Message; }
    }

    /// <summary>One Markdown file for the document, one for each linked document and one with the verses, all in <paramref name="dir"/>; the links point at the files.</summary>
    async Task WriteDocumentMarkdownAsync(string dir, string title, string body, long selfId, List<Document> linked)
    {
        Directory.CreateDirectory(dir);
        var verses = await ExportVersesAsync(linked.Select(d => d.Body).Prepend(body));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string FileFor(string t) { var n = SafeName(t, L("Dokument")); var f = n + ".md"; for (var i = 2; !used.Add(f); i++) f = $"{n} ({i}).md"; return f; }
        // in an export names no longer change: each document gets a file named after its title, and the links point at the files
        var pages = linked.Prepend(new Document(selfId, title, body, null, false, DateTime.Now, [])).ToList();
        var files = pages.ToDictionary(p => p, p => FileFor(p.Title));
        (string Title, string File)? Target(DocumentLinks.DocLink l) => VaultStore.Resolve(pages, l) is { } d ? (d.Title, files[d]) : null;
        var verseFile = FileFor(L("Bibelstellen"));
        var byVerse = verses.ToDictionary(v => v.Span, v => Uri.EscapeDataString(verseFile) + "#" + v.Anchor);
        string? VerseHref(string span) => byVerse.TryGetValue(span, out var h) ? h : null;
        string Page(string t, string text) => "# " + t + "\n\n" + DocumentLinks.ToMarkdownLinks(DocumentLinks.WithoutTitleHeading(text, t), Target, VerseHref);

        var utf8 = new UTF8Encoding(false);
        foreach (var p in pages) await File.WriteAllTextAsync(Path.Combine(dir, files[p]), Page(p.Title, p.Body), utf8);
        if (verses.Count > 0)
        {
            var sb = new StringBuilder("# " + L("Bibelstellen") + "\n\n");
            foreach (var v in verses) sb.Append($"<a id=\"{v.Anchor}\"></a>\n\n## {v.Span}\n\n{v.Quote}");
            await File.WriteAllTextAsync(Path.Combine(dir, verseFile), sb.ToString(), utf8);
        }
    }

    async Task SaveDocumentAsMarkdownAsync()
    {
        var (title, body, _, id, linked) = CurrentDocumentForExport();
        if (title.Length == 0 && body.Trim().Length == 0) { DocInfo.Text = L("Nichts zu exportieren."); return; }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L("Ordner für den Markdown-Export wählen") });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } parent) return;
        try
        {
            // a folder of its own, so the files of the linked documents never land between other files
            var dir = Path.Combine(parent, SafeName(title, L("Dokument")));
            await WriteDocumentMarkdownAsync(dir, title, body, id, linked);
            StatusText.Text = L("{0} Dokument(e) als Markdown gespeichert in {1}", 1 + linked.Count, dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = L("Speichern nicht möglich: ") + ex.Message; }
    }

}
