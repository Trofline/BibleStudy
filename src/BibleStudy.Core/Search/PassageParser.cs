using System.Text.RegularExpressions;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Search;

/// <summary>A passage: a whole chapter, a verse or a verse range (also across chapters).</summary>
public sealed record PassageRef(string Book, int Chapter, int? FromVerse, int? ToVerse, int? ToChapter = null);

/// <summary>Reads "Joh 3,16", "Psalm 119", "Joh 15,11; 16,24", "Mt 14,13-21; Mk 6,30-44" – the whole input must be passages.</summary>
public static partial class PassageParser
{
    /// <summary>Null when the input is not a list of passages. <paramref name="single"/> is true for exactly one passage without a trailing semicolon.</summary>
    public static IReadOnlyList<PassageRef>? Parse(string input, out bool single)
    {
        single = false;
        var text = input.Trim();
        if (text.Length == 0) return null;
        var forceList = text.EndsWith(';');
        var result = new List<PassageRef>();
        string? book = null;
        foreach (var raw in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = Segment().Match(raw);
            if (!m.Success) return null;
            if (m.Groups["b"].Success)
            {
                book = DocumentLinks.BookId(m.Groups["b"].Value);
                if (book is null) return null;
            }
            else if (book is null) return null;

            var c = int.Parse(m.Groups["c"].Value);
            if (m.Groups["v1"].Success)
            {
                var v1 = int.Parse(m.Groups["v1"].Value);
                if (m.Groups["c2"].Success) result.Add(new(book, c, v1, int.Parse(m.Groups["v2"].Value), int.Parse(m.Groups["c2"].Value)));
                else result.Add(new(book, c, v1, m.Groups["v2"].Success ? int.Parse(m.Groups["v2"].Value) : v1));
            }
            else if (m.Groups["cend"].Success) result.Add(new(book, c, null, null, int.Parse(m.Groups["cend"].Value)));
            else if (!m.Groups["b"].Success && result.Count > 0 && result[^1].FromVerse is not null) result.Add(new(book, result[^1].Chapter, c, c)); // "Joh 3,16; 18"
            else result.Add(new(book, c, null, null));
        }
        if (result.Count == 0) return null;
        single = result.Count == 1 && !forceList;
        return result;
    }

    [GeneratedRegex(@"^(?<b>(?:[1-5]\s?\.?\s?)?\p{L}{1,20}\.?)?\s*(?<c>[0-9]{1,3})(?:\s*[,:]\s*(?<v1>[0-9]{1,3})(?:\s*[-–]\s*(?:(?<c2>[0-9]{1,3})\s*[,:]\s*)?(?<v2>[0-9]{1,3}))?|\s*[-–]\s*(?<cend>[0-9]{1,3}))?$")]
    private static partial Regex Segment();

    /// <summary>
    /// Reads passages out of a pasted text, as it comes from a web page or an e-mail: one passage per line or separated by semicolons, with bullets or numbers in front,
    /// in brackets, or followed by the text of the verse ("Joh 3,16 – Denn so sehr …"). <paramref name="unknown"/> gets the lines in which no passage was found.
    /// </summary>
    public static List<PassageRef> Extract(string text, out List<string> unknown)
    {
        unknown = [];
        var found = new List<PassageRef>();
        foreach (var line in text.Replace(' ', ' ').Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var piece = line.Trim().Trim('(', ')', '[', ']', '„', '“', '"', '*').Trim();
            if (piece.Length == 0) continue;
            var stripped = Bullet().Replace(piece, "").Trim().Trim('(', ')', '[', ']').Trim();
            IReadOnlyList<PassageRef>? parsed = Parse(piece, out _) ?? (stripped.Length > 0 ? Parse(stripped, out _) : null);
            if (parsed is null)
            {
                var list = new List<PassageRef>();
                foreach (Match m in Inline().Matches(piece))
                    if (Parse(m.Value, out _) is { Count: > 0 } one) list.AddRange(one);
                parsed = list.Count > 0 ? list : null;
            }
            if (parsed is null) { unknown.Add(piece.Length > 40 ? piece[..40] + "…" : piece); continue; }
            foreach (var p in parsed) if (!found.Contains(p)) found.Add(p);
        }
        return found;
    }

    [GeneratedRegex(@"^\s*(?:[-–—•*·▪●○]|\d{1,3}[.)])\s+")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:[1-5]\s?\.?\s?)?\p{L}{2,20}\.?\s*[0-9]{1,3}(?:\s*[,:]\s*[0-9]{1,3}(?:\s*[-–]\s*(?:[0-9]{1,3}\s*[,:]\s*)?[0-9]{1,3})?|\s*[-–]\s*[0-9]{1,3})?")]
    private static partial Regex Inline();
}
