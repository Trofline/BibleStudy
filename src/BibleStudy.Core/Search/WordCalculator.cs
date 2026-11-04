using BibleStudy.Core.Models;

namespace BibleStudy.Core.Search;

/// <summary>One book (or group of books): how many hits, how many of them were subtracted, how many words it has, and how often the comparison word stands in it.</summary>
public sealed record CalcRow(string Book, int Count, int Subtracted, int Words, VerseRef? First, int Other = 0)
{
    public int Rest => Count - Subtracted;
    /// <summary>Remaining hits per 1000 words.</summary>
    public double Per1000 => Words == 0 ? 0 : Rest * 1000.0 / Words;
    /// <summary>Remaining hits for each hit of the comparison word (0 when there is none).</summary>
    public double Ratio => Other == 0 ? 0 : (double)Rest / Other;
}

/// <summary>Result of a calculation; <see cref="Chapters"/> holds the remaining hits per chapter (for the barcode).</summary>
public sealed record CalcResult(IReadOnlyList<CalcRow> Rows, string? Error, bool NeedsStrongs, IReadOnlyDictionary<(string Book, int Chapter), int>? Chapters = null);

/// <summary>
/// The word calculator: counts the hits of one search per book, takes away the hits that lie inside a hit of a second search
/// ("Christus" minus "Jesus Christus") and relates the rest to the number of words of the book. A third search can be counted next to it
/// so that two words can be put in relation, and books can be put together (by writer, group …).
/// </summary>
public static class WordCalculator
{
    /// <summary>Puts books together (writer, group, testament …): the numbers add up, the first place is the earliest one in Bible order.</summary>
    public static List<CalcRow> Group(IEnumerable<CalcRow> rows, Func<string, string> key, IReadOnlyList<string> bookOrder)
    {
        var order = new Dictionary<string, int>();
        for (var i = 0; i < bookOrder.Count; i++) order[bookOrder[i]] = i;
        return rows.GroupBy(r => key(r.Book)).Select(g =>
        {
            var firstRow = g.Where(r => r.First is not null).OrderBy(r => order.GetValueOrDefault(r.Book, int.MaxValue)).FirstOrDefault();
            return new CalcRow(g.Key, g.Sum(r => r.Count), g.Sum(r => r.Subtracted), g.Sum(r => r.Words), firstRow?.First, g.Sum(r => r.Other));
        }).ToList();
    }

    /// <summary>
    /// Counts <paramref name="include"/> per book, takes away the hits inside a hit of <paramref name="exclude"/>, and (if given) also counts
    /// <paramref name="compare"/> per book.
    /// </summary>
    public static CalcResult Run(IReadOnlyList<CorpusVerse> corpus, string include, string exclude, StrongIndex? strongs, SearchOptions options, string compare = "")
    {
        var a = SearchEngine.Run(corpus, include, strongs, options);
        if (a.Error is not null || a.NeedsStrongs) return new([], a.Error, a.NeedsStrongs);

        var cut = new Dictionary<VerseRef, List<(int Start, int Len)>>();
        if (exclude.Trim().Length > 0)
        {
            var b = SearchEngine.Run(corpus, exclude, strongs, options);
            if (b.Error is not null || b.NeedsStrongs) return new([], b.Error, b.NeedsStrongs);
            foreach (var r in b.Results) cut[r.Ref] = [.. r.Spans];
        }

        var words = new Dictionary<string, int>();
        foreach (var v in corpus) words[v.Ref.Book] = words.GetValueOrDefault(v.Ref.Book) + TextTokens.CountWords(v);

        var count = new Dictionary<string, int>();
        var taken = new Dictionary<string, int>();
        var first = new Dictionary<string, VerseRef>();
        var chapters = new Dictionary<(string Book, int Chapter), int>();
        foreach (var r in a.Results)
        {
            cut.TryGetValue(r.Ref, out var inside);
            foreach (var (s, l) in r.Spans)
            {
                count[r.Ref.Book] = count.GetValueOrDefault(r.Ref.Book) + 1;
                if (inside is not null && inside.Any(c => s < c.Start + c.Len && s + l > c.Start)) taken[r.Ref.Book] = taken.GetValueOrDefault(r.Ref.Book) + 1;
                else
                {
                    first.TryAdd(r.Ref.Book, r.Ref);
                    chapters[(r.Ref.Book, r.Ref.Chapter)] = chapters.GetValueOrDefault((r.Ref.Book, r.Ref.Chapter)) + 1;
                }
            }
        }

        var other = new Dictionary<string, int>();
        if (compare.Trim().Length > 0)
        {
            var c = SearchEngine.Run(corpus, compare, strongs, options);
            if (c.Error is not null || c.NeedsStrongs) return new([], c.Error, c.NeedsStrongs);
            foreach (var r in c.Results) other[r.Ref.Book] = other.GetValueOrDefault(r.Ref.Book) + r.Spans.Count;
        }
        foreach (var book in other.Keys.Where(b => !count.ContainsKey(b)).ToList()) count[book] = 0;   // a book with only the comparison word still gets a row

        var rows = count.Select(c => new CalcRow(c.Key, c.Value, taken.GetValueOrDefault(c.Key), words.GetValueOrDefault(c.Key), first.TryGetValue(c.Key, out var f) ? f : null, other.GetValueOrDefault(c.Key))).ToList();
        return new(rows, null, false, chapters);
    }
}
