using System.Text.Json;

namespace BibleStudy.Core;

public sealed record Letter(string Glyph, string Name, string Translit, string Sound);

/// <summary>Alphabets and a starter vocabulary (Strong numbers of frequent and important words) for learning Greek and Hebrew.</summary>
public static class Learning
{
    public static readonly Letter[] Greek =
    [
        new("Α α", "Alpha", "a", "[a]"), new("Β β", "Beta", "b", "[b]"), new("Γ γ", "Gamma", "g", "[g]"), new("Δ δ", "Delta", "d", "[d]"),
        new("Ε ε", "Epsilon", "e", "[e]"), new("Ζ ζ", "Zeta", "z", "[zd]"), new("Η η", "Eta", "ē", "[ɛː]"), new("Θ θ", "Theta", "th", "[tʰ]"),
        new("Ι ι", "Iota", "i", "[i]"), new("Κ κ", "Kappa", "k", "[k]"), new("Λ λ", "Lambda", "l", "[l]"), new("Μ μ", "Mu", "m", "[m]"),
        new("Ν ν", "Nu", "n", "[n]"), new("Ξ ξ", "Xi", "x", "[ks]"), new("Ο ο", "Omicron", "o", "[o]"), new("Π π", "Pi", "p", "[p]"),
        new("Ρ ρ", "Rho", "r", "[r]"), new("Σ σ ς", "Sigma", "s", "[s]"), new("Τ τ", "Tau", "t", "[t]"), new("Υ υ", "Upsilon", "y", "[y]"),
        new("Φ φ", "Phi", "ph", "[pʰ]"), new("Χ χ", "Chi", "ch", "[kʰ]"), new("Ψ ψ", "Psi", "ps", "[ps]"), new("Ω ω", "Omega", "ō", "[ɔː]"),
    ];

    public static readonly Letter[] Hebrew =
    [
        new("א", "Aleph", "ʾ", "[ʔ]"), new("ב", "Bet", "b / v", "[b] / [v]"), new("ג", "Gimel", "g", "[g]"), new("ד", "Dalet", "d", "[d]"),
        new("ה", "He", "h", "[h]"), new("ו", "Vav", "w / v", "[w] / [v]"), new("ז", "Zayin", "z", "[z]"), new("ח", "Chet", "ḥ", "[ħ]"),
        new("ט", "Tet", "ṭ", "[tˤ]"), new("י", "Yod", "y", "[j]"), new("כ ך", "Kaf", "k / kh", "[k] / [x]"), new("ל", "Lamed", "l", "[l]"),
        new("מ ם", "Mem", "m", "[m]"), new("נ ן", "Nun", "n", "[n]"), new("ס", "Samekh", "s", "[s]"), new("ע", "Ayin", "ʿ", "[ʕ]"),
        new("פ ף", "Pe", "p / f", "[p] / [f]"), new("צ ץ", "Tsade", "ṣ", "[ts]"), new("ק", "Qof", "q", "[q]"), new("ר", "Resh", "r", "[r]"),
        new("שׁ שׂ", "Shin / Sin", "š / ś", "[ʃ] / [s]"), new("ת", "Tav", "t", "[t]"),
    ];

    /// <summary>Strong numbers in the order they are taught: the most frequent and most important words first.</summary>
    public static readonly string[] GreekVocabulary =
    [
        "G3588", "G2532", "G1161", "G1722", "G846", "G1519", "G3756", "G1473", "G4771", "G2316", "G2424", "G5547", "G2962", "G3004", "G1510",
        "G3778", "G3956", "G235", "G1063", "G3361", "G1537", "G575", "G3754", "G2443", "G5100", "G3962", "G5207", "G444", "G3056", "G4151",
        "G932", "G26", "G25", "G4102", "G4100", "G1135", "G2250", "G2889", "G2222", "G5485", "G1515", "G266", "G2588", "G3101", "G3772",
        "G1093", "G1325", "G2192", "G2064", "G4160", "G1096", "G3708", "G191", "G1492", "G1097", "G3767", "G4012", "G1909", "G3326",
        "G3004", "G1223", "G5124", "G3739", "G2036", "G2227", "G1722", "G1565", "G3880", "G2147", "G2983", "G863", "G2564", "G3306",
        "G5547", "G40", "G2098", "G225", "G3551", "G4561", "G5207", "G1411", "G1391", "G2041", "G3361", "G846", "G4102", "G907",
        "G1577", "G4396", "G652", "G1242", "G1680", "G4982", "G4991", "G1343", "G3340", "G2222", "G32", "G1785", "G3809", "G1410",
    ];

    public static readonly string[] HebrewVocabulary =
    [
        "H853", "H3068", "H430", "H559", "H1961", "H1697", "H1121", "H776", "H3117", "H376", "H5971", "H4428", "H1004", "H3027", "H834",
        "H3605", "H935", "H6213", "H5414", "H7200", "H8085", "H3045", "H1870", "H3478", "H8034", "H8064", "H5921", "H6440", "H5315", "H3820",
        "H7307", "H120", "H1", "H802", "H3205", "H3318", "H3372", "H157", "H2617", "H571", "H6664", "H4941", "H8451", "H1285", "H5030",
        "H3548", "H5650", "H4325", "H7121", "H7225", "H8104", "H5769", "H2896", "H1419", "H6918", "H6944", "H7965", "H3444", "H3519", "H2403",
        "H5771", "H2416", "H4191", "H2388", "H1288", "H539", "H3722", "H6635", "H7706", "H136", "H4196", "H2142", "H5647", "H3427", "H3212",
        "H6680", "H7392", "H5927", "H3381", "H6965", "H7725", "H3947", "H4639", "H1818", "H5892", "H2022", "H7235", "H3966", "H2617",
    ];

    public static IReadOnlyList<string> Vocabulary(bool greek) => (greek ? GreekVocabulary : HebrewVocabulary).Distinct().ToList();
}

/// <summary>Progress of a card in the vocabulary trainer (Leitner boxes: the better you know a word, the later it comes back).</summary>
public sealed class CardState
{
    public int Box { get; set; }
    public DateTime Due { get; set; }
}

public sealed class LearnStore(string path)
{
    static readonly int[] Days = [0, 1, 3, 7, 16, 35, 90];
    Dictionary<string, CardState>? _cards;

    Dictionary<string, CardState> Cards
    {
        get
        {
            if (_cards is not null) return _cards;
            try { _cards = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, CardState>>(File.ReadAllText(path)) : null; } catch { }
            return _cards ??= [];
        }
    }

    public CardState? Get(string id) => Cards.GetValueOrDefault(id);

    public bool IsDue(string id, DateTime now) => Cards.TryGetValue(id, out var c) && c.Due <= now;

    /// <summary>Right answer: next box, longer wait. Wrong answer: back to the first box, due again soon.</summary>
    public void Answer(string id, bool knew, DateTime now)
    {
        var c = Cards.TryGetValue(id, out var x) ? x : Cards[id] = new CardState();
        c.Box = knew ? Math.Min(c.Box + 1, Days.Length - 1) : 0;
        c.Due = knew ? now.Date.AddDays(Days[c.Box]) : now;
        Save();
    }

    public int Known => Cards.Values.Count(c => c.Box >= 3);
    public int Seen => Cards.Count;

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(Cards));
    }
}
