using System.Text;
using System.Text.RegularExpressions;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Search;

/// <summary>One verse prepared for searching: folded text and word positions.</summary>
public sealed partial class CorpusVerse
{
    public VerseRef Ref { get; }
    public string Text { get; }
    public string Fold { get; }   // no diacritics, case kept
    public string Lower { get; }  // no diacritics, lower case
    public (int Start, int Len)[] Words { get; }

    public CorpusVerse(VerseRef r, string text)
    {
        Ref = r; Text = text;
        Fold = SearchQuery.Fold(text, false);
        Lower = Fold.ToLowerInvariant();
        Words = Tokens().Matches(Fold).Select(m => (m.Index, m.Length)).ToArray();
    }

    public string Word(int i, bool lower) => (lower ? Lower : Fold).Substring(Words[i].Start, Words[i].Len);

    [GeneratedRegex(@"[\p{L}\p{N}]+")] private static partial Regex Tokens();
}

/// <summary>Strong's tags of a translation: Strong number → verse → the words that carry it.</summary>
public sealed class StrongIndex
{
    readonly Dictionary<string, Dictionary<VerseRef, List<string>>> _by = [];
    readonly Dictionary<string, HashSet<string>> _idsByWord = [];

    public StrongIndex(IEnumerable<StrongsOccurrence> occurrences)
    {
        foreach (var o in occurrences)
        {
            if (!SearchQuery.IsStrongId(o.Strong)) continue;
            var id = SearchQuery.NormalizeStrong(o.Strong);
            if (!_by.TryGetValue(id, out var verses)) _by[id] = verses = [];
            if (!verses.TryGetValue(o.Ref, out var words)) verses[o.Ref] = words = [];
            var folded = SearchQuery.Fold(o.Word.Trim().Trim('.', ',', ';', ':', '!', '?', '(', ')', '„', '“', '"'), true);
            words.Add(folded);
            if (folded.Length == 0) continue;
            if (!_idsByWord.TryGetValue(folded, out var ids)) _idsByWord[folded] = ids = [];
            ids.Add(id);
        }
    }

    /// <summary>The Strong numbers whose translated words (or a part of a multi-word rendering) satisfy the test.</summary>
    public HashSet<string> IdsForWords(Func<string, bool> test)
    {
        var found = new HashSet<string>();
        foreach (var (word, ids) in _idsByWord)
            if (test(word) || (word.Contains(' ') && word.Split(' ').Any(test))) found.UnionWith(ids);
        return found;
    }

    public bool Has(string id, VerseRef r) => _by.TryGetValue(id, out var v) && v.ContainsKey(r);
    public IReadOnlyList<string>? Words(string id, VerseRef r) => _by.TryGetValue(id, out var v) && v.TryGetValue(r, out var w) ? w : null;
}

public sealed record SearchResult(VerseRef Ref, string Text, IReadOnlyList<(int Start, int Len)> Spans)
{
    /// <summary>The verse text with hits wrapped in « ».</summary>
    public string Marked()
    {
        var sb = new StringBuilder();
        var pos = 0;
        foreach (var (s, l) in Spans)
        {
            sb.Append(Text, pos, s - pos).Append('«').Append(Text, s, l).Append('»');
            pos = s + l;
        }
        sb.Append(Text, pos, Text.Length - pos);
        return sb.ToString();
    }
}

public sealed record SearchOutcome(IReadOnlyList<SearchResult> Results, string? Error, bool NeedsStrongs, IReadOnlyCollection<string>? Strongs = null);

public enum WordMatch { Prefix, Whole, Suffix, Contains }

/// <summary>How plain words are matched and where the words of one search have to stand.</summary>
/// <param name="Match">Word beginning (lieb → liebe, lieben), whole word, word ending or part of a word.</param>
/// <param name="CaseSensitive">Capitals count (Herr ≠ HERR).</param>
/// <param name="Original">Plain words are looked up as the Hebrew/Greek words behind the translated word (via Strong's tags).</param>
/// <param name="Range">0 = all words in one verse; n > 0 = within n verses of each other; -1 = within one chapter.</param>
public sealed record SearchOptions(WordMatch Match = WordMatch.Prefix, bool CaseSensitive = false, bool Original = false, int Range = 0);

