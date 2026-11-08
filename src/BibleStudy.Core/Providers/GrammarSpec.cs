using System.Text.RegularExpressions;

namespace BibleStudy.Core.Providers;

/// <summary>
/// Reads a grammar description typed into a search: Gram:"verb aorist passive participle" or Gram:"noun genitive plural G26".
/// The terms are the English labels of the grammar search (verb, aorist, passive …), the German names (Verb, Aorist, Passiv, Partizip …),
/// and a Strong number or a base form narrows it to one word. Hebrew is recognised by its own terms (qatal, piel, construct) or an H number.
/// </summary>
public static partial class GrammarSpec
{
    public sealed record Result(GrammarQuery Query, bool Hebrew, IReadOnlyList<string> Unknown);

    // term → (field, value); the value is the one used by the Greek data, Hebrew differences are fixed afterwards
    static readonly Dictionary<string, (string Field, string Value)> Terms = Build();

    static readonly HashSet<string> HebrewOnly = ["qatal", "yiqtol", "wayyiqtol", "weqatal", "jussive", "qal", "niphal", "piel", "pual", "hiphil", "hophal", "hithpael", "absolute", "construct", "dual", "common"];

    static Dictionary<string, (string, string)> Build()
    {
        var d = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        void Add(string field, string value, params string[] names) { d[value] = (field, value); foreach (var n in names) d[n] = (field, value); }
        Add("class", "verb", "Verb");
        Add("class", "noun", "Nomen", "Substantiv", "Hauptwort");
        Add("class", "adj", "adjective", "Adjektiv");
        Add("class", "adv", "adverb", "Adverb");
        Add("class", "det", "article", "Artikel");
        Add("class", "pron", "pronoun", "Pronomen");
        Add("class", "prep", "preposition", "Präposition");
        Add("class", "conj", "conjunction", "Konjunktion");
        Add("class", "ptcl", "particle", "Partikel");
        Add("class", "num", "number-word", "Zahlwort");
        Add("class", "intj", "interjection", "Interjektion");
        Add("tense", "present", "Präsens");
        Add("tense", "imperfect", "Imperfekt");
        Add("tense", "future", "Futur");
        Add("tense", "aorist", "Aorist");
        Add("tense", "perfect", "Perfekt");
        Add("tense", "pluperfect", "Plusquamperfekt");
        Add("tense", "qatal"); Add("tense", "yiqtol"); Add("tense", "wayyiqtol"); Add("tense", "weqatal"); Add("tense", "jussive");
        Add("mood", "indicative", "Indikativ");
        Add("mood", "subjunctive", "Konjunktiv");
        Add("mood", "optative", "Optativ");
        Add("mood", "imperative", "Imperativ");
        Add("mood", "infinitive", "Infinitiv");
        Add("mood", "participle", "Partizip");
        Add("voice", "active", "Aktiv");
        Add("voice", "middle", "Medium");
        Add("voice", "passive", "Passiv");
        foreach (var stem in new[] { "qal", "niphal", "piel", "pual", "hiphil", "hophal", "hithpael" }) Add("voice", stem);
        Add("person", "first", "erste", "1st");
        Add("person", "second", "zweite", "2nd");
        Add("person", "third", "dritte", "3rd");
        Add("number", "singular", "Singular", "Einzahl");
        Add("number", "plural", "Plural", "Mehrzahl");
        Add("number", "dual", "Dual");
        Add("gender", "masculine", "maskulin");
        Add("gender", "feminine", "feminin");
        Add("gender", "neuter", "neutrum");
        Add("gender", "common", "gemeinsam");
        Add("case", "nominative", "Nominativ");
        Add("case", "genitive", "Genitiv");
        Add("case", "dative", "Dativ");
        Add("case", "accusative", "Akkusativ");
        Add("case", "vocative", "Vokativ");
        Add("case", "absolute", "absolut");
        Add("case", "construct", "Konstruktus");
        return d;
    }

    public static Result Parse(string spec)
    {
        var tokens = Split().Matches(spec).Select(m => m.Value).ToList();
        var fields = new Dictionary<string, string>();
        var unknown = new List<string>();
        string? word = null;
        var hebrew = false;
        foreach (var raw in tokens)
        {
            if (Terms.TryGetValue(raw, out var t))
            {
                if (HebrewOnly.Contains(t.Value)) hebrew = true;
                fields[t.Field] = t.Value;
            }
            else if (Regex.IsMatch(raw, @"^[GgHh]\d{1,4}[a-cA-C]?$")) { word = raw.ToUpperInvariant(); if (word[0] == 'H') hebrew = true; }
            else unknown.Add(raw);
        }
        // the Hebrew data names some things differently than the Greek
        if (hebrew)
        {
            if (fields.TryGetValue("class", out var c)) fields["class"] = c switch { "det" => "art", "conj" => "cj", "intj" => "ij", "ptcl" => "om", _ => c };
            if (fields.GetValueOrDefault("mood") == "imperative") { fields.Remove("mood"); fields["tense"] = "imperative"; }
            if (fields.GetValueOrDefault("mood") == "infinitive")
            {
                var kind = tokens.Contains("absolute", StringComparer.OrdinalIgnoreCase) ? "absolute" : "construct";
                fields.Remove("mood"); fields["tense"] = "infinitive " + kind;
                if (kind == "absolute") fields.Remove("case"); else if (fields.GetValueOrDefault("case") == "construct") fields.Remove("case");
            }
            if (fields.GetValueOrDefault("mood") == "participle")
            {
                fields.Remove("mood");
                fields["tense"] = fields.GetValueOrDefault("voice") == "passive" ? "participle passive" : "participle active";
                if (fields.GetValueOrDefault("voice") is "active" or "passive") fields.Remove("voice");
            }
        }
        string? F(string k) => fields.GetValueOrDefault(k);
        return new Result(new GrammarQuery(F("class"), F("tense"), F("voice"), F("mood"), F("person"), F("number"), F("gender"), F("case"), word), hebrew, unknown);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")] private static partial Regex Split();
}
