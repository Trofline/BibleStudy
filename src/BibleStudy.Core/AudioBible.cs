using System.Net.Http;
using System.Text.RegularExpressions;
using BibleStudy.Core.Import;

namespace BibleStudy.Core;

/// <summary>
/// The audio Bible: chapter recordings of the World English Bible read by Winfred W. Henson, public domain, hosted by eBible.org.
/// Nothing is bundled. A chapter is downloaded when it is played (about 3–10 MB), kept in the cache folder so it plays offline
/// afterwards, and the oldest ones are removed when the cache grows past <see cref="MaxCacheBytes"/>.
/// </summary>
public static class AudioBible
{
    public const string Name = "World English Bible";
    public const string Narrator = "Winfred W. Henson";
    public const string SourceUrl = "https://ebible.org/eng-web/audio/";
    public const long MaxCacheBytes = 400L * 1024 * 1024;

    /// <summary>The folder of each book on the server, in canonical order (same order as <see cref="BookData.UsfmIds"/>).</summary>
    static readonly string[] Folders =
        ("01_Genesis 02_Exodus 03_Leviticus 04_Numbers 05_Deuteronomy 06_Joshua 07_Judges 08_Ruth 09_First_Samuel 10_Second_Samuel 11_First_Kings 12_Second_Kings " +
         "13_First_Chronicles 14_Second_Chronicles 15_Ezra 16_Nehemiah 17_Esther 18_Job 19_Psalms 20_Proverbs 21_Ecclesiastes 22_Song_of_Solomon 23_Isaiah 24_Jeremiah " +
         "25_Lamentations 26_Ezekiel 27_Daniel 28_Hosea 29_Joel 30_Amos 31_Obadiah 32_Jonah 33_Micah 34_Nahum 35_Habakkuk 36_Zephaniah 37_Haggai 38_Zechariah 39_Malachi " +
         "40_Matthew 41_Mark 42_Luke 43_John 44_Acts 45_Romans 46_First_Corinthians 47_Second_Corinthians 48_Galatians 49_Ephesians 50_Philippians 51_Colossians " +
         "52_First_Thessalonians 53_Second_Thessalonians 54_First_Timothy 55_Second_Timothy 56_Titus 57_Philemon 58_Hebrews 59_James 60_First_Peter 61_Second_Peter " +
         "62_First_John 63_Second_John 64_Third_John 65_Jude 66_Revelations").Split(' ');

    /// <summary>Chapters per book in the same order (the recording follows this numbering; Psalms are 1–150).</summary>
    public static readonly int[] ChapterCounts =
    [
        50, 40, 27, 36, 34, 24, 21, 4, 31, 24, 22, 25, 29, 36, 10, 13, 10, 42, 150, 31, 12, 8, 66, 52, 5, 48, 12, 14, 3, 9, 1, 4, 7, 3, 3, 3, 2, 14, 4,
        28, 16, 24, 21, 28, 16, 16, 13, 6, 6, 4, 4, 5, 3, 6, 4, 3, 1, 13, 5, 5, 3, 5, 1, 1, 1, 22,
    ];

    public static int Chapters(string usfm) { var i = Array.IndexOf(BookData.UsfmIds, usfm); return i >= 0 ? ChapterCounts[i] : 0; }

    public static string? Folder(string usfm)
    {
        var i = Array.IndexOf(BookData.UsfmIds, usfm);
        return i >= 0 ? Folders[i] : null;
    }

    /// <summary>True for the 66 books of the recording (the apocrypha and other extra books have no audio).</summary>
    public static bool Has(string usfm) => Folder(usfm) is not null;

    /// <summary>
    /// The chapter files of a book from the folder page of the server, by chapter number. The files are named differently from book to book
    /// ("0997 John-Chapter One.mp3", "01 0973 Luke-Chapter One.mp3", "01_01_Genesis_Chapter_One.mp3") and the running numbers in front have
    /// a slip here and there (Isaiah 16 and 17 share one), so the chapter is read from the number in words at the end of the name.
    /// A file that appears twice ("… (1).mp3", Psalm 71, Luke 17) is taken once.
    /// </summary>
    public static SortedDictionary<int, string> ParseFiles(string html)
    {
        var map = new SortedDictionary<int, string>();
        foreach (var href in Regex.Matches(html, @"href=""([^""]+\.mp3)""", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal))
            if (ChapterOf(Uri.UnescapeDataString(href)) is int n and > 0) map.TryAdd(n, href);
        return map;
    }

