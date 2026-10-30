using System.Globalization;
using System.Text;

namespace StrongsAdder;

/// <summary>
/// Estimates which Strong's numbers belong to the words of a verse by comparing it, word for word, with the same verse of a translation
/// that has them. It is only a guess: words that look alike (same stem) in the same order get the numbers of their counterpart.
/// </summary>
public static class Aligner
{
    public static string Normalize(string word, string language)
    {
        var s = word.ToLowerInvariant().Replace("’", "").Replace("'", "");
        if (language == "deu")
            s = s.Replace("ß", "ss").Replace("th", "t").Replace("y", "i");
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>1 for equal words, 0.7 for the same stem (Herr / Herrn), otherwise 0. Short words must be equal.</summary>
    public static double Similarity(string a, string b)
    {
        if (a == b) return 1;
        var min = Math.Min(a.Length, b.Length);
        if (min < 4) return 0;
        var p = 0;
        while (p < min && a[p] == b[p]) p++;
        return p >= Math.Max(4, (int)Math.Ceiling(0.75 * min)) ? 0.7 : 0;
    }

    /// <summary>
    /// Returns word position (in <paramref name="words"/>) → numbers. The first pass keeps the order of the words (best chain of
    /// matches), the second takes equal words that stand elsewhere in the verse.
    /// </summary>
    public static Dictionary<int, string[]> Align(IReadOnlyList<string> words, IReadOnlyList<(string Word, string[]? Strongs)> reference, string language, Lexicon? lexicon = null)
    {
        var result = new Dictionary<int, string[]>();
        var t = words.Select(w => Normalize(w, language)).ToArray();
        var r = reference.Select(x => Normalize(x.Word, language)).ToArray();
        var n = t.Length; var m = r.Length;
        if (n == 0 || m == 0) return result;

        // best monotone chain: score[i, j] over the first i words and the first j reference words
        var score = new double[n + 1, m + 1];
        for (var i = 1; i <= n; i++)
            for (var j = 1; j <= m; j++)
            {
                var best = Math.Max(score[i - 1, j], score[i, j - 1]);
                var sim = reference[j - 1].Strongs is null ? 0 : Similarity(t[i - 1], r[j - 1]);
                if (sim > 0) best = Math.Max(best, score[i - 1, j - 1] + sim);
                score[i, j] = best;
            }
        var usedR = new bool[m];
        for (int i = n, j = m; i > 0 && j > 0;)
        {
            var sim = reference[j - 1].Strongs is null ? 0 : Similarity(t[i - 1], r[j - 1]);
            if (sim > 0 && Math.Abs(score[i, j] - (score[i - 1, j - 1] + sim)) < 1e-9) { result[i - 1] = reference[j - 1].Strongs!; usedR[j - 1] = true; i--; j--; }
            else if (Math.Abs(score[i, j] - score[i - 1, j]) < 1e-9) i--;
            else j--;
        }

        // equal words that were left out because of a different word order
        for (var i = 0; i < n; i++)
        {
            if (result.ContainsKey(i)) continue;
            for (var j = 0; j < m; j++)
                if (!usedR[j] && reference[j].Strongs is not null && t[i] == r[j]) { result[i] = reference[j].Strongs!; usedR[j] = true; break; }
        }
        if (lexicon is not null) Refine(result, t, reference, lexicon);
        return result;
    }

    /// <summary>
    /// Checks the order-based guesses against what the reference translation teaches about its words: a word that usually carries a certain number of
    /// this verse gets it (also when it stands elsewhere); a word that normally carries none loses a number it got by mistake.
    /// </summary>
    static void Refine(Dictionary<int, string[]> result, string[] t, IReadOnlyList<(string Word, string[]? Strongs)> reference, Lexicon lexicon)
    {
        var inVerse = reference.SelectMany(x => x.Strongs ?? []).Distinct().ToList();
        for (var i = 0; i < t.Length; i++)
        {
            string? best = null; var bestScore = 0.0;
            foreach (var s in inVerse)
            {
                var sc = lexicon.Score(t[i], s);
                if (sc > bestScore) { bestScore = sc; best = s; }
            }
            if (result.TryGetValue(i, out var guess))
            {
                // a frequent word (the, of, who …) may only carry numbers it is known to carry; the numbers of a phrase it stands in do not belong to it
                if (lexicon.Seen(t[i]) >= 20)
                {
                    var known = guess.Where(s => lexicon.Score(t[i], s) >= 0.05).ToArray();
                    if (known.Length == 0) { result.Remove(i); guess = []; }
                    else if (known.Length < guess.Length) result[i] = guess = known;
                }
                if (guess.Length == 0) { if (best is not null && bestScore >= 0.5) result[i] = [best]; continue; }
                if (best is not null && !guess.Contains(best) && bestScore >= 0.6) result[i] = [best];
                else if (best is null && lexicon.TaggedRate(t[i]) < 0.15) result.Remove(i);
            }
            else if (best is not null && bestScore >= 0.5) result[i] = [best];
        }
    }
}
