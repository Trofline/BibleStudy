using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BibleStudy.Core.Import;

/// <summary>
/// Readers for Bible texts in ordinary book and document formats (EPUB, Word, OpenDocument, RTF, FictionBook, PDF, web pages).
/// Each one turns the file into plain lines – headings as "#" lines, verse numbers as &lt;sup&gt; – and hands them to the Markdown reader,
/// which recognises books, chapters and verses. Files with a copy protection (DRM) are refused, never opened: BibleStudy does not
/// remove copy protection. Whether you may use a file in this way is your own responsibility.
/// </summary>
public static partial class BibleImporters
{
    /// <summary>Raised for protected files; the message is shown to the user.</summary>
    public static InvalidDataException Protected() => new(L("Diese Datei ist kopiergeschützt (DRM). BibleStudy liest keine geschützten Dateien und entfernt keinen Kopierschutz. Besorge dir die Datei ohne Kopierschutz oder frage den Verlag danach."));

    /// <summary>Kindle and similar formats: nearly always protected, and not supported.</summary>
    public static NotSupportedException ProtectedFormat(string ext) => new(L("{0}-Dateien sind in der Regel kopiergeschützt und werden nicht unterstützt. Besorge dir den Text als EPUB, Word- oder PDF-Datei ohne Kopierschutz.", ext.TrimStart('.').ToUpperInvariant()));

    // ---- web pages and EPUB -------------------------------------------------------------------------------------------

