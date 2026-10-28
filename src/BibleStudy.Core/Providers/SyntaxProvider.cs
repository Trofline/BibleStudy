using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BibleStudy.Core.Providers;

/// <summary>A word of the original text with its grammar (MACULA: lemma, gloss, part of speech, tense, case …).</summary>
public sealed record SyntaxWord(string Text, string Lemma, string Strong, string Gloss, string Translit, string PartOfSpeech, bool Hebrew, IReadOnlyList<KeyValuePair<string, string>> Features, string? Role);

/// <summary>One line of the clause outline: depth, role in the clause (s, v, o …), the original words and their glosses.</summary>
public sealed record SyntaxLine(int Depth, string Kind, string? Role, string Text, string Gloss);

public sealed record SyntaxVerse(IReadOnlyList<SyntaxWord> Words, IReadOnlyList<SyntaxLine> Outline);

/// <summary>
/// Syntax trees of the New Testament (MACULA Greek, Nestle 1904) and the Old Testament (MACULA Hebrew, WLC) from the Clear Bible
/// project (https://github.com/Clear-Bible, CC BY 4.0). Downloaded per book (Greek) or chapter (Hebrew) on first use and cached.
/// </summary>
public sealed class SyntaxProvider(HttpClient http, string cacheDir)
{
    public static string Attribution => L("Syntax und Morphologie: MACULA Greek / Hebrew © Biblica, Clear Bible, CC BY 4.0 (github.com/Clear-Bible)");

    const string GreekBase = "https://raw.githubusercontent.com/Clear-Bible/macula-greek/main/Nestle1904/lowfat/";
    const string HebrewBase = "https://raw.githubusercontent.com/Clear-Bible/macula-hebrew/main/WLC/lowfat/";

    static readonly string[] GreekBooks =
    [
        "MAT:01-matthew", "MRK:02-mark", "LUK:03-luke", "JHN:04-john", "ACT:05-acts", "ROM:06-romans", "1CO:07-1corinthians", "2CO:08-2corinthians",
        "GAL:09-galatians", "EPH:10-ephesians", "PHP:11-philippians", "COL:12-colossians", "1TH:13-1thessalonians", "2TH:14-2thessalonians",
        "1TI:15-1timothy", "2TI:16-2timothy", "TIT:17-titus", "PHM:18-philemon", "HEB:19-hebrews", "JAS:20-james", "1PE:21-1peter", "2PE:22-2peter",
        "1JN:23-1john", "2JN:24-2john", "3JN:25-3john", "JUD:26-jude", "REV:27-revelation",
    ];

    static readonly string[] HebrewBooks =
    [
        "GEN:01-Gen", "EXO:02-Exo", "LEV:03-Lev", "NUM:04-Num", "DEU:05-Deu", "JOS:06-Jos", "JDG:07-Jdg", "RUT:08-Rut", "1SA:09-1Sa", "2SA:10-2Sa",
        "1KI:11-1Ki", "2KI:12-2Ki", "1CH:13-1Ch", "2CH:14-2Ch", "EZR:15-Ezr", "NEH:16-Neh", "EST:17-Est", "JOB:18-Job", "PSA:19-Psa", "PRO:20-Pro",
        "ECC:21-Ecc", "SNG:22-Sng", "ISA:23-Isa", "JER:24-Jer", "LAM:25-Lam", "EZK:26-Ezk", "DAN:27-Dan", "HOS:28-HOS", "JOL:29-Jol", "AMO:30-Amo",
        "OBA:31-Oba", "JON:32-Jon", "MIC:33-Mic", "NAM:34-Nam", "HAB:35-Hab", "ZEP:36-Zep", "HAG:37-Hag", "ZEC:38-Zec", "MAL:39-Mal",
    ];

    static string? Find(string[] table, string book) =>
        table.FirstOrDefault(x => x.StartsWith(book + ":", StringComparison.Ordinal))?[(book.Length + 1)..];

    public static bool IsAvailable(string book) => Find(GreekBooks, book) is not null || Find(HebrewBooks, book) is not null;

