using System.Text;
using System.Text.RegularExpressions;

namespace BibleStudy.L10n;

/// <summary>A string literal found in C# source.</summary>
public sealed record Lit(int Start, int End, bool Interpolated, bool Verbatim, bool Raw, string Content);

/// <summary>Finds the texts of the program: calls of <c>L("…")</c> in C# and the fixed texts in the XAML files.</summary>
public static class Extractor
{
    // ---- C# lexing ---------------------------------------------------------------------------------------------------------

    /// <summary>All string literals of a C# file (comments and char literals are skipped).</summary>
    public static List<Lit> Lex(string s)
    {
        var result = new List<Lit>();
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') { i = s.IndexOf("*/", i + 2, StringComparison.Ordinal); if (i < 0) break; i++; continue; }
            if (c == '\'')
            {
                // char literal: 'x', '\n', '\''
                var j = i + 1;
                if (j < s.Length && s[j] == '\\') j++;
                j++;
                if (j < s.Length && s[j] == '\'') { i = j; continue; }
            }
            if (c != '"' && !((c == '$' || c == '@') && i + 1 < s.Length && (s[i + 1] == '"' || s[i + 1] == '$' || s[i + 1] == '@'))) continue;

            var start = i; var interp = false; var verb = false;
            while (i < s.Length && (s[i] == '$' || s[i] == '@')) { if (s[i] == '$') interp = true; else verb = true; i++; }
            if (i >= s.Length || s[i] != '"') continue;
            if (i + 2 < s.Length && s[i + 1] == '"' && s[i + 2] == '"')
            {
                var end = s.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                if (end < 0) break;
                end += 3; while (end < s.Length && s[end] == '"') end++;
                result.Add(new Lit(start, end, interp, false, true, s[(i + 3)..(end - 3)]));
                i = end - 1; continue;
            }
            var contentStart = i + 1; i++;
            var depth = 0;
            while (i < s.Length)
            {
                var ch = s[i];
                if (depth == 0)
                {
                    if (ch == '"')
                    {
                        if (verb && i + 1 < s.Length && s[i + 1] == '"') { i += 2; continue; }
                        break;
                    }
                    if (!verb && ch == '\\') { i += 2; continue; }
                    if (interp && ch == '{') { if (i + 1 < s.Length && s[i + 1] == '{') { i += 2; continue; } depth = 1; i++; continue; }
                    i++;
                }
                else
                {
                    if (ch == '"') { i = SkipString(s, i); continue; }
                    if (ch == '{') depth++;
                    else if (ch == '}') depth--;
                    i++;
                }
            }
            result.Add(new Lit(start, i + 1, interp, verb, false, s[contentStart..Math.Min(i, s.Length)]));
        }
        return result;
    }

    static int SkipString(string s, int i)
    {
        i++;
        while (i < s.Length) { if (s[i] == '\\') { i += 2; continue; } if (s[i] == '"') return i + 1; i++; }
        return i;
    }

    public static string Unescape(string raw)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\\' || i + 1 >= raw.Length) { sb.Append(raw[i]); continue; }
            var n = raw[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '\'': sb.Append('\''); break;
                case '0': sb.Append('\0'); break;
                case 'u': sb.Append((char)Convert.ToInt32(raw.Substring(i + 1, 4), 16)); i += 4; break;
                default: sb.Append('\\').Append(n); break;
            }
        }
        return sb.ToString();
    }

    public static bool HasLetters(string s) => s.Any(ch => char.IsLetter(ch));

    // ---- interpolated string -> L("…{0}…", args) -------------------------------------------------------------------------------

    /// <summary>Splits the content of an interpolated string into the format text and the hole expressions; null when too complex.</summary>
    public static (string Format, List<string> Args)? SplitInterpolated(string content)
    {
        var fmt = new StringBuilder(); var args = new List<string>();
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '{' && i + 1 < content.Length && content[i + 1] == '{') { fmt.Append("{{"); i++; continue; }
            if (c == '}' && i + 1 < content.Length && content[i + 1] == '}') { fmt.Append("}}"); i++; continue; }
            if (c == '\\' && i + 1 < content.Length) { fmt.Append(c).Append(content[i + 1]); i++; continue; }
            if (c != '{') { fmt.Append(c); continue; }
            // a hole: find its end
            var depth = 1; var j = i + 1;
            while (j < content.Length && depth > 0)
            {
                if (content[j] == '"') { j = SkipString(content, j); continue; }
                if (content[j] == '{') depth++; else if (content[j] == '}') depth--;
                j++;
            }
            if (depth != 0) return null;
            var hole = content[(i + 1)..(j - 1)];
            // top-level ':' (format) and '?' (ternary), '(' '[' '{' nesting, strings
            int d = 0, colon = -1, comma = -1; var ternary = false;
            for (var k = 0; k < hole.Length; k++)
            {
                var h = hole[k];
                if (h == '"') { k = SkipString(hole, k) - 1; continue; }
                if (h is '(' or '[' or '{') d++; else if (h is ')' or ']' or '}') d--;
                else if (d == 0 && h == '?' && !(k + 1 < hole.Length && (hole[k + 1] == '.' || hole[k + 1] == '?' || hole[k + 1] == '['))) ternary = true;
                else if (d == 0 && h == ':' && colon < 0 && !ternary) colon = k;
                else if (d == 0 && h == ',' && comma < 0 && colon < 0) comma = k;
            }
            if (comma >= 0) return null;
            var expr = (colon >= 0 ? hole[..colon] : hole).Trim();
            var spec = colon >= 0 ? hole[colon..] : "";
            if (ternary) expr = "(" + expr + ")";
            fmt.Append('{').Append(args.Count).Append(spec).Append('}');
            args.Add(expr);
            i = j - 1;
        }
        return (fmt.ToString(), args);
    }

    // ---- keys used by the program --------------------------------------------------------------------------------------------

    static readonly Regex LCall = new(@"(?<![\w.])(?:Loc\.)?L\(\s*(?<lit>\$?@?""(?:[^""\\]|\\.)*"")", RegexOptions.Compiled);

    public static IEnumerable<(string File, string Key)> CsKeys(string srcRoot)
    {
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (Skip(file)) continue;
            var text = File.ReadAllText(file);
            foreach (var l in Lex(text))
            {
                // is this literal the first argument of L( … ) ?
                var before = text[Math.Max(0, l.Start - 12)..l.Start].TrimEnd();
                if (!((before.EndsWith("L(") || before.EndsWith("K(")) && !before.EndsWith("IL(") && !before.EndsWith("XL("))) continue;
                if (l.Interpolated || l.Raw) continue;
                var key = l.Verbatim ? l.Content.Replace("\"\"", "\"") : Unescape(l.Content);
                if (HasLetters(key)) yield return (file, key);
            }
        }
    }

    static bool Skip(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        || file.EndsWith("Loc.cs") || file.EndsWith("LocXaml.cs");

    static readonly Regex XamlAttr = new(@"\b(?<n>Text|Content|Header|Watermark|PlaceholderText|ToolTip\.Tip|Title)=""(?<v>[^""]*)""", RegexOptions.Compiled);
    static readonly Regex XamlElementText = new(@">(?<v>[^<>{}]*[A-Za-zÄÖÜäöüß][^<>{}]*)</(?:TextBlock|Button|CheckBox|ToggleButton|ComboBoxItem|TabItem|MenuItem|Run|Label)>", RegexOptions.Compiled);

    public static IEnumerable<(string File, string Key)> XamlKeys(string srcRoot)
    {
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.axaml", SearchOption.AllDirectories))
        {
            if (Skip(file)) continue;
            var text = File.ReadAllText(file);
            foreach (Match m in XamlAttr.Matches(text))
            {
                var v = System.Net.WebUtility.HtmlDecode(m.Groups["v"].Value);
                if (v.StartsWith('{') || !HasLetters(v)) continue;
                // "Text" is only a fixed UI text on TextBlock and Run (a TextBox's Text is what the user types)
                if (m.Groups["n"].Value == "Text")
                {
                    var tagStart = text.LastIndexOf('<', m.Index);
                    var tag = tagStart >= 0 ? Regex.Match(text[tagStart..], @"^<([\w:.]+)").Groups[1].Value : "";
                    if (tag is not ("TextBlock" or "Run" or "Label")) continue;
                }
                yield return (file, v);
            }
            foreach (Match m in XamlElementText.Matches(text))
            {
                var v = System.Net.WebUtility.HtmlDecode(m.Groups["v"].Value).Trim();
                if (HasLetters(v)) yield return (file, v);
            }
        }
    }

    public static HashSet<string> AllKeys(string srcRoot) =>
        CsKeys(srcRoot).Concat(XamlKeys(srcRoot)).Select(k => k.Key).ToHashSet();

    /// <summary>The {0}, {1} … placeholders of a text (must be the same in every language).</summary>
    public static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text.Replace("{{", "").Replace("}}", ""), @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Distinct().Order());
}
