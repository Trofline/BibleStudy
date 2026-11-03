using BibleStudy.Core.Models;

namespace BibleStudy.Core.Search;

/// <summary>One book: how many hits, how many of them were subtracted, how many words the book has.</summary>
public sealed record CalcRow(string Book, int Count, int Subtracted, int Words, VerseRef? First)
{
    public int Rest => Count - Subtracted;
    /// <summary>Remaining hits per 1000 words of the book.</summary>
    public double Per1000 => Words == 0 ? 0 : Rest * 1000.0 / Words;
}

public sealed record CalcResult(IReadOnlyList<CalcRow> Rows, string? Error, bool NeedsStrongs);

/// <summary>
/// The word calculator: counts the hits of one search per book, takes away the hits that lie inside a hit of a second search
/// ("Christus" minus "Jesus Christus") and relates the rest to the number of words of the book.
/// </summary>
public static class WordCalculator
{
    public static CalcResult Run(IReadOnlyList<CorpusVerse> corpus, string include, string exclude, StrongIndex? strongs, SearchOptions options)
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
        foreach (var v in corpus) words[v.Ref.Book] = words.GetValueOrDefault(v.Ref.Book) + v.Words.Length;

        var count = new Dictionary<string, int>();
        var taken = new Dictionary<string, int>();
        var first = new Dictionary<string, VerseRef>();
        foreach (var r in a.Results)
        {
            cut.TryGetValue(r.Ref, out var inside);
            foreach (var (s, l) in r.Spans)
            {
                count[r.Ref.Book] = count.GetValueOrDefault(r.Ref.Book) + 1;
                if (inside is not null && inside.Any(c => s < c.Start + c.Len && s + l > c.Start)) taken[r.Ref.Book] = taken.GetValueOrDefault(r.Ref.Book) + 1;
                else first.TryAdd(r.Ref.Book, r.Ref);
            }
        }
        var rows = count.Select(c => new CalcRow(c.Key, c.Value, taken.GetValueOrDefault(c.Key), words.GetValueOrDefault(c.Key), first.TryGetValue(c.Key, out var f) ? f : null)).ToList();
        return new(rows, null, false);
    }
}
