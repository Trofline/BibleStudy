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

public static partial class SearchQuery
{
    /// <summary>Folds a character for comparison: no diacritics, ß → s. Length is preserved so positions map back to the original text.</summary>
    public static char FoldChar(char c)
    {
        if (c == 'ß') return 's';
        if (c < 128) return c;
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

    /// <summary>Parses a query. Returns null (and a message) when there is nothing to search for.</summary>
    public static SearchNode? Parse(string input, out string? error)
    {
        error = null;
        var tokens = Tokenize(input);
        var pos = 0;
        var node = ParseAnd(tokens, ref pos);
        if (node is null) { error = L("Nichts zu suchen."); return null; }
        if (!HasPositive(node)) { error = L("Es braucht mindestens ein Suchwort, das vorkommen soll."); return null; }
        return node;
    }

    static bool HasPositive(SearchNode n) => n switch
    {
        NotNode => false,
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
        var first = ParseUnary(t, ref p);
        if (first is not null) items.Add(first);
        while (p < t.Count && t[p].Kind == K.Or)
        {
            p++;
            var next = ParseUnary(t, ref p);
            if (next is not null) items.Add(next);
        }
        return items.Count switch { 0 => null, 1 => items[0], _ => new OrNode(items) };
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

        var lemma = LemmaRx().Match(text);
        if (lemma.Success) return new LemmaNode(lemma.Groups["w"].Value, lemma.Groups["m"].Value == "~");

        var orig = OriginalRx().Match(text);
        if (orig.Success) return new OriginalNode(orig.Groups["w"].Value.Trim('"'));

        var sw = StrongWordRx().Match(text);
        if (sw.Success) return new StrongWordNode(NormalizeStrong(sw.Groups["id"].Value), sw.Groups["w"].Value.Trim('"'));

        if (IsStrongId(text)) return new StrongNode(NormalizeStrong(text));
        // punctuation never stands inside a word of the text: "Herr," is Herr, "Menschen-Sohn" are two words in a row
        var parts = NotWordRx().Split(text).Where(x => x.Length > 0).ToList();
        return parts.Count switch { 0 => null, 1 => new WordNode(parts[0]), _ => parts.Any(x => x.Contains('*')) ? new AndNode(parts.Select(x => (SearchNode)new WordNode(x)).ToList()) : new PhraseNode(parts) };
    }

    static SearchNode? Phrase(string text)
    {
        var words = WordRx().Matches(text).Select(m => m.Value).ToList();
        return words.Count switch { 0 => null, 1 => new WordNode(words[0]), _ => new PhraseNode(words) };
    }

    [GeneratedRegex(@"^[GgHh]\d{1,4}[a-cA-C]?$")] private static partial Regex StrongRx();
    [GeneratedRegex(@"^[Ll](?<m>[=~])(?<w>.+)$")] private static partial Regex LemmaRx();
    [GeneratedRegex(@"^[Oo]=(?<w>.+)$")] private static partial Regex OriginalRx();
    [GeneratedRegex(@"^(?<id>[GgHh]\d{1,4}[a-cA-C]?)=(?<w>.+)$")] private static partial Regex StrongWordRx();
    [GeneratedRegex(@"[\p{L}\p{N}]+")] private static partial Regex WordRx();
    [GeneratedRegex(@"[^\p{L}\p{N}*]+")] private static partial Regex NotWordRx();
}
