using Microsoft.Data.Sqlite;

namespace BibleStudy.Core;

/// <summary>What one church father (or later teacher) wrote about a passage.</summary>
public sealed record FatherQuote(string Father, int Year, string Text, string SourceTitle, string SourceUrl, string Category, int StartChapter, int StartVerse, int EndChapter, int EndVerse)
{
    public string Range => StartChapter == EndChapter
        ? (EndVerse > StartVerse ? $"{StartChapter},{StartVerse}–{EndVerse}" : $"{StartChapter},{StartVerse}")
        : $"{StartChapter},{StartVerse}–{EndChapter},{EndVerse}";
}

/// <summary>
/// The church fathers on a verse: the "Historical Christian Faith" commentaries database (public domain dedication; some excerpts of
/// later authors are included there under fair use, as the source says). About 180 MB, loaded once on request and kept in the cache.
/// </summary>
public sealed class ChurchFathers(HttpClient http, string cacheDir)
{
    public const string Url = "https://github.com/HistoricalChristianFaith/Commentaries-Database/releases/download/latest/commentaries.sqlite";
    public const string Website = "https://historicalchristian.faith";
    public const string Attribution = "Historical Christian Faith – Commentaries Database (public domain dedication; some excerpts under fair use according to the source)";

    string FilePath => Path.Combine(cacheDir, "fathers", "commentaries.sqlite");
    public bool IsLoaded => File.Exists(FilePath);

    static readonly Dictionary<string, string> Books = new()
    {
        ["GEN"] = "genesis", ["EXO"] = "exodus", ["LEV"] = "leviticus", ["NUM"] = "numbers", ["DEU"] = "deuteronomy", ["JOS"] = "joshua", ["JDG"] = "judges",
        ["RUT"] = "ruth", ["1SA"] = "1samuel", ["2SA"] = "2samuel", ["1KI"] = "1kings", ["2KI"] = "2kings", ["1CH"] = "1chronicles", ["2CH"] = "2chronicles",
        ["EZR"] = "ezra", ["NEH"] = "nehemiah", ["EST"] = "esther", ["JOB"] = "job", ["PSA"] = "psalms", ["PRO"] = "proverbs", ["ECC"] = "ecclesiastes",
        ["SNG"] = "songofsolomon", ["ISA"] = "isaiah", ["JER"] = "jeremiah", ["LAM"] = "lamentations", ["EZK"] = "ezekiel", ["DAN"] = "daniel", ["HOS"] = "hosea",
        ["JOL"] = "joel", ["AMO"] = "amos", ["OBA"] = "obadiah", ["JON"] = "jonah", ["MIC"] = "micah", ["NAM"] = "nahum", ["HAB"] = "habakkuk",
        ["ZEP"] = "zephaniah", ["HAG"] = "haggai", ["ZEC"] = "zechariah", ["MAL"] = "malachi", ["TOB"] = "tobit", ["JDT"] = "judith", ["WIS"] = "wisdom",
        ["SIR"] = "sirach", ["BAR"] = "baruch", ["1MA"] = "1maccabees", ["2MA"] = "2maccabees",
        ["MAT"] = "matthew", ["MRK"] = "mark", ["LUK"] = "luke", ["JHN"] = "john", ["ACT"] = "acts", ["ROM"] = "romans", ["1CO"] = "1corinthians",
        ["2CO"] = "2corinthians", ["GAL"] = "galatians", ["EPH"] = "ephesians", ["PHP"] = "philippians", ["COL"] = "colossians", ["1TH"] = "1thessalonians",
        ["2TH"] = "2thessalonians", ["1TI"] = "1timothy", ["2TI"] = "2timothy", ["TIT"] = "titus", ["PHM"] = "philemon", ["HEB"] = "hebrews", ["JAS"] = "james",
        ["1PE"] = "1peter", ["2PE"] = "2peter", ["1JN"] = "1john", ["2JN"] = "2john", ["3JN"] = "3john", ["JUD"] = "jude", ["REV"] = "revelation",
    };

    public static string? BookKey(string bookId) => Books.GetValueOrDefault(bookId.ToUpperInvariant());

    /// <summary>Loads the database once (about 180 MB); progress in bytes. A half loaded file is never kept.</summary>
    public async Task LoadAsync(IProgress<(long Done, long? Total)>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var part = FilePath + ".part";
        using (var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(part);
            var buffer = new byte[1 << 16];
            long done = 0, lastReport = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                done += n;
                if (done - lastReport > 1 << 20) { lastReport = done; progress?.Report((done, total)); }
            }
            progress?.Report((done, total));
        }
        // a SQLite file starts with this; anything else (an error page) is thrown away
        var head = new byte[16];
        await using (var f = File.OpenRead(part)) _ = await f.ReadAsync(head, ct).ConfigureAwait(false);
        if (!System.Text.Encoding.ASCII.GetString(head).StartsWith("SQLite format 3")) { File.Delete(part); throw new InvalidDataException("not a SQLite file"); }
        File.Move(part, FilePath, true);
    }

    public void Delete()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    /// <summary>
    /// What was written on this verse, the oldest first. <paramref name="untilYear"/> leaves out later authors (the church fathers in the
    /// narrow sense end around 750).
    /// </summary>
    public List<FatherQuote> For(string bookId, int chapter, int verse, int untilYear = 9999)
    {
        if (!IsLoaded || BookKey(bookId) is not { } book) return [];
        var at = chapter * 1_000_000L + verse;
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FilePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT c.father_name, c.append_to_author_name, c.ts, c.txt, c.source_title, c.source_url, COALESCE(m.father_category, ''), c.location_start, c.location_end
            FROM commentary c LEFT JOIN father_meta m ON m.name = c.father_name
            WHERE c.book = $b AND c.location_start <= $at AND c.location_end >= $at AND c.ts <= $y
            ORDER BY c.ts, c.father_name, c.location_start
            """;
        cmd.Parameters.AddWithValue("$b", book); cmd.Parameters.AddWithValue("$at", at); cmd.Parameters.AddWithValue("$y", untilYear);
        var list = new List<FatherQuote>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var name = r.GetString(0) + (r.IsDBNull(1) ? "" : r.GetString(1));
            var start = r.GetInt64(7); var end = r.GetInt64(8);
            list.Add(new FatherQuote(name, r.IsDBNull(2) || !int.TryParse(Convert.ToString(r.GetValue(2), System.Globalization.CultureInfo.InvariantCulture), out var year) ? 0 : year, (r.IsDBNull(3) ? "" : r.GetString(3)).Trim(),
                r.IsDBNull(4) ? "" : r.GetString(4), r.IsDBNull(5) ? "" : r.GetString(5), r.GetString(6),
                (int)(start / 1_000_000), (int)(start % 1_000_000), (int)(end / 1_000_000), (int)(end % 1_000_000)));
        }
        return list;
    }
}
