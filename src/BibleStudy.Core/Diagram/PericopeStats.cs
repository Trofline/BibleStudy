using BibleStudy.Core.Providers;

namespace BibleStudy.Core.Diagram;

public sealed record KeywordRow(string Lemma, string Strong, string Gloss, string Text, int Count, IReadOnlyList<int> Verses);

public sealed record VerseRow(int Verse, int Words, int Nouns, int Verbs, int Others, int Connectors);

public sealed record SentenceRow(int Index, int FirstVerse, int LastVerse, int Words, int Clauses, int SubClauses);

/// <summary>How clauses are joined: the relation (such as "Begründung") and the words that express it.</summary>
public sealed record ConnectorRow(string Relation, int Count, IReadOnlyList<string> Words);

public sealed class PericopeResult
{
    public bool Hebrew;
    public int FromVerse, ToVerse;
    public int Words, Lemmas, Verses, Clauses, Sentences, SubClauses, Verbs;
    public List<(string Class, int Count)> Classes = [];
    /// <summary>Row and column names of the verb form grid (Greek: tense × mood; Hebrew: stem × verb form) with the counts.</summary>
    public List<(string Row, string Col, int Count)> VerbGrid = [];
    public List<KeywordRow> Keywords = [];
    public List<VerseRow> VerseRows = [];
    public List<SentenceRow> SentenceRows = [];
    public List<ConnectorRow> Connectors = [];
    public int Imperatives;
}

/// <summary>
/// The numbers behind the pericope analysis: word classes, verb forms, key words and where they occur, the rhythm of the verses, the sentences and
/// how the clauses are joined. Worked out from the grammar data of the Greek or Hebrew text (MACULA, Clear Bible, CC BY 4.0).
/// </summary>
public static class PericopeStats
{
    /// <summary>MACULA Hebrew and Greek name some classes differently; both are folded to one set.</summary>
    public static string ClassOf(string c) => c switch { "art" => "det", "cj" => "conj", "ij" => "intj", "" => "other", _ => c };

    static bool IsFunctionWord(string cls) => cls is not ("noun" or "verb" or "adj" or "num");

    /// <summary>Greek conjunctions by Strong number (and Hebrew ones by lemma) with the relation they mostly express.</summary>
    static readonly Dictionary<string, string> Relations = new()
    {
        ["G2532"] = "Addition", ["G5037"] = "Addition", ["G1161"] = "Fortführung", ["G235"] = "Gegensatz", ["G1063"] = "Begründung", ["G3767"] = "Folgerung", ["G1355"] = "Folgerung",
        ["G3754"] = "Begründung", ["G2443"] = "Zweck", ["G5620"] = "Ergebnis", ["G1487"] = "Bedingung", ["G1437"] = "Bedingung", ["G3753"] = "Zeit", ["G3752"] = "Zeit", ["G2193"] = "Zeit",
        ["G2228"] = "Alternative", ["G3361"] = "Zweck", ["G3303"] = "Gegensatz", ["G1893"] = "Begründung", ["G5613"] = "Vergleich", ["G2531"] = "Vergleich",
        ["H3588"] = "Begründung", ["H518"] = "Bedingung", ["H4616"] = "Zweck", ["H6435"] = "Zweck", ["H3282"] = "Begründung", ["H1115"] = "Zweck", ["H5704"] = "Zeit",
        ["ו"] = "Addition",
    };

