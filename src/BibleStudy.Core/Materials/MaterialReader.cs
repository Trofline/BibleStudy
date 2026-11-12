using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BibleStudy.Core.Import;
using static BibleStudy.Core.Loc;

namespace BibleStudy.Core.Materials;

/// <summary>What a block of a material is: a heading (levels 1–3), a paragraph, an item of a list, a row of a table, the start of a page or slide, a picture, or a line of the table of contents.</summary>
public enum BlockKind { Heading1, Heading2, Heading3, Paragraph, ListItem, TableRow, Page, Image, TocEntry }

/// <summary>A piece of text with its look (bold, italic).</summary>
public sealed record Span(string Text, bool Bold = false, bool Italic = false);

/// <param name="Anchors">Names of the bookmarks at this block (what a line of the table of contents jumps to).</param>
/// <param name="LinkTo">For a line of the table of contents: the bookmark it jumps to.</param>
/// <param name="Level">For a line of the table of contents: its level (1 = top).</param>
/// <param name="Page">For a PDF outline: the page it points to.</param>
public sealed record Block(BlockKind Kind, IReadOnlyList<Span> Spans, string? ImagePath = null, IReadOnlyList<string>? Anchors = null, string? LinkTo = null, int Level = 0, int Page = 0)
{
    public bool IsHeading => Kind is BlockKind.Heading1 or BlockKind.Heading2 or BlockKind.Heading3;
    public int HeadingLevel => Kind switch { BlockKind.Heading1 => 1, BlockKind.Heading2 => 2, BlockKind.Heading3 => 3, _ => 0 };
    public string Text => string.Concat(Spans.Select(s => s.Text));
    public static Block Of(BlockKind kind, string text) => new(kind, [new Span(text)]);
}

/// <summary>A file of the materials folder as the program shows it: its blocks, or why it cannot be shown here.</summary>
public sealed record MaterialDoc(string Path, IReadOnlyList<Block> Blocks, string? Problem = null);

