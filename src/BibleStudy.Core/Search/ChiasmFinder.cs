using BibleStudy.Core.Models;

namespace BibleStudy.Core.Search;

/// <summary>One step of a mirror structure: the verse on the left and its partner on the right share a word.</summary>
public sealed record ChiasmLevel(string Label, VerseRef Left, VerseRef Right, string LeftWord, string RightWord, double Weight);

/// <summary>
/// A candidate for a chiasm (A B C … C' B' A'): the verses it spans, the verse or verse pair in the middle, the levels from the outside in,
/// and a score (rarer shared words count more).
/// </summary>
public sealed record Chiasm(VerseRef From, VerseRef To, VerseRef CenterFrom, VerseRef CenterTo, IReadOnlyList<ChiasmLevel> Levels, double Score)
{
    /// <summary>True when one verse stands alone in the middle; false when the innermost two verses are the turning point.</summary>
    public bool SingleCenter => CenterFrom == CenterTo;
}

/// <summary>
/// Looks for mirror structures in a passage: verse n from the start and verse n from the end share a word, one level after the other, around a
/// middle. It is a search aid and no proof: the program points to the candidates and says which words connect them; whether a chiasm was meant is
/// for the reader to judge. Only whole verses are compared (no mirror inside one verse), and a shared word counts the less the more often it
/// occurs in the whole text.
/// </summary>
public static class ChiasmFinder
{
    /// <summary>In how many verses of the text each word stands (lower case, no diacritics).</summary>
    public static Dictionary<string, int> DocumentFrequencies(IEnumerable<CorpusVerse> corpus)
    {
        var df = new Dictionary<string, int>();
        foreach (var v in corpus)
            foreach (var w in Distinct(v)) df[w] = df.GetValueOrDefault(w) + 1;
        return df;
    }

    static IEnumerable<string> Distinct(CorpusVerse v)
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < v.Words.Length; i++)
        {
            var w = v.Word(i, true);
            if (!TextTokens.IsStrongTag(w) && seen.Add(w)) yield return w;
        }
    }

    /// <param name="docFreq">Verse frequencies of the whole text (see <see cref="DocumentFrequencies"/>); without them every shared word weighs the same.</param>
    /// <param name="totalVerses">Number of verses of the whole text.</param>
    /// <param name="minLevels">At least so many levels (pairs of verses) are needed; 3 is the usual lower limit for calling something a chiasm.</param>
    public static List<Chiasm> Find(IReadOnlyList<CorpusVerse> passage, IReadOnlyDictionary<string, int>? docFreq = null, int totalVerses = 0, int minLevels = 3, int maxResults = 12)
    {
        var n = passage.Count;
        if (n < 2 * minLevels) return [];

        // content words per verse: no short words, no numbers, no words that stand in more than one verse of twenty in the whole text
        var content = new List<string>[n];
        for (var i = 0; i < n; i++)
            content[i] = Distinct(passage[i]).Where(w => w.Length >= 4 && !w.All(char.IsDigit) && !TooCommon(w, docFreq, totalVerses)).ToList();

        // words that run through the whole passage are its theme, not a mirror
        if (n >= 6)
        {
            var inPassage = new Dictionary<string, int>();
            foreach (var l in content) foreach (var w in l) inPassage[w] = inPassage.GetValueOrDefault(w) + 1;
            for (var i = 0; i < n; i++) content[i].RemoveAll(w => inPassage[w] > n * 0.4);
        }

        double Weight(string a, string b)
        {
            if (docFreq is null || totalVerses == 0) return 1;
            var df = Math.Min(docFreq.GetValueOrDefault(a, 1), docFreq.GetValueOrDefault(b, 1));
            return Math.Log((totalVerses + 1.0) / (df + 1));
        }

        var found = new List<(int From, int To, Chiasm Chiasm)>();
        for (var c2 = 1; c2 <= 2 * n - 3; c2++)
        {
            int l, r;
            var single = c2 % 2 == 0;
            if (single) { var c = c2 / 2; l = c - 1; r = c + 1; }
            else { l = (c2 - 1) / 2; r = l + 1; }
            if (l < 0 || r >= n) continue;
            var centerFrom = single ? passage[c2 / 2].Ref : passage[l].Ref;
            var centerTo = single ? passage[c2 / 2].Ref : passage[r].Ref;

            var used = new HashSet<string>();
            var pairs = new List<(int L, int R, string LW, string RW, double W)>();
            for (; l >= 0 && r < n; l--, r++)
            {
                (string LW, string RW, double W)? best = null;
                foreach (var a in content[l])
                {
                    if (used.Contains(Key(a))) continue;
                    foreach (var b in content[r])
                    {
                        if (used.Contains(Key(b)) || !Related(a, b)) continue;
                        var w = Weight(a, b);
                        if (best is null || w > best.Value.W) best = (a, b, w);
                    }
                }
                if (best is null) break;
                used.Add(Key(best.Value.LW)); used.Add(Key(best.Value.RW));
                pairs.Add((l, r, best.Value.LW, best.Value.RW, best.Value.W));
            }
            if (pairs.Count < minLevels) continue;

            var levels = new List<ChiasmLevel>();
            for (var i = pairs.Count - 1; i >= 0; i--)   // outside in
            {
                var p = pairs[i];
                levels.Add(new ChiasmLevel(Label(pairs.Count - 1 - i), passage[p.L].Ref, passage[p.R].Ref, p.LW, p.RW, p.W));
            }
            var score = pairs.Sum(p => p.W) + pairs.Count * 1.5 + (single ? 1 : 0);
            var outer = pairs[^1];
            found.Add((outer.L, outer.R, new Chiasm(passage[outer.L].Ref, passage[outer.R].Ref, centerFrom, centerTo, levels, score)));
        }

        // the best first; a structure that lies inside a better one is only a part of it
        var kept = new List<(int From, int To, Chiasm Chiasm)>();
        foreach (var f in found.OrderByDescending(f => f.Chiasm.Score))
        {
            if (kept.Any(k => f.From >= k.From && f.To <= k.To)) continue;
            kept.Add(f);
            if (kept.Count >= maxResults) break;
        }
        return kept.Select(k => k.Chiasm).ToList();
    }

    static bool TooCommon(string w, IReadOnlyDictionary<string, int>? docFreq, int totalVerses) =>
        docFreq is not null && totalVerses > 0 && docFreq.GetValueOrDefault(w) > totalVerses / 20;

    /// <summary>The same word, or two forms of it (Herr / Herrn): one begins like the other and they differ by at most three letters.</summary>
    static bool Related(string a, string b)
    {
        if (a == b) return true;
        var (shortW, longW) = a.Length <= b.Length ? (a, b) : (b, a);
        return shortW.Length >= 4 && longW.Length - shortW.Length <= 3 && longW.StartsWith(shortW, StringComparison.Ordinal);
    }

    static string Key(string w) => w.Length <= 4 ? w : w[..4];

    static string Label(int i) => i < 26 ? ((char)('A' + i)).ToString() : "L" + (i + 1);
}