public static class SearchEngine
{
    /// <param name="exactWords">Plain words must match whole words; otherwise they match word beginnings (lieb → liebe, lieben).</param>
    public static SearchOutcome Run(IReadOnlyList<CorpusVerse> corpus, string input, StrongIndex? strongs, bool exactWords = false, IReadOnlyCollection<string>? books = null)
        => Run(corpus, input, strongs, new SearchOptions(exactWords ? WordMatch.Whole : WordMatch.Prefix), books);

    public static SearchOutcome Run(IReadOnlyList<CorpusVerse> corpus, string input, StrongIndex? strongs, SearchOptions options, IReadOnlyCollection<string>? books = null)
    {
        var node = SearchQuery.Parse(input, out var error);
        if (node is null) return new([], error, false);
        if (strongs is null && (UsesStrongs(node) || options.Original)) return new([], null, true);

        var ctx = new Ctx(strongs, options);
        var scope = books is null ? corpus : corpus.Where(v => books.Contains(v.Ref.Book)).ToList();
        var results = options.Range != 0 && node is AndNode { Items.Count: > 1 } and ? RunNear(scope, and, ctx) : RunSingle(scope, node, ctx);
        return new(results, null, false, ctx.Used.Count > 0 ? ctx.Used.OrderBy(x => x[0]).ThenBy(x => int.Parse(x[1..])).ToList() : null);
    }

    static List<SearchResult> RunSingle(IEnumerable<CorpusVerse> scope, SearchNode node, Ctx ctx)
    {
        var results = new List<SearchResult>();
        foreach (var v in scope)
        {
            var spans = new List<(int, int)>();
            if (!Eval(node, v, ctx, spans)) continue;
            results.Add(new(v.Ref, v.Text, Merge(spans)));
        }
        return results;
    }

    /// <summary>All parts of an AND search must occur close together (a few verses, or one chapter) instead of in the same verse.</summary>
    static List<SearchResult> RunNear(IReadOnlyList<CorpusVerse> scope, AndNode and, Ctx ctx)
    {
        var positive = and.Items.Where(i => i is not NotNode).ToList();
        var negative = and.Items.OfType<NotNode>().Select(n => n.Inner).ToList();
        var own = positive.Select(_ => new List<(int, int)>?[scope.Count]).ToArray();
        var banned = new bool[scope.Count];
        for (var i = 0; i < scope.Count; i++)
        {
            for (var k = 0; k < positive.Count; k++)
            {
                var spans = new List<(int, int)>();
                if (Eval(positive[k], scope[i], ctx, spans)) own[k][i] = spans;
            }
            banned[i] = negative.Any(n => Eval(n, scope[i], ctx, []));
        }

        bool SameChapter(int a, int b) => scope[a].Ref.Book == scope[b].Ref.Book && scope[a].Ref.Chapter == scope[b].Ref.Chapter;
        var chapterHas = new Dictionary<(string, int), bool[]>();
        var chapterBanned = new HashSet<(string, int)>();
        if (ctx.Options.Range < 0)
            for (var i = 0; i < scope.Count; i++)
            {
                var key = (scope[i].Ref.Book, scope[i].Ref.Chapter);
                if (!chapterHas.TryGetValue(key, out var has)) chapterHas[key] = has = new bool[positive.Count];
                for (var k = 0; k < positive.Count; k++) if (own[k][i] is not null) has[k] = true;
                if (banned[i]) chapterBanned.Add(key);
            }

        var results = new List<SearchResult>();
        for (var i = 0; i < scope.Count; i++)
        {
            if (positive.Select((_, k) => own[k][i]).All(x => x is null)) continue;   // the verse itself carries at least one part
            bool ok;
            if (ctx.Options.Range < 0)
            {
                var key = (scope[i].Ref.Book, scope[i].Ref.Chapter);
                ok = chapterHas[key].All(x => x) && !chapterBanned.Contains(key);
            }
            else
            {
                var have = new bool[positive.Count]; var bad = false;
                void Collect(int j) { for (var k = 0; k < positive.Count; k++) if (own[k][j] is not null) have[k] = true; if (banned[j]) bad = true; }
                for (var j = i; j >= 0 && SameChapter(i, j) && scope[i].Ref.Verse - scope[j].Ref.Verse <= ctx.Options.Range; j--) Collect(j);
                for (var j = i + 1; j < scope.Count && SameChapter(i, j) && scope[j].Ref.Verse - scope[i].Ref.Verse <= ctx.Options.Range; j++) Collect(j);
                ok = have.All(x => x) && !bad;
            }
            if (!ok) continue;
            var spans = new List<(int, int)>();
            for (var k = 0; k < positive.Count; k++) if (own[k][i] is { } s) spans.AddRange(s);
            results.Add(new(scope[i].Ref, scope[i].Text, Merge(spans)));
        }
        return results;
    }

