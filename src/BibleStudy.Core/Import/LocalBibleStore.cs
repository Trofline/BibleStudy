using System.Globalization;
using System.Text.Json;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Import;

/// <summary>A Bible read from a file the user owns, before it is stored.</summary>
public sealed record ParsedVerse(string Book, int Chapter, int Verse, IReadOnlyList<Segment> Segments);
public sealed record ParsedBible(string Name, string ShortName, string LanguageCode, IReadOnlyList<ParsedVerse> Verses, IReadOnlyList<(string Book, int Chapter, Footnote Note)> Footnotes);

/// <summary>
/// Translations imported from the user's own files (private use). Stored only on this computer in one SQLite file.
/// Acts as a Bible provider for translation ids starting with "local:".
/// </summary>
public sealed class LocalBibleStore : SqliteDb, IBibleProvider
{
    public const string Prefix = "local:";
    public string Name => L("Lokal importiert");

    sealed record SegmentDto(string? T, string[]? S, int? N, bool? J);

    public LocalBibleStore(string dbPath) : base(dbPath)
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS translations(id TEXT PRIMARY KEY, name TEXT, short_name TEXT, language TEXT, imported TEXT);
            CREATE TABLE IF NOT EXISTS verses(translation TEXT, book TEXT, chapter INT, verse INT, segs TEXT, PRIMARY KEY(translation,book,chapter,verse));
            CREATE TABLE IF NOT EXISTS notes(translation TEXT, book TEXT, chapter INT, note_id INT, verse INT, text TEXT);
            CREATE INDEX IF NOT EXISTS notes_pos ON notes(translation, book, chapter);
            """);
    }

    public static bool Owns(string translationId) => translationId.StartsWith(Prefix, StringComparison.Ordinal);

    public Task<IReadOnlyList<TranslationInfo>> GetTranslationsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<TranslationInfo> list = Query("SELECT id,name,short_name,language FROM translations ORDER BY name", _ => { }, r =>
        {
            var lang = r.GetString(3);
            var culture = SafeCulture(lang);
            var license = new LicenseInfo("-", "-", "-", false, false);
            return new TranslationInfo(r.GetString(0), r.GetString(1), r.GetString(1), r.GetString(2), lang, culture?.EnglishName ?? lang, "ltr",
                L("lokaler Import, nur private Nutzung"), null, license, 66);
        });
        return Task.FromResult(list);
    }

    static CultureInfo? SafeCulture(string code)
    {
        try { return CultureInfo.GetCultureInfo(code); } catch (CultureNotFoundException) { return null; }
    }

    public async Task<IReadOnlyList<BookInfo>> GetBooksAsync(string translationId, CancellationToken ct = default)
    {
        var lang = Query("SELECT language FROM translations WHERE id=$i", c => c.Parameters.AddWithValue("$i", translationId), r => r.GetString(0)).FirstOrDefault() ?? "en";
        var books = Query("SELECT book,MAX(chapter) FROM verses WHERE translation=$i GROUP BY book", c => c.Parameters.AddWithValue("$i", translationId), r => (Id: r.GetString(0), Chapters: r.GetInt32(1)));
        return await Task.FromResult<IReadOnlyList<BookInfo>>(books.OrderBy(b => Array.IndexOf(BookData.UsfmIds, b.Id)).Select(b =>
        {
            var name = BookData.NameOf(b.Id, lang);
            return new BookInfo(b.Id, name, name, Array.IndexOf(BookData.UsfmIds, b.Id) + 1, b.Chapters);
        }).ToList());
    }

    public Task<Chapter> GetChapterAsync(string translationId, string bookId, int chapter, CancellationToken ct = default)
    {
        var verses = Query("SELECT verse,segs FROM verses WHERE translation=$t AND book=$b AND chapter=$c ORDER BY verse", c =>
        {
            c.Parameters.AddWithValue("$t", translationId); c.Parameters.AddWithValue("$b", bookId); c.Parameters.AddWithValue("$c", chapter);
        }, r => new Verse(r.GetInt32(0), FromJson(r.GetString(1))));
        var notes = Query("SELECT note_id,verse,text FROM notes WHERE translation=$t AND book=$b AND chapter=$c ORDER BY note_id", c =>
        {
            c.Parameters.AddWithValue("$t", translationId); c.Parameters.AddWithValue("$b", bookId); c.Parameters.AddWithValue("$c", chapter);
        }, r => new Footnote(r.GetInt32(0), r.GetInt32(1), r.GetString(2)));
        return Task.FromResult(new Chapter(translationId, bookId, chapter, verses, notes));
    }

    public async Task<IReadOnlyList<VerseText>> GetAllVersesAsync(string translationId, CancellationToken ct = default)
    {
        var rows = Query("SELECT book,chapter,verse,segs FROM verses WHERE translation=$t", c => c.Parameters.AddWithValue("$t", translationId),
            r => new VerseText(r.GetString(0), r.GetInt32(1), r.GetInt32(2), new Verse(r.GetInt32(2), FromJson(r.GetString(3))).Text));
        // Bible order, whatever order the imported file had (search hits and the concordance are listed in this order)
        var order = BookData.UsfmIds.Select((b, i) => (b, i)).ToDictionary(x => x.b, x => x.i);
        return await Task.FromResult<IReadOnlyList<VerseText>>(rows.OrderBy(v => order.GetValueOrDefault(v.BookId, 99)).ThenBy(v => v.Chapter).ThenBy(v => v.Number).ToList());
    }

    static string ToJson(IReadOnlyList<Segment> segments) =>
        JsonSerializer.Serialize(segments.Select(s => new SegmentDto(s.NoteId is null ? s.Text : null, s.Strongs?.ToArray(), s.NoteId, s.WordsOfJesus ? true : null)));

    static List<Segment> FromJson(string json) =>
        JsonSerializer.Deserialize<List<SegmentDto>>(json)!.Select(d => new Segment(d.T ?? "", d.J == true, d.S, d.N)).ToList();

    /// <summary>Stores a parsed Bible (replacing one with the same id) and returns its translation id.</summary>
    public string Save(ParsedBible bible)
    {
        var id = IdFor(bible);
        using var con = Open();
        using var tx = con.BeginTransaction();
        void Run(string sql, params (string, object)[] args)
        {
            using var cmd = con.CreateCommand(); cmd.CommandText = sql;
            foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
            cmd.ExecuteNonQuery();
        }
        Run("DELETE FROM verses WHERE translation=$i", ("$i", id)); Run("DELETE FROM notes WHERE translation=$i", ("$i", id));
        Run("INSERT OR REPLACE INTO translations VALUES($i,$n,$s,$l,$d)", ("$i", id), ("$n", bible.Name), ("$s", bible.ShortName), ("$l", bible.LanguageCode), ("$d", DateTime.UtcNow.ToString("o")));
        using (var ins = con.CreateCommand())
        {
            ins.CommandText = "INSERT OR REPLACE INTO verses VALUES($t,$b,$c,$v,$s)";
            ins.Parameters.AddWithValue("$t", id);
            var pb = ins.Parameters.Add("$b", SqliteType.Text); var pc = ins.Parameters.Add("$c", SqliteType.Integer);
            var pv = ins.Parameters.Add("$v", SqliteType.Integer); var ps = ins.Parameters.Add("$s", SqliteType.Text);
            foreach (var v in bible.Verses) { pb.Value = v.Book; pc.Value = v.Chapter; pv.Value = v.Verse; ps.Value = ToJson(v.Segments); ins.ExecuteNonQuery(); }
        }
        using (var ins = con.CreateCommand())
        {
            ins.CommandText = "INSERT INTO notes VALUES($t,$b,$c,$n,$v,$x)";
            ins.Parameters.AddWithValue("$t", id);
            var pb = ins.Parameters.Add("$b", SqliteType.Text); var pc = ins.Parameters.Add("$c", SqliteType.Integer); var pn = ins.Parameters.Add("$n", SqliteType.Integer);
            var pv = ins.Parameters.Add("$v", SqliteType.Integer); var px = ins.Parameters.Add("$x", SqliteType.Text);
            foreach (var (book, chapter, note) in bible.Footnotes) { pb.Value = book; pc.Value = chapter; pn.Value = note.NoteId; pv.Value = note.Verse; px.Value = note.Text; ins.ExecuteNonQuery(); }
        }
        tx.Commit();
        return id;
    }

    /// <summary>
    /// The same Bible imported again (same name) replaces its earlier import; a different Bible never does, even when the names
    /// start alike ("Schlachter2000-AT" and "Schlachter2000-NT" once shared their 12-letter short name and overwrote each other).
    /// </summary>
    string IdFor(ParsedBible bible)
    {
        var same = Query("SELECT id FROM translations WHERE name=$n", c => c.Parameters.AddWithValue("$n", bible.Name), r => r.GetString(0)).FirstOrDefault();
        if (same is not null) return same;
        var taken = Query("SELECT id FROM translations", _ => { }, r => r.GetString(0)).ToHashSet();
        static string Slug(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).Take(40).ToArray());
        // the short name first (as before), the full name when that is taken by another Bible, then a number
        foreach (var slug in new[] { Slug(bible.ShortName), Slug(bible.Name) })
            if (slug.Length > 0 && !taken.Contains(Prefix + slug)) return Prefix + slug;
        var basis = Prefix + (Slug(bible.Name) is { Length: > 0 } s ? s : "bible");
        var id = basis;
        for (var n = 2; taken.Contains(id); n++) id = basis + n;
        return id;
    }

    public void Remove(string translationId)
    {
        Exec("DELETE FROM verses WHERE translation=$i", c => c.Parameters.AddWithValue("$i", translationId));
        Exec("DELETE FROM notes WHERE translation=$i", c => c.Parameters.AddWithValue("$i", translationId));
        Exec("DELETE FROM translations WHERE id=$i", c => c.Parameters.AddWithValue("$i", translationId));
    }
}

/// <summary>Combines the web provider and the locally imported translations behind one interface.</summary>
public sealed class CompositeProvider(IBibleProvider web, LocalBibleStore local, IBibleProvider? plugins = null) : IBibleProvider
{
    public string Name => web.Name;

    /// <summary>Strong's numbers added afterwards to translations that have none (see StrongsOverlay).</summary>
    public StrongsOverlay? Overlay { get; set; }

    IBibleProvider For(string id) => LocalBibleStore.Owns(id) ? local : plugins is not null && Plugins.PluginBibleProvider.Owns(id) ? plugins : web;

    public async Task<IReadOnlyList<TranslationInfo>> GetTranslationsAsync(CancellationToken ct = default)
    {
        var mine = (await local.GetTranslationsAsync(ct)).ToList();
        if (plugins is not null) mine.AddRange(await plugins.GetTranslationsAsync(ct));
        try { return [.. mine, .. await web.GetTranslationsAsync(ct)]; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && mine.Count > 0) { return mine; } // offline or timed out: imported translations still work
    }

    public Task<IReadOnlyList<BookInfo>> GetBooksAsync(string translationId, CancellationToken ct = default) => For(translationId).GetBooksAsync(translationId, ct);
    public async Task<Chapter> GetChapterAsync(string translationId, string bookId, int chapter, CancellationToken ct = default)
    {
        var result = await For(translationId).GetChapterAsync(translationId, bookId, chapter, ct);
        return Overlay is not null && Overlay.Has(translationId) ? Overlay.Apply(result) : result;
    }
    public Task<IReadOnlyList<VerseText>> GetAllVersesAsync(string translationId, CancellationToken ct = default) => For(translationId).GetAllVersesAsync(translationId, ct);
}
