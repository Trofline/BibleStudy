using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Import;

/// <summary>
/// More readers for Bible files the user already owns: OSIS and USX (XML), USFM (also zipped), MySword modules, and plain text / CSV / JSON
/// with one verse per line or entry. Only the verse text is taken (no notes, headings or word data).
/// </summary>
public static partial class BibleImporters
{
    /// <summary>Collects verse text in the order it appears; text of one verse may arrive in pieces.</summary>
    sealed class Collector
    {
        readonly Dictionary<(string, int, int), StringBuilder> _by = [];
        readonly List<(string, int, int)> _order = [];

        public void Add(string book, int chapter, int verse, string text)
        {
            text = Whitespace().Replace(text, " ").Trim();
            if (text.Length == 0 || verse < 1 || chapter < 1) return;
            var key = (book, chapter, verse);
            if (!_by.TryGetValue(key, out var sb)) { _by[key] = sb = new StringBuilder(); _order.Add(key); }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(text);
        }

        public ParsedBible Done(string name, string languageCode)
        {
            if (_order.Count < 10) throw new InvalidDataException("In der Datei wurden kaum Verse gefunden. Ist es wirklich eine Bibelübersetzung in diesem Format?");
            var verses = _order.Select(k => new ParsedVerse(k.Item1, k.Item2, k.Item3, [new Segment(_by[k].ToString())])).ToList();
            return new ParsedBible(name, name.Length > 12 ? name[..12].Trim() : name, languageCode, verses, []);
        }
    }

    // ---- book names -------------------------------------------------------------------------------------------------------

    static readonly Dictionary<string, string> BookLookup = BuildBookLookup();

