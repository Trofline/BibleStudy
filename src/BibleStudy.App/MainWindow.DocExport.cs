using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Documents that link to each other: renaming one keeps the links to it, and exporting one takes the documents it links to
/// (and those they link to) along, so the links still work in the file: inside one HTML page, or between Markdown files.
/// </summary>
public partial class MainWindow
{
    /// <summary>A document got a new title: every [[link]] to it in the other documents follows.</summary>
    void RenameDocLinks(long id, string oldTitle, string newTitle)
    {
        if (string.Equals(oldTitle.Trim(), newTitle.Trim(), StringComparison.Ordinal) || oldTitle.Trim().Length == 0) return;
        foreach (var d in _vault.GetDocuments().Where(d => d.Id != id))
        {
            var body = DocumentLinks.RenameLink(d.Body, oldTitle, newTitle);
            if (body != d.Body) _vault.UpdateDocument(d.Id, d.Title, body, d.Path, d.IsTemplate, d.Tags, d.FolderId);
        }
    }

    /// <summary>The documents the text links to, and those they link to, each once (not the document itself).</summary>
    List<Document> LinkedDocuments(string body, long selfId)
    {
        var result = new List<Document>();
        var seen = new HashSet<long> { selfId };
        var queue = new Queue<string>(DocumentLinks.WikiLinks(body));
        while (queue.Count > 0)
        {
            if (_vault.FindDocument(queue.Dequeue()) is not { } d || !seen.Add(d.Id)) continue;
            result.Add(d);
            foreach (var t in DocumentLinks.WikiLinks(d.Body)) queue.Enqueue(t);
        }
        return result;
    }

    (string Title, string Body, string Tags, long Id, List<Document> Linked) CurrentDocumentForExport()
    {
        var title = DocTitleBox.Text?.Trim() ?? "";
        var body = DocBodyBox.Text ?? "";
        var id = _doc?.Id ?? 0;
        return (title, body, DocTagsBox.Text?.Trim() ?? "", id, LinkedDocuments(body, id));
    }

    /// <summary>The page of the document and, after it, the linked documents; links between them jump inside the page.</summary>
    string DocumentWithLinkedHtml(string title, string body, string tags, long id, List<Document> linked)
    {
        var anchors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (title.Length > 0) anchors[title] = "#doc-0";
        for (var i = 0; i < linked.Count; i++) anchors.TryAdd(linked[i].Title, $"#doc-{i + 1}");
        string? Href(string t) => anchors.TryGetValue(t, out var h) ? h : null;
        var sb = new StringBuilder($"<section id=\"doc-0\"><h1>{H(title)}</h1>" + (tags.Length > 0 ? $"<p class=\"meta\">{H(tags)}</p>" : "") + MarkdownToHtml(body, Href) + "</section>");
        for (var i = 0; i < linked.Count; i++)
            sb.Append($"<hr><section id=\"doc-{i + 1}\"><h1>{H(linked[i].Title)}</h1>{MarkdownToHtml(linked[i].Body, Href)}<p class=\"small\"><a href=\"#doc-0\">↑ {H(title)}</a></p></section>");
        return sb.ToString();
    }

    void PrintDocument()
    {
        var (title, body, tags, id, linked) = CurrentDocumentForExport();
        if (title.Length == 0 && body.Trim().Length == 0) { DocInfo.Text = L("Nichts zu drucken."); return; }
        ExportPrint(title, DocumentWithLinkedHtml(title, body, tags, id, linked), SafeName(title, L("Dokument")));
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
                 + DocumentWithLinkedHtml(title, body, tags, id, linked) + "<footer>BibleStudy · " + DateTime.Now.ToString("d") + "</footer></html>";
        try
        {
            await File.WriteAllTextAsync(path, html, new UTF8Encoding(false));
            DocInfo.Text = linked.Count == 0 ? L("Gespeichert: {0}", path) : L("Gespeichert: {0} (mit {1} verlinkten Dokumenten)", path, linked.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { DocInfo.Text = L("Speichern nicht möglich: ") + ex.Message; }
    }

    [GeneratedRegex(@"\[\[(?<t>[^\]|#]+)(?:[|#](?<a>[^\]]*))?\]\]")]
    private static partial Regex WikiLinkPattern();

    /// <summary>Markdown for other programs: [[Title|text]] becomes [text](Title.md), pointing at the file of that document.</summary>
    static string MarkdownWithFileLinks(string body, IReadOnlyDictionary<string, string> files) =>
        WikiLinkPattern().Replace(body, m =>
        {
            var t = m.Groups["t"].Value.Trim();
            if (!files.TryGetValue(t, out var file)) return m.Value;
            var shown = m.Groups["a"].Success && m.Groups["a"].Value.Trim().Length > 0 ? m.Groups["a"].Value.Trim() : t;
            return $"[{shown}]({Uri.EscapeDataString(file)})";
        });

    async Task SaveDocumentAsMarkdownAsync()
    {
        var (title, body, _, id, linked) = CurrentDocumentForExport();
        if (title.Length == 0 && body.Trim().Length == 0) { DocInfo.Text = L("Nichts zu exportieren."); return; }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L("Ordner für den Markdown-Export wählen") });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } parent) return;
        try
        {
            // its own folder, so the files of the linked documents never land between other files
            var dir = Path.Combine(parent, SafeName(title, L("Dokument")));
            Directory.CreateDirectory(dir);
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string FileFor(string t) { var n = SafeName(t, L("Dokument")); var f = n + ".md"; for (var i = 2; !used.Add(f); i++) f = $"{n} ({i}).md"; return f; }
            if (title.Length > 0) files[title] = FileFor(title);
            foreach (var d in linked) files.TryAdd(d.Title, FileFor(d.Title));
            var mainFile = title.Length > 0 ? files[title] : FileFor(L("Dokument"));
            await File.WriteAllTextAsync(Path.Combine(dir, mainFile), "# " + title + "\n\n" + MarkdownWithFileLinks(body, files), new UTF8Encoding(false));
            foreach (var d in linked)
                await File.WriteAllTextAsync(Path.Combine(dir, files[d.Title]), "# " + d.Title + "\n\n" + MarkdownWithFileLinks(d.Body, files), new UTF8Encoding(false));
            DocInfo.Text = L("{0} Dokument(e) als Markdown gespeichert in {1}", 1 + linked.Count, dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { DocInfo.Text = L("Speichern nicht möglich: ") + ex.Message; }
    }
}
