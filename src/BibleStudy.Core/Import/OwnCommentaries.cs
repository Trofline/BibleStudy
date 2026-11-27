using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using BibleStudy.Core.Materials;
using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Import;

/// <summary>One entry of a commentary: the verses From..To of a chapter (From 0: the chapter as a whole, an introduction; To 999: on into the next chapter).</summary>
public sealed record CommentaryEntry(string Book, int Chapter, int From, int To, string Text);

public sealed record ParsedCommentary(string Name, IReadOnlyList<CommentaryEntry> Entries);

/// <summary>
/// Commentaries the user brings along. BibleStudy ships none: what a commentary teaches is for the user to judge, so only files the user chooses are read:
/// <list type="bullet">
/// <item>modules of Bible programs: e-Sword (.cmtx, e-Sword HD .cmti), MySword (.cmt.mybible), MyBible (.commentaries.SQLite3), theWord (.cmt.twm)
/// and SWORD (Xiphos, BibleTime, AndBible: the .zip of CrossWire, a .conf of an installed module, or a .imp file);</item>
/// <item>OSIS commentaries (.xml, .osis);</item>
/// <item>documents that another program (Logos, Accordance, Word …) exported: Word (.docx), OpenDocument (.odt), RTF, PDF and web pages (HTML),
/// and Markdown or text files. In them a line with a Bible reference ("## Joh 3,16-18", "John 3:16–18", after a heading with the book also "3:16" or "Vers 16")
/// starts the entry for these verses, and a heading with a chapter ("## Johannes 3") the introduction of that chapter.</item>
/// </list>
/// Protected files (Logos resources, locked or encrypted modules) are not read, and BibleStudy removes no protection.
/// </summary>
public static partial class OwnCommentaries
{
    public static readonly string[] Extensions =
        [".cmtx", ".cmti", ".mybible", ".sqlite3", ".twm", ".zip", ".conf", ".imp", ".xml", ".osis", ".docx", ".odt", ".rtf", ".pdf", ".html", ".htm", ".md", ".markdown", ".txt"];

    /// <summary>The patterns for the file dialog.</summary>
    public static string[] Patterns => Extensions.Select(e => "*" + e).ToArray();