    static string Key(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    static Dictionary<string, string> BuildBookLookup()
    {
        var d = new Dictionary<string, string>();
        void Put(string name, string usfm) { var k = Key(name); if (k.Length > 0) d.TryAdd(k, usfm); }
        for (var i = 0; i < 66; i++)
        {
            var id = BookData.UsfmIds[i];
            Put(id, id); Put(BookData.GermanNames[i], id); Put(BookData.EnglishNames[i], id); Put(BookData.GermanAbbreviations[i], id); Put((i + 1).ToString(), id);
        }
        // OSIS ids and common English abbreviations
        const string extra = "Gen:GEN Exod:EXO Lev:LEV Num:NUM Deut:DEU Josh:JOS Judg:JDG Ruth:RUT 1Sam:1SA 2Sam:2SA 1Kgs:1KI 2Kgs:2KI 1Chr:1CH 2Chr:2CH Ezra:EZR Neh:NEH Esth:EST Job:JOB Ps:PSA Prov:PRO Eccl:ECC Song:SNG Isa:ISA Jer:JER Lam:LAM Ezek:EZK Dan:DAN Hos:HOS Joel:JOL Amos:AMO Obad:OBA Jonah:JON Mic:MIC Nah:NAM Hab:HAB Zeph:ZEP Hag:HAG Zech:ZEC Mal:MAL " +
            "Matt:MAT Mark:MRK Luke:LUK John:JHN Acts:ACT Rom:ROM 1Cor:1CO 2Cor:2CO Gal:GAL Eph:EPH Phil:PHP Col:COL 1Thess:1TH 2Thess:2TH 1Tim:1TI 2Tim:2TI Titus:TIT Phlm:PHM Heb:HEB Jas:JAS 1Pet:1PE 2Pet:2PE 1John:1JN 2John:2JN 3John:3JN Jude:JUD Rev:REV " +
            "Ge:GEN Ex:EXO Le:LEV Nu:NUM Dt:DEU Jos:JOS Jdg:JDG 1Sa:1SA 2Sa:2SA 1Ki:1KI 2Ki:2KI 1Ch:1CH 2Ch:2CH Ne:NEH Es:EST Pr:PRO Ec:ECC So:SNG Is:ISA Eze:EZK Da:DAN Ho:HOS Jl:JOL Am:AMO Ob:OBA Jon:JON Mi:MIC Na:NAM Zep:ZEP Zec:ZEC Mt:MAT Mk:MRK Lk:LUK Jn:JHN Ro:ROM 1Co:1CO 2Co:2CO Ga:GAL Php:PHP 1Th:1TH 2Th:2TH 1Ti:1TI 2Ti:2TI Tit:TIT Phm:PHM He:HEB Jam:JAS 1Pe:1PE 2Pe:2PE 1Jn:1JN 2Jn:2JN 3Jn:3JN Re:REV Psalm:PSA Psalms:PSA";
        foreach (var pair in extra.Split(' ')) { var p = pair.Split(':'); Put(p[0], p[1]); }
        return d;
    }

    /// <summary>"Gen", "1. Mose", "Joh", "John", "1John", "PSA" … → USFM id; null when unknown.</summary>
    public static string? BookFromName(string name) => BookLookup.GetValueOrDefault(Key(name));

    // ---- OSIS / USX (XML) ------------------------------------------------------------------------------------------------

    /// <summary>Reads the root element to tell Zefania, OSIS and USX apart.</summary>
    static string XmlKind(string path)
    {
        using var r = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        while (r.Read()) if (r.NodeType == XmlNodeType.Element) return r.LocalName.ToLowerInvariant();
        return "";
    }

    public static ParsedBible ReadOsis(string path, string languageCode)
    {
        var c = new Collector();
        var name = Path.GetFileNameWithoutExtension(path);
        using var r = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        (string Book, int Chapter, int Verse)? cur = null;
        var container = false;                       // <verse osisID>text</verse> (otherwise milestones: sID … eID)
        var text = new StringBuilder();
        void Flush() { if (cur is { } k) c.Add(k.Book, k.Chapter, k.Verse, text.ToString()); text.Clear(); cur = null; container = false; }
        (string, int, int)? Parse(string? id)
        {
            if (id is null) return null;
            var p = id.Split(' ')[0].Split('-')[0].Split('.');
            if (p.Length < 3 || BookFromName(p[0]) is not { } b || !int.TryParse(p[1], out var ch) || !int.TryParse(p[2], out var v)) return null;
            return (b, ch, v);
        }
        r.Read();
        while (!r.EOF)
        {
            switch (r.NodeType)
            {
                case XmlNodeType.Element:
                    var n = r.LocalName.ToLowerInvariant();
                    if (n == "osistext" && r.GetAttribute("osisIDWork") is { Length: > 0 } work) name = work;
                    if (cur is not null && n is "note" or "title" or "rdg") { r.Skip(); continue; }
                    if (n == "verse")
                    {
                        if (r.GetAttribute("eID") is not null && r.GetAttribute("sID") is null) Flush();
                        else if (r.GetAttribute("sID") is { } sid) { Flush(); cur = Parse(sid); }
                        else if (r.GetAttribute("osisID") is { } oid && !r.IsEmptyElement) { Flush(); cur = Parse(oid); container = cur is not null; }
                    }
                    break;
                case XmlNodeType.EndElement:
                    if (container && r.LocalName.Equals("verse", StringComparison.OrdinalIgnoreCase)) Flush();
                    break;
                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    if (cur is not null) text.Append(r.Value);
                    break;
            }
            r.Read();
        }
        Flush();
        return c.Done(name, languageCode);
    }

    static readonly HashSet<string> UsxHeadingStyles = ["s", "s1", "s2", "s3", "r", "d", "h", "mt", "mt1", "mt2", "mt3", "ms", "ms1", "mr", "sp", "toc1", "toc2", "toc3", "ide", "rem", "cl", "is", "ip", "imt", "ie", "periph"];

    public static ParsedBible ReadUsx(string path, string languageCode) => ReadUsxStream(File.OpenRead(path), Path.GetFileNameWithoutExtension(path), languageCode, new Collector());

    static ParsedBible ReadUsxStream(Stream stream, string name, string languageCode, Collector c)
    {
        using (stream) ReadUsxInto(stream, c);
        return c.Done(name, languageCode);
    }

    static void ReadUsxInto(Stream stream, Collector c)
    {
        using var r = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        string? book = null; var chapter = 0; var verse = 0;
        r.Read();
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                var skip = false;
                switch (r.LocalName.ToLowerInvariant())
                {
                    case "book": if (r.GetAttribute("style") == "id") { book = BookFromName(r.GetAttribute("code") ?? ""); chapter = verse = 0; skip = true; } break;
                    case "chapter": if (r.GetAttribute("number") is { } cn && int.TryParse(cn, out var ci)) { chapter = ci; verse = 0; } break;
                    case "verse": verse = r.GetAttribute("number") is { } vn && int.TryParse(vn.Split('-')[0], out var vi) ? vi : 0; break;
                    case "note": skip = true; break;
                    case "para": skip = UsxHeadingStyles.Contains(r.GetAttribute("style") ?? ""); break;
                }
                if (skip) { r.Skip(); continue; }
            }
            else if (book is not null && verse > 0 && r.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace)
                c.Add(book, chapter, verse, r.Value);
            r.Read();
        }
    }

    // ---- USFM (single file, or a zip with one file per book) -------------------------------------------------------------

    public static ParsedBible ReadUsfm(string path, string languageCode)
    {
        var c = new Collector();
        var name = Path.GetFileNameWithoutExtension(path);
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var e in zip.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
            {
                var ext = Path.GetExtension(e.Name).ToLowerInvariant();
                if (ext is ".usfm" or ".sfm")
                {
                    using var s = new StreamReader(e.Open(), Encoding.UTF8);
                    ParseUsfm(s.ReadToEnd(), c);
                }
                else if (ext == ".usx") ReadUsxInto(e.Open(), c);
            }
        }
        else ParseUsfm(File.ReadAllText(path), c);
        return c.Done(name, languageCode);
    }

    static void ParseUsfm(string text, Collector c)
    {
        text = UsfmNotes().Replace(text, " ");
        text = UsfmWord().Replace(text, "$1");
        string? book = null; var chapter = 0; var verse = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('\\'))
            {
                var m = UsfmMarker().Match(line);
                var marker = m.Groups["m"].Value; var rest = m.Groups["r"].Value;
                if (marker == "id") { book = BookFromName(rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ""); chapter = verse = 0; continue; }
                if (marker == "c") { chapter = int.TryParse(rest.Split(' ')[0], out var ci) ? ci : chapter; verse = 0; continue; }
                if (marker == "v")
                {
                    var vm = UsfmVerse().Match(rest);
                    if (vm.Success) { verse = int.Parse(vm.Groups["n"].Value); if (book is not null) c.Add(book, chapter, verse, UsfmInline().Replace(vm.Groups["t"].Value, "")); }
                    continue;
                }
                if (UsxHeadingStyles.Contains(marker) || marker is "ca" or "cp" or "va" or "vp" or "restore" or "sts") continue;   // headings, parallel passages …
                line = rest;
            }
            if (book is not null && verse > 0) c.Add(book, chapter, verse, UsfmInline().Replace(line, ""));
        }
    }

    [GeneratedRegex(@"\\(f|fe|x|ef|ex)\s.*?\\\1\*", RegexOptions.Singleline)] private static partial Regex UsfmNotes();
    [GeneratedRegex(@"\\\+?w\s+([^|\\]*?)(?:\|[^\\]*)?\\\+?w\*")] private static partial Regex UsfmWord();
    [GeneratedRegex(@"^\\(?<m>[a-z0-9]+)\*?\s*(?<r>.*)$", RegexOptions.IgnoreCase)] private static partial Regex UsfmMarker();
    [GeneratedRegex(@"^(?<n>\d+)[a-b]?(?:[-,]\d+[a-b]?)*\s*(?<t>.*)$")] private static partial Regex UsfmVerse();
    [GeneratedRegex(@"\\\+?[a-z0-9]+\*?\s?|\|[^\\]*(?=\\)", RegexOptions.IgnoreCase)] private static partial Regex UsfmInline();

    // ---- plain text, CSV, JSON -----------------------------------------------------------------------------------------------

    [GeneratedRegex(@"^\s*(?<b>(?:\d\.?\s*)?[^\W\d_][\w.]*(?:\s[^\W\d_][\w.]*)*)\s+(?<c>\d+)[:,.](?<v>\d+)\s*[\t ;|:-]?\s*(?<t>.+)$")] private static partial Regex LineRef();

    public static ParsedBible ReadTextLines(string path, string languageCode)
    {
        var c = new Collector();
        var csv = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(path);
        var delimiter = csv ? new[] { ';', '\t', ',', '|' }.MaxBy(d => lines.Take(20).Sum(l => l.Count(ch => ch == d))) : '\t';
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0) continue;
            if (csv || line.Contains('\t'))
            {
                var f = SplitFields(line, delimiter == '\t' || csv ? delimiter : '\t');
                if (f.Count >= 4 && BookFromName(f[0]) is { } b && int.TryParse(f[1], out var ch) && int.TryParse(f[2], out var v)) { c.Add(b, ch, v, string.Join(" ", f.Skip(3))); continue; }
                if (f.Count >= 2 && LineRef().Match(f[0] + " x") is { Success: true } rm && BookFromName(rm.Groups["b"].Value) is { } b2) { c.Add(b2, int.Parse(rm.Groups["c"].Value), int.Parse(rm.Groups["v"].Value), string.Join(" ", f.Skip(1))); continue; }
            }
            var m = LineRef().Match(line);
            if (m.Success && BookFromName(m.Groups["b"].Value) is { } bk) c.Add(bk, int.Parse(m.Groups["c"].Value), int.Parse(m.Groups["v"].Value), m.Groups["t"].Value);
        }
        return c.Done(Path.GetFileNameWithoutExtension(path), languageCode);
    }

    static List<string> SplitFields(string line, char delimiter)
    {
        var fields = new List<string>(); var sb = new StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else quoted = !quoted; }
            else if (ch == delimiter && !quoted) { fields.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString().Trim());
        return fields;
    }

    public static ParsedBible ReadJson(string path, string languageCode)
    {
        var c = new Collector();
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        static string? Str(JsonElement e, params string[] names) { foreach (var p in e.EnumerateObject()) if (names.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString(); return null; }
        string? bookOf(string? s) => s is null ? null : BookFromName(s);

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("books", out var books) && books.ValueKind == JsonValueKind.Array) root = books;
        if (root.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in root.EnumerateArray())
            {
                index++;
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("chapters", out var chapters) && chapters.ValueKind == JsonValueKind.Array)
                {
                    // { "name": "...", "chapters": [ ["verse 1", "verse 2"], ... ] } or chapters with verse objects
                    var b = bookOf(Str(item, "abbrev", "abbreviation", "id", "code", "osis")) ?? bookOf(Str(item, "name", "book", "title")) ?? (index <= 66 ? BookData.UsfmIds[index - 1] : null);
                    if (b is null) continue;
                    var ch = 0;
                    foreach (var chapter in chapters.EnumerateArray())
                    {
                        ch++;
                        var verses = chapter.ValueKind == JsonValueKind.Array ? chapter : chapter.TryGetProperty("verses", out var vv) ? vv : default;
                        if (verses.ValueKind != JsonValueKind.Array) continue;
                        var n = 0;
                        foreach (var v in verses.EnumerateArray())
                        {
                            n++;
                            if (v.ValueKind == JsonValueKind.String) c.Add(b, ch, n, v.GetString() ?? "");
                            else if (v.ValueKind == JsonValueKind.Object) c.Add(b, ch, int.TryParse(Str(v, "verse", "v", "number"), out var vn) ? vn : n, Str(v, "text", "t", "scripture") ?? "");
                        }
                    }
                }
                else if (bookOf(Str(item, "book", "b", "book_name", "bookname", "name")) is { } bk && int.TryParse(Str(item, "chapter", "c", "ch"), out var cn) && int.TryParse(Str(item, "verse", "v", "vers"), out var vn2))
                    c.Add(bk, cn, vn2, Str(item, "text", "t", "scripture", "content") ?? "");
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            // { "Genesis": { "1": { "1": "In the beginning …" } } }
            foreach (var b in root.EnumerateObject())
            {
                if (bookOf(b.Name) is not { } book || b.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var ch in b.Value.EnumerateObject())
                {
                    if (!int.TryParse(ch.Name, out var cn) || ch.Value.ValueKind != JsonValueKind.Object) continue;
                    foreach (var v in ch.Value.EnumerateObject())
                        if (int.TryParse(v.Name, out var vn) && v.Value.ValueKind == JsonValueKind.String) c.Add(book, cn, vn, v.Value.GetString() ?? "");
                }
            }
        }
        return c.Done(Path.GetFileNameWithoutExtension(path), languageCode);
    }
}

