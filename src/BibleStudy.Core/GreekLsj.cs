using System.Net;
using System.Text.RegularExpressions;

namespace BibleStudy.Core;

/// <summary>One part of the long Greek entry (a Strong number can have several, e.g. G0001 has two words).</summary>
public sealed record LsjEntry(string Greek, string Gloss, IReadOnlyList<string> Lines);

/// <summary>
/// The Liddell-Scott-Jones Greek lexicon as edited by Tyndale House Cambridge (STEPBible "TFLSJ", CC BY 4.0, based on the Perseus
/// digitisation of the public domain edition). Downloaded on first use (about 24 MB) into the cache folder and read line by line.
/// </summary>
public sealed partial class GreekLsj(HttpClient http, string cacheDir)
{
    const string Url = "https://raw.githubusercontent.com/STEPBible/STEPBible-Data/master/Lexicons/TFLSJ%20%200-5624%20-%20Translators%20Formatted%20full%20LSJ%20Bible%20lexicon%20-%20STEPBible.org%20CC%20BY.txt";
    public const string Attribution = "Liddell-Scott-Jones, Greek-English Lexicon, bearbeitet von Tyndale House Cambridge (STEPBible), CC BY 4.0";

    Dictionary<string, List<LsjEntry>>? _byStrong;
    readonly SemaphoreSlim _gate = new(1, 1);

    async Task<string> FileAsync(CancellationToken ct)
    {
        var path = Path.Combine(cacheDir, "lsj", "tflsj.txt");
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await using (var s = await http.GetStreamAsync(Url, ct))
        await using (var f = File.Create(tmp)) await s.CopyToAsync(f, ct);
        if (new FileInfo(tmp).Length < 5_000_000) { File.Delete(tmp); throw new IOException("incomplete download"); }   // only a complete file is kept
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    /// <summary>The entries for a Strong number such as "G2316" (empty when the lexicon has none).</summary>
    public async Task<IReadOnlyList<LsjEntry>> GetAsync(string strongId, CancellationToken ct = default)
    {
        if (_byStrong is null)
        {
            await _gate.WaitAsync(ct);
            try { _byStrong ??= Parse(await File.ReadAllTextAsync(await FileAsync(ct), ct)); }
            finally { _gate.Release(); }
        }
        var digits = new string(strongId.Skip(1).TakeWhile(char.IsDigit).ToArray());
        return digits.Length > 0 && strongId[0] is 'G' or 'g' && _byStrong.TryGetValue("G" + int.Parse(digits).ToString("D4"), out var l) ? l : [];
    }

    /// <summary>Tab-separated lines: eStrong, dStrong, uStrong, Greek, transliteration, morph, gloss, entry (HTML-like).</summary>
    public static Dictionary<string, List<LsjEntry>> Parse(string text)
    {
        var map = new Dictionary<string, List<LsjEntry>>();
        foreach (var raw in text.Split('\n'))
        {
            var c = raw.TrimEnd('\r').Split('\t');
            if (c.Length < 8 || !StrongKey().IsMatch(c[0])) continue;
            var lines = Render(c[7]);
            if (lines.Count == 0) continue;
            if (!map.TryGetValue(c[0], out var list)) map[c[0]] = list = [];
            list.Add(new LsjEntry(c[3].Trim(), c[6].Trim(), lines));
        }
        return map;
    }

    [GeneratedRegex(@"^G\d{4}$")] private static partial Regex StrongKey();
    [GeneratedRegex(@"<br\s*/?>|<Level\d>", RegexOptions.IgnoreCase)] private static partial Regex Break();
    [GeneratedRegex(@"<a [^>]*>(?<t>.*?)</a>", RegexOptions.IgnoreCase)] private static partial Regex Link();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex Tag();
    [GeneratedRegex(@"\[\s*\]")] private static partial Regex RefMark();

    /// <summary>The entry as plain lines: line breaks kept, markup and the hidden reference links removed.</summary>
    public static List<string> Render(string html)
    {
        var s = Link().Replace(html, ""); s = RefMark().Replace(s, "");
        s = Break().Replace(s, "\n");
        s = Tag().Replace(s, "");
        s = WebUtility.HtmlDecode(s).Replace("__", "");
        return s.Split('\n').Select(l => Regex.Replace(l, @"[ \t]+", " ").Trim().Trim(';', ',').Trim()).Where(l => l.Length > 0).ToList();
    }
}
