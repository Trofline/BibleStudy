using System.Net;
using System.Text.RegularExpressions;
using BibleStudy.Core.Storage;

namespace BibleStudy.Core.Providers;

/// <summary>A word of the STEPBible brief lexicon. <paramref name="Meaning"/> is empty for Hebrew (see <see cref="StepLexicon"/>).</summary>
public sealed record StepEntry(string Id, string Word, string Translit, string Gloss, string Meaning);

/// <summary>
/// The brief lexicons of STEPBible (Tyndale House Cambridge, CC BY 4.0, github.com/STEPBible/STEPBible-Data): a short English meaning for
/// every Greek and Hebrew word, for Greek with the fuller entry of Abbott-Smith (1922, public domain). Downloaded on first use, cached, not bundled.
/// The Hebrew file says its long definitions come from the Abridged BDB of the Online Bible and that permission has to be asked for before they
/// are used in a project: so only the short gloss of the Hebrew words is used here, never those definitions.
/// </summary>
public sealed partial class StepLexicon(HttpClient http, string cacheDir)
{
    public static string Attribution => L("Kurzlexikon: STEPBible (Tyndale House Cambridge), CC BY 4.0, stepbible.org; Griechisch mit Abbott-Smith (1922, gemeinfrei)");

    const string Base = "https://raw.githubusercontent.com/STEPBible/STEPBible-Data/master/Lexicons/";
    const string GreekFile = "TBESG%20-%20Translators%20Brief%20lexicon%20of%20Extended%20Strongs%20for%20Greek%20-%20STEPBible.org%20CC%20BY.txt";
    const string HebrewFile = "TBESH%20-%20Translators%20Brief%20lexicon%20of%20Extended%20Strongs%20for%20Hebrew%20-%20STEPBible.org%20CC%20BY.txt";

    readonly Dictionary<char, Dictionary<int, StepEntry>> _data = [];
    readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The entry of a Strong number ("G26", "H430"); null when there is none.</summary>
    public async Task<StepEntry?> LookupAsync(string id, CancellationToken ct = default)
    {
        if (id.Length < 2 || char.ToUpperInvariant(id[0]) is not ('G' or 'H') || !int.TryParse(id[1..].TrimEnd('a', 'b', 'c', 'A', 'B', 'C'), out var number)) return null;
        var lang = char.ToUpperInvariant(id[0]);
        await _gate.WaitAsync(ct);
        try
        {
            if (!_data.TryGetValue(lang, out var table)) _data[lang] = table = Parse(await LoadAsync(lang, ct), lang);
            return table.GetValueOrDefault(number);
        }
        finally { _gate.Release(); }
    }

    async Task<string> LoadAsync(char lang, CancellationToken ct)
    {
        var (file, name) = lang == 'G' ? (GreekFile, "greek.txt") : (HebrewFile, "hebrew.txt");
        var path = Path.Combine(cacheDir, "step", name);
        if (File.Exists(path)) return await File.ReadAllTextAsync(path, ct);
        var text = await http.GetStringAsync(Base + file, ct);
        await SafeFile.WriteAllTextAsync(path, text);
        return text;
    }

    /// <summary>The table rows (first column "G0026" / "H0430"); the rows before the header are explanations. One entry per number: the first one wins.</summary>
    public static Dictionary<int, StepEntry> Parse(string text, char lang)
    {
        var result = new Dictionary<int, StepEntry>();
        foreach (var line in text.Split('\n'))
        {
            var f = line.TrimEnd('\r').Split('\t');
            if (f.Length < 8 || f[0].Length != 5 || f[0][0] != lang || !int.TryParse(f[0].AsSpan(1), out var number) || result.ContainsKey(number)) continue;
            var meaning = lang == 'G' ? Plain(f[7]) : "";   // Hebrew: gloss only (see the class comment)
            result[number] = new StepEntry(f[0][0] + number.ToString(), f[3].Trim(), f[4].Trim(), f[6].Trim(), meaning);
        }
        return result;
    }

    /// <summary>The lexicon's small HTML as plain text with line breaks.</summary>
    public static string Plain(string html)
    {
        var s = LineBreak().Replace(html, "\n");
        s = Tags().Replace(s, "");
        s = WebUtility.HtmlDecode(s).Replace("__", "  ");
        s = Spaces().Replace(s, " ");
        s = string.Join("\n", s.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        return s.Trim();
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)] private static partial Regex LineBreak();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"[ \t]+")] private static partial Regex Spaces();
}