    public static ParsedCommentary Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var fallback = NameOf(path);
        var parsed = ext switch
        {
            ".md" or ".markdown" or ".txt" => ReadText(File.ReadAllText(path), fallback),
            ".imp" => ReadImp(File.ReadAllText(path), fallback),
            ".docx" or ".odt" or ".rtf" or ".pdf" or ".html" or ".htm" => ReadDocument(path, fallback),
            ".xml" or ".osis" => ReadOsis(path, fallback),
            ".zip" or ".conf" => ReadSword(path),
            ".twm" => ReadTheWord(path, fallback),
            ".lbxlls" or ".logos4" or ".lbslcr" or ".lbspbb" => throw LogosProtected(),
            _ => ReadModule(path, fallback),
        };
        if (parsed.Entries.Count == 0)
            throw new InvalidDataException(L("In der Datei wurden keine Kommentare gefunden. Erwartet: ein Kommentar aus e-Sword, MySword, MyBible, theWord oder SWORD, eine OSIS-Datei oder ein Dokument (Word, PDF, HTML, Markdown, Text), in dem jede Bibelstelle (zum Beispiel „## Joh 3,16“) eine eigene Zeile hat."));
        return parsed;
    }

    /// <summary>Logos keeps its books in its own protected files. What Logos lets you export (Word, RTF, PDF) can be read in.</summary>
    public static InvalidDataException LogosProtected() =>
        new(L("Logos-Ressourcen sind kopiergeschützt und werden nicht gelesen. Exportiere den Text in Logos als Word-Datei (Drucken/Exportieren → Microsoft Word) und lies diese Datei ein."));

    /// <summary>"Calvin.cmt.mybible" → "Calvin", "Henry.commentaries.SQLite3" → "Henry".</summary>
    static string NameOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var tail in new[] { ".cmt", ".commentaries" })
            if (name.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) name = name[..^tail.Length];
        return name.Trim().Length > 0 ? name.Trim() : "Kommentar";
    }

    // ---- e-Sword, MySword and MyBible modules ------------------------------------------------------------------------------

    static ParsedCommentary ReadModule(string path, string fallback)
    {
        ImportedNotes read;
        try { read = EswordNotes.Read(path); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 26)   // SQLITE_NOTADB: encrypted, or no module at all
        {
            throw new InvalidDataException(L("Die Datei lässt sich nicht als Modul öffnen. Gekaufte Module (zum Beispiel aus e-Sword HD) sind oft verschlüsselt; solche Dateien liest BibleStudy nicht."));
        }
        var entries = read.Notes.Select(n => new CommentaryEntry(n.Ref.Book, n.Ref.Chapter, n.Ref.Verse, Math.Max(n.Ref.Verse, n.EndVerse), n.Text)).ToList();
        // the text about a chapter or a book as a whole (the reader names it "<module>: <book> <chapter>")
        foreach (var t in read.Texts)
        {
            var m = ChapterTitle().Match(t.Title);
            if (m.Success) entries.Add(new CommentaryEntry(m.Groups["b"].Value, m.Groups["c"].Success ? int.Parse(m.Groups["c"].Value) : 1, 0, 0, t.Text));
        }
        return new ParsedCommentary(ModuleTitle(path) ?? fallback, entries);
    }

    /// <summary>The title the module gives itself (e-Sword: Details.Title, MySword: Details.Description, MyBible: info "description"); null when it has none.</summary>
    static string? ModuleTitle(string path)
    {
        try
        {
            using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            con.Open();
            foreach (var sql in new[] { "SELECT Title FROM Details LIMIT 1", "SELECT Description FROM Details LIMIT 1", "SELECT value FROM info WHERE name='description' LIMIT 1" })
            {
                try
                {
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = sql;
                    if (cmd.ExecuteScalar() is string s && s.Trim().Length > 0) return EswordNotes.ToText(s).Split('\n')[0].Trim();
                }
                catch (SqliteException) { /* the next */ }
            }
        }
        catch (SqliteException) { /* no title: the file name serves */ }
        return null;
    }

    [GeneratedRegex(@": (?<b>[1-3A-Z]{3})(?: (?<c>\d+))?$")] private static partial Regex ChapterTitle();

    // ---- theWord -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// theWord commentaries (.cmt.twm): SQLite with the texts in "content" (RTF or HTML) and the places they belong to in "bible_refs"
    /// (book 1–66, chapter, verse and the end of the range). A module whose texts are encrypted or packed is not read.
    /// </summary>
    static ParsedCommentary ReadTheWord(string path, string fallback)
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        try { con.Open(); Columns(con, "content"); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 26) { throw BibleImporters.Protected(); }
        var content = Columns(con, "content");
        var refs = Columns(con, "bible_refs");
        if (content.Count == 0 || refs.Count == 0)
        {
            // an older module without topics: the e-Sword reader knows its tables by their columns
            return ReadModule(path, fallback);
        }
        string? Pick(List<string> cols, params string[] names) => cols.FirstOrDefault(c => names.Any(n => c.Equals(n, StringComparison.OrdinalIgnoreCase)));
        var text = Pick(content, "data", "content", "text");
        var topic = Pick(content, "topic_id", "id");
        var refTopic = Pick(refs, "topic_id");
        var bi = Pick(refs, "bi", "book"); var ci = Pick(refs, "ci", "chapter"); var vi = Pick(refs, "vi", "verse");
        var eci = Pick(refs, "eci"); var evi = Pick(refs, "evi");
        if (text is null || topic is null || refTopic is null || bi is null || ci is null || vi is null) return ReadModule(path, fallback);
        static string Q(string? c) => c is null ? "NULL" : "\"" + c.Replace("\"", "\"\"") + "\"";
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT r.{Q(bi)}, r.{Q(ci)}, r.{Q(vi)}, {(eci is null ? "NULL" : "r." + Q(eci))}, {(evi is null ? "NULL" : "r." + Q(evi))}, c.{Q(text)} "
            + $"FROM bible_refs r JOIN content c ON c.{Q(topic)} = r.{Q(refTopic)} ORDER BY r.rowid";
        var entries = new List<CommentaryEntry>();
        var unreadable = 0;
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (r.IsDBNull(5) || r.IsDBNull(0) || r.IsDBNull(1)) continue;
                if (r.GetValue(5) is not string raw) { unreadable++; continue; }   // a packed or encrypted text (a BLOB)
                var b = r.GetInt32(0); var c = r.GetInt32(1); var v = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                if (b is < 1 or > 66 || c < 1) continue;
                var body = EswordNotes.ToText(raw);
                if (body.Length == 0) continue;
                if (body.Count(ch => ch < ' ' && ch is not '\n' and not '\t') > body.Length / 20) { unreadable++; continue; }
                var ec = r.IsDBNull(3) ? c : r.GetInt32(3); var ev = r.IsDBNull(4) ? v : r.GetInt32(4);
                entries.Add(new CommentaryEntry(BookData.UsfmIds[b - 1], c, v, v == 0 ? 0 : ec > c ? 999 : Math.Max(v, ev), body));
            }
        }
        if (entries.Count == 0 && unreadable > 0) throw BibleImporters.Protected();
        string? title = null;
        try
        {
            using var t = con.CreateCommand();
            t.CommandText = "SELECT value FROM config WHERE name IN ('title','description') ORDER BY name DESC LIMIT 1";
            title = (t.ExecuteScalar() as string)?.Trim();
        }
        catch (SqliteException) { /* no title */ }
        return new ParsedCommentary(string.IsNullOrWhiteSpace(title) ? fallback : title!, entries);
    }

    static List<string> Columns(SqliteConnection con, string table)
    {
        using var c = con.CreateCommand();
        c.CommandText = "SELECT name FROM pragma_table_info($t)";
        c.Parameters.AddWithValue("$t", table);
        using var r = c.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    // ---- SWORD -------------------------------------------------------------------------------------------------------------

    static ParsedCommentary ReadSword(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !SwordModule.IsSwordZip(path))
            throw new InvalidDataException(L("Diese ZIP-Datei ist kein SWORD-Modul (es fehlt der Ordner mods.d mit der .conf-Datei)."));
        var module = SwordModule.Read(path, wantBible: false);
        var entries = new List<CommentaryEntry>();
        foreach (var e in module.Entries)
        {
            // "@LINK": the verse shares the note of the verse before it, which already stands there
            if (e.Markup.StartsWith("@LINK", StringComparison.Ordinal)) continue;
            var text = SwordModule.ToText(e.Markup);
            if (text.Length == 0) continue;
            var chapter = Math.Max(1, e.Chapter);
            // verses linked to one note (they point to the same text) become one entry for all of them
            if (e.Verse > 0 && entries.Count > 0 && entries[^1] is var last && last.Book == e.Book && last.Chapter == chapter && last.From > 0 && last.To == e.Verse - 1 && last.Text == text)
            { entries[^1] = last with { To = e.Verse }; continue; }
            entries.Add(new CommentaryEntry(e.Book, chapter, e.Verse, e.Verse, text));
        }
        return new ParsedCommentary(module.Description.Length > 0 ? module.Description : module.Name, entries);
    }

    /// <summary>The text format of SWORD (mod2imp): "$$$Gen 1:1" starts the entry of a verse.</summary>
    public static ParsedCommentary ReadImp(string text, string fallbackName)
    {
        var sb = new StringBuilder();
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
            sb.Append(line.StartsWith("$$$", StringComparison.Ordinal) ? "## " + line[3..].Trim() : SwordModule.ToText(line)).Append('\n');
        return ReadText(sb.ToString(), fallbackName);
    }

    // ---- OSIS --------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// An OSIS commentary: an element with annotateRef ("Gen.1.1-Gen.1.3") or a verse (osisID or the milestones sID … eID) holds the entry for its verses;
    /// text right in a chapter (osisID "Gen.1") is its introduction.
    /// </summary>
    static ParsedCommentary ReadOsis(string path, string fallback)
    {
        var name = fallback;
        var entries = new List<CommentaryEntry>();
        var open = new List<(int Depth, (string Book, int Chapter, int From, int To) At, StringBuilder Text)>();
        (string Book, int Chapter, int From, int To)? milestone = null;
        var milestoneText = new StringBuilder();
        var titleNext = false;
        void Done((string Book, int Chapter, int From, int To) at, StringBuilder sb)
        {
            var t = Regex.Replace(Regex.Replace(sb.ToString(), @"[ \t]+", " "), @" ?\n ?", "\n");
            t = Regex.Replace(t, @"\n{3,}", "\n\n").Trim();
            if (t.Length > 0) entries.Add(new CommentaryEntry(at.Book, at.Chapter, at.From, at.To, t));
            sb.Clear();
        }
        StringBuilder? Target() => milestone is not null ? milestoneText : open.Count > 0 ? open[^1].Text : null;

        using var r = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        while (r.Read())
        {
            switch (r.NodeType)
            {
                case XmlNodeType.Element:
                {
                    var n = r.LocalName;
                    if (n == "title" && !r.IsEmptyElement && open.Count == 0 && milestone is null && name == fallback) titleNext = true;
                    if (n is "p" or "lb" or "l" or "item" or "br" or "div") Target()?.Append('\n');
                    if (n == "verse" && r.GetAttribute("eID") is not null && r.GetAttribute("sID") is null)
                    {
                        if (milestone is { } m) Done(m, milestoneText);
                        milestone = null;
                        break;
                    }
                    if (n == "verse" && r.GetAttribute("sID") is not null)
                    {
                        if (milestone is { } m) Done(m, milestoneText);
                        milestone = OsisPlace(r.GetAttribute("osisID") ?? r.GetAttribute("sID"));
                        break;
                    }
                    var place = OsisPlace(r.GetAttribute("annotateRef"))
                        ?? (n is "verse" ? OsisPlace(r.GetAttribute("osisID")) : null)
                        ?? (n is "chapter" or "div" ? OsisChapter(r.GetAttribute("osisID")) : null);
                    if (place is { } p && !r.IsEmptyElement) open.Add((r.Depth, p, new StringBuilder()));
                    break;
                }
                case XmlNodeType.EndElement:
                    if (r.LocalName is "p" or "l" or "item" or "title" or "head" or "div") Target()?.Append('\n');
                    if (open.Count > 0 && open[^1].Depth == r.Depth)
                    {
                        // a milestone verse that is still open when its chapter ends is closed with it
                        if (milestone is { } m) { Done(m, milestoneText); milestone = null; }
                        Done(open[^1].At, open[^1].Text);
                        open.RemoveAt(open.Count - 1);
                    }
                    break;
                case XmlNodeType.Text or XmlNodeType.CDATA:
                    if (titleNext) { name = r.Value.Trim(); titleNext = false; break; }
                    Target()?.Append(r.Value);
                    break;
                case XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace:
                    Target()?.Append(' ');
                    break;
            }
        }
        if (milestone is { } last) Done(last, milestoneText);
        return new ParsedCommentary(name, entries);
    }

    /// <summary>"Gen.1.1", "Gen.1.1-Gen.1.3", "Gen.1.1 Gen.1.2" → the verses.</summary>
    static (string Book, int Chapter, int From, int To)? OsisPlace(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var parts = id.Trim().Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);
        static (string, int, int)? One(string s)
        {
            var p = s.Split('!')[0].Split('.');
            if (p.Length < 3) return null;
            var at = p[0].Contains(':') ? p[0][(p[0].IndexOf(':') + 1)..] : p[0];
            if (BibleImporters.BookFromName(at) is not { } b || !int.TryParse(p[1], out var c) || !int.TryParse(p[2], out var v)) return null;
            return (b, c, v);
        }
        if (One(parts[0]) is not { } first) return null;
        var to = first.Item3;
        if (parts.Length > 1 && One(parts[^1]) is { } end && end.Item1 == first.Item1) to = end.Item2 > first.Item2 ? 999 : Math.Max(first.Item3, end.Item3);
        return (first.Item1, first.Item2, first.Item3, to);
    }

    /// <summary>"Gen.1" → the introduction of Genesis 1.</summary>
    static (string Book, int Chapter, int From, int To)? OsisChapter(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var p = id.Trim().Split(' ')[0].Split('.');
        if (p.Length != 2 || BibleImporters.BookFromName(p[0]) is not { } b || !int.TryParse(p[1], out var c)) return null;
        return (b, c, 0, 0);
    }

    // ---- documents (exports of Logos and other programs) ------------------------------------------------------------------

    /// <summary>
    /// A document (Word, OpenDocument, RTF, PDF, HTML) is read into lines and then like Markdown: headings stay headings, and a paragraph that begins
    /// with a reference in bold ("**Joh 3,16** Gott liebt …", the way Logos and other programs export commentaries) begins the entry for it.
    /// </summary>
    static ParsedCommentary ReadDocument(string path, string fallback)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var lines = new List<string>();
        if (ext is ".html" or ".htm")
        {
            var html = File.ReadAllText(path);
            var title = Regex.Match(html, @"<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (title.Success && System.Net.WebUtility.HtmlDecode(title.Groups[1].Value).Trim() is { Length: > 0 } t) lines.Add("# " + t);
            // bold at the start of a paragraph stays marked, so that a reference there is found
            html = Regex.Replace(html, @"<(b|strong)\b[^>]*>(.*?)</\1\s*>", "**$2**", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            lines.AddRange(BibleImporters.HtmlToLines(html));
        }
        else
        {
            var doc = MaterialReader.Read(path);
            if (doc.Problem is { } problem) throw new InvalidDataException(problem);
            foreach (var b in doc.Blocks)
            {
                var text = b.Text.Trim();
                if (text.Length == 0) continue;
                if (b.IsHeading) { lines.Add(new string('#', b.HeadingLevel) + " " + text); continue; }
                // a paragraph that begins in bold keeps the mark, so that a reference there is found ("**16** Denn also …")
                if (b.Spans.Count > 1 && b.Spans[0].Bold && b.Spans[0].Text.Trim() is { Length: > 0 and <= 40 } lead && lead.Any(char.IsDigit))
                    lines.Add("**" + lead + "** " + string.Concat(b.Spans.Skip(1).Select(s => s.Text)).Trim());
                else lines.Add(text);
                lines.Add("");
            }
        }
        return ReadText(string.Join("\n", lines), fallback, lenient: true);
    }

    // ---- Markdown and text ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A line that is only a Bible reference (also as a heading, bold or with a colon) starts the entry for these verses; the lines below belong to it until the next
    /// such line. A heading with a book and a chapter ("# Johannes 3") starts the introduction of that chapter; after a heading with the book, "3:16", "Kapitel 3"
    /// or "Vers 16" are enough. A first heading that is no reference names the commentary.
    /// </summary>
    /// <param name="lenient">For exported documents: a line that begins with a reference in bold ("**3:16** text") also starts an entry, with the rest of the line as its text.</param>
    public static ParsedCommentary ReadText(string text, string fallbackName, bool lenient = false)
    {
        var lines = text.TrimStart('﻿').ReplaceLineEndings("\n").Split('\n').ToList();
        string? name = null;
        if (lines.Count > 0 && lines[0].Trim() == "---")
        {
            var end = lines.FindIndex(1, l => l.Trim() == "---");
            if (end > 0)
            {
                foreach (var l in lines.Take(end).Skip(1)) if (l.StartsWith("title:", StringComparison.OrdinalIgnoreCase)) name = l[6..].Trim().Trim('"', '\'');
                lines = lines.Skip(end + 1).ToList();
            }
        }
        var entries = new List<CommentaryEntry>();
        (string Book, int Chapter, int From, int To)? open = null;
        string? book = null; var chapter = 0;
        var body = new List<string>();
        void Close()
        {
            if (open is { } o)
            {
                var t = string.Join("\n", body.Select(b => b.Trim()).SkipWhile(b => b.Length == 0).Reverse().SkipWhile(b => b.Length == 0).Reverse());
                t = Regex.Replace(t, @"\n{3,}", "\n\n");
                if (t.Length > 0) entries.Add(new CommentaryEntry(o.Book, o.Chapter, o.From, o.To, t));
            }
            body.Clear();
        }
        foreach (var line in lines)
        {
            if (Header(line, book, chapter) is { } h)
            {
                Close();
                book = h.Book; chapter = h.BookOnly ? 0 : h.Chapter;
                // the text under a heading with only the book is the introduction of its first chapter
                open = (h.Book, h.Chapter, h.From, h.To);
                continue;
            }
            if (lenient && LeadingBold().Match(line) is { Success: true } lb
                && (Header(lb.Groups["r"].Value, book, chapter) is { BookOnly: false } named ? named : BoldVerse(lb.Groups["r"].Value, book, chapter)) is { } inline)
            {
                Close();
                book = inline.Book; chapter = inline.Chapter;
                open = (inline.Book, inline.Chapter, inline.From, inline.To);
                body.Add(lb.Groups["t"].Value);
                continue;
            }
            // bold at the start that is no reference stays text, without the marks
            var plain = lenient && LeadingBold().Match(line) is { Success: true } bold ? bold.Groups["r"].Value + " " + bold.Groups["t"].Value : line;
            if (open is null)
            {
                if (name is null && line.StartsWith("# ", StringComparison.Ordinal) && line[2..].Trim().Length > 0) name = line[2..].Trim();
                continue;
            }
            body.Add(plain);
        }
        Close();
        return new ParsedCommentary(string.IsNullOrWhiteSpace(name) ? fallbackName : name!, entries);
    }

    [GeneratedRegex(@"^\s*\*\*(?<r>[^*]{1,40})\*\*\s*[:.]?\s*(?<t>\S.*)$")] private static partial Regex LeadingBold();

    /// <summary>A bold number at the start of a paragraph ("**16** For God …", "**16–18**"): a verse of the chapter of the heading before.</summary>
    static Place? BoldVerse(string text, string? book, int chapter)
    {
        var m = Regex.Match(text.Trim(), @"^(?<v>\d{1,3})[a-e]?(?:\s*[-–—]\s*(?<e>\d{1,3})[a-e]?)?$");
        if (!m.Success || book is null || chapter <= 0) return null;
        var from = int.Parse(m.Groups["v"].Value);
        var to = m.Groups["e"].Success ? int.Parse(m.Groups["e"].Value) : from;
        return from is > 0 and <= 176 ? new(book, chapter, from, Math.Max(from, to)) : null;
    }

    sealed record Place(string Book, int Chapter, int From, int To, bool BookOnly = false);

    /// <summary>
    /// The place a line stands for if the line is nothing else than that place; null otherwise. <paramref name="book"/> and <paramref name="chapter"/> are those of
    /// the heading before, for the short forms ("3:16", "Vers 16").
    /// </summary>
    static Place? Header(string line, string? book, int chapter)
    {
        var heading = line.TrimStart().StartsWith('#');
        var t = line.Trim().TrimStart('#', '>', ' ').Trim().Trim('*', '_', ' ').TrimEnd(':', '.').Trim().Trim('*', '_', ' ');
        if (t.Length == 0 || t.Length > 70) return null;
        t = t.Replace(' ', ' ');
        if (DocumentLinks.VerseSpans(t) is { Count: > 0 })
        {
            var span = DocumentLinks.Spans(t);
            if (span.Count == 1 && span[0].Index == 0 && span[0].Length >= t.Length - 1)
            {
                var s = span[0];
                return new(s.Ref.Book, s.Ref.Chapter, s.Ref.Verse, s.EndChapter > s.Ref.Chapter ? 999 : Math.Max(s.Ref.Verse, s.EndVerse));
            }
        }
        // a reference with a name the links do not know ("Song of Solomon 2:1", "Ge 1:1" from Logos)
        var full = FullRef().Match(t);
        if (full.Success && BookName(full.Groups["b"].Value) is { } named) return Range(named, full);
        if (book is not null)
        {
            // after a heading with the book: "3:16", "3,16-18"
            var bare = BareRef().Match(t);
            if (bare.Success) return Range(book, bare);
            // after a heading with the chapter: "Vers 16", "V. 16-18", "Verses 16–18", "vv. 16-18"
            var verse = VerseWord().Match(t);
            if (verse.Success && chapter > 0)
            {
                var from = int.Parse(verse.Groups["v"].Value);
                var to = verse.Groups["e"].Success ? int.Parse(verse.Groups["e"].Value) : from;
                if (from > 0) return new(book, chapter, from, Math.Max(from, to));
            }
            // a heading "Kapitel 3", "Chapter 3"
            var ch = ChapterWord().Match(t);
            if (ch.Success && heading && int.Parse(ch.Groups["c"].Value) is > 0 and var c) return new(book, c, 0, 0);
        }
        var m = ChapterLine().Match(t);
        if (m.Success && BookName(m.Groups["b"].Value.Trim()) is { } b && int.Parse(m.Groups["c"].Value) is > 0 and var chapterNumber && (heading || m.Groups["rest"].Length == 0))
            return new(b, chapterNumber, 0, 0);
        // a heading that is only a book ("# Johannes", "# The Gospel of John")
        if (heading && t.Length <= 40 && !t.Any(char.IsDigit) || heading && BookName(t) is not null)
        {
            var only = BookName(t) ?? BookName(Regex.Replace(t, @"^(?:Das |Der |Die |The )?(?:Evangelium nach |Gospel (?:of|according to) |Buch |Book of |Brief an die |Epistle to the |Letter to the )", "", RegexOptions.IgnoreCase));
            if (only is not null) return new(only, 1, 0, 0, BookOnly: true);
        }
        return null;
    }

    static Place Range(string book, Match m)
    {
        var c = int.Parse(m.Groups["c"].Value); var v = int.Parse(m.Groups["v"].Value);
        var ec = m.Groups["ec"].Success ? int.Parse(m.Groups["ec"].Value) : c;
        var ev = m.Groups["e"].Success ? int.Parse(m.Groups["e"].Value) : v;
        return new(book, c, v, ec > c ? 999 : Math.Max(v, ev));
    }

    /// <summary>A book by any name the importers know (German, English, OSIS, the abbreviations of Logos).</summary>
    static string? BookName(string name)
    {
        var n = name.Trim().TrimEnd('.');
        return n.Length == 0 ? null : DocumentLinks.BookId(n) ?? BibleImporters.BookFromName(n);
    }

    const string Verses = @"(?<c>\d{1,3})\s*[:,]\s*(?<v>\d{1,3})[a-e]?(?:\s*[-–—]\s*(?:(?<ec>\d{1,3})\s*[:,]\s*)?(?<e>\d{1,3})[a-e]?)?(?:\s?ff?\.?)?";
    [GeneratedRegex(@"^(?<b>(?:[1-3]\s?\.?\s?)?\p{L}[\p{L}.]*(?:\s(?:of\s)?\p{L}[\p{L}.]*){0,3})\s+" + Verses + "$")] private static partial Regex FullRef();
    [GeneratedRegex(@"^(?:\(|\[)?" + Verses + @"(?:\)|\])?$")] private static partial Regex BareRef();
    [GeneratedRegex(@"^(?:Vers(?:e)?|V|Vv|Verses?|v|vv)\.?\s*(?<v>\d{1,3})[a-e]?(?:\s*[-–—]\s*(?<e>\d{1,3})[a-e]?)?(?:\s?ff?\.?)?$", RegexOptions.IgnoreCase)] private static partial Regex VerseWord();
    [GeneratedRegex(@"^(?:Kapitel|Kap|Chapter|Ch|Chap)\.?\s*(?<c>\d{1,3})$", RegexOptions.IgnoreCase)] private static partial Regex ChapterWord();
    [GeneratedRegex(@"^(?<b>[1-3]?\.?\s?\p{L}[\p{L}\. ]*?)\s+(?<c>\d{1,3})\s*(?<rest>[(\[:–—-].*)?$")] private static partial Regex ChapterLine();
}
