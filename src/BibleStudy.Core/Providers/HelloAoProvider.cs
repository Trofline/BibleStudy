using System.Text.Json;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Providers;

/// <summary>
/// Free Use Bible API (https://bible.helloao.org). Delivers license metadata per translation.
/// Responses are cached on disk so texts stay available offline.
/// </summary>
public sealed class HelloAoProvider(HttpClient http, string cacheDir) : IBibleProvider
{
    const string Base = "https://bible.helloao.org";
    public string Name => "HelloAO Free Use Bible API";

    public async Task<IReadOnlyList<TranslationInfo>> GetTranslationsAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("/api/available_translations.json", ct);
        return doc.RootElement.GetProperty("translations").EnumerateArray().Select(t =>
        {
            var l = t.GetProperty("license");
            var lic = new LicenseInfo(Str(l, "attribution"), Str(l, "commercialUse"), Str(l, "derivatives"),
                l.TryGetProperty("copyleft", out var c) && c.ValueKind == JsonValueKind.True,
                l.TryGetProperty("publicDomain", out var p) && p.ValueKind == JsonValueKind.True);
            return new TranslationInfo(Str(t, "id"), Str(t, "name"), Str(t, "englishName"), Str(t, "shortName"),
                Str(t, "language"), Str(t, "languageEnglishName"), Str(t, "textDirection"),
                Str(t, "licenseUrl"), Str(t, "website"), lic, t.TryGetProperty("numberOfBooks", out var n) ? n.GetInt32() : 0);
        }).ToList();
    }

    public async Task<IReadOnlyList<BookInfo>> GetBooksAsync(string translationId, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"/api/{translationId}/books.json", ct);
        return doc.RootElement.GetProperty("books").EnumerateArray().Select(b =>
            new BookInfo(Str(b, "id"), Str(b, "name"), Str(b, "commonName"), b.GetProperty("order").GetInt32(),
                b.GetProperty("numberOfChapters").GetInt32())).ToList();
    }

    public async Task<Chapter> GetChapterAsync(string translationId, string bookId, int chapter, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"/api/{translationId}/{bookId}/{chapter}.json", ct);
        return new Chapter(translationId, bookId, chapter, ParseVerses(doc.RootElement.GetProperty("chapter"), await TryGetWordsAsync(translationId, bookId, chapter, ct)));
    }

    public const string CrossRefAttribution = "Querverweise © OpenBible.info, CC BY 4.0 (openbible.info/labs/cross-references)";

    /// <summary>Cross references of one verse, best first (dataset "open-cross-ref").</summary>
    public async Task<IReadOnlyList<CrossRef>> GetCrossReferencesAsync(string book, int chapter, int verse, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"/api/d/open-cross-ref/{book}/{chapter}.json", ct);
        foreach (var item in doc.RootElement.GetProperty("chapter").GetProperty("content").EnumerateArray())
        {
            if (item.GetProperty("verse").GetInt32() != verse) continue;
            return item.GetProperty("references").EnumerateArray().Select(r =>
            {
                var v = r.GetProperty("verse").GetInt32();
                return new CrossRef(new VerseRef(Str(r, "book"), r.GetProperty("chapter").GetInt32(), v),
                    r.TryGetProperty("endVerse", out var e) ? e.GetInt32() : v, r.TryGetProperty("score", out var s) ? s.GetInt32() : 0);
            }).OrderByDescending(r => r.Score).ToList();
        }
        return [];
    }

    /// <summary>Per-word Strong's tags; only some translations have them, so a miss is remembered per session.</summary>
    async Task<JsonDocument?> TryGetWordsAsync(string translationId, string bookId, int chapter, CancellationToken ct)
    {
        lock (_noWords) if (_noWords.Contains(translationId)) return null;
        try { return await GetJsonAsync($"/api/{translationId}/{bookId}/{chapter}.words.json", ct); }
        catch (HttpRequestException) { lock (_noWords) _noWords.Add(translationId); return null; }
    }

    readonly HashSet<string> _noWords = [];

    public async Task<IReadOnlyList<VerseText>> GetAllVersesAsync(string translationId, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"/api/{translationId}/complete.json", ct);
        var result = new List<VerseText>();
        foreach (var book in doc.RootElement.GetProperty("books").EnumerateArray())
            foreach (var ch in book.GetProperty("chapters").EnumerateArray())
            {
                var chapter = ch.GetProperty("chapter");
                foreach (var v in ParseVerses(chapter))
                    result.Add(new VerseText(Str(book, "id"), chapter.GetProperty("number").GetInt32(), v.Number, v.Text));
            }
        return result;
    }

    static List<Verse> ParseVerses(JsonElement chapter, JsonDocument? words = null)
    {
        using var _ = words;
        var tagged = words?.RootElement.TryGetProperty("verses", out var wv) == true ? wv : (JsonElement?)null;
        var verses = new List<Verse>();
        foreach (var item in chapter.GetProperty("content").EnumerateArray())
        {
            if (Str(item, "type") != "verse") continue;
            var number = item.GetProperty("number").GetInt32();
            var tags = Tags(tagged, number);
            var segs = new List<Segment>();
            var index = 0;
            foreach (var part in item.GetProperty("content").EnumerateArray())
            {
                var partIndex = index++;
                var partTags = tags.Where(t => t.Index == partIndex).ToList();
                if (part.ValueKind == JsonValueKind.String) segs.AddRange(Split(part.GetString()!, false, partTags));
                else if (part.TryGetProperty("text", out var tx))
                    segs.AddRange(Split(tx.GetString()!, part.TryGetProperty("wordsOfJesus", out var w) && w.ValueKind == JsonValueKind.True, partTags));
            }
            verses.Add(new Verse(number, segs));
        }
        return verses;
    }

    record Tag(int Index, int Start, int End, string[] Strongs);

    static List<Tag> Tags(JsonElement? verses, int number)
    {
        if (verses is not { } v || !v.TryGetProperty(number.ToString(), out var list)) return [];
        return list.EnumerateArray()
            .Select(t => new Tag(t.GetProperty("contentIndex").GetInt32(), t.GetProperty("start").GetInt32(), t.GetProperty("end").GetInt32(),
                t.GetProperty("strongs").EnumerateArray().Select(s => s.GetString()!).ToArray()))
            .GroupBy(t => (t.Index, t.Start, t.End))
            .Select(g => new Tag(g.Key.Index, g.Key.Start, g.Key.End, g.SelectMany(t => t.Strongs).Distinct().ToArray()))
            .ToList();
    }

    /// <summary>Cuts a text into plain and Strong's-tagged segments (offsets are relative to the text).</summary>
    static IEnumerable<Segment> Split(string text, bool jesus, List<Tag> tags)
    {
        var pos = 0;
        foreach (var t in tags.OrderBy(t => t.Start))
        {
            if (t.Start < pos || t.End > text.Length || t.End <= t.Start) continue;
            if (t.Start > pos) yield return new Segment(text[pos..t.Start], jesus);
            yield return new Segment(text[t.Start..t.End], jesus, t.Strongs);
            pos = t.End;
        }
        if (pos < text.Length) yield return new Segment(text[pos..], jesus);
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        var file = Path.Combine(cacheDir, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(file)) return JsonDocument.Parse(await File.ReadAllBytesAsync(file, ct));
        var bytes = await http.GetByteArrayAsync(Base + path, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, bytes, ct);
        return JsonDocument.Parse(bytes);
    }
}
