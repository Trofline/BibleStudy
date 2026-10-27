using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Import;

/// <summary>
/// Readers for Bible files the user already owns: e-Sword modules (.bblx), Zefania XML (.xml) and an offline HTML export
/// of csv-bibel.de (.html, with Strong's numbers and footnotes). Nothing is downloaded; nothing leaves the computer.
/// </summary>
public static partial class BibleImporters
{
    public static ParsedBible Read(string path, string languageCode, IProgress<string>? progress = null)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".bblx" => ReadEsword(path, languageCode),
            ".xml" => ReadZefania(path, languageCode),
            ".html" or ".htm" => ReadCsvOfflineHtml(path, progress),
            _ => throw new NotSupportedException("Unterstützt: e-Sword (.bblx), Zefania (.xml) und die Offline-HTML von csv-bibel.de."),
        };
    }

    // ---- e-Sword ------------------------------------------------------------------------------------------------------

    public static ParsedBible ReadEsword(string path, string languageCode)
    {
        using var con = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        con.Open();
        string title = Path.GetFileNameWithoutExtension(path), abbreviation = title;
        try
        {
            using var details = con.CreateCommand();
            details.CommandText = "SELECT Title, Abbreviation FROM Details LIMIT 1";
            using var r = details.ExecuteReader();
            if (r.Read()) { title = r.IsDBNull(0) ? title : r.GetString(0); abbreviation = r.IsDBNull(1) ? abbreviation : r.GetString(1); }
        }
        catch (SqliteException) { /* module without a Details table */ }
        var verses = new List<ParsedVerse>();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Book, Chapter, Verse, Scripture FROM Bible ORDER BY Book, Chapter, Verse";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var book = reader.GetInt32(0);
            if (book < 1 || book > 66) continue;
            verses.Add(new ParsedVerse(BookData.UsfmIds[book - 1], reader.GetInt32(1), reader.GetInt32(2), [new Segment(CleanPlain(reader.GetString(3)))]));
        }
        return new ParsedBible(title, abbreviation, languageCode, verses, []);
    }

    // ---- Zefania ------------------------------------------------------------------------------------------------------

    public static ParsedBible ReadZefania(string path, string languageCode)
    {
        var verses = new List<ParsedVerse>();
        string name = Path.GetFileNameWithoutExtension(path);
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = false });
        int book = 0, chapter = 0;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            switch (reader.Name.ToUpperInvariant())
            {
                case "XMLBIBLE": name = reader.GetAttribute("biblename") ?? name; break;
                case "BIBLEBOOK": book = int.TryParse(reader.GetAttribute("bnumber"), out var b) ? b : book + 1; break;
                case "CHAPTER": chapter = int.TryParse(reader.GetAttribute("cnumber"), out var c) ? c : chapter + 1; break;
                case "VERS":
                    var number = int.TryParse(reader.GetAttribute("vnumber"), out var v) ? v : 0;
                    var text = ReadVerseText(reader);
                    if (book is >= 1 and <= 66 && number > 0 && text.Length > 0) verses.Add(new ParsedVerse(BookData.UsfmIds[book - 1], chapter, number, [new Segment(text)]));
                    break;
            }
        }
        return new ParsedBible(name, name.Length > 12 ? name[..12] : name, languageCode, verses, []);
    }

    /// <summary>Text of a VERS element without notes and cross references.</summary>
    static string ReadVerseText(XmlReader reader)
    {
        if (reader.IsEmptyElement) return "";
        var sb = new System.Text.StringBuilder();
        var depth = reader.Depth;
        reader.Read();
        while (reader.Depth > depth)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name.ToUpperInvariant() is "NOTE" or "XREF") { reader.Skip(); continue; }
            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace) sb.Append(reader.Value);
            reader.Read();
        }
        return Whitespace().Replace(sb.ToString(), " ").Trim();
    }

    // ---- csv-bibel.de offline HTML ------------------------------------------------------------------------------------

    public static ParsedBible ReadCsvOfflineHtml(string path, IProgress<string>? progress = null)
    {
        progress?.Report("Datei wird gelesen …");
        var html = File.ReadAllText(path);
        var matches = VerseButton().Matches(html);
        if (matches.Count < 1000) throw new InvalidDataException("Das sieht nicht nach der Offline-Datei der CSV-Bibel aus (zu wenige Verse gefunden).");
        var verses = new List<ParsedVerse>(matches.Count);
        var footnotes = new List<(string, int, Footnote)>();
        var noteCounter = new Dictionary<(string, int), int>();
        for (var i = 0; i < matches.Count; i++)
        {
            if (i % 2000 == 0) progress?.Report($"Verse werden gelesen … {i}/{matches.Count}");
            var m = matches[i];
            var reference = Reference().Match(WebUtility.HtmlDecode(m.Groups["ref"].Value));
            if (!reference.Success) continue;
            var abbreviation = reference.Groups["b"].Value.Trim();
            var bookIndex = Array.FindIndex(BookData.GermanAbbreviations, a => string.Equals(a, abbreviation, StringComparison.OrdinalIgnoreCase));
            if (bookIndex < 0) continue;
            var book = BookData.UsfmIds[bookIndex];
            int chapter = reference.Groups["c"].Success ? int.Parse(reference.Groups["c"].Value) : 1, verse = int.Parse(reference.Groups["v"].Value);

            var start = html.IndexOf('>', m.Index + m.Length) + 1;                       // end of the verse number button's opening tag
            var next = i + 1 < matches.Count ? matches[i + 1].Index : Math.Min(html.Length, start + 20000);
            var close = html.IndexOf("</p>", start, Math.Max(0, next - start), StringComparison.Ordinal);
            var end = close >= 0 ? close : Math.Max(start, html.LastIndexOf("<span", Math.Max(start, next - 1), Math.Max(0, next - start)));
            if (end <= start) continue;
            var key = (book, chapter);
            var segments = ParseCsvVerse(html.AsSpan(start, end - start).ToString(), text =>
            {
                var id = noteCounter.GetValueOrDefault(key);
                noteCounter[key] = id + 1;
                footnotes.Add((book, chapter, new Footnote(id, verse, text)));
                return id;
            });
            if (segments.Count > 0) verses.Add(new ParsedVerse(book, chapter, verse, segments));
        }
        return new ParsedBible("Elberfelder Übersetzung (Edition CSV, eigene Offline-Datei)", "ELBCSV", "de", verses, footnotes);
    }

    /// <summary>Turns the HTML of one verse into text segments with Strong's numbers and footnote markers.</summary>
    static List<Segment> ParseCsvVerse(string fragment, Func<string, int> addFootnote)
    {
        // The verse number button is closed first; drop everything up to and including it.
        var afterButton = fragment.IndexOf("</button>", StringComparison.Ordinal);
        if (afterButton >= 0) fragment = fragment[(afterButton + 9)..];
        var segments = new List<Segment>();
        foreach (Match t in Token().Matches(fragment))
        {
            if (t.Groups["strong"].Success)
            {
                var word = StrongSuffix().Replace(WebUtility.HtmlDecode(Tags().Replace(t.Groups["inner"].Value, "")), "").Trim();
                var id = StrongId().Match(t.Groups["pos"].Value);
                if (word.Length > 0) segments.Add(new Segment(word, false, id.Success ? [$"{id.Groups["l"].Value.ToUpperInvariant()}{int.Parse(id.Groups["n"].Value):D4}"] : null));
            }
            else if (t.Groups["note"].Success)
            {
                // The note text itself contains escaped HTML (links to Strong's numbers); keep only the words.
                var note = Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(WebUtility.HtmlDecode(t.Groups["text"].Value), "")), " ").Trim();
                segments.Add(new Segment("", false, null, addFootnote(StrongSuffixInline().Replace(note, ""))));
            }
            else if (t.Groups["plain"].Success) { var text = WebUtility.HtmlDecode(t.Groups["plain"].Value); if (text.Length > 0) segments.Add(new Segment(text)); }
        }
        return Normalize(segments);
    }

    /// <summary>Collapses repeated spaces across segment borders and trims the verse.</summary>
    static List<Segment> Normalize(List<Segment> segments)
    {
        var result = new List<Segment>();
        var lastEndedWithSpace = true;
        foreach (var s in segments)
        {
            if (s.NoteId is not null) { result.Add(s); continue; }
            var text = Whitespace().Replace(s.Text, " ");
            if (lastEndedWithSpace) text = text.TrimStart();
            if (text.Length == 0) continue;
            lastEndedWithSpace = text.EndsWith(' ');
            result.Add(s with { Text = text });
        }
        for (var i = result.Count - 1; i >= 0; i--)
        {
            if (result[i].NoteId is not null) continue;
            result[i] = result[i] with { Text = result[i].Text.TrimEnd() };
            if (result[i].Text.Length == 0) result.RemoveAt(i); else break;
        }
        return result;
    }

    static string CleanPlain(string raw)
    {
        var withoutNotes = EswordNotes().Replace(raw, "");
        return Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(withoutNotes, "")), " ").Trim();
    }

    [GeneratedRegex("""data-reference="(?<ref>[^"]+)""")] private static partial Regex VerseButton();
    [GeneratedRegex(@"^(?<b>.+?)\s+(?:(?<c>\d+),)?(?<v>\d+)$")] private static partial Regex Reference();   // one-chapter books have no chapter: "Jud 3"
    [GeneratedRegex("""
        (?<strong><a\b[^>]*data-position="(?<pos>[^"]*)"[^>]*>(?<inner>.*?)</a>)|(?<note><sup\b[^>]*data-footnote="(?<text>[^"]*)"[^>]*>.*?</sup>)|(?:<[^>]+>)|(?<plain>[^<]+)
        """, RegexOptions.Singleline)] private static partial Regex Token();
    [GeneratedRegex(@"(?<l>[GgHh])0*(?<n>\d+)")] private static partial Regex StrongId();
    [GeneratedRegex(@"\s*[~(]*[GH]\d+\S*\s*$")] private static partial Regex StrongSuffix();
    [GeneratedRegex(@"\s[~(]*[GH]\d{1,4}(:\d+)?\)?(?=\s|$|[.,;:!?])")] private static partial Regex StrongSuffixInline();
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
    [GeneratedRegex("<(RF|FI)>.*?<(Rf|Fi)>", RegexOptions.Singleline)] private static partial Regex EswordNotes();
}