    public static bool UsesStrongs(SearchNode n) => n switch
    {
        StrongNode or StrongWordNode or OriginalNode => true,
        NotNode x => UsesStrongs(x.Inner),
        AndNode a => a.Items.Any(UsesStrongs),
        OrNode o => o.Items.Any(UsesStrongs),
        _ => false,
    };

    sealed class Ctx(StrongIndex? strongs, SearchOptions options)
    {
        public StrongIndex? Strongs { get; } = strongs;
        public SearchOptions Options { get; } = options;
        public HashSet<string> Used { get; } = [];
        public Dictionary<string, HashSet<string>> IdsByTerm { get; } = [];
    }

    static bool Eval(SearchNode n, CorpusVerse v, Ctx c, List<(int, int)> spans)
    {
        switch (n)
        {
            case WordNode w: return c.Options.Original ? Original(w.Text, v, c, spans) : Word(w.Text, v, c, spans);
            case OriginalNode o: return Original(o.Text, v, c, spans);
            case PhraseNode p: return Phrase(p.Words, v, c.Options.CaseSensitive, spans);
            case LemmaNode l: return Lemma(l, v, spans);
            case StrongNode s:
            {
                var words = c.Strongs?.Words(s.Id, v.Ref);
                if (words is null) return false;
                foreach (var w in words) AddWord(w, v, spans);
                return true;
            }
            case StrongWordNode sw:
            {
                var words = c.Strongs?.Words(sw.Id, v.Ref);
                if (words is null) return false;
                var want = SearchQuery.Fold(sw.Word, true);
                var hit = false;
                foreach (var w in words)
                    if (w == want || w.Split(' ').Contains(want) || w.StartsWith(want + " ") || w.Contains(" " + want + " "))
                    { hit = true; AddWord(w, v, spans); }
                return hit;
            }
            case NotNode x: return !Eval(x.Inner, v, c, []);
            case AndNode a:
            {
                var tmp = new List<(int, int)>();
                foreach (var item in a.Items) if (!Eval(item, v, c, tmp)) return false;
                spans.AddRange(tmp);
                return true;
            }
            case OrNode o:
            {
                var any = false;
                foreach (var item in o.Items) if (Eval(item, v, c, spans)) any = true;
                return any;
            }
            default: return false;
        }
    }

