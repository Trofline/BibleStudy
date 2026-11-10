using System.Text.RegularExpressions;

namespace BibleStudy.Core;

/// <summary>A word of the New Testament where the Greek editions differ.</summary>
public sealed record GreekVariant(string Greek, string English, string Editions, string Alternative);

/// <summary>
/// Text criticism for the New Testament: where the major Greek editions (NA28/NA27, Tyndale House GNT, SBL, Westcott-Hort, Tregelles,
/// Textus Receptus, Byzantine) differ. From the STEPBible "TAGNT" files by Tyndale House Cambridge (CC BY 4.0); downloaded on first
/// use per half of the New Testament (about 15 MB each) and kept in the cache folder.
/// </summary>
public sealed partial class TextVariants(HttpClient http, string cacheDir)
{
    const string Base = "https://raw.githubusercontent.com/STEPBible/STEPBible-Data/master/Translators%20Amalgamated%20OT+NT/";
    public const string Attribution = "TAGNT (Translators Amalgamated Greek NT), STEPBible / Tyndale House Cambridge, CC BY 4.0";
    public const string EditionsLegend = "NA28/NA27 = Nestle-Aland, Tyn = Tyndale House GNT, SBL = SBL Greek NT, WH = Westcott-Hort, Treg = Tregelles, TR = Textus Receptus, Byz = Byzantine";

    static readonly string[] FirstHalf = ["MAT", "MRK", "LUK", "JHN"];
    readonly Dictionary<bool, Dictionary<string, List<GreekVariant>>> _halves = [];
    readonly SemaphoreSlim _gate = new(1, 1);

    public static bool IsNewTestament(string bookId) => bookId.ToUpperInvariant() is "MAT" or "MRK" or "LUK" or "JHN" or "ACT" or "ROM" or "1CO" or "2CO" or "GAL" or "EPH" or "PHP" or "COL" or "1TH" or "2TH" or "1TI" or "2TI" or "TIT" or "PHM" or "HEB" or "JAS" or "1PE" or "2PE" or "1JN" or "2JN" or "3JN" or "JUD" or "REV";

    public async Task<IReadOnlyList<GreekVariant>> GetAsync(string bookId, int chapter, int verse, CancellationToken ct = default)
    {
        if (!IsNewTestament(bookId)) return [];
        var first = FirstHalf.Contains(bookId.ToUpperInvariant());
        await _gate.WaitAsync(ct);
        try
        {
            if (!_halves.TryGetValue(first, out var map))
            {
                var name = first ? "TAGNT Mat-Jhn - Translators Amalgamated Greek NT - STEPBible.org CC-BY.txt" : "TAGNT Act-Rev - Translators Amalgamated Greek NT - STEPBible.org CC-BY.txt";
                var path = Path.Combine(cacheDir, "tagnt", first ? "mat-jhn.txt" : "act-rev.txt");
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var tmp = path + ".tmp";
                    await using (var s = await http.GetStreamAsync(Base + Uri.EscapeDataString(name), ct))
                    await using (var f = File.Create(tmp)) await s.CopyToAsync(f, ct);
                    if (new FileInfo(tmp).Length < 5_000_000) { File.Delete(tmp); throw new IOException("incomplete download"); }
                    File.Move(tmp, path, overwrite: true);
                }
                _halves[first] = map = await Task.Run(() => Parse(File.ReadAllText(path)), ct);
            }
            var key = $"{char.ToUpperInvariant(bookId[0])}{bookId[1..].ToLowerInvariant()}.{chapter}.{verse}";
            return map.TryGetValue(key, out var list) ? list : [];
        }
        finally { _gate.Release(); }
    }

    [GeneratedRegex(@"^([1-3]?[A-Za-z]{2,3}\.\d+\.\d+)#\d+=")] private static partial Regex RowId();
    [GeneratedRegex(@"[«»]\d*")] private static partial Regex Mark();

    static readonly string[] All = ["NA28", "NA27", "Tyn", "SBL", "WH", "Treg", "TR", "Byz"];

    /// <summary>Rows where the editions differ (a word missing from some, or another reading given), grouped by verse.</summary>
    public static Dictionary<string, List<GreekVariant>> Parse(string text)
    {
        var map = new Dictionary<string, List<GreekVariant>>(StringComparer.OrdinalIgnoreCase);   // the rows name the books "1Co", the program asks with "1CO"
        foreach (var raw in text.Split('\n'))
        {
            var c = raw.TrimEnd('\r').Split('\t');
            if (c.Length < 7) continue;
            var m = RowId().Match(c[0]);
            if (!m.Success) continue;
            var editions = Mark().Replace(c[5], "").Trim();
            var set = editions.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToHashSet();
            var alternative = c[6].Trim();
            if (alternative.Length == 0 && All.All(set.Contains)) continue;            // every edition has it, nothing to tell
            if (alternative.Length == 0 && set.Count == 0) continue;
            var greek = Regex.Replace(c[1], @"\s*\(.*?\)\s*$", "").Trim();
            if (!map.TryGetValue(m.Groups[1].Value, out var list)) map[m.Groups[1].Value] = list = [];
            list.Add(new GreekVariant(greek, c[2].Trim(), editions, alternative));
        }
        return map;
    }
}