public static partial class BibleImporters
{
    [GeneratedRegex(@"<sup>\s*(\d+)\s*</sup>|\^(\d+)\^|\*\*\s*(\d+)\s*\*\*|<b>\s*(\d+)\s*</b>")] private static partial Regex MdVerseMark();
    [GeneratedRegex(@"\[\^[^\]]*\]|\[([^\]]*)\]\([^)]*\)|<[^>]+>|[*_`~]+")] private static partial Regex MdInline();
    [GeneratedRegex(@"^(?<b>.+?)\s+(?<c>\d+)$")] private static partial Regex MdBookChapter();
    [GeneratedRegex(@"^(?:Kapitel|Chapter|Capítulo|Chapitre|Глава|Kap\.?|Ch\.?)?\s*(?<c>\d+)\.?$", RegexOptions.IgnoreCase)] private static partial Regex MdChapter();
    [GeneratedRegex(@"^\W*(?<v>\d{1,3})(?:[\s.:)\]-]+)(?<t>\S.*)$")] private static partial Regex MdVerseLine();

    /// <summary>
    /// Markdown: headings name the book ("# Genesis", "# 1. Mose") and the chapter ("## 1", "## Kapitel 1", "## Psalm 23");
    /// a verse is a line that starts with its number (also **1**, &lt;sup&gt;1&lt;/sup&gt;, "1." or "1 "). Lines with a full reference ("Joh 3,16 …") work too.
    /// </summary>
    public static ParsedBible ReadMarkdown(string path, string languageCode) => ReadMarkdownLines(File.ReadAllLines(path), Path.GetFileNameWithoutExtension(path), languageCode);