/// <summary>
/// Reads the documents of a Bible study (Word, OpenDocument, RTF, PDF, PowerPoint, web pages, Markdown, text and pictures) into simple blocks,
/// so they can be read inside the program without opening Word. Only the text and its rough form are kept; for the exact layout the file can
/// still be opened with its own program.
/// </summary>
public static partial class MaterialReader
{
    public static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
    public static readonly HashSet<string> Readable = new(StringComparer.OrdinalIgnoreCase) { ".docx", ".doc", ".odt", ".rtf", ".pdf", ".pptx", ".txt", ".md", ".markdown", ".html", ".htm", ".csv", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
    /// <summary>Files listed in the materials (also those that only open in their own program, such as old .doc files).</summary>
    public static readonly HashSet<string> Listed = new(Readable.Concat([".doc", ".ppt", ".xls", ".xlsx", ".ods", ".odp", ".epub", ".mp3", ".mp4", ".m4a", ".wav"]), StringComparer.OrdinalIgnoreCase);

    public static bool CanShow(string path) => Readable.Contains(System.IO.Path.GetExtension(path));

    /// <summary>The files of a folder that belong in the list (hidden and temporary files of Office, such as ~$Datei.docx, are left out).</summary>
    public static List<string> Scan(string folder, bool subfolders, int max = 5000)
    {
        var list = new List<string>();
        if (!Directory.Exists(folder)) return list;
        var options = new EnumerationOptions { RecurseSubdirectories = subfolders, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        foreach (var f in Directory.EnumerateFiles(folder, "*", options))
        {
            var name = System.IO.Path.GetFileName(f);
            if (name.StartsWith("~$") || name.StartsWith('.') || !Listed.Contains(System.IO.Path.GetExtension(f))) continue;
            list.Add(f);
            if (list.Count >= max) break;
        }
        return list;
    }

    public static MaterialDoc Read(string path, string? mediaDir = null)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (Pictures.Contains(ext)) return new(path, [new Block(BlockKind.Image, [], path)]);
            var blocks = ext switch
            {
                ".docx" => ReadDocx(path, mediaDir),
                ".doc" => WordBinary.Read(path),
                ".odt" => ReadOdt(path, mediaDir),
                ".rtf" => BibleImporters.RtfToLines(File.ReadAllText(path, Encoding.Latin1)).Select(l => Block.Of(BlockKind.Paragraph, Clean(l))).ToList(),
                ".pdf" => ReadPdf(path),
                ".pptx" => ReadPptx(path, mediaDir),
                ".md" or ".markdown" => ReadMarkdown(File.ReadAllText(path)),
                ".html" or ".htm" => ReadHtml(File.ReadAllText(path)),
                ".txt" or ".csv" => ReadText(File.ReadAllText(path)),
                _ => null,
            };
            if (blocks is null) return new(path, [], L("Diese Datei kann nur mit ihrem eigenen Programm geöffnet werden."));
            if (blocks.Count == 0) return new(path, [], L("In dieser Datei steht kein Text, der sich hier zeigen lässt (vielleicht nur Bilder oder ein eingescanntes Dokument)."));
            return new(path, blocks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException or NotSupportedException)
        {
            return new(path, [], L("Die Datei lässt sich nicht lesen: {0}", ex.Message));
        }
        catch (Exception ex) when (ex.GetType().Namespace?.StartsWith("UglyToad") == true)
        {
            return new(path, [], L("Die Datei lässt sich nicht lesen: {0}", ex.Message));
        }
    }

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();

    /// <summary>Verse numbers that the Bible reader marks as &lt;sup&gt; are plain numbers here.</summary>
    static string Clean(string s) => Spaces().Replace(s.Replace("<sup>", "").Replace("</sup>", ""), " ").Trim();

    static List<Span> Merge(List<Span> spans)
    {
        var merged = new List<Span>();
        foreach (var s in spans)
        {
            if (s.Text.Length == 0) continue;
            if (merged.Count > 0 && merged[^1].Bold == s.Bold && merged[^1].Italic == s.Italic) merged[^1] = merged[^1] with { Text = merged[^1].Text + s.Text };
            else merged.Add(s);
        }
        if (merged.Count > 0) { merged[0] = merged[0] with { Text = merged[0].Text.TrimStart() }; merged[^1] = merged[^1] with { Text = merged[^1].Text.TrimEnd() }; }
        return merged.Where(s => s.Text.Length > 0).ToList();
    }

    static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    static readonly HashSet<string> ShowablePictures = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    /// <summary>The targets of the relations of a part (id → path in the package), e.g. "rId5" → "word/media/image1.png".</summary>
    static Dictionary<string, string> Relations(ZipArchive zip, string part)
    {
        var dir = part.Contains('/') ? part[..part.LastIndexOf('/')] : "";
        var rels = zip.GetEntry($"{dir}/_rels/{part[(dir.Length + 1)..]}.rels");
        var map = new Dictionary<string, string>();
        if (rels is null) return map;
        XDocument doc;
        using (var s = rels.Open()) doc = XDocument.Load(s);
        foreach (var r in doc.Descendants(PkgRel + "Relationship"))
        {
            var id = (string?)r.Attribute("Id"); var target = (string?)r.Attribute("Target");
            if (id is null || target is null || (string?)r.Attribute("TargetMode") == "External") continue;
            map[id] = Normalize(target.StartsWith('/') ? target[1..] : dir + "/" + target);
        }
        return map;
    }

    /// <summary>"word/../media/x.png" → "media/x.png".</summary>
    static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var p in path.Split('/'))
            if (p == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (p is not ("" or ".")) parts.Add(p);
        return string.Join('/', parts);
    }

    /// <summary>Copies a picture out of the package into the folder for pictures; null when there is no folder or the format cannot be shown (EMF, WMF …).</summary>
    static Block? Picture(ZipArchive zip, string entryPath, string? mediaDir)
    {
        if (mediaDir is null || !ShowablePictures.Contains(System.IO.Path.GetExtension(entryPath)) || zip.GetEntry(entryPath) is not { } entry) return null;
        Directory.CreateDirectory(mediaDir);
        var file = System.IO.Path.Combine(mediaDir, entryPath.Replace('/', '_'));
        if (!File.Exists(file)) entry.ExtractToFile(file, true);
        return new Block(BlockKind.Image, [], file);
    }

