using System.Globalization;
using System.Text;
using BibleStudy.Core.Search;

namespace BibleStudy.Core.Providers;

/// <summary>One condition of a clause search: some word in the clause has this syntactic role (and, if given, is this word).</summary>
public sealed record RoleCondition(string Role, string? Term);

public sealed record ClauseHit(string Book, int Chapter, int Verse, string Text);

/// <summary>
/// Finds clauses by the parts they consist of: "subject Jesus, object Peter" or "verb λέγω, subject any". All conditions must be met inside
/// the same clause. The term can be a Strong number, the Greek base form, the English gloss or the written form (accents and case do not matter).
/// A term also knows: Jesus / Peter (one of them), -Peter or NOT Peter (no such word in this role), and the placeholders * and ?.
/// </summary>
public static class ClauseSearch
{
    public static List<ClauseHit> Run(IEnumerable<GrammarWord> words, IReadOnlyList<RoleCondition> conditions, int max = 2000)
    {
        var hits = new List<ClauseHit>();
        if (conditions.Count == 0) return hits;
        var compiled = conditions.Select(c => (c.Role, Term: Compile(c.Term))).ToArray();
        foreach (var clause in words.Where(w => w.Clause > 0).GroupBy(w => (w.Book, w.Clause)))
        {
            var list = clause.ToList();
            var ok = true;
            GrammarWord? first = null;
            foreach (var (role, term) in compiled)
            {
                var match = list.FirstOrDefault(w => w.Role == role && term.Test(w));
                if (term.Negated) ok = match is null;
                else if (match is null) ok = false;
                else first ??= match;
                if (!ok) break;
            }
            if (!ok) continue;
            var f = first ?? list[0];
            hits.Add(new ClauseHit(f.Book, f.Chapter, f.Verse, string.Join(" ", list.Take(16).Select(w => w.Text)) + (list.Count > 16 ? " …" : "")));
            if (hits.Count >= max) break;
        }
        return hits;
    }

    static (bool Negated, Func<GrammarWord, bool> Test) Compile(string? term)
    {
        var text = term?.Trim() ?? "";
        if (text.Length == 0) return (false, _ => true);
        var negated = false;
        if (text.StartsWith("NOT ", StringComparison.Ordinal)) { negated = true; text = text[4..].Trim(); }
        else if (text.Length > 1 && text[0] == '-') { negated = true; text = text[1..].Trim(); }
        var options = text.Replace(" OR ", "/", StringComparison.Ordinal).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Option).ToArray();
        return (negated, w => options.Any(o => o(w)));
    }

    static Func<GrammarWord, bool> Option(string raw)
    {
        var folded = Fold(raw);
        Func<string, bool> same = SearchQuery.HasWildcard(folded) ? SearchQuery.WildcardRegex(folded).IsMatch : t => t == folded;
        return w => w.Strong.Equals(raw, StringComparison.OrdinalIgnoreCase) || same(Fold(w.Lemma)) || same(Fold(w.Gloss)) || same(Fold(w.Text));
    }

    static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c == 'ς' ? 'σ' : c));
        return sb.ToString().Trim();
    }
}