    public static PericopeResult Compute(IReadOnlyList<GrammarWord> chapterWords, int fromVerse, int toVerse, IReadOnlyList<DSentence>? sentences, bool hebrew)
    {
        var words = chapterWords.Where(w => w.Verse >= fromVerse && w.Verse <= toVerse).ToList();
        var r = new PericopeResult { Hebrew = hebrew, FromVerse = fromVerse, ToVerse = toVerse, Words = words.Count };
        r.Lemmas = words.Select(w => w.Lemma).Where(l => l.Length > 0).Distinct().Count();
        r.Verses = words.Select(w => w.Verse).Distinct().Count();
        r.Clauses = words.Where(w => w.Clause > 0).Select(w => w.Clause).Distinct().Count();

        r.Classes = words.GroupBy(w => ClassOf(w.Class)).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ToList();
        var verbs = words.Where(w => ClassOf(w.Class) == "verb").ToList();
        r.Verbs = verbs.Count;
        r.Imperatives = verbs.Count(v => hebrew ? v.Tense == "imperative" : v.Mood == "imperative");

        // the verb grid
        foreach (var g in verbs.GroupBy(v => hebrew ? (v.Voice ?? "?", v.Tense ?? "?") : (v.Tense ?? "?", v.Mood ?? "?")))
            r.VerbGrid.Add((g.Key.Item1, g.Key.Item2, g.Count()));

        // key words: content words that come back, most frequent first
        r.Keywords = words.Where(w => !IsFunctionWord(ClassOf(w.Class)) && w.Lemma.Length > 0 && w.Lemma is not ("εἰμί" or "אמר"))
            .GroupBy(w => w.Lemma)
            .Select(g => new KeywordRow(g.Key, g.First().Strong, g.First().Gloss, g.First().Text, g.Count(), g.Select(w => w.Verse).Distinct().Order().ToList()))
            .Where(k => k.Count >= 2).OrderByDescending(k => k.Count).ThenBy(k => k.Lemma).Take(12).ToList();

        // the rhythm of the verses
        for (var v = fromVerse; v <= toVerse; v++)
        {
            var inVerse = words.Where(w => w.Verse == v).ToList();
            if (inVerse.Count == 0) continue;
            var nouns = inVerse.Count(w => ClassOf(w.Class) is "noun" or "pron" or "adj" or "num");
            var vb = inVerse.Count(w => ClassOf(w.Class) == "verb");
            r.VerseRows.Add(new VerseRow(v, inVerse.Count, nouns, vb, inVerse.Count - nouns - vb, inVerse.Count(w => ClassOf(w.Class) == "conj")));
        }

        // how the clauses are joined
        foreach (var g in words.Where(w => ClassOf(w.Class) == "conj")
                     .Select(w => (Relation: Relations.GetValueOrDefault(w.Strong) ?? Relations.GetValueOrDefault(w.Lemma) ?? Relations.GetValueOrDefault(w.Text.TrimStart('ו')) ?? "", w))
                     .Where(x => x.Relation.Length > 0).GroupBy(x => x.Relation))
            r.Connectors.Add(new ConnectorRow(g.Key, g.Count(), g.GroupBy(x => x.w.Lemma.Length > 0 ? x.w.Lemma : x.w.Text).OrderByDescending(x => x.Count()).Select(x => $"{x.Key} ×{x.Count()}").ToList()));
        r.Connectors = r.Connectors.OrderByDescending(c => c.Count).ToList();

        // the sentences
        if (sentences is not null)
        {
            foreach (var s in sentences.Where(s => s.LastVerse >= fromVerse && s.FirstVerse <= toVerse))
            {
                var (main, sub) = CountClauses(s.Root);
                r.SentenceRows.Add(new SentenceRow(s.Index, s.FirstVerse, s.LastVerse, s.Words.Count, main + sub, sub));
            }
            r.Sentences = r.SentenceRows.Count;
            r.SubClauses = r.SentenceRows.Sum(s => s.SubClauses);
        }
        return r;
    }

    /// <summary>Counts the clauses of a sentence: those on the main lines and those that depend on something.</summary>
    public static (int Main, int Sub) CountClauses(DUnit u)
    {
        int main = 0, sub = 0;
        void Clause(DClause c, bool depends)
        {
            if (depends) sub++; else main++;
            Phrase(c.Subject); Phrase(c.Object); Phrase(c.Complement); Mods(c.Mods);
        }
        void Mods(IEnumerable<DMod> mods)
        {
            foreach (var m in mods)
            {
                if (m.Clause is { } c) Clause(c, true);
                Phrase(m.Phrase);
            }
        }
        void Phrase(DPhrase? p) { if (p is not null) Mods(p.Mods); }
        void Walk(DUnit unit)
        {
            if (unit.Clause is { } c) Clause(c, false);
            foreach (var i in unit.Items) Walk(i);
        }
        Walk(u);
        return (main, sub);
    }
}
