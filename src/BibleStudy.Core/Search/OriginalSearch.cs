using System.Text.RegularExpressions;
using BibleStudy.Core.Providers;

namespace BibleStudy.Core.Search;

/// <summary>
/// Searches in the Greek and Hebrew text by what a word means and what it stands for, as Logos does with its own data, here with MACULA
/// (Clear Bible, CC BY 4.0):
///   meaning   – every word in the same meaning field (Louw-Nida for Greek, SDBH core domains for Hebrew): ἀφίημι finds χαρίζομαι and ἀπολύω too
///   refers to – a pronoun or a verb whose (also unwritten) subject is that person: "he said" counts for Jesus
///   Septuagint – which Hebrew words the Greek Old Testament renders with a Greek word, and the other way round
///   agreement – two words close together that agree in case, number and gender (an adjective and its noun)
/// </summary>
public static partial class OriginalSearch
{
    /// <summary>A found word and the line shown under the verse.</summary>
    public sealed record Hit(GrammarWord Word, string Note);

    /// <summary>
    /// The test for "this word": a Strong number (G863), a Greek or Hebrew base form (accents do not matter), an English gloss word (forgive),
    /// or one of <paramref name="strongs"/> (the numbers behind a word of the translation, for words in other languages).
    /// </summary>
    public static Func<GrammarWord, bool> Matcher(string term, IReadOnlyCollection<string>? strongs = null)
    {
        term = term.Trim().Trim('"', '„', '“', '”');
        // G863=forgive: the word only where it has this meaning (its English gloss)
        if (term.IndexOf('=') is var eq and > 0 && eq < term.Length - 1)
        {
            var word = Matcher(term[..eq], strongs);
            var gloss = Fold(term[(eq + 1)..].Trim());
            return w => word(w) && GlossHas(w.Gloss, gloss);
        }
        if (SearchQuery.IsStrongId(term))
        {
            var id = SearchQuery.NormalizeStrong(term);
            return w => w.Strong == id;
        }
        var folded = Fold(term);
        var extra = strongs?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        return w => Fold(w.Lemma) == folded || GlossHas(w.Gloss, folded) || (w.Strong.Length > 0 && extra.Contains(w.Strong));
    }

    public static string Fold(string s) => SearchQuery.Fold(s, true);

    static bool GlossHas(string gloss, string folded) =>
        gloss.Length > 0 && Words().Matches(Fold(gloss)).Any(m => m.Value == folded);

    /// <summary>True when the text is written in Hebrew letters.</summary>
    public static bool IsHebrew(string s) => s.Any(c => c is >= '֐' and <= '׿');

    /// <summary>True when the text is written in Greek letters.</summary>
    public static bool IsGreek(string s) => s.Any(c => c is >= 'Ͱ' and <= 'Ͽ' or >= 'ἀ' and <= '῿');

    static IEnumerable<string> Codes(GrammarWord w) => w.Domains?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    // ---- meaning ---------------------------------------------------------------------------------------------------------

    /// <summary>The result of a meaning search: the words found, which base forms make up the field (most frequent first), and the field codes.</summary>
    /// <param name="Senses">The meanings the searched word has, each with its most frequent gloss and how often: more than one means the search took the main one.</param>
    public sealed record MeaningResult(List<Hit> Hits, List<(string Lemma, string Gloss, int Count)> Members, HashSet<string> Codes)
    {
        public List<(string Gloss, int Count, bool Used)> Senses { get; init; } = [];
    }

