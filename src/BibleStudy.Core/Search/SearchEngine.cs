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

    public StrongIndex(IEnumerable<StrongsOccurrence> occurrences)
    {
        foreach (var o in occurrences)
        {
            if (!SearchQuery.IsStrongId(o.Strong)) continue;
            var id = SearchQuery.NormalizeStrong(o.Strong);
            if (!_by.TryGetValue(id, out var verses)) _by[id] = verses = [];
            if (!verses.TryGetValue(o.Ref, out var words)) verses[o.Ref] = words = [];
            words.Add(SearchQuery.Fold(o.Word.Trim().Trim('.', ',', ';', ':', '!', '?', '(', ')', '„', '“', '"'), true));
        }
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

public sealed record SearchOutcome(IReadOnlyList<SearchResult> Results, string? Error, bool NeedsStrongs);

public static class SearchEngine
{
    /// <param name="exactWords">Plain words must match whole words; otherwise they match word beginnings (lieb → liebe, lieben).</param>
    public static SearchOutcome Run(IReadOnlyList<CorpusVerse> corpus, string input, StrongIndex? strongs, bool exactWords = false, IReadOnlyCollection<string>? books = null)
    {
        var node = SearchQuery.Parse(input, out var error);
        if (node is null) return new([], error, false);
        if (strongs is null && UsesStrongs(node)) return new([], null, true);

        var ctx = new Ctx(strongs, exactWords);
        var results = new List<SearchResult>();
        foreach (var v in corpus)
        {
            if (books is not null && !books.Contains(v.Ref.Book)) continue;
            var spans = new List<(int, int)>();
            if (!Eval(node, v, ctx, spans)) continue;
            results.Add(new(v.Ref, v.Text, Merge(spans)));
        }
        return new(results, null, false);
    }

    public static bool UsesStrongs(SearchNode n) => n switch
    {
        StrongNode or StrongWordNode => true,
        NotNode x => UsesStrongs(x.Inner),
        AndNode a => a.Items.Any(UsesStrongs),
        OrNode o => o.Items.Any(UsesStrongs),
        _ => false,
    };

    sealed record Ctx(StrongIndex? Strongs, bool Exact);

    static bool Eval(SearchNode n, CorpusVerse v, Ctx c, List<(int, int)> spans)
    {
        switch (n)
        {
            case WordNode w: return Word(w.Text, v, c, spans);
            case PhraseNode p: return Phrase(p.Words, v, spans);
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

    static bool Word(string text, CorpusVerse v, Ctx c, List<(int, int)> spans)
    {
        var q = SearchQuery.Fold(text, true);
        Regex? rx = null;
        if (q.Contains('*')) rx = new Regex("^" + string.Join(@"[\p{L}\p{N}]*", q.Split('*').Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant);
        var hit = false;
        for (var i = 0; i < v.Words.Length; i++)
        {
            var tok = v.Word(i, true);
            var ok = rx is not null ? rx.IsMatch(tok) : c.Exact ? tok == q : tok.StartsWith(q, StringComparison.Ordinal);
            if (!ok) continue;
            hit = true; spans.Add(v.Words[i]);
        }
        return hit;
    }

    static bool Phrase(IReadOnlyList<string> words, CorpusVerse v, List<(int, int)> spans)
    {
        var q = words.Select(w => SearchQuery.Fold(w, true)).ToArray();
        var hit = false;
        for (var i = 0; i + q.Length <= v.Words.Length; i++)
        {
            var ok = true;
            for (var k = 0; k < q.Length && ok; k++) ok = v.Word(i + k, true) == q[k];
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
