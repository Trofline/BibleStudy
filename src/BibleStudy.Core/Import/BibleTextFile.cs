using System.Text;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.Core.Import;

/// <summary>
/// The own text format of BibleStudy: a whole translation in one .txt file, with everything the importer recognized (Strong's numbers,
/// footnotes and where their markers stand, headings, words of Jesus, the exact pieces of each verse). The importer reads it back
/// into the very same translation, so a translation can be kept, edited by hand or given to someone else.
/// <code>
/// #BIBLESTUDY-TXT 1
/// #NAME	Elberfelder Übersetzung
/// #SHORT	ELB
/// #LANG	de
/// #HEADING	GEN 1:1	Die Erschaffung der Welt
/// GEN 1:1	[Im Anfang|H7225] [schuf|H1254] Gott&lt;F0&gt; …
/// #NOTE	GEN 1:1	0	Text der Fußnote
/// </code>
/// In a verse: <c>[Wort|H1 G2]</c> a word with its Strong's numbers, <c>&lt;J&gt;…&lt;/J&gt;</c> words of Jesus, <c>&lt;F3&gt;</c> where footnote 3 of the
/// chapter stands, <c>|</c> the border between two pieces of plain text, <c>&lt;E&gt;</c> an empty piece, <c>\</c> takes the next sign as it is
/// (<c>\n</c>, <c>\t</c>, <c>\r</c> a line break, tab, return; <c>\s</c> a space at the end of the line). Lines with an unknown "#" are comments.
/// Without the first line the file is read as plain text with one verse per line, and the lines still give book, chapter, verse and text.
/// </summary>
public static class BibleTextFile
{
    public const string Magic = "#BIBLESTUDY-TXT";
    public const int Version = 1;