    /// <summary>Tests one word of the text against a search word: wildcards (*) or the chosen match mode.</summary>
    static Func<string, bool> Matcher(string text, SearchOptions o)
    {
        var q = SearchQuery.Fold(text, !o.CaseSensitive);
        if (q.Contains('*'))
        {
            var rx = new Regex("^" + string.Join(@"[\p{L}\p{N}]*", q.Split('*').Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant);
            return rx.IsMatch;
        }
        return o.Match switch
        {
            WordMatch.Whole => t => t == q,
            WordMatch.Suffix => t => t.EndsWith(q, StringComparison.Ordinal),
            WordMatch.Contains => t => t.Contains(q, StringComparison.Ordinal),
            _ => t => t.StartsWith(q, StringComparison.Ordinal),
        };
    }

    static bool Word(string text, CorpusVerse v, Ctx c, List<(int, int)> spans)
    {
        var test = Matcher(text, c.Options);
        var hit = false;
        for (var i = 0; i < v.Words.Length; i++)
        {
            if (!test(v.Word(i, !c.Options.CaseSensitive))) continue;
            hit = true; spans.Add(v.Words[i]);
        }
        return hit;
    }

    /// <summary>The word as it stands in the Hebrew/Greek text: every Strong number the translation tags on a matching word, wherever it occurs.</summary>
    static bool Original(string text, CorpusVerse v, Ctx c, List<(int, int)> spans)
    {
        if (c.Strongs is null) return false;
        if (!c.IdsByTerm.TryGetValue(text, out var ids))
        {
            // the numbers are found without regard to capitals; the options only decide how the word has to match
            c.IdsByTerm[text] = ids = c.Strongs.IdsForWords(Matcher(text, c.Options with { CaseSensitive = false }));
            c.Used.UnionWith(ids);
        }
        var hit = false;
        foreach (var id in ids)
        {
            var words = c.Strongs.Words(id, v.Ref);
            if (words is null) continue;
            hit = true;
            foreach (var w in words) AddWord(w, v, spans);
        }
        return hit;
    }

    static bool Phrase(IReadOnlyList<string> words, CorpusVerse v, bool cs, List<(int, int)> spans)
    {
        var q = words.Select(w => SearchQuery.Fold(w, !cs)).ToArray();
        var hit = false;
        for (var i = 0; i + q.Length <= v.Words.Length; i++)
        {
            var ok = true;
            for (var k = 0; k < q.Length && ok; k++) ok = v.Word(i + k, !cs) == q[k];
            if (!ok) continue;
            hit = true;
            var last = v.Words[i + q.Length - 1];
            spans.Add((v.Words[i].Start, last.Start + last.Len - v.Words[i].Start));
        }
        return hit;
    }

    static void AddWord(string folded, CorpusVerse v, List<(int, int)> spans)
    {
        if (folded.Length == 0) return;
        var from = 0;
        while ((from = v.Lower.IndexOf(folded, from, StringComparison.Ordinal)) >= 0)
        {
            var end = from + folded.Length;
            var startOk = from == 0 || !char.IsLetterOrDigit(v.Lower[from - 1]);
            var endOk = end >= v.Lower.Length || !char.IsLetterOrDigit(v.Lower[end]);
            if (startOk && endOk) spans.Add((from, folded.Length));
            from = end;
        }
    }

    // ---- lemma (base form) search -------------------------------------------------------------------------------------
    // German has no free lemma data, so regular inflection is recognised by stem + ending, plus a table of common irregular verbs.

    static readonly Dictionary<string, string[]> Irregular = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gehen"] = ["ging", "gingen", "gingst", "gegangen"], ["kommen"] = ["kam", "kamen", "kamst", "kamt", "gekommen"],
        ["sein"] = ["bin", "bist", "ist", "sind", "seid", "war", "warst", "waren", "wart", "gewesen", "sei", "seien", "wäre", "wären"],
        ["haben"] = ["hat", "hast", "hatte", "hatten", "hattest", "gehabt", "habe", "habt"], ["sehen"] = ["sieht", "siehst", "sah", "sahen", "gesehen"],
        ["geben"] = ["gibt", "gibst", "gab", "gaben", "gegeben"], ["nehmen"] = ["nimmt", "nimmst", "nahm", "nahmen", "genommen"],
        ["stehen"] = ["stand", "standen", "gestanden"], ["finden"] = ["fand", "fanden", "gefunden"], ["wissen"] = ["weiß", "weißt", "wusste", "wussten", "gewusst"],
        ["lassen"] = ["lässt", "ließ", "ließen", "gelassen"], ["tun"] = ["tut", "tat", "taten", "getan", "tust"], ["werden"] = ["wird", "wirst", "wurde", "wurden", "geworden", "worden"],
        ["schreiben"] = ["schrieb", "schrieben", "geschrieben"], ["sprechen"] = ["spricht", "sprichst", "sprach", "sprachen", "gesprochen"],
        ["rufen"] = ["rief", "riefen", "gerufen"], ["bringen"] = ["brachte", "brachten", "gebracht"], ["denken"] = ["dachte", "dachten", "gedacht"],
        ["essen"] = ["isst", "aß", "aßen", "gegessen"], ["trinken"] = ["trank", "tranken", "getrunken"], ["nennen"] = ["nannte", "nannten", "genannt"],
        ["halten"] = ["hält", "hielt", "hielten", "gehalten"], ["fallen"] = ["fällt", "fiel", "fielen", "gefallen"], ["laufen"] = ["läuft", "lief", "liefen", "gelaufen"],
        ["bleiben"] = ["blieb", "blieben", "geblieben"], ["liegen"] = ["lag", "lagen", "gelegen"], ["sitzen"] = ["saß", "saßen", "gesessen"],
        ["tragen"] = ["trägt", "trug", "trugen", "getragen"], ["schlagen"] = ["schlägt", "schlug", "schlugen", "geschlagen"], ["schlafen"] = ["schläft", "schlief", "schliefen", "geschlafen"],
        ["fahren"] = ["fährt", "fuhr", "fuhren", "gefahren"], ["heißen"] = ["hieß", "hießen", "geheißen"], ["ziehen"] = ["zog", "zogen", "gezogen"],
        ["stehlen"] = ["stiehlt", "stahl", "gestohlen"], ["sterben"] = ["stirbt", "starb", "starben", "gestorben"], ["helfen"] = ["hilft", "half", "halfen", "geholfen"],
        ["lesen"] = ["liest", "las", "lasen", "gelesen"], ["treten"] = ["tritt", "trat", "traten", "getreten"], ["gelten"] = ["gilt", "galt", "galten", "gegolten"],
        ["fliehen"] = ["floh", "flohen", "geflohen"], ["steigen"] = ["stieg", "stiegen", "gestiegen"], ["bleiben "] = [], ["senden"] = ["sandte", "sandten", "gesandt"],
        ["kennen"] = ["kannte", "kannten", "gekannt"], ["bitten"] = ["bat", "baten", "gebeten"], ["binden"] = ["band", "banden", "gebunden"],
    };