    // ---- Word -------------------------------------------------------------------------------------------------------------

    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static List<Block> ReadDocx(string path, string? mediaDir = null)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException(L("Das ist keine gültige Word-Datei (.docx)."));
        XDocument doc;
        using (var s = entry.Open()) doc = XDocument.Load(s);
        var rels = Relations(zip, "word/document.xml");
        var body = doc.Root?.Element(W + "body");
        var blocks = new List<Block>();
        if (body is null) return blocks;
        void Pictures(XElement e)
        {
            // pictures of a paragraph (new drawings and old VML ones) follow its text
            foreach (var id in e.Descendants(A + "blip").Select(b => (string?)b.Attribute(R + "embed"))
                         .Concat(e.Descendants().Where(x => x.Name.LocalName == "imagedata").Select(x => (string?)x.Attribute(R + "id"))))
                if (id is not null && rels.TryGetValue(id, out var target) && Picture(zip, target, mediaDir) is { } pic) blocks.Add(pic);
        }
        var inToc = 0;   // inside the field of a table of contents (it may span many paragraphs)
        void Walk(XElement parent)
        {
            foreach (var e in parent.Elements())
            {
                if (e.Name == W + "p")
                {
                    if (DocxParagraph(e, ref inToc) is { } b) blocks.Add(b);
                    Pictures(e);
                }
                else if (e.Name == W + "tbl")
                {
                    foreach (var row in e.Elements(W + "tr"))
                    {
                        var cells = row.Elements(W + "tc").Select(c => Spaces().Replace(string.Join(" ", c.Descendants(W + "p").Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)))), " ").Trim()).ToList();
                        if (cells.Any(c => c.Length > 0)) blocks.Add(Block.Of(BlockKind.TableRow, string.Join("  │  ", cells)));
                    }
                    Pictures(e);
                }
                // a table of contents usually sits in a content control (w:sdt); other wrappers hold paragraphs as well
                else if (e.Name == W + "sdt") { if (e.Element(W + "sdtContent") is { } content) Walk(content); }
                else if (e.Name.LocalName is "customXml" or "smartTag" or "ins") Walk(e);
            }
        }
        Walk(body);
        return blocks;
    }

    static readonly Regex TocStyle = new(@"^(?:TOC|toc|Verzeichnis|Inhaltsverzeichnis|Contents)\s*(\d)", RegexOptions.IgnoreCase);
    static readonly Regex InnerLink = new(@"^\s*(?:HYPERLINK\s.*\\l\s*""?([^""\s]+)|PAGEREF\s+(\S+))", RegexOptions.IgnoreCase);

    static Block? DocxParagraph(XElement p, ref int inToc)
    {
        var props = p.Element(W + "pPr");
        var style = (string?)props?.Element(W + "pStyle")?.Attribute(W + "val") ?? "";
        var spans = new List<Span>();
        string? link = null;
        var code = new System.Text.StringBuilder();
        var inCode = false;
        foreach (var x in p.Descendants())
        {
            // fields: begin, code (instrText), separate, result, end; the code of a TOC field opens the table of contents,
            // HYPERLINK \l "_Toc…" and PAGEREF _Toc… in a line tell where it jumps to
            if (x.Name == W + "fldChar")
            {
                var type = (string?)x.Attribute(W + "fldCharType");
                if (type == "begin") { inCode = true; code.Clear(); }
                else if (type == "separate")
                {
                    inCode = false;
                    var c = code.ToString();
                    if (c.TrimStart().StartsWith("TOC", StringComparison.OrdinalIgnoreCase)) inToc++;
                    if (InnerLink.Match(c) is { Success: true } m) link ??= m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                }
                else if (type == "end") inCode = false;
            }
            else if (x.Name == W + "instrText" && inCode) code.Append(x.Value);
            else if (x.Name == W + "hyperlink" && (string?)x.Attribute(W + "anchor") is { } anchor) link ??= anchor;
            else if (x.Name == W + "fldSimple" && InnerLink.Match((string?)x.Attribute(W + "instr") ?? "") is { Success: true } sm) link ??= sm.Groups[1].Success ? sm.Groups[1].Value : sm.Groups[2].Value;
            else if (x.Name == W + "r" && !inCode)
            {
                var rp = x.Element(W + "rPr");
                bool On(string name) => rp?.Element(W + name) is { } y && (string?)y.Attribute(W + "val") is not ("0" or "false");
                var text = string.Concat(x.Elements().Select(y => y.Name == W + "t" ? y.Value : y.Name == W + "tab" ? "\t" : y.Name == W + "br" ? "\n" : ""));
                spans.Add(new Span(text, On("b"), On("i")));
            }
        }
        // the end of the table of contents: the last "end" of the TOC field (a field that closes in this paragraph without opening)
        var ends = p.Descendants(W + "fldChar").Count(f => (string?)f.Attribute(W + "fldCharType") == "end");
        var begins = p.Descendants(W + "fldChar").Count(f => (string?)f.Attribute(W + "fldCharType") == "begin");
        var anchors = p.Descendants(W + "bookmarkStart").Select(b => (string?)b.Attribute(W + "name")).OfType<string>().Where(n => n != "_GoBack").ToList();
        var merged = Merge(spans);
        var tocLevel = TocStyle.Match(style) is { Success: true } tm ? int.Parse(tm.Groups[1].Value) : 0;
        Block? block = null;
        if (merged.Count > 0)
        {
            if (tocLevel > 0 || (inToc > 0 && link is not null))
            {
                var line = string.Concat(merged.Select(s => s.Text));
                var cut = line.LastIndexOf('\t');
                var page = cut >= 0 ? line[(cut + 1)..].Trim() : "";
                var title = Spaces().Replace((cut >= 0 && Regex.IsMatch(page, @"^[\divxlcIVXLC]+$") ? line[..cut] : line).Replace('\t', ' '), " ").Trim();
                if (cut < 0 || !Regex.IsMatch(page, @"^[\divxlcIVXLC]+$")) page = "";
                block = new Block(BlockKind.TocEntry, [new Span(title), new Span(page)], LinkTo: link, Level: Math.Max(1, tocLevel));
            }
            else
            {
                var kind = HeadingLevel(style) switch { 1 => BlockKind.Heading1, 2 => BlockKind.Heading2, 3 or 4 or 5 or 6 => BlockKind.Heading3, _ => props?.Element(W + "numPr") is not null || style.Contains("List", StringComparison.OrdinalIgnoreCase) || style.Contains("Liste", StringComparison.OrdinalIgnoreCase) ? BlockKind.ListItem : BlockKind.Paragraph };
                block = new Block(kind, merged.Select(s => s with { Text = s.Text.Replace('\t', ' ') }).ToList());
            }
            if (anchors.Count > 0) block = block with { Anchors = anchors };
        }
        if (inToc > 0 && ends > begins) inToc = Math.Max(0, inToc - (ends - begins));
        return block;
    }

    /// <summary>The heading level of a Word style ("Heading2", "Überschrift2", "berschrift2" in older files, "Title"); 0 for a paragraph.</summary>
    public static int HeadingLevel(string style)
    {
        if (style.Equals("Title", StringComparison.OrdinalIgnoreCase) || style.Equals("Titel", StringComparison.OrdinalIgnoreCase)) return 1;
        var m = Regex.Match(style, @"(?:eading|berschrift)\s*(\d)", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    // ---- OpenDocument ---------------------------------------------------------------------------------------------------------

    public static List<Block> ReadOdt(string path, string? mediaDir = null)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("content.xml") ?? throw new InvalidDataException(L("Das ist keine gültige OpenDocument-Datei (.odt)."));
        XDocument doc;
        using (var s = entry.Open()) doc = XDocument.Load(s);
        XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0", draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0", xlink = "http://www.w3.org/1999/xlink";
        var blocks = new List<Block>();
        foreach (var e in doc.Descendants().Where(e => (e.Name == text + "p" || e.Name == text + "h") && !e.Ancestors(text + "note").Any()))
        {
            var line = Spaces().Replace(string.Concat(e.DescendantNodes().OfType<XText>().Where(t => !t.Ancestors(draw + "frame").Any()).Select(t => t.Value)), " ").Trim();
            if (line.Length > 0)
            {
                var level = (int?)e.Attribute(text + "outline-level") ?? 1;
                var kind = e.Name == text + "h" ? (level <= 1 ? BlockKind.Heading1 : level == 2 ? BlockKind.Heading2 : BlockKind.Heading3) : e.Ancestors(text + "list-item").Any() ? BlockKind.ListItem : BlockKind.Paragraph;
                blocks.Add(Block.Of(kind, line));
            }
            // the pictures that stand in this paragraph (not in a paragraph inside it)
            foreach (var img in e.Descendants(draw + "image").Where(i => i.Ancestors().First(x => x.Name == text + "p" || x.Name == text + "h") == e))
                if ((string?)img.Attribute(xlink + "href") is { } href && Picture(zip, Normalize(href), mediaDir) is { } pic) blocks.Add(pic);
        }
        return blocks;
    }

    // ---- PowerPoint -------------------------------------------------------------------------------------------------------

    public static List<Block> ReadPptx(string path, string? mediaDir = null)
    {
        using var zip = ZipFile.OpenRead(path);
        var slides = zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$"))
            .OrderBy(e => int.Parse(Regex.Match(e.FullName, @"\d+").Value)).ToList();
        var blocks = new List<Block>();
        var n = 0;
        foreach (var slide in slides)
        {
            n++;
            XDocument doc;
            using (var s = slide.Open()) doc = XDocument.Load(s);
            blocks.Add(Block.Of(BlockKind.Page, L("Folie {0}", n)));
            var first = true;
            foreach (var p in doc.Descendants(A + "p"))
            {
                var line = Spaces().Replace(string.Concat(p.Descendants(A + "t").Select(t => t.Value)), " ").Trim();
                if (line.Length == 0) continue;
                blocks.Add(Block.Of(first ? BlockKind.Heading2 : BlockKind.ListItem, line));   // the first text of a slide is its title
                first = false;
            }
            var rels = Relations(zip, slide.FullName);
            foreach (var id in doc.Descendants(A + "blip").Select(b => (string?)b.Attribute(R + "embed")).Distinct())
                if (id is not null && rels.TryGetValue(id, out var target) && Picture(zip, target, mediaDir) is { } pic) blocks.Add(pic);
        }
        return blocks;
    }

    // ---- PDF ---------------------------------------------------------------------------------------------------------------

    /// <summary>The outline of a PDF (its bookmarks) as lines of a table of contents that point to pages; empty when it has none.</summary>
    public static List<Block> PdfOutline(string path)
    {
        var list = new List<Block>();
        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
            if (!pdf.TryGetBookmarks(out var bookmarks)) return list;
            void Add(UglyToad.PdfPig.Outline.BookmarkNode node)
            {
                var page = node is UglyToad.PdfPig.Outline.DocumentBookmarkNode d ? d.PageNumber : 0;
                if (node.Title.Trim().Length > 0 && page > 0) list.Add(new Block(BlockKind.TocEntry, [new Span(node.Title.Trim()), new Span(page.ToString())], Level: node.Level + 1, Page: page));
                foreach (var c in node.Children) Add(c);
            }
            foreach (var root in bookmarks.Roots) Add(root);
        }
        catch (Exception) { }   // a PDF without a readable outline simply has none
        return list;
    }

    public static List<Block> ReadPdf(string path)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
        var blocks = new List<Block>();
        var anyText = false;
        foreach (var page in pdf.GetPages())
        {
            blocks.Add(Block.Of(BlockKind.Page, L("Seite {0}", page.Number)));
            // the words as lines: words whose baselines are close belong together, from top to bottom and left to right
            var lines = new List<(double Y, List<(double X, string T)> Words)>();
            foreach (var w in page.GetWords())
            {
                var y = w.BoundingBox.Bottom;
                var line = lines.FirstOrDefault(l => Math.Abs(l.Y - y) < Math.Max(2, w.BoundingBox.Height * 0.4));
                if (line.Words is null) lines.Add((y, [(w.BoundingBox.Left, w.Text)]));
                else line.Words.Add((w.BoundingBox.Left, w.Text));
            }
            foreach (var (_, words) in lines.OrderByDescending(l => l.Y))
            {
                var text = string.Join(" ", words.OrderBy(x => x.X).Select(x => x.T)).Trim();
                if (text.Length == 0) continue;
                anyText = true;
                blocks.Add(Block.Of(BlockKind.Paragraph, text));
            }
        }
        return anyText ? blocks : [];
    }

    // ---- Markdown, web pages, text ---------------------------------------------------------------------------------------

    public static List<Block> ReadMarkdown(string md)
    {
        var blocks = new List<Block>();
        var para = new StringBuilder();
        void Flush() { if (para.Length > 0) { blocks.Add(new Block(BlockKind.Paragraph, MarkdownSpans(para.ToString().Trim()))); para.Clear(); } }
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            var h = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            var li = Regex.Match(line, @"^\s*(?:[-*+]|\d+[.)])\s+(.*)$");
            if (line.Trim().Length == 0) Flush();
            else if (h.Success) { Flush(); blocks.Add(new Block(h.Groups[1].Length == 1 ? BlockKind.Heading1 : h.Groups[1].Length == 2 ? BlockKind.Heading2 : BlockKind.Heading3, MarkdownSpans(h.Groups[2].Value))); }
            else if (li.Success) { Flush(); blocks.Add(new Block(BlockKind.ListItem, MarkdownSpans(li.Groups[1].Value))); }
            else para.Append(line.Trim()).Append(' ');
        }
        Flush();
        return blocks;
    }

    /// <summary>**bold** and *italic* (also with underscores) as spans.</summary>
    public static List<Span> MarkdownSpans(string s)
    {
        var spans = new List<Span>();
        var pos = 0;
        foreach (Match m in Regex.Matches(s, @"(\*\*|__)(.+?)\1|(\*|_)(.+?)\3"))
        {
            if (m.Index > pos) spans.Add(new Span(s[pos..m.Index]));
            spans.Add(m.Groups[2].Success ? new Span(m.Groups[2].Value, Bold: true) : new Span(m.Groups[4].Value, Italic: true));
            pos = m.Index + m.Length;
        }
        if (pos < s.Length) spans.Add(new Span(s[pos..]));
        return spans;
    }

    public static List<Block> ReadHtml(string html)
    {
        html = Regex.Replace(html, @"<(script|style|head)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var blocks = new List<Block>();
        foreach (Match m in Regex.Matches(html, @"<(h[1-6]|p|li|td|th|div|blockquote|pre)[^>]*>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var inner = m.Groups[2].Value;
            if (Regex.IsMatch(inner, @"<(p|div|li|h[1-6])[\s>]", RegexOptions.IgnoreCase)) continue;   // a container: its parts come on their own
            var text = Spaces().Replace(WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(inner, @"<br\s*/?>", " ", RegexOptions.IgnoreCase), "<[^>]+>", "")), " ").Trim();
            if (text.Length == 0) continue;
            var tag = m.Groups[1].Value.ToLowerInvariant();
            blocks.Add(Block.Of(tag switch { "h1" => BlockKind.Heading1, "h2" => BlockKind.Heading2, "h3" or "h4" or "h5" or "h6" => BlockKind.Heading3, "li" => BlockKind.ListItem, _ => BlockKind.Paragraph }, text));
        }
        if (blocks.Count == 0)
        {
            var plain = Spaces().Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), " ").Trim();
            if (plain.Length > 0) blocks.Add(Block.Of(BlockKind.Paragraph, plain));
        }
        return blocks;
    }

    /// <summary>Plain text: an empty line ends a paragraph; single lines stay lines.</summary>
    public static List<Block> ReadText(string text) =>
        Regex.Split(text.Replace("\r", ""), @"\n\s*\n").Select(p => p.Trim('\n', ' ', '\t')).Where(p => p.Length > 0).Select(p => Block.Of(BlockKind.Paragraph, p)).ToList();
}