    /// <summary>True when the text starts with the first line of this format (empty lines and a byte order mark before it are allowed).</summary>
    public static bool IsOwnFormat(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.TrimStart('﻿').Trim();
            if (line.Length == 0) continue;
            return line.StartsWith(Magic, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    // ---- writing ------------------------------------------------------------------------------------------------------------

    /// <summary>The whole translation as text in this format (lines end with \n).</summary>
    public static string Write(ParsedBible bible)
    {
        var sb = new StringBuilder();
        sb.Append(Magic).Append(' ').Append(Version).Append('\n');
        sb.Append("# BibleStudy: ").Append(bible.Verses.Count).Append(" verses, ").Append(bible.Footnotes.Count).Append(" footnotes, ").Append(bible.Headings?.Count ?? 0).Append(" headings\n");
        sb.Append("#NAME\t").Append(EscapeField(bible.Name)).Append('\n');
        sb.Append("#SHORT\t").Append(EscapeField(bible.ShortName)).Append('\n');
        sb.Append("#LANG\t").Append(EscapeField(bible.LanguageCode)).Append('\n');

        var headings = (bible.Headings ?? []).GroupBy(h => (h.Book, h.Chapter)).ToDictionary(g => g.Key, g => g.ToList());
        var notes = bible.Footnotes.GroupBy(n => (n.Book, n.Chapter)).ToDictionary(g => g.Key, g => g.ToList());
        // chapters in the order the verses come; chapters that have only notes or headings after them
        var chapters = bible.Verses.Select(v => (v.Book, v.Chapter)).Concat(headings.Keys).Concat(notes.Keys).Distinct().ToList();
        var verses = bible.Verses.GroupBy(v => (v.Book, v.Chapter)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var key in chapters)
        {
            var mine = headings.GetValueOrDefault(key) ?? [];
            var used = new bool[mine.Count];
            foreach (var v in verses.GetValueOrDefault(key) ?? [])
            {
                for (var i = 0; i < mine.Count; i++)
                    if (!used[i] && mine[i].Verse <= v.Verse) { used[i] = true; Heading(mine[i]); }   // also one before a verse the file does not have
                sb.Append(Ref(v.Book, v.Chapter, v.Verse)).Append('\t').Append(WriteVerse(v.Segments)).Append('\n');
            }
            for (var i = 0; i < mine.Count; i++) if (!used[i]) Heading(mine[i]);   // after the last verse
            foreach (var (book, chapter, note) in notes.GetValueOrDefault(key) ?? [])
                sb.Append("#NOTE\t").Append(Ref(book, chapter, note.Verse)).Append('\t').Append(note.NoteId).Append('\t').Append(EscapeField(note.Text ?? "")).Append('\n');
        }
        return sb.ToString();

        void Heading((string Book, int Chapter, int Verse, string Text) h) =>
            sb.Append("#HEADING\t").Append(Ref(h.Book, h.Chapter, h.Verse)).Append('\t').Append(EscapeField(h.Text)).Append('\n');
    }

    static string Ref(string book, int chapter, int verse) => $"{book} {chapter}:{verse}";

    /// <summary>One verse in the markup of this format.</summary>
    public static string WriteVerse(IReadOnlyList<Segment> segments)
    {
        var sb = new StringBuilder();
        var jesus = false;
        var lastPlain = false;   // the piece before was plain text: a following plain piece needs a border
        foreach (var s in segments)
        {
            if (s.WordsOfJesus != jesus) { sb.Append(s.WordsOfJesus ? "<J>" : "</J>"); jesus = s.WordsOfJesus; lastPlain = false; }
            if (s.NoteId is { } id)
            {
                sb.Append("<F").Append(id);
                foreach (var st in s.Strongs ?? []) sb.Append(' ').Append(Escape(st, true));
                sb.Append('>');
                lastPlain = false;
            }
            else if (s.Strongs is { } strongs)
            {
                sb.Append('[').Append(Escape(s.Text, false)).Append('|').Append(string.Join(' ', strongs.Select(x => Escape(x, true)))).Append(']');
                lastPlain = false;
            }
            else if (s.Text.Length == 0) { sb.Append("<E>"); lastPlain = false; }
            else
            {
                if (lastPlain) sb.Append('|');
                sb.Append(Escape(s.Text, false));
                lastPlain = true;
            }
        }
        if (jesus) sb.Append("</J>");
        return EndSpace(sb.ToString());
    }

    static string Escape(string text, bool strong)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\' or '[' or ']' or '|' or '<' or '>': sb.Append('\\').Append(c); break;
                case '\n': sb.Append(@"\n"); break;
                case '\t': sb.Append(@"\t"); break;
                case '\r': sb.Append(@"\r"); break;
                case ' ' when strong: sb.Append(@"\s"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Text of a #-line field: only the backslash, line breaks and tabs are written as \x.</summary>
    static string EscapeField(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(c switch { '\\' => @"\\", '\n' => @"\n", '\t' => @"\t", '\r' => @"\r", _ => c.ToString() });
        return EndSpace(sb.ToString());
    }

    /// <summary>A space at the end of a line would be lost in many editors: it is written as \s.</summary>
    static string EndSpace(string line) => line.EndsWith(' ') ? line[..^1] + @"\s" : line;

    // ---- reading ------------------------------------------------------------------------------------------------------------

    /// <summary>Reads a file of this format; <paramref name="languageCode"/> counts only when the file names no language.</summary>
    public static ParsedBible Read(IEnumerable<string> lines, string fallbackName, string languageCode)
    {
        string? name = null, shortName = null, language = null;
        var verses = new List<ParsedVerse>();
        var footnotes = new List<(string Book, int Chapter, Footnote Note)>();
        var headings = new List<(string Book, int Chapter, int Verse, string Text)>();
        var number = 0;
        foreach (var raw in lines)
        {
            number++;
            var line = raw.TrimStart('﻿').TrimEnd('\r');
            if (line.Trim().Length == 0) continue;
            if (line.StartsWith('#'))
            {
                var tab = line.IndexOf('\t');
                var keyword = (tab < 0 ? line : line[..tab]).Trim().ToUpperInvariant();
                var rest = tab < 0 ? "" : line[(tab + 1)..];
                switch (keyword)
                {
                    case "#NAME": name = UnescapeField(rest); break;
                    case "#SHORT": shortName = UnescapeField(rest); break;
                    case "#LANG": language = UnescapeField(rest).Trim(); break;
                    case "#HEADING":
                    {
                        var f = rest.Split('\t', 2);
                        if (f.Length == 2 && ParseRef(f[0]) is { } r) headings.Add((r.Book, r.Chapter, r.Verse, UnescapeField(f[1])));
                        else throw Bad(number);
                        break;
                    }
                    case "#NOTE":
                    {
                        var f = rest.Split('\t', 3);
                        if (f.Length == 3 && ParseRef(f[0]) is { } r && int.TryParse(f[1].Trim(), out var id)) footnotes.Add((r.Book, r.Chapter, new Footnote(id, r.Verse, UnescapeField(f[2]))));
                        else throw Bad(number);
                        break;
                    }
                    // the first line, comments and keywords of a later version are passed over
                }
                continue;
            }
            var sep = line.IndexOf('\t');
            if (sep < 0 || ParseRef(line[..sep]) is not { } at) throw Bad(number);
            verses.Add(new ParsedVerse(at.Book, at.Chapter, at.Verse, ReadVerse(line[(sep + 1)..], number)));
        }
        if (verses.Count == 0) throw new InvalidDataException(L("Keine Verse gefunden."));
        name = string.IsNullOrWhiteSpace(name) ? fallbackName : name;
        shortName = string.IsNullOrWhiteSpace(shortName) ? (name.Length > 12 ? name[..12].Trim() : name) : shortName;
        return new ParsedBible(name, shortName, string.IsNullOrWhiteSpace(language) ? languageCode : language, verses, footnotes, headings.Count > 0 ? headings : null);
    }

    static InvalidDataException Bad(int line) => new(L("Zeile {0} der Datei lässt sich nicht lesen.", line));

    /// <summary>"GEN 1:1" (also a name the importer knows, "1. Mose 1:1"); the verse may be 0 (a psalm title in some modules).</summary>
    static (string Book, int Chapter, int Verse)? ParseRef(string text)
    {
        text = text.Trim();
        var space = text.LastIndexOf(' ');
        if (space <= 0) return null;
        var place = text[(space + 1)..].Split(':');
        if (place.Length != 2 || !int.TryParse(place[0], out var chapter) || !int.TryParse(place[1], out var verse) || chapter < 0 || verse < 0) return null;
        var book = text[..space].Trim();
        return (BookData.UsfmIds.Contains(book) ? book : BibleImporters.BookFromName(book) ?? book, chapter, verse);
    }

    /// <summary>The pieces of one verse from its markup.</summary>
    public static List<Segment> ReadVerse(string body, int line = 0)
    {
        var segments = new List<Segment>();
        var text = new StringBuilder();
        var jesus = false;
        var pending = false;   // plain text has begun (also after a border)
        var i = 0;
        void Flush() { if (pending && text.Length > 0) segments.Add(new Segment(text.ToString(), jesus)); text.Clear(); pending = false; }
        while (i < body.Length)
        {
            var c = body[i];
            if (c == '\\') { text.Append(Unescape(body, ref i)); pending = true; continue; }
            if (c == '|') { Flush(); i++; continue; }
            if (c == '<')
            {
                var close = i + 1;
                while (close < body.Length && body[close] != '>') close += body[close] == '\\' ? 2 : 1;
                if (close >= body.Length) throw Bad(line);
                var tag = body[(i + 1)..close];
                Flush();
                if (tag == "J") jesus = true;
                else if (tag == "/J") jesus = false;
                else if (tag == "E") segments.Add(new Segment("", jesus));
                else if (tag.StartsWith('F'))
                {
                    var parts = tag[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);   // the numbers write a space as \s
                    if (parts.Length == 0 || !int.TryParse(parts[0], out var id)) throw Bad(line);
                    segments.Add(new Segment("", jesus, parts.Length > 1 ? parts.Skip(1).Select(UnescapeField).ToList() : null, id));
                }
                else throw Bad(line);
                i = close + 1;
                continue;
            }
            if (c == '[')
            {
                Flush();
                i++;
                var word = new StringBuilder();
                while (i < body.Length && body[i] != '|')
                {
                    if (body[i] == '\\') word.Append(Unescape(body, ref i));
                    else if (body[i] == ']') throw Bad(line);
                    else word.Append(body[i++]);
                }
                if (i >= body.Length) throw Bad(line);
                i++;   // the '|'
                var numbers = new List<string>();
                var one = new StringBuilder();
                while (i < body.Length && body[i] != ']')
                {
                    if (body[i] == '\\') one.Append(Unescape(body, ref i));
                    else if (body[i] == ' ') { if (one.Length > 0) numbers.Add(one.ToString()); one.Clear(); i++; }
                    else one.Append(body[i++]);
                }
                if (i >= body.Length) throw Bad(line);
                if (one.Length > 0) numbers.Add(one.ToString());
                i++;   // the ']'
                segments.Add(new Segment(word.ToString(), jesus, numbers));
                continue;
            }
            text.Append(c); pending = true; i++;
        }
        Flush();
        return segments;
    }

    static string Unescape(string s, ref int i)
    {
        if (i + 1 >= s.Length) { i++; return "\\"; }
        var c = s[i + 1];
        i += 2;
        return c switch { 'n' => "\n", 't' => "\t", 'r' => "\r", 's' => " ", _ => c.ToString() };
    }

    static string UnescapeField(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length;)
        {
            if (s[i] == '\\') sb.Append(Unescape(s, ref i));
            else sb.Append(s[i++]);
        }
        return sb.ToString();
    }

    // ---- a translation from any source -----------------------------------------------------------------------------------

    /// <summary>
    /// A translation that is not imported (from the web or a plugin) read chapter by chapter, as the source gives it: only what the source
    /// itself has (Strong's numbers that the program added afterwards are not part of it).
    /// </summary>
    public static async Task<ParsedBible> FromProviderAsync(IBibleProvider provider, TranslationInfo translation, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var verses = new List<ParsedVerse>();
        var footnotes = new List<(string, int, Footnote)>();
        var headings = new List<(string, int, int, string)>();
        var books = await provider.GetBooksAsync(translation.Id, ct);
        var done = 0; var total = books.Sum(b => b.NumberOfChapters);
        foreach (var book in books)
            for (var c = 1; c <= book.NumberOfChapters; c++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(L("Kapitel {0} von {1} …", ++done, total));
                var chapter = await provider.GetChapterAsync(translation.Id, book.Id, c, ct);
                foreach (var v in chapter.Verses)
                    verses.Add(new ParsedVerse(book.Id, c, v.Number, v.Segments.Select(s => s.Estimated ? s with { Strongs = null, Estimated = false } : s).ToList()));
                foreach (var n in chapter.Footnotes ?? []) footnotes.Add((book.Id, c, n));
                foreach (var h in chapter.Headings ?? []) headings.Add((book.Id, c, h.BeforeVerse, h.Text));
            }
        return new ParsedBible(translation.Name, translation.ShortName, translation.Language, verses, footnotes, headings.Count > 0 ? headings : null);
    }
}