    /// <summary>
    /// Every word in the meaning field of the words the test finds. A word with several meanings counts with its main one, so that "leave" and
    /// "allow" do not mix with "forgive" for ἀφίημι; <see cref="MeaningResult.Senses"/> names the others, and G863=forgive chooses one.
    /// An English word as the term picks the meaning by itself: only the places translated "forgive" decide.
    /// </summary>
    public static MeaningResult Meaning(IReadOnlyList<GrammarWord> words, Func<GrammarWord, bool> seed)
    {
        var seeds = words.Where(w => Content(w) && seed(w)).ToList();
        var counts = seeds.SelectMany(Codes).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        if (counts.Count == 0) return new([], [], []);
        var top = counts.Values.Max();
        var codes = counts.Where(kv => kv.Value == top).Select(kv => kv.Key).ToHashSet();
        var hits = new List<Hit>();
        foreach (var w in words)
        {
            if (w.Domains is null || !Content(w) || !Codes(w).Any(codes.Contains)) continue;
            hits.Add(new Hit(w, $"{w.Text} ({Short(w.Gloss)})"));
        }
        var members = hits.GroupBy(h => h.Word.Lemma)
            .Select(g => (g.Key, Short(g.GroupBy(h => h.Word.Gloss).OrderByDescending(x => x.Count()).First().Key), g.Count()))
            .OrderByDescending(m => m.Item3).ToList();
        // the meanings of the word itself: grouped by the first field of each place, named by the gloss used most there
        var senses = seeds.Where(w => w.Domains is not null).GroupBy(w => Codes(w).First())
            .Select(g => (Gloss: BaseGloss(g.GroupBy(w => BaseGloss(w.Gloss)).OrderByDescending(x => x.Count()).First().Key), Count: g.Count(), Used: codes.Contains(g.Key)))
            .GroupBy(s => s.Gloss).Select(g => (g.Key, g.Sum(s => s.Count), g.Any(s => s.Used)))
            .Where(s => s.Item2 * 20 >= seeds.Count).OrderByDescending(s => s.Item2).ToList();
        return new(hits, members, codes) { Senses = senses };
    }

    static string Short(string gloss) => gloss.Replace('.', ' ').Trim();

    /// <summary>The main word of a gloss, for naming a meaning: "having.left" → left, "[the] righteous" → righteous, "I have forgiven" → forgiven.</summary>
    static string BaseGloss(string gloss)
    {
        var words = Words().Matches(Short(gloss).ToLowerInvariant()).Select(m => m.Value)
            .Where(w => w is not ("a" or "an" or "the" or "to" or "of" or "i" or "you" or "he" or "she" or "it" or "we" or "they" or "having" or "has" or "have" or "had" or "will" or "be" or "been" or "being" or "is" or "was" or "were" or "are" or "may" or "might" or "shall" or "should" or "would" or "do" or "does" or "did" or "not" or "let" or "his" or "her" or "their" or "my" or "your" or "our"))
            .ToList();
        return words.Count > 0 ? words[^1] : Short(gloss);
    }

    /// <summary>Words that carry a meaning of their own; a pronoun takes over the field of what it stands for, an article or a preposition has none worth searching.</summary>
    static bool Content(GrammarWord w) => w.Class is not ("pron" or "det" or "art" or "prep" or "conj" or "cj" or "ptcl" or "om" or "rel");

    // ---- what a word stands for --------------------------------------------------------------------------------------------

    /// <summary>
    /// The words that name the person or thing the test finds, and the words that stand for it: pronouns ("him") and verbs whose subject it is,
    /// also when the subject is not written out ("[he] said"). A chain (a pronoun that points to a pronoun) is followed to its end.
    /// </summary>
    public static List<Hit> RefersTo(IReadOnlyList<GrammarWord> words, Func<GrammarWord, bool> target)
    {
        var byId = new Dictionary<string, GrammarWord>(words.Count);
        foreach (var w in words) if (w.Id is { } id) byId.TryAdd(id, w);

        GrammarWord? End(string id)
        {
            byId.TryGetValue(id, out var w);
            for (var i = 0; i < 8 && w?.Refers is { } next; i++)
            {
                var first = next.Split(' ')[0];
                if (!byId.TryGetValue(first, out var further) || ReferenceEquals(further, w)) break;
                w = further;
            }
            return w;
        }
        GrammarWord? Matching(string? ids) =>
            ids is null ? null : ids.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(End).FirstOrDefault(e => e is not null && target(e));

        var hits = new List<Hit>();
        foreach (var w in words)
        {
            if (target(w)) { hits.Add(new Hit(w, w.Text)); continue; }
            if (Matching(w.Refers) is { } named) hits.Add(new Hit(w, $"{w.Text} → {named.Lemma}"));
            else if (w.Class == "verb" && Matching(w.Subject) is { } subject) hits.Add(new Hit(w, $"{w.Text} ({Short(w.Gloss)}) → {subject.Lemma}"));
        }
        return hits;
    }

