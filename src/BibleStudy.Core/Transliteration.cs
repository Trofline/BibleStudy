using System.Text;

namespace BibleStudy.Core;

/// <summary>
/// Typing Greek and Hebrew with a Latin keyboard: "logos" becomes λογος, "shalom" becomes שלם. The conversion is deliberately simple
/// (Hebrew without vowels; ε/η and ο/ω cannot be told apart from plain letters: use ē and ō or the letter keyboard for the long ones).
/// </summary>
public static class Transliteration
{
    static readonly (string Latin, string Greek)[] GreekRules =
    [
        ("th", "θ"), ("ph", "φ"), ("ch", "χ"), ("ps", "ψ"), ("ks", "ξ"), ("rh", "ρ"), ("ē", "η"), ("ê", "η"), ("ō", "ω"), ("ô", "ω"),
        ("a", "α"), ("b", "β"), ("g", "γ"), ("d", "δ"), ("e", "ε"), ("z", "ζ"), ("i", "ι"), ("k", "κ"), ("l", "λ"), ("m", "μ"), ("n", "ν"),
        ("x", "ξ"), ("o", "ο"), ("p", "π"), ("r", "ρ"), ("s", "σ"), ("t", "τ"), ("u", "υ"), ("y", "υ"), ("f", "φ"), ("c", "κ"), ("w", "ω"), ("q", "θ"), ("v", "β"),
    ];

    static readonly (string Latin, string Hebrew)[] HebrewRules =
    [
        ("sh", "ש"), ("kh", "ח"), ("ch", "ח"), ("ts", "צ"), ("tz", "צ"), ("š", "ש"), ("ś", "ש"), ("ḥ", "ח"), ("ṭ", "ט"), ("ṣ", "צ"), ("ʾ", "א"), ("ʿ", "ע"),
        ("'", "א"), ("`", "ע"), ("b", "ב"), ("v", "ב"), ("g", "ג"), ("d", "ד"), ("h", "ה"), ("w", "ו"), ("z", "ז"), ("x", "ח"), ("y", "י"), ("k", "כ"), ("l", "ל"),
        ("m", "מ"), ("n", "נ"), ("s", "ס"), ("p", "פ"), ("f", "פ"), ("q", "ק"), ("r", "ר"), ("t", "ת"), ("T", "ט"),
    ];

    public static string Greek(string latin) => Convert(latin, GreekRules, greek: true);
    public static string Hebrew(string latin) => Convert(latin, HebrewRules, greek: false);

    static string Convert(string latin, (string Latin, string Out)[] rules, bool greek)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < latin.Length)
        {
            var rest = latin.AsSpan(i);
            var hit = false;
            // Hebrew keeps capitals for the one letter pair that differs (t / T); everything else is case-insensitive
            foreach (var (l, o) in rules)
            {
                if (!rest.StartsWith(l, greek || l is "T" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
                    && !(greek && rest.StartsWith(l, StringComparison.OrdinalIgnoreCase))) continue;
                var upper = greek && char.IsUpper(latin[i]);
                sb.Append(upper ? o.ToUpperInvariant() : o);
                i += l.Length; hit = true; break;
            }
            if (hit) continue;
            var c = latin[i++];
            if (!greek && "aeiouAEIOU".Contains(c)) continue;   // Hebrew vowels are not written here
            if (greek && c is 'h' or 'H') continue;              // a lone h is only the rough breathing
            sb.Append(c);
        }
        var text = sb.ToString();
        return greek ? FinalSigma(text) : FinalForms(text);
    }

    static string FinalSigma(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] == 'σ' && (i + 1 == chars.Length || !char.IsLetter(chars[i + 1]))) chars[i] = 'ς';
        return new string(chars);
    }

    static string FinalForms(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i + 1 < chars.Length && char.IsLetter(chars[i + 1])) continue;
            chars[i] = chars[i] switch { 'כ' => 'ך', 'מ' => 'ם', 'נ' => 'ן', 'פ' => 'ף', 'צ' => 'ץ', var x => x };
        }
        return new string(chars);
    }
}