    static bool Lemma(LemmaNode l, CorpusVerse v, List<(int, int)> spans)
    {
        var pattern = SearchQuery.Fold(l.Pattern, l.IgnoreCase);
        var hit = false;
        Regex? rx = null;
        string[]? forms = null; string stem = "";
        if (pattern.Contains('*'))
            rx = new Regex("^" + string.Join(@"[\p{L}\p{N}]*", pattern.Split('*').Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant);
        else
        {
            var isVerb = pattern.EndsWith("en", StringComparison.Ordinal) && pattern.Length > 4 && char.IsLower(pattern[0]);
            stem = isVerb ? pattern[..^2] : pattern;
            if (pattern.Length > 3 && pattern.EndsWith("ern", StringComparison.Ordinal) && char.IsLower(pattern[0])) stem = pattern[..^1];
            var irregular = Irregular.FirstOrDefault(kv => SearchQuery.Fold(kv.Key, l.IgnoreCase) == pattern).Value;
            forms = irregular?.Select(f => SearchQuery.Fold(f, l.IgnoreCase)).ToArray();
        }
        for (var i = 0; i < v.Words.Length; i++)
        {
            var tok = v.Word(i, l.IgnoreCase);
            bool ok;
            if (rx is not null) ok = rx.IsMatch(tok);
            else
            {
                ok = tok == pattern || (tok.Length - stem.Length is >= 0 and <= 3 && tok.StartsWith(stem, StringComparison.Ordinal))
                    || (tok.Length > 4 && tok.StartsWith("ge", StringComparison.Ordinal) && tok.Length - stem.Length - 2 is >= 1 and <= 2 && tok.AsSpan(2).StartsWith(stem, StringComparison.Ordinal) && char.IsLower(pattern[0]));
                if (!ok && forms is not null) ok = forms.Contains(tok);
            }
            if (!ok) continue;
            hit = true; spans.Add(v.Words[i]);
        }
        return hit;
    }

    static IReadOnlyList<(int Start, int Len)> Merge(List<(int, int)> spans)
    {
        if (spans.Count == 0) return [];
        var sorted = spans.OrderBy(s => s.Item1).ThenByDescending(s => s.Item2).ToList();
        var merged = new List<(int, int)> { sorted[0] };
        foreach (var (s, l) in sorted.Skip(1))
        {
            var (ps, pl) = merged[^1];
            if (s < ps + pl) merged[^1] = (ps, Math.Max(pl, s + l - ps));
            else merged.Add((s, l));
        }
        return merged;
    }
}
