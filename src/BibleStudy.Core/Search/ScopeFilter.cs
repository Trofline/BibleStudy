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
            // a list of passages and whole books ("Röm 1-8; Gal"): passages one after another keep their book ("Joh 3; 5"), a book alone is all of it
            var run = new List<string>();
            bool Flush()
            {
                if (run.Count == 0) return true;
                var part = PassageParser.Parse(string.Join("; ", run), out _);
                run.Clear();
                if (part is null) return false;
                found.AddRange(part);
                return true;
            }
            string? bad = null;
            foreach (var piece in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if ((PassageParser.Parse(piece, out _) is null ? DocumentLinks.BookId(piece) : null) is { } whole)
                {
                    if (!Flush()) { bad = piece; break; }
                    found.Add(new PassageRef(whole, 1, null, null, 999));
                }
                else run.Add(piece);
            }
            if (bad is null && Flush()) continue;
            error = L("„{0}“ ist keine Bibelstelle und kein Buch.", bad ?? text);
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