    // ---- Septuagint --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// With a Greek word (G26): the Hebrew words the Septuagint renders with it. With a Hebrew word: where it stands, and which Greek word renders it there.
    /// <paramref name="equivalents"/> counts the other side: the Hebrew base forms for a Greek word, the Greek numbers for a Hebrew word.
    /// </summary>
    public static List<Hit> Septuagint(IReadOnlyList<GrammarWord> hebrew, string term, IReadOnlyCollection<string>? strongs, out List<(string Key, int Count)> equivalents)
    {
        term = term.Trim();
        List<Hit> hits;
        if (SearchQuery.IsStrongId(term) && char.ToUpperInvariant(term[0]) == 'G')
        {
            var id = SearchQuery.NormalizeStrong(term);
            hits = hebrew.Where(w => w.Lxx == id).Select(w => new Hit(w, $"{w.Text} ({w.Strong}, {Short(w.Gloss)})")).ToList();
            equivalents = hits.GroupBy(h => $"{h.Word.Lemma} {h.Word.Strong}").Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ToList();
            return hits;
        }
        var test = Matcher(term, strongs);
        hits = hebrew.Where(w => test(w)).Select(w => new Hit(w, w.Lxx is { } g ? $"{w.Text} → {g}" : $"{w.Text} → –")).ToList();
        equivalents = hits.Where(h => h.Word.Lxx is not null).GroupBy(h => h.Word.Lxx!).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ToList();
        return hits;
    }

    // ---- agreement --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Pairs of words in one verse, at most <paramref name="distance"/> words apart, the first matching <paramref name="a"/> and the second <paramref name="b"/>,
    /// that agree in case, number and gender wherever both have them (Hebrew: number and gender). At least one of them has to be shared.
    /// </summary>
    public static List<Hit> Agreeing(IReadOnlyList<GrammarWord> words, GrammarQuery a, GrammarQuery b, bool hebrew, int distance = 4)
    {
        var hits = new List<Hit>();
        for (var i = 0; i < words.Count; i++)
        {
            var x = words[i];
            if (!a.Matches(x)) continue;
            for (var j = Math.Max(0, i - distance); j <= Math.Min(words.Count - 1, i + distance); j++)
            {
                if (j == i) continue;
                var y = words[j];
                if (y.Book != x.Book || y.Chapter != x.Chapter || y.Verse != x.Verse || !b.Matches(y) || !Agree(x, y, hebrew)) continue;
                hits.Add(new Hit(x, i < j ? $"{x.Text} … {y.Text}" : $"{y.Text} … {x.Text}"));
                break;
            }
        }
        return hits;
    }

    /// <summary>The same case, number and gender wherever both words have one, and at least one of them in common.</summary>
    public static bool Agree(GrammarWord x, GrammarWord y, bool hebrew)
    {
        var shared = 0;
        bool Same(string? p, string? q)
        {
            if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(q)) return true;
            shared++;
            // Hebrew "common" gender goes with masculine and feminine
            return p == q || (hebrew && (p == "common" || q == "common"));
        }
        var ok = Same(x.Number, y.Number) && Same(x.Gender, y.Gender) && (hebrew || Same(x.Case, y.Case));
        return ok && shared > 0;
    }

    /// <summary>
    /// True when every word of the term starts a word of the name, compared by its stem: "baptism" finds "Jesus Baptized",
    /// "crossing red sea" finds "Crossing the Red Sea". The stem is the word without its last three letters, at least four letters long.
    /// </summary>
    public static bool NameMatches(string name, string term)
    {
        var names = Words().Matches(Fold(name)).Select(m => m.Value).ToList();
        var terms = Words().Matches(Fold(term)).Select(m => m.Value).ToList();
        if (terms.Count == 0) return false;
        return terms.All(t =>
        {
            var stem = t.Length <= 4 ? t : t[..Math.Max(4, t.Length - 3)];
            return names.Any(n => n.StartsWith(stem, StringComparison.Ordinal));
        });
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")] private static partial Regex Words();
}
