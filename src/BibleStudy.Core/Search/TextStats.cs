using BibleStudy.Core.Models;

namespace BibleStudy.Core.Search;

/// <summary>A word with how often it stands in the examined part, how often in the whole text, and how striking that is (log-likelihood, higher = more).</summary>
public sealed record WordScore(string Word, int Count, int Total, double Score);

/// <summary>A word that occurs only once in the whole text.</summary>
public sealed record RareWord(string Word, VerseRef Ref);

/// <summary>
/// Word statistics over the text of one translation: which words keep company with a search (collocates), which words are typical for a book
/// (key words, compared with the rest of the Bible by Dunning's log-likelihood), and which words occur only once.
/// </summary>
public static class TextStats
{
    public static Dictionary<string, int> Frequencies(IEnumerable<CorpusVerse> corpus)
    {
        var freq = new Dictionary<string, int>();
        foreach (var v in corpus)
            for (var i = 0; i < v.Words.Length; i++)
            {
                var w = v.Word(i, true);
                freq[w] = freq.GetValueOrDefault(w) + 1;
            }
        return freq;
    }

    /// <summary>Dunning's G²: how far a word's share in the focus part (a of c words) departs from its share in the rest (b of d words).</summary>
    public static double LogLikelihood(long a, long b, long c, long d)
    {
        if (c <= 0 || d <= 0 || a + b == 0) return 0;
        double e1 = c * (double)(a + b) / (c + d), e2 = d * (double)(a + b) / (c + d);
        double g = 0;
        if (a > 0) g += a * Math.Log(a / e1);
        if (b > 0) g += b * Math.Log(b / e2);
        return 2 * g;
    }

    static bool Worth(string w) => w.Length > 1 && !w.All(char.IsDigit);

    /// <summary>The words that stand within <paramref name="window"/> words of the hits more often than chance would have it, strongest first.</summary>
    public static List<WordScore> Collocates(IReadOnlyList<CorpusVerse> corpus, IEnumerable<SearchResult> hits, int window = 5, int top = 40, Dictionary<string, int>? freq = null)
    {
        freq ??= Frequencies(corpus);
        var byRef = new Dictionary<VerseRef, CorpusVerse>();
        foreach (var v in corpus) byRef[v.Ref] = v;
        var near = new Dictionary<string, int>();
        var own = new HashSet<string>();
        long nearTotal = 0;
        foreach (var h in hits)
        {
            if (!byRef.TryGetValue(h.Ref, out var v) || h.Spans.Count == 0) continue;
            var taken = new HashSet<int>();
            var inHit = new HashSet<int>();
            foreach (var s in h.Spans)
            {
                var (first, last) = SearchEngine.WordRange(v, s);
                if (first < 0) continue;
                for (var i = first; i <= last; i++) { inHit.Add(i); own.Add(v.Word(i, true)); }
                for (var i = Math.Max(0, first - window); i <= Math.Min(v.Words.Length - 1, last + window); i++) taken.Add(i);
            }
            foreach (var i in taken.Where(i => !inHit.Contains(i)))
            {
                var w = v.Word(i, true);
                near[w] = near.GetValueOrDefault(w) + 1;
                nearTotal++;
            }
        }
        var all = freq.Values.Sum(x => (long)x);
        return near.Where(kv => kv.Value >= 2 && Worth(kv.Key) && !own.Contains(kv.Key))
            .Select(kv =>
            {
                var total = freq.GetValueOrDefault(kv.Key, kv.Value);
                var outside = Math.Max(0, total - kv.Value);
                var over = kv.Value / (double)Math.Max(1, nearTotal) > outside / (double)Math.Max(1, all - nearTotal);
                return new WordScore(kv.Key, kv.Value, total, over ? LogLikelihood(kv.Value, outside, nearTotal, all - nearTotal) : 0);
            })
            .Where(x => x.Score > 3.84)   // p < 0.05
            .OrderByDescending(x => x.Score).Take(top).ToList();
    }

    /// <summary>The words that are typical for the given books compared with all the others, strongest first.</summary>
    public static List<WordScore> KeyWords(IReadOnlyList<CorpusVerse> corpus, IReadOnlyCollection<string> books, int top = 40, Dictionary<string, int>? freq = null)
    {
        freq ??= Frequencies(corpus);
        var inside = Frequencies(corpus.Where(v => books.Contains(v.Ref.Book)));
        var c = inside.Values.Sum(x => (long)x);
        var d = freq.Values.Sum(x => (long)x) - c;
        if (c == 0 || d == 0) return [];
        return inside.Where(kv => kv.Value >= 3 && Worth(kv.Key))
            .Select(kv =>
            {
                var total = freq.GetValueOrDefault(kv.Key, kv.Value);
                var outside = Math.Max(0, total - kv.Value);
                var over = kv.Value / (double)c > outside / (double)d;
                return new WordScore(kv.Key, kv.Value, total, over ? LogLikelihood(kv.Value, outside, c, d) : 0);
            })
            .Where(x => x.Score > 3.84)
            .OrderByDescending(x => x.Score).Take(top).ToList();
    }

    /// <summary>Words of the given books that occur nowhere else in the text (hapax legomena of this translation), in text order.</summary>
    public static List<RareWord> Rare(IReadOnlyList<CorpusVerse> corpus, IReadOnlyCollection<string> books, int max = 300, Dictionary<string, int>? freq = null)
    {
        freq ??= Frequencies(corpus);
        var found = new List<RareWord>();
        foreach (var v in corpus.Where(v => books.Contains(v.Ref.Book)))
            for (var i = 0; i < v.Words.Length && found.Count < max; i++)
            {
                var w = v.Word(i, true);
                if (Worth(w) && freq.GetValueOrDefault(w) == 1) found.Add(new RareWord(v.Word(i, false), v.Ref));
            }
        return found;
    }
}
