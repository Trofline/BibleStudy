using System.Text.RegularExpressions;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Search;

/// <summary>
/// Limits a search to passages: in:"Mt 1-5", in:Joh, in:"Mt 5-7; Lk 6". Everything else of the query is searched only inside those passages.
/// (The same idea as the "Search in" range of Logos.)
/// </summary>
public static partial class ScopeFilter
{
    /// <summary>Takes the in:… parts out of the query. Null passages: there was none. Error: a part that is no passage or book.</summary>
    public static string Extract(string query, out IReadOnlyList<PassageRef>? passages, out string? error)
    {
        passages = null; error = null;
        var found = new List<PassageRef>();
        foreach (Match m in InRx().Matches(query))
        {
            var text = (m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["w"].Value).Trim();
            if (PassageParser.Parse(text, out _) is { } parsed) { found.AddRange(parsed); continue; }
            if (DocumentLinks.BookId(text) is { } book) { found.Add(new PassageRef(book, 1, null, null, 999)); continue; }
            error = L("„{0}“ ist keine Bibelstelle und kein Buch.", text);
            return query;
        }
        if (found.Count == 0) return query;
        passages = found;
        return InRx().Replace(query, " ").Trim();
    }

    public static bool Contains(IReadOnlyList<PassageRef> passages, VerseRef r)
    {
        foreach (var p in passages)
        {
            if (p.Book != r.Book) continue;
            var start = (p.Chapter, p.FromVerse ?? 0);
            var end = p.ToChapter is int tc ? (tc, p.ToVerse ?? int.MaxValue)
                : p.FromVerse is null ? (p.Chapter, int.MaxValue)
                : (p.Chapter, p.ToVerse ?? p.FromVerse.Value);
            var at = (r.Chapter, r.Verse);
            if (at.CompareTo(start) >= 0 && at.CompareTo(end) <= 0) return true;
        }
        return false;
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:in|im|bereich|range)\s*:\s*(?:[""„“](?<q>[^""„“”]+)[""“”]|(?<w>[^\s""„“]+))", RegexOptions.IgnoreCase)]
    private static partial Regex InRx();
}
