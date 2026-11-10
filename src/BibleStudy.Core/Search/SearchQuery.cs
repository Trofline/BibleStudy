using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BibleStudy.Core.Search;

/// <summary>Parsed search expression (syntax after csv-bibel.de: words, "phrases", /, -, ( ), L=lemma, G3056, H8392=word).</summary>
public abstract record SearchNode;
public sealed record WordNode(string Text) : SearchNode;
public sealed record PhraseNode(IReadOnlyList<string> Words) : SearchNode;
public sealed record LemmaNode(string Pattern, bool IgnoreCase) : SearchNode;
public sealed record StrongNode(string Id) : SearchNode;
/// <summary>O=Gnade: the Hebrew/Greek word(s) behind a translated word.</summary>
public sealed record OriginalNode(string Text) : SearchNode;
public sealed record StrongWordNode(string Id, string Word) : SearchNode;
public sealed record NotNode(SearchNode Inner) : SearchNode;
public sealed record AndNode(IReadOnlyList<SearchNode> Items) : SearchNode;
public sealed record OrNode(IReadOnlyList<SearchNode> Items) : SearchNode;
/// <summary>Gnade NAH5 Wahrheit: both within 5 words of each other, in either order. Gnade DANN5 Wahrheit: Wahrheit follows Gnade within 5 words.</summary>
public sealed record NearNode(SearchNode Left, SearchNode Right, int Words, bool Ordered, NearUnit Unit = NearUnit.Words, bool Not = false) : SearchNode;
/// <summary>What the distance of a <see cref="NearNode"/> counts: words inside a verse, whole verses, or "somewhere in the same chapter".</summary>
public enum NearUnit { Words, Verses, Chapter, Sentence }
/// <summary>~Gnaden: the word with a small spelling difference (a letter more, less or different).</summary>
public sealed record FuzzyNode(string Text) : SearchNode;

public static partial class SearchQuery
{
    /// <summary>Folds a character for comparison: no diacritics, ß → s. Length is preserved so positions map back to the original text.</summary>
    public static char FoldChar(char c)
    {
        if (c == 'ß') return 's';
        if (c < 128 || char.IsSurrogate(c)) return c;   // half of a character outside the basic plane (an emoji in an imported text) has no form of its own
        var d = c.ToString().Normalize(NormalizationForm.FormD);
        foreach (var x in d) if (CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark) return x;
        return c;
    }

