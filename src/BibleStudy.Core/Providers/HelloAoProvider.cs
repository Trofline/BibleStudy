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
        var verses = new List<Verse>();
        foreach (var item in doc.RootElement.GetProperty("chapter").GetProperty("content").EnumerateArray())
        {
            if (Str(item, "type") != "verse") continue;
            var segs = new List<Segment>();
            foreach (var part in item.GetProperty("content").EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String) segs.Add(new Segment(part.GetString()!));
                else if (part.TryGetProperty("text", out var tx))
                    segs.Add(new Segment(tx.GetString()!, part.TryGetProperty("wordsOfJesus", out var w) && w.ValueKind == JsonValueKind.True));
            }
            verses.Add(new Verse(item.GetProperty("number").GetInt32(), segs));
        }
        return new Chapter(translationId, bookId, chapter, verses);
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
