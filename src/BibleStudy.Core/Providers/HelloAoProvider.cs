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
        var node = doc.RootElement.GetProperty("chapter");
        return new Chapter(translationId, bookId, chapter, ParseVerses(node, await TryGetWordsAsync(translationId, bookId, chapter, ct)), ParseFootnotes(node));
    }

    static List<Footnote> ParseFootnotes(JsonElement chapter)
    {
        if (!chapter.TryGetProperty("footnotes", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        return list.EnumerateArray().Select(f => new Footnote(
            f.GetProperty("noteId").GetInt32(),
            f.TryGetProperty("reference", out var r) && r.TryGetProperty("verse", out var v) ? v.GetInt32() : 0,
            Str(f, "text"))).ToList();
    }

    public const string CrossRefAttribution = "Querverweise © OpenBible.info, CC BY 4.0 (openbible.info/labs/cross-references)";

    /// <summary>Cross references of one verse, best first (dataset "open-cross-ref").</summary>
    public async Task<IReadOnlyList<CrossRef>> GetCrossReferencesAsync(string book, int chapter, int verse, CancellationToken ct = default) =>
        (await GetChapterCrossRefsAsync(book, chapter, ct)).GetValueOrDefault(verse) ?? [];

    /// <summary>Cross references of all verses of a chapter, by verse number, best first.</summary>
    public async Task<Dictionary<int, List<CrossRef>>> GetChapterCrossRefsAsync(string book, int chapter, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"/api/d/open-cross-ref/{book}/{chapter}.json", ct);
        var result = new Dictionary<int, List<CrossRef>>();
        foreach (var item in doc.RootElement.GetProperty("chapter").GetProperty("content").EnumerateArray())
            result[item.GetProperty("verse").GetInt32()] = item.GetProperty("references").EnumerateArray().Select(r =>
            {
                var v = r.GetProperty("verse").GetInt32();
                return new CrossRef(new VerseRef(Str(r, "book"), r.GetProperty("chapter").GetInt32(), v),
                    r.TryGetProperty("endVerse", out var e) ? e.GetInt32() : v, r.TryGetProperty("score", out var s) ? s.GetInt32() : 0);
            }).OrderByDescending(r => r.Score).ToList();
        return result;
    }

    /// <summary>
    /// Original-language words of a chapter by verse (dataset "bsb-interlinear"): Greek for the New Testament, Hebrew/Aramaic for the Old.
    /// </summary>
    public async Task<Dictionary<int, List<InterlinearWord>>> GetInterlinearAsync(string book, int chapter, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"/api/d/bsb-interlinear/{book}/{chapter}.json", ct);
        var result = new Dictionary<int, List<InterlinearWord>>();
        foreach (var verse in doc.RootElement.GetProperty("chapter").GetProperty("content").EnumerateArray())
        {
            if (!verse.TryGetProperty("words", out var words)) continue;
            result[verse.GetProperty("verse").GetInt32()] = words.EnumerateArray().Select(w => new InterlinearWord(
                w.TryGetProperty("pos", out var p) ? p.GetInt32() : 0, Str(w, "language"), Str(w, "text"), Str(w, "transliteration"),
                Str(w, "parsing"), Str(w, "parsingFull"), Str(w, "strongs"), Str(w, "gloss"))).OrderBy(w => w.Position).ToList();
        }
        return result;
    }

    public const string InterlinearAttribution = "Interlinear: Berean Standard Bible Interlinear (bereanbible.com), gemeinfrei";

    public const string PlacesAttribution = "Orte: Theographic Bible Metadata (CC BY-SA 4.0)";

    /// <summary>Places with coordinates mentioned in a chapter, in order of their first verse.</summary>
    public async Task<IReadOnlyList<MapPlace>> GetChapterPlacesAsync(string book, int chapter, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"/api/d/theographic/{book}/{chapter}.json", ct);
        if (!doc.RootElement.GetProperty("chapter").TryGetProperty("places", out var places)) return [];
        return places.EnumerateArray()
            .Where(p => p.TryGetProperty("latitude", out var lat) && lat.ValueKind == JsonValueKind.Number && p.TryGetProperty("longitude", out var lon) && lon.ValueKind == JsonValueKind.Number)
            .Select(p => new MapPlace(Str(p, "id"), Str(p, "name"), Str(p, "featureType"), p.GetProperty("latitude").GetDouble(), p.GetProperty("longitude").GetDouble(),
                p.TryGetProperty("verses", out var v) ? v.EnumerateArray().Select(x => x.GetInt32()).ToList() : []))
            .OrderBy(p => p.Verses.DefaultIfEmpty(int.MaxValue).Min()).ToList();
    }

    /// <summary>Books of the cross-reference dataset with their chapter counts, in Bible order.</summary>
    public async Task<IReadOnlyList<(string Book, int Chapters)>> GetCrossRefBooksAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("/api/d/open-cross-ref/books.json", ct);
        return doc.RootElement.GetProperty("books").EnumerateArray().OrderBy(b => b.GetProperty("order").GetInt32())
            .Select(b => (Str(b, "id"), b.GetProperty("numberOfChapters").GetInt32())).ToList();
    }

    /// <summary>Per-word Strong's tags; only some translations have them, so a miss is remembered per session.</summary>
    async Task<JsonDocument?> TryGetWordsAsync(string translationId, string bookId, int chapter, CancellationToken ct)
    {
        lock (_noWords) if (_noWords.Contains(translationId)) return null;
        try { return await GetJsonAsync($"/api/{translationId}/{bookId}/{chapter}.words.json", ct); }
        catch (HttpRequestException) { lock (_noWords) _noWords.Add(translationId); return null; }
    }

    readonly HashSet<string> _noWords = [];

    /// <summary>False once a chapter of the translation was loaded and had no Strong's tags (so word study cannot work there).</summary>
    public bool MayHaveStrongs(string translationId) { lock (_noWords) return !_noWords.Contains(translationId); }

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
                else if (part.TryGetProperty("noteId", out var note)) segs.Add(new Segment("", false, null, note.GetInt32()));
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

    // ---- library: commentaries and the Bible dictionary (Theographic, based on Easton's Bible Dictionary) ------------------

    public const string LexiconAttribution = "Lexikon: Theographic Bible Metadata (CC BY-SA 4.0), Texte aus Easton's Bible Dictionary (1897, gemeinfrei)";

    public async Task<IReadOnlyList<CommentaryInfo>> GetCommentariesAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("/api/available_commentaries.json", ct);
        return doc.RootElement.GetProperty("commentaries").EnumerateArray()
            .Select(c => new CommentaryInfo(Str(c, "id"), Str(c, "name"), Str(c, "licenseUrl"), Str(c, "languageName"))).ToList();
    }

    /// <summary>Comments of one chapter: introduction (book introduction for chapter 1) and the notes per verse. Null when the work has no notes here.</summary>
    public async Task<CommentaryChapter?> GetCommentaryAsync(string commentaryId, string book, int chapter, CancellationToken ct = default)
    {
        JsonDocument doc;
        try { doc = await GetJsonAsync($"/api/c/{commentaryId}/{book}/{chapter}.json", ct); }
        catch (HttpRequestException) { return null; }
        using (doc)
        {
            var ch = doc.RootElement.GetProperty("chapter");
            var intro = Str(ch, "introduction");
            if (chapter == 1 && doc.RootElement.TryGetProperty("book", out var b) && Str(b, "introduction") is { Length: > 0 } bookIntro) intro = (bookIntro + "\n\n" + intro).Trim();
            var notes = new List<CommentaryNote>();
            foreach (var item in ch.GetProperty("content").EnumerateArray())
            {
                if (Str(item, "type") != "verse" || !item.TryGetProperty("number", out var n)) continue;
                var text = string.Join("\n\n", item.GetProperty("content").EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
                if (text.Length > 0) notes.Add(new CommentaryNote(n.GetInt32(), text));
            }
            return new CommentaryChapter(intro, notes);
        }
    }

    /// <summary>All people and places of the dictionary (names only; details are loaded per entry).</summary>
    public async Task<IReadOnlyList<LexiconItem>> GetLexiconIndexAsync(CancellationToken ct = default)
    {
        var list = new List<LexiconItem>();
        using (var people = await GetJsonAsync("/api/d/theographic/people.json", ct))
            list.AddRange(people.RootElement.GetProperty("people").EnumerateArray().Select(p => new LexiconItem("people", Str(p, "id"), Str(p, "name"), p.TryGetProperty("numberOfReferences", out var r) ? r.GetInt32() : 0)));
        using (var places = await GetJsonAsync("/api/d/theographic/places.json", ct))
            list.AddRange(places.RootElement.GetProperty("places").EnumerateArray().Select(p => new LexiconItem("places", Str(p, "id"), Str(p, "name"), p.TryGetProperty("numberOfReferences", out var r) ? r.GetInt32() : 0)));
        return list;
    }

    public async Task<LexiconEntry?> GetLexiconEntryAsync(string kind, string id, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"/api/d/theographic/{kind}/{id}.json", ct);
        var e = doc.RootElement.GetProperty(kind == "people" ? "person" : "place");
        static List<string> Strings(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : [];
        var refs = e.TryGetProperty("references", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Select(x => new VerseRef(Str(x, "book"), x.GetProperty("chapter").GetInt32(), x.GetProperty("verse").GetInt32())).ToList() : [];
        var related = new List<LexiconItem>();
        foreach (var rel in new[] { "father", "mother", "children", "siblings", "partners", "parents" })
            if (e.TryGetProperty(rel, out var arr) && arr.ValueKind == JsonValueKind.Array)
                related.AddRange(arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => new LexiconItem(Str(x, "type") is { Length: > 0 } t ? t : "people", Str(x, "id"), Str(x, "name"), 0)));
        return new LexiconEntry(kind, id, Str(e, "name"), Strings(e, "alsoCalled"), Strings(e, "description"), refs, related, Str(e, "featureType"));
    }

    /// <summary>People and places mentioned in a chapter.</summary>
    public async Task<IReadOnlyList<(LexiconItem Item, IReadOnlyList<int> Verses)>> GetChapterLexiconAsync(string book, int chapter, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"/api/d/theographic/{book}/{chapter}.json", ct);
        var ch = doc.RootElement.GetProperty("chapter");
        var result = new List<(LexiconItem, IReadOnlyList<int>)>();
        foreach (var kind in new[] { "people", "places" })
            if (ch.TryGetProperty(kind, out var arr))
                foreach (var p in arr.EnumerateArray())
                    result.Add((new LexiconItem(kind, Str(p, "id"), Str(p, "name"), 0), p.TryGetProperty("verses", out var v) ? v.EnumerateArray().Select(x => x.GetInt32()).ToList() : []));
        return result;
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
