namespace BibleStudy.Core.Models;

/// <summary>
/// The second ring of the cross reference star: which of the verses around the middle point to each other. Two verses are joined when
/// a cross reference of one of them lands inside the other (a reference to a run of verses counts for each verse of it).
/// </summary>
public static class StarLinks
{
    /// <summary>
    /// Pairs (A, B, Score) of positions in <paramref name="ring"/> with A &lt; B. <paramref name="refsOf"/> gives the cross references of the verse at
    /// a position (missing: none known). The score is the stronger of the two directions.
    /// </summary>
    public static List<(int A, int B, int Score)> Between(IReadOnlyList<CrossRef> ring, Func<int, IReadOnlyList<CrossRef>?> refsOf)
    {
        static bool Hits(CrossRef from, CrossRef to) =>
            from.Target.Book == to.Target.Book && from.Target.Chapter == to.Target.Chapter
            && from.Target.Verse <= Math.Max(to.Target.Verse, to.EndVerse) && Math.Max(from.Target.Verse, from.EndVerse) >= to.Target.Verse;

        var result = new List<(int, int, int)>();
        for (var a = 0; a < ring.Count; a++)
            for (var b = a + 1; b < ring.Count; b++)
            {
                var score = -1;
                foreach (var x in refsOf(a) ?? []) if (Hits(x, ring[b])) score = Math.Max(score, x.Score);
                foreach (var x in refsOf(b) ?? []) if (Hits(x, ring[a])) score = Math.Max(score, x.Score);
                if (score >= 0) result.Add((a, b, score));
            }
        return result;
    }
}