    public static string Fold(string s, bool lower)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) { var f = FoldChar(c); sb.Append(lower ? char.ToLowerInvariant(f) : f); }
        return sb.ToString();
    }

    /// <summary>True when the text looks like a Strong number (G3056, h1).</summary>
    public static bool IsStrongId(string s) => StrongRx().IsMatch(s);

    public static string NormalizeStrong(string s) => $"{char.ToUpperInvariant(s[0])}{int.Parse(s[1..].TrimEnd('a', 'b', 'c', 'A', 'B', 'C'))}";

    /// <summary>* stands for any number of letters, ? for exactly one.</summary>
    public static bool HasWildcard(string s) => s.Contains('*') || s.Contains('?');

    public static Regex WildcardRegex(string folded) => new("^" + string.Concat(folded.Select(ch => ch switch
    {
        '*' => @"[\p{L}\p{N}]*",
        '?' => @"[\p{L}\p{N}]",
        _ => Regex.Escape(ch.ToString()),
    })) + "$", RegexOptions.CultureInvariant);

    /// <summary>Parses a query. Returns null (and a message) when there is nothing to search for.</summary>
    public static SearchNode? Parse(string input, out string? error)
    {
        error = null;
        var tokens = Tokenize(input);
        // the Bible texts here carry no paragraph marks: better to say so than to search for the word PARAGRAPH
        if (tokens.Any(t => t.Kind == K.Term && !t.Quoted && t.Text is "PARAGRAPH" or "PARAGRAPHS")) { error = L("Absätze sind in den Bibeltexten nicht gekennzeichnet. Nimm WITHIN 1 SENTENCE oder WITHIN 3 VERSES."); return null; }
        var pos = 0;
        // a ")" without its "(" ends a group; at the top it is skipped, so the words after it still count
        var parts = new List<SearchNode>();
        while (pos < tokens.Count)
        {
            if (ParseAnd(tokens, ref pos) is { } part) parts.Add(part);
            if (pos < tokens.Count && tokens[pos].Kind == K.Close) pos++;
        }
        var flat = parts.SelectMany(p => p is AndNode a ? a.Items : [p]).ToList();
        var node = flat.Count switch { 0 => null, 1 => flat[0], _ => new AndNode(flat) };
        if (node is null) { error = L("Nichts zu suchen."); return null; }
        if (!HasPositive(node)) { error = L("Es braucht mindestens ein Suchwort, das vorkommen soll."); return null; }
        return node;
    }

    static bool HasPositive(SearchNode n) => n switch
    {
        NotNode => false,
        NearNode => true,
        AndNode a => a.Items.Any(HasPositive),
        OrNode o => o.Items.Any(HasPositive),
        _ => true,
    };

    // ---- tokens -------------------------------------------------------------------------------------------------------

    enum K { Open, Close, Or, Minus, Term }
    readonly record struct Tok(K Kind, string Text = "", bool Quoted = false);

    static List<Tok> Tokenize(string s)
    {
        var list = new List<Tok>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c) || c == ';') { i++; continue; }
            if (c == '(') { list.Add(new(K.Open)); i++; continue; }
            if (c == ')') { list.Add(new(K.Close)); i++; continue; }
            if (c == '/') { list.Add(new(K.Or)); i++; continue; }
            if ((c is '-' or '—' or '–' or '−') && (i + 1 < s.Length && !char.IsWhiteSpace(s[i + 1])) && (i == 0 || char.IsWhiteSpace(s[i - 1]) || s[i - 1] is '(' or '/'))
            { list.Add(new(K.Minus)); i++; continue; }
            if (c == '"' || c == '„' || c == '“')
            {
                var end = i + 1;
                while (end < s.Length && s[end] is not ('"' or '“' or '”')) end++;
                list.Add(new(K.Term, s[(i + 1)..Math.Min(end, s.Length)], true));
                i = Math.Min(end + 1, s.Length);
                continue;
            }
            var start = i;
            var sb = new StringBuilder();
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('(' or ')' or '/' or ';'))
            {
                if (s[i] is '"' or '„' or '“')
                {
                    // quoted part after "=" belongs to the same term: G1080="geboren werden"
                    var end = i + 1;
                    while (end < s.Length && s[end] is not ('"' or '“' or '”')) end++;
                    sb.Append('"').Append(s[(i + 1)..Math.Min(end, s.Length)]).Append('"');
                    i = Math.Min(end + 1, s.Length);
                    continue;
                }
                sb.Append(s[i]); i++;
            }
            var text = sb.ToString();
            if (text.Equals("OR", StringComparison.OrdinalIgnoreCase) || text.Equals("ODER", StringComparison.OrdinalIgnoreCase)) list.Add(new(K.Or));
            else if (text == "AND") { /* the words of a search belong together anyway */ }
            else if (text is "NOT" or "ANDNOT") list.Add(new(K.Minus));
            else if (text.Length > 0) list.Add(new(K.Term, text));
            if (i == start) i++;
        }
        return list;
    }

    // ---- grammar: and := or+ ; or := unary (/ unary)* ; unary := - primary | primary ----------------------------------------

    static SearchNode? ParseAnd(List<Tok> t, ref int p)
    {
        var items = new List<SearchNode>();
        while (p < t.Count && t[p].Kind != K.Close)
        {
            var n = ParseOr(t, ref p);
            if (n is not null) items.Add(n);
        }
        return items.Count switch { 0 => null, 1 => items[0], _ => new AndNode(items) };
    }

    static SearchNode? ParseOr(List<Tok> t, ref int p)
    {
        var items = new List<SearchNode>();
        var first = ParseNear(t, ref p);
        if (first is not null) items.Add(first);
        while (p < t.Count && t[p].Kind == K.Or)
        {
            p++;
            var next = ParseNear(t, ref p);
            if (next is not null) items.Add(next);
        }
        return items.Count switch { 0 => null, 1 => items[0], _ => new OrNode(items) };
    }

    /// <summary>unary (NAH5 | DANN5 unary)*: the number is required (a bare "dann" is an ordinary word of the text).</summary>
    static SearchNode? ParseNear(List<Tok> t, ref int p)
    {
        var left = ParseUnary(t, ref p);
        while (left is not null && p < t.Count)
        {
            // "A NOT WITHIN 3 WORDS B": a minus sign (NOT) in front of the operator turns it around
            var q = p; var not = false;
            if (t[q].Kind == K.Minus && q + 1 < t.Count && t[q + 1].Kind == K.Term && !t[q + 1].Quoted && IsOperator(t[q + 1].Text)) { not = true; q++; }
            if (t[q].Kind != K.Term || t[q].Quoted) break;
            int words, used; bool ordered, swap = false; var unit = NearUnit.Words;
            if (NearRx().Match(t[q].Text) is { Success: true } m)
            {
                used = 1; words = int.Parse(m.Groups["n"].Value); ordered = m.Groups["k"].Value.ToUpperInvariant() is "DANN" or "THEN";
            }
            else if (!LogosOperator(t, q, out words, out used, out ordered, out swap, out unit)) break;
            p = q + used;
            var right = ParseUnary(t, ref p);
            if (right is null) break;
            words = Math.Clamp(words, unit == NearUnit.Verses ? 0 : 1, 999);
            left = swap ? new NearNode(right, left, words, true, unit, not) : new NearNode(left, right, words, ordered, unit, not);
        }
        return left;
    }

    static bool IsOperator(string s) => NearRx().IsMatch(s) || s is "BEFORE" or "AFTER" or "NEAR" or "WITHIN";

    /// <summary>The operators known from Logos, in capitals: A BEFORE B, A AFTER B, A NEAR B (8 words), A WITHIN 3 WORDS B, A WITHIN 2 VERSES B, A WITHIN 1 CHAPTER B; BEFORE and AFTER take a distance too (A BEFORE 5 WORDS B).</summary>
    static bool LogosOperator(List<Tok> t, int p, out int words, out int used, out bool ordered, out bool swap, out NearUnit unit)
    {
        words = 8; used = 1; ordered = false; swap = false; unit = NearUnit.Words;
        switch (t[p].Text)
        {
            case "BEFORE": words = 999; ordered = true; break;
            case "AFTER": words = 999; ordered = true; swap = true; break;
            case "NEAR" or "WITHIN": break;
            default: return false;
        }
        var next = p + 1;
        if (next < t.Count && t[next].Kind == K.Term && int.TryParse(t[next].Text, out var n))
        {
            words = n; used++; next++;
            if (next < t.Count && t[next].Kind == K.Term)
            {
                var u = t[next].Text;
                if (u is "WORDS" or "WORD") used++;
                else if (u is "VERSES" or "VERSE") { unit = NearUnit.Verses; used++; }
                else if (u is "CHAPTERS" or "CHAPTER") { unit = NearUnit.Chapter; used++; }
                else if (u is "SENTENCES" or "SENTENCE") { unit = NearUnit.Sentence; used++; }
            }
        }
        else if (next < t.Count && t[next].Kind == K.Term && t[next].Text is "CHAPTER" or "VERSE" or "SENTENCE")
        {
            // "WITHIN CHAPTER" / "WITHIN VERSE" / "WITHIN SENTENCE" without a number
            unit = t[next].Text switch { "CHAPTER" => NearUnit.Chapter, "SENTENCE" => NearUnit.Sentence, _ => NearUnit.Verses };
            words = unit == NearUnit.Verses ? 0 : 1; used++;
        }
        return true;
    }

    static SearchNode? ParseUnary(List<Tok> t, ref int p)
    {
        if (p >= t.Count) return null;
        var tok = t[p];
        switch (tok.Kind)
        {
            case K.Minus:
                p++;
                return ParseUnary(t, ref p) is { } inner ? new NotNode(inner) : null;
            case K.Open:
                p++;
                var group = ParseAnd(t, ref p);
                if (p < t.Count && t[p].Kind == K.Close) p++;
                return group;
            case K.Term:
                p++;
                return Term(tok);
            default:
                p++; // stray ) or /
                return null;
        }
    }

    static SearchNode? Term(Tok tok)
    {
        var text = tok.Text.Trim();
        if (text.Length == 0) return null;
        if (tok.Quoted) return Phrase(text);

        if (text.Length > 1 && text[0] == '~' && WordRx().IsMatch(text[1..]) && !text.Contains('*')) return new FuzzyNode(NotWordRx().Replace(text[1..], ""));

        var lemma = LemmaRx().Match(text);
        if (lemma.Success) return new LemmaNode(lemma.Groups["w"].Value, lemma.Groups["m"].Value == "~");

        var orig = OriginalRx().Match(text);
        if (orig.Success) return new OriginalNode(orig.Groups["w"].Value.Trim('"'));

        var sw = StrongWordRx().Match(text);
        if (sw.Success) return new StrongWordNode(NormalizeStrong(sw.Groups["id"].Value), sw.Groups["w"].Value.Trim('"'));

        if (IsStrongId(text)) return new StrongNode(NormalizeStrong(text));
        // punctuation never stands inside a word of the text: "Herr," is Herr, "Menschen-Sohn" are two words in a row
        // a "?" at the very end is a question mark of the text, inside or in front of a word it stands for exactly one letter
        var parts = NotWordRx().Split(text.TrimEnd('?')).Where(x => x.Length > 0).ToList();
        return parts.Count switch { 0 => null, 1 => new WordNode(parts[0]), _ => parts.Any(HasWildcard) ? new AndNode(parts.Select(x => (SearchNode)new WordNode(x)).ToList()) : new PhraseNode(parts) };
    }

    static SearchNode? Phrase(string text)
    {
        var words = WordRx().Matches(text).Select(m => m.Value).ToList();
        return words.Count switch { 0 => null, 1 => new WordNode(words[0]), _ => new PhraseNode(words) };
    }

    [GeneratedRegex(@"^(?<k>NAH|NEAR|DANN|THEN)(?<n>\d{1,3})$")] private static partial Regex NearRx();
    [GeneratedRegex(@"^[GgHh]\d{1,4}[a-cA-C]?$")] private static partial Regex StrongRx();
    [GeneratedRegex(@"^[Ll](?<m>[=~])(?<w>.+)$")] private static partial Regex LemmaRx();
    [GeneratedRegex(@"^[Oo]=(?<w>.+)$")] private static partial Regex OriginalRx();
    [GeneratedRegex(@"^(?<id>[GgHh]\d{1,4}[a-cA-C]?)=(?<w>.+)$")] private static partial Regex StrongWordRx();
    [GeneratedRegex(@"[\p{L}\p{N}*?]+")] private static partial Regex WordRx();
    [GeneratedRegex(@"[^\p{L}\p{N}*?]+")] private static partial Regex NotWordRx();
}