    public async Task<SyntaxVerse?> GetVerseAsync(string book, int chapter, int verse, CancellationToken ct = default)
    {
        string url, file;
        if (Find(GreekBooks, book) is { } g) { file = g + ".xml"; url = GreekBase + file; }
        else if (Find(HebrewBooks, book) is { } h) { file = $"{h}-{chapter:D3}-lowfat.xml"; url = HebrewBase + file; }
        else return null;

        var path = Path.Combine(cacheDir, "macula", file);
        string xml;
        if (File.Exists(path)) xml = await File.ReadAllTextAsync(path, ct);
        else
        {
            xml = await http.GetStringAsync(url, ct);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, xml, ct);
        }
        return await Task.Run(() => Parse(xml, chapter, verse), ct);
    }

    static readonly Regex RefPattern = new(@"(\d+):(\d+)!\d+$", RegexOptions.Compiled);

    static bool InVerse(XElement w, int chapter, int verse) =>
        w.Attribute("ref")?.Value is { } r && RefPattern.Match(r) is { Success: true } m && int.Parse(m.Groups[1].Value) == chapter && int.Parse(m.Groups[2].Value) == verse;

    static readonly string[] FeatureNames = ["tense", "voice", "mood", "person", "number", "gender", "case", "degree", "stem", "type", "state"];

    public static SyntaxVerse Parse(string xml, int chapter, int verse)
    {
        var doc = XDocument.Parse(xml);
        var words = new List<SyntaxWord>();
        var lines = new List<SyntaxLine>();

        foreach (var w in doc.Descendants("w").Where(x => InVerse(x, chapter, verse)))
        {
            var hebrew = (string?)w.Attribute("lang") == "H";
            var strong = Strong(w, hebrew);
            var features = FeatureNames.Select(n => (n, v: (string?)w.Attribute(n))).Where(f => !string.IsNullOrEmpty(f.v)).Select(f => new KeyValuePair<string, string>(f.n, f.v!)).ToList();
            words.Add(new SyntaxWord(w.Value.Trim(), (string?)w.Attribute("lemma") ?? "", strong, Gloss(w, hebrew), (string?)w.Attribute("transliteration") ?? "",
                (string?)w.Attribute("class") ?? (string?)w.Attribute("pos") ?? "", hebrew, features, (string?)w.Attribute("role")));
        }

        // outline: sentences that contain words of the verse, clauses nested, phrases collapsed into one line
        foreach (var sentence in doc.Descendants("sentence").Where(s => s.Descendants("w").Any(w => InVerse(w, chapter, verse))))
            Walk(sentence, 0);

        void Walk(XElement node, int depth)
        {
            foreach (var child in node.Elements())
            {
                if (child.Name == "w")
                {
                    if (InVerse(child, chapter, verse) && (string?)child.Attribute("role") is { } wordRole)
                        lines.Add(new SyntaxLine(depth, "word", wordRole, child.Value.Trim(), Gloss(child, (string?)child.Attribute("lang") == "H")));
                    continue;
                }
                if (child.Name != "wg") continue;
                var inside = child.Descendants("w").Where(w => InVerse(w, chapter, verse)).ToList();
                if (inside.Count == 0) continue;
                var cls = (string?)child.Attribute("class");
                var role = (string?)child.Attribute("role");
                var hasClause = child.Elements("wg").Any(c => (string?)c.Attribute("class") == "cl" || c.Descendants("wg").Any(d => (string?)d.Attribute("class") == "cl"));
                if (cls is null) { Walk(child, depth); continue; }   // grouping without a class: transparent
                var text = string.Join(" ", inside.Select(w => w.Value.Trim()));
                var gloss = string.Join(" ", inside.Select(w => Gloss(w, (string?)w.Attribute("lang") == "H")));
                if (cls == "cl") { lines.Add(new SyntaxLine(depth, "clause", role, text, gloss)); Walk(child, depth + 1); }
                else if (hasClause) { lines.Add(new SyntaxLine(depth, "phrase", role, "", "")); Walk(child, depth + 1); }
                else lines.Add(new SyntaxLine(depth, "phrase", role, text, gloss));
            }
        }
        return new SyntaxVerse(words, lines);
    }

    static string Strong(XElement w, bool hebrew)
    {
        if (hebrew) return (string?)w.Attribute("strongnumberx") is { Length: > 0 } sx && Regex.Match(sx, @"^\d+") is { Success: true } m ? "H" + int.Parse(m.Value) : "";
        return (string?)w.Attribute("strong") is { Length: > 0 } s && int.TryParse(s, out var n) ? "G" + n : "";
    }

    static string Gloss(XElement w, bool hebrew) =>
        ((string?)w.Attribute("gloss") ?? (string?)w.Attribute("english") ?? "").Replace('.', ' ').Trim();
}
