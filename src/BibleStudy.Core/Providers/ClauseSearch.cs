using System.Globalization;
using System.Text;

namespace BibleStudy.Core.Providers;

/// <summary>One condition of a clause search: some word in the clause has this syntactic role (and, if given, is this word).</summary>
public sealed record RoleCondition(string Role, string? Term);

public sealed record ClauseHit(string Book, int Chapter, int Verse, string Text);

/// <summary>
/// Finds clauses by the parts they consist of: "subject Jesus, object Peter" or "verb λέγω, subject any". All conditions must be met inside
/// the same clause. The term can be a Strong number, the Greek base form, the English gloss or the written form (accents and case do not matter).
/// </summary>
public static class ClauseSearch
{
    public static List<ClauseHit> Run(IEnumerable<GrammarWord> words, IReadOnlyList<RoleCondition> conditions, int max = 2000)
    {
        var hits = new List<ClauseHit>();
        if (conditions.Count == 0) return hits;
        var terms = conditions.Select(c => c.Term is { Length: > 0 } t ? Fold(t) : null).ToArray();
        foreach (var clause in words.Where(w => w.Clause > 0).GroupBy(w => (w.Book, w.Clause)))
        {
            var list = clause.ToList();
            var ok = true;
            GrammarWord? first = null;
            for (var i = 0; i < conditions.Count && ok; i++)
            {
                var match = list.FirstOrDefault(w => w.Role == conditions[i].Role && (terms[i] is null || Matches(w, conditions[i].Term!, terms[i]!)));
                if (match is null) ok = false; else first ??= match;
            }
            if (!ok) continue;
            var f = first ?? list[0];
            hits.Add(new ClauseHit(f.Book, f.Chapter, f.Verse, string.Join(" ", list.Take(16).Select(w => w.Text)) + (list.Count > 16 ? " …" : "")));
            if (hits.Count >= max) break;
        }
        return hits;
    }

    static bool Matches(GrammarWord w, string raw, string folded) =>
        w.Strong.Equals(raw.Trim(), StringComparison.OrdinalIgnoreCase) || Fold(w.Lemma) == folded || Fold(w.Gloss) == folded || Fold(w.Text) == folded;

    static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c == 'ς' ? 'σ' : c));
        return sb.ToString().Trim();
    }
}