    [GeneratedRegex(@"<(script|style|head)\b.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex HtmlHidden();
    [GeneratedRegex(@"<h[1-6]\b[^>]*>(.*?)</h[1-6]\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex HtmlHeading();
    [GeneratedRegex(@"<span\b[^>]*class=""[^""]*(?:verse|vers|versenum|v-num|vnum)[^""]*""[^>]*>\s*(\d{1,3})\s*</span>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex HtmlVerseSpan();
    [GeneratedRegex(@"</?(?:p|div|br|li|tr|blockquote|section|article)\b[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex HtmlBreak();
    [GeneratedRegex(@"<(?!/?sup\b)[^>]+>", RegexOptions.IgnoreCase)] private static partial Regex HtmlOtherTags();

    /// <summary>HTML as lines: headings become "# …", marked verse numbers become &lt;sup&gt;n&lt;/sup&gt;, blocks become lines.</summary>
    public static IEnumerable<string> HtmlToLines(string html)
    {
        var s = HtmlHidden().Replace(html, " ");
        s = HtmlHeading().Replace(s, m => "\n# " + HtmlOtherTags().Replace(m.Groups[1].Value, "").Replace("<sup>", "").Replace("</sup>", "") + "\n");
        s = HtmlVerseSpan().Replace(s, "<sup>$1</sup>");
        s = HtmlBreak().Replace(s, "\n");
        s = HtmlOtherTags().Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        return s.Split('\n').Select(l => Whitespace().Replace(l, " ").Trim()).Where(l => l.Length > 0);
    }

    static ParsedBible ReadHtmlFile(string path, string languageCode, IProgress<string>? progress)
    {
        var html = File.ReadAllText(path);
        if (VerseButton().Matches(html).Count >= 1000) return ReadCsvOfflineHtml(path, progress);   // the offline page of the CSV Bible keeps its own, richer reader
        return ReadMarkdownLines(PromoteHeadings(HtmlToLines(html)), Path.GetFileNameWithoutExtension(path), languageCode);
    }

    /// <summary>Plain lines that stand alone and name a book or a chapter ("Genesis", "Kapitel 3", "Psalm 23") become headings, so documents without heading styles can be read.</summary>
    public static IEnumerable<string> PromoteHeadings(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith('#') || line.Length > 40 || line.Length == 0) { yield return line; continue; }
            var plain = line.Trim().TrimEnd(':');
            var isBook = BookFromName(plain) is not null && !char.IsDigit(plain[^1]) || BookFromName(plain) is not null && plain.Any(char.IsLetter);
            var bookChapter = MdBookChapter().Match(plain);
            var isBookChapter = bookChapter.Success && BookFromName(bookChapter.Groups["b"].Value) is not null && bookChapter.Groups["b"].Value.Any(char.IsLetter);
            var isChapter = MdChapter().Match(plain) is { Success: true } && plain.Any(char.IsLetter);   // "Kapitel 3", not a bare number (that is a verse)
            yield return isBook || isBookChapter || isChapter ? "# " + plain : line;
        }
    }

    public static ParsedBible ReadEpub(string path, string languageCode)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.GetEntry("META-INF/rights.xml") is not null) throw Protected();
        if (zip.GetEntry("META-INF/encryption.xml") is { } enc)
        {
            // fonts may be obfuscated (that is not a copy protection); encrypted text pages are
            using var s = enc.Open();
            var doc = XDocument.Load(s);
            if (doc.Descendants().Where(e => e.Name.LocalName == "CipherReference").Select(e => (string?)e.Attribute("URI") ?? "").Any(u => !FontExtension().IsMatch(u))) throw Protected();
        }
        var pages = EpubPages(zip);
        if (pages.Count == 0) throw new InvalidDataException(L("In der EPUB-Datei wurden keine Textseiten gefunden."));
        var lines = pages.SelectMany(p => HtmlToLines(ReadEntry(p)));
        return ReadMarkdownLines(PromoteHeadings(lines), Path.GetFileNameWithoutExtension(path), languageCode);
    }

    [GeneratedRegex(@"\.(ttf|otf|woff2?|eot)$", RegexOptions.IgnoreCase)] private static partial Regex FontExtension();

    /// <summary>The text pages of an EPUB in reading order (the spine of the package file); without a usable spine all pages by name.</summary>
    static List<ZipArchiveEntry> EpubPages(ZipArchive zip)
    {
        var all = zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"\.(x?html?)$", RegexOptions.IgnoreCase)).ToList();
        try
        {
            var container = zip.GetEntry("META-INF/container.xml");
            var opfPath = container is null ? null : (string?)XDocument.Parse(ReadEntry(container)).Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path");
            if (opfPath is not null && zip.GetEntry(opfPath) is { } opfEntry)
            {
                var opf = XDocument.Parse(ReadEntry(opfEntry));
                var dir = opfPath.Contains('/') ? opfPath[..(opfPath.LastIndexOf('/') + 1)] : "";
                var manifest = opf.Descendants().Where(e => e.Name.LocalName == "item").ToDictionary(e => (string?)e.Attribute("id") ?? "", e => Uri.UnescapeDataString((string?)e.Attribute("href") ?? ""));
                var ordered = new List<ZipArchiveEntry>();
                foreach (var itemref in opf.Descendants().Where(e => e.Name.LocalName == "itemref"))
                    if (manifest.TryGetValue((string?)itemref.Attribute("idref") ?? "", out var href) && zip.GetEntry(NormalizePath(dir + href)) is { } page && !ordered.Contains(page)) ordered.Add(page);
                if (ordered.Count > 0) return ordered;
            }
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidDataException) { /* damaged package file: fall back to the file names */ }
        return all.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string NormalizePath(string p)
    {
        var parts = new List<string>();
        foreach (var part in p.Split('/'))
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); } else if (part != "." && part.Length > 0) parts.Add(part);
        return string.Join('/', parts);
    }

    static string ReadEntry(ZipArchiveEntry e)
    {
        using var s = new StreamReader(e.Open(), Encoding.UTF8, true);
        return s.ReadToEnd();
    }

    // ---- Word (.docx) and OpenDocument (.odt) ---------------------------------------------------------------------------

    public static ParsedBible ReadDocx(string path, string languageCode)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException(L("Das ist keine gültige Word-Datei (.docx)."));
        var doc = XDocument.Parse(ReadEntry(entry));
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var lines = new List<string>();
        foreach (var p in doc.Descendants(w + "p"))
        {
            var style = (string?)p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val") ?? "";
            var heading = style.Contains("eading", StringComparison.OrdinalIgnoreCase) || style.Contains("berschrift", StringComparison.OrdinalIgnoreCase) || style.Equals("Title", StringComparison.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            foreach (var r in p.Descendants(w + "r"))
            {
                var sup = (string?)r.Element(w + "rPr")?.Element(w + "vertAlign")?.Attribute(w + "val") == "superscript";
                var text = string.Concat(r.Elements().Select(e => e.Name == w + "t" ? e.Value : e.Name == w + "tab" ? " " : e.Name == w + "br" ? " " : ""));
                if (text.Length == 0) continue;
                sb.Append(sup && text.Trim().All(char.IsDigit) ? $"<sup>{text.Trim()}</sup> " : text);
            }
            var line = Whitespace().Replace(sb.ToString(), " ").Trim();
            if (line.Length > 0) lines.Add(heading ? "# " + line : line);
        }
        return ReadMarkdownLines(PromoteHeadings(lines), Path.GetFileNameWithoutExtension(path), languageCode);
    }

    public static ParsedBible ReadOdt(string path, string languageCode)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.GetEntry("META-INF/manifest.xml") is { } mf && ReadEntry(mf).Contains("encryption-data", StringComparison.OrdinalIgnoreCase)) throw Protected();
        var entry = zip.GetEntry("content.xml") ?? throw new InvalidDataException(L("Das ist keine gültige OpenDocument-Datei (.odt)."));
        var doc = XDocument.Parse(ReadEntry(entry));
        XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0", style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0", fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
        var superscript = doc.Descendants(style + "style")
            .Where(s => ((string?)s.Element(style + "text-properties")?.Attribute(style + "text-position") ?? "").StartsWith("super"))
            .Select(s => (string?)s.Attribute(style + "name") ?? "").ToHashSet();
        string Inline(XElement e)
        {
            var sb = new StringBuilder();
            foreach (var n in e.Nodes())
                if (n is XText t) sb.Append(t.Value);
                else if (n is XElement x)
                {
                    if (x.Name == text + "s") sb.Append(' ');
                    else if (x.Name == text + "line-break" || x.Name == text + "tab") sb.Append(' ');
                    else if (x.Name == text + "span")
                    {
                        var inner = Inline(x);
                        sb.Append(superscript.Contains((string?)x.Attribute(text + "style-name") ?? "") && inner.Trim().All(char.IsDigit) && inner.Trim().Length > 0 ? $"<sup>{inner.Trim()}</sup> " : inner);
                    }
                    else if (x.Name != text + "note") sb.Append(Inline(x));   // footnotes are left out
                }
            return sb.ToString();
        }
        var lines = new List<string>();
        foreach (var e in doc.Descendants().Where(e => (e.Name == text + "p" || e.Name == text + "h") && !e.Ancestors(text + "note").Any()))   // footnote paragraphs are not text
        {
            var line = Whitespace().Replace(Inline(e), " ").Trim();
            if (line.Length > 0) lines.Add(e.Name == text + "h" ? "# " + line : line);
        }
        _ = fo;
        return ReadMarkdownLines(PromoteHeadings(lines), Path.GetFileNameWithoutExtension(path), languageCode);
    }

    // ---- FictionBook (.fb2) -------------------------------------------------------------------------------------------

    public static ParsedBible ReadFb2(string path, string languageCode) =>
        ReadMarkdownLines(PromoteHeadings(Fb2ToLines(XDocument.Load(path))), Path.GetFileNameWithoutExtension(path), languageCode);

    public static List<string> Fb2ToLines(XDocument doc)
    {
        var lines = new List<string>();
        string Text(XElement e) => string.Concat(e.DescendantNodes().OfType<XText>().Where(t => t.Parent is { } p && p.Name.LocalName is not ("note" or "a")).Select(t => t.Parent!.Name.LocalName == "sup" ? $"<sup>{t.Value}</sup> " : t.Value));
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName is "p" or "v" or "subtitle" && e.Parent?.Name.LocalName != "title"))
        {
            var line = Whitespace().Replace(Text(e), " ").Trim();
            if (line.Length > 0) lines.Add(e.Name.LocalName == "subtitle" ? "# " + line : line);
        }
        foreach (var title in doc.Descendants().Where(e => e.Name.LocalName == "title" && e.Parent?.Name.LocalName == "section"))
        {
            var head = Whitespace().Replace(string.Concat(title.DescendantNodes().OfType<XText>().Select(t => t.Value + " ")), " ").Trim();
            if (head.Length == 0) continue;
            // titles belong in front of their section: put them back at the right place (before the first paragraph of the section)
            var first = title.Parent!.Elements().SkipWhile(x => x.Name.LocalName == "title").FirstOrDefault(x => x.Name.LocalName == "p");
            var firstLine = first is null ? null : Whitespace().Replace(Text(first), " ").Trim();
            var at = firstLine is null ? -1 : lines.IndexOf(firstLine);
            if (at >= 0) lines.Insert(at, "# " + head); else lines.Add("# " + head);
        }
        return lines;
    }

    // ---- RTF ---------------------------------------------------------------------------------------------------------------

    public static ParsedBible ReadRtf(string path, string languageCode) =>
        ReadMarkdownLines(PromoteHeadings(RtfToLines(File.ReadAllText(path, Encoding.Latin1))), Path.GetFileNameWithoutExtension(path), languageCode);

    static readonly HashSet<string> RtfSkip = ["fonttbl", "colortbl", "stylesheet", "info", "pict", "header", "footer", "footnote", "object", "listtable", "listoverridetable", "generator", "xmlnstbl"];

    /// <summary>Text of an RTF file: paragraphs as lines, superscript digits as &lt;sup&gt;; special characters (\'e4, \u228) are decoded.</summary>
    public static IEnumerable<string> RtfToLines(string rtf)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);   // Windows code pages (RTF stores \x27e4 etc. in cp1252)
        var lines = new List<string>(); var sb = new StringBuilder();
        var skipDepth = new Stack<bool>(); var superDepth = new Stack<bool>();
        bool skipping = false, super = false; var depth = 0; var skipAt = int.MaxValue;
        var pendingSuper = new StringBuilder();
        void Flush() { var l = Whitespace().Replace(sb.ToString(), " ").Trim(); if (l.Length > 0) lines.Add(l); sb.Clear(); }
        void Emit(string t)
        {
            if (skipping) return;
            if (super && t.All(char.IsDigit)) { pendingSuper.Append(t); return; }
            if (pendingSuper.Length > 0) { sb.Append("<sup>").Append(pendingSuper).Append("</sup> "); pendingSuper.Clear(); }
            sb.Append(t);
        }
        for (var i = 0; i < rtf.Length; i++)
        {
            var ch = rtf[i];
            if (ch == '{') { depth++; superDepth.Push(super); skipDepth.Push(skipping); if (i + 2 < rtf.Length && rtf[i + 1] == '\\' && rtf[i + 2] == '*') { skipping = true; skipAt = depth; } }
            else if (ch == '}') { if (depth == skipAt) skipAt = int.MaxValue; depth--; if (skipDepth.Count > 0) skipping = skipDepth.Pop(); if (superDepth.Count > 0) super = superDepth.Pop(); }
            else if (ch == '\\' && i + 1 < rtf.Length)
            {
                var n = rtf[i + 1];
                if (n is '\\' or '{' or '}') { Emit(n.ToString()); i++; }
                else if (n == '\'' && i + 3 < rtf.Length && int.TryParse(rtf.AsSpan(i + 2, 2), System.Globalization.NumberStyles.HexNumber, null, out var hex)) { Emit(Encoding.GetEncoding(1252).GetString([(byte)hex])); i += 3; }
                else if (char.IsLetter(n))
                {
                    var j = i + 1; while (j < rtf.Length && char.IsLetter(rtf[j])) j++;
                    var word = rtf[(i + 1)..j];
                    var k = j; if (k < rtf.Length && rtf[k] == '-') k++;
                    while (k < rtf.Length && char.IsDigit(rtf[k])) k++;
                    var arg = rtf[j..k];
                    if (k < rtf.Length && rtf[k] == ' ') k++;
                    if (RtfSkip.Contains(word)) skipping = true;
                    else if (word is "par" or "line" or "sect" or "page") { if (!skipping) { if (pendingSuper.Length > 0) { sb.Append("<sup>").Append(pendingSuper).Append("</sup> "); pendingSuper.Clear(); } Flush(); } }
                    else if (word == "tab") Emit(" ");
                    else if (word == "super") super = true;
                    else if (word is "nosupersub") super = false;
                    else if (word == "u" && int.TryParse(arg, out var code)) { Emit(char.ConvertFromUtf32(code < 0 ? code + 65536 : code)); if (k < rtf.Length && rtf[k] == '?') k++; }
                    i = k - 1;
                }
                else i++;
            }
            else if (ch is '\r' or '\n') { }
            else Emit(ch.ToString());
        }
        if (pendingSuper.Length > 0) sb.Append("<sup>").Append(pendingSuper).Append("</sup>");
        Flush();
        return lines;
    }

    // ---- PDF ---------------------------------------------------------------------------------------------------------------

    public static ParsedBible ReadPdf(string path, string languageCode)
    {
        var lines = new List<string>();
        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
            if (pdf.IsEncrypted) throw Protected();   // not opened even when it could be: that would be removing a protection
            foreach (var page in pdf.GetPages())
                // the words of a page as lines: same baseline = same line, top to bottom, left to right
                foreach (var row in page.GetWords().GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3)).OrderByDescending(g => g.Key))
                {
                    var line = string.Join(" ", row.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)).Trim();
                    if (line.Length > 0) lines.Add(line);
                }
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase)) { throw Protected(); }
        return ReadMarkdownLines(PromoteHeadings(lines), Path.GetFileNameWithoutExtension(path), languageCode);
    }
}