    /// <summary>The same reader for text that another format was turned into (headings as "#" lines, verse numbers as &lt;sup&gt;).</summary>
    static ParsedBible ReadMarkdownLines(IEnumerable<string> lines, string name, string languageCode)
    {
        var c = new Collector();
        string? book = null; var chapter = 0; var lastVerse = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("---") || line.StartsWith("```")) continue;
            if (line.StartsWith('#'))
            {
                var h = MdInline().Replace(line.TrimStart('#'), "$1").Trim();
                // "## 1" is a chapter, not the first book (book numbers are only for other formats)
                var ch = MdChapter().Match(h);
                if (ch.Success && book is not null) { chapter = int.Parse(ch.Groups["c"].Value); lastVerse = 0; continue; }
                if (!h.All(char.IsDigit) && BookFromName(h) is { } onlyBook) { book = onlyBook; chapter = 0; lastVerse = 0; continue; }
                var bc = MdBookChapter().Match(h);
                if (bc.Success && BookFromName(bc.Groups["b"].Value) is { } b2) { book = b2; chapter = int.Parse(bc.Groups["c"].Value); lastVerse = 0; }
                continue;
            }
            line = line.TrimStart('>', ' ');
            // marked verse numbers (<sup>1</sup>, **1**, ^1^): a paragraph may hold several verses
            var marks = MdVerseMark().Matches(line);
            if (marks.Count > 0 && book is not null && chapter > 0)
            {
                var before = Clean(line[..marks[0].Index]);
                if (before.Length > 0 && lastVerse > 0) c.Add(book, chapter, lastVerse, before);
                for (var i = 0; i < marks.Count; i++)
                {
                    var end = i + 1 < marks.Count ? marks[i + 1].Index : line.Length;
                    NextVerse(int.Parse(marks[i].Groups.Cast<Group>().Skip(1).First(g => g.Success).Value));
                    c.Add(book, chapter, lastVerse, Clean(line[(marks[i].Index + marks[i].Length)..end]));
                }
                continue;
            }
            line = Clean(MdVerseMark().Replace(line, m => " " + m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value + " "));
            if (line.Length == 0) continue;
            var full = LineRef().Match(line);
            if (full.Success && BookFromName(full.Groups["b"].Value) is { } bk)
            {
                book = bk; chapter = int.Parse(full.Groups["c"].Value); lastVerse = int.Parse(full.Groups["v"].Value);
                c.Add(bk, chapter, lastVerse, full.Groups["t"].Value);
                continue;
            }
            // a line that starts with a number is a new verse only when the number fits the count ("12 Stämme …" inside a verse is text)
            var vm = MdVerseLine().Match(line);
            if (vm.Success && book is not null && chapter > 0 && int.Parse(vm.Groups["v"].Value) is var n && (lastVerse == 0 || n == 1 || (n > lastVerse && n <= lastVerse + 3)))
            { NextVerse(n); c.Add(book, chapter, lastVerse, vm.Groups["t"].Value); }
            else if (book is not null && chapter > 0 && lastVerse > 0) c.Add(book, chapter, lastVerse, line);   // a verse that continues in the next line
        }
        return c.Done(name, languageCode);

        // verse 1 after higher verses without a heading in between: the next chapter has begun
        void NextVerse(int n) { if (n == 1 && lastVerse > 1) chapter++; lastVerse = n; }
        static string Clean(string s) => MdInline().Replace(s, "$1").Trim();
    }
}