    static readonly Dictionary<string, int> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
        ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30,
        ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    /// <summary>The number in words at the end of a file name ("Genesis_Chapter_Twenty_One.mp3" gives 21), or null.</summary>
    public static int? ChapterOf(string fileName)
    {
        var plain = Path.GetFileNameWithoutExtension(fileName);
        if (Regex.Match(plain, @"Chapter[\s_\-]*0*(\d+)", RegexOptions.IgnoreCase) is { Success: true } digits) return int.Parse(digits.Groups[1].Value);   // Proverbs: "Chapter 001"
        var words = Regex.Matches(plain, "[A-Za-z]+").Select(m => m.Value.ToLowerInvariant()).ToList();
        int? value = null; var total = 0;
        foreach (var w in words)
        {
            if (Words.TryGetValue(w, out var v)) { total += v; value = total; }
            else if (w == "hundred") { total = Math.Max(1, total) * 100; value = total; }
            else { total = 0; value = null; }   // a word that is not a number ends the run: only the last run counts
        }
        return value;
    }

    /// <summary>The chapter after the given one, or null after Revelation.</summary>
    public static (string Book, int Chapter)? Next(string usfm, int chapter)
    {
        if (chapter < Chapters(usfm)) return (usfm, chapter + 1);
        var i = Array.IndexOf(BookData.UsfmIds, usfm);
        return i >= 0 && i + 1 < BookData.UsfmIds.Length ? (BookData.UsfmIds[i + 1], 1) : null;
    }

    /// <summary>The chapter before the given one, or null before Genesis.</summary>
    public static (string Book, int Chapter)? Previous(string usfm, int chapter)
    {
        if (chapter > 1) return (usfm, chapter - 1);
        var i = Array.IndexOf(BookData.UsfmIds, usfm);
        return i > 0 ? (BookData.UsfmIds[i - 1], ChapterCounts[i - 1]) : null;
    }

    static string Dir(string cacheDir) => Path.Combine(cacheDir, "audio", "web");

    /// <summary>Where a chapter is kept once downloaded.</summary>
    public static string CachePath(string cacheDir, string usfm, int chapter) => Path.Combine(Dir(cacheDir), $"{usfm}-{chapter:000}.mp3");

    public static bool IsCached(string cacheDir, string usfm, int chapter) => File.Exists(CachePath(cacheDir, usfm, chapter)) && new FileInfo(CachePath(cacheDir, usfm, chapter)).Length > 0;

    /// <summary>The local file of a chapter; downloads it first when it is not in the cache yet.</summary>
    public static async Task<string> GetChapterFileAsync(HttpClient http, string cacheDir, string usfm, int chapter, CancellationToken ct = default)
    {
        var folder = Folder(usfm) ?? throw new InvalidOperationException("No recording for " + usfm);
        var path = CachePath(cacheDir, usfm, chapter);
        if (IsCached(cacheDir, usfm, chapter)) { try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch (IOException) { } return path; }
        Directory.CreateDirectory(Dir(cacheDir));

        var indexPath = Path.Combine(Dir(cacheDir), folder + ".idx");
        var files = new Dictionary<int, string>();
        if (File.Exists(indexPath))
            foreach (var line in File.ReadAllLines(indexPath)) if (line.Split('\t') is [var no, var name] && int.TryParse(no, out var n)) files[n] = name;
        if (!files.ContainsKey(chapter))
        {
            var html = await http.GetStringAsync($"{SourceUrl}{folder}/", ct);
            files = ParseFiles(html).ToDictionary(x => x.Key, x => x.Value);
            if (files.Count > 0) File.WriteAllLines(indexPath, files.Select(x => $"{x.Key}\t{x.Value}"));
        }
        if (!files.TryGetValue(chapter, out var file)) throw new FileNotFoundException($"{folder}: chapter {chapter} has no recording");

        var tmp = $"{path}.{Guid.NewGuid():N}.part";   // the prefetch of the next chapter and a click on "next" may load the same chapter at once
        try
        {
            using var resp = await http.GetAsync($"{SourceUrl}{folder}/{file}", HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using (var dst = File.Create(tmp)) await src.CopyToAsync(dst, ct);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { }   // no half-finished file stays behind
            throw;
        }
        TrimCache(cacheDir, MaxCacheBytes, keep: path);
        return path;
    }

    /// <summary>Deletes the least recently played chapters until the cache fits into <paramref name="maxBytes"/>. Returns how many files were removed.</summary>
    public static int TrimCache(string cacheDir, long maxBytes, string? keep = null)
    {
        var dir = Dir(cacheDir);
        if (!Directory.Exists(dir)) return 0;
        var files = new DirectoryInfo(dir).GetFiles("*.mp3").OrderBy(f => f.LastWriteTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        var removed = 0;
        foreach (var f in files)
        {
            if (total <= maxBytes) break;
            if (keep is not null && string.Equals(f.FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
            try { total -= f.Length; f.Delete(); removed++; } catch (IOException) { }
        }
        return removed;
    }
}
