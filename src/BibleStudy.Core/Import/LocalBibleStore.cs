using System.Globalization;
using System.Text.Json;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Import;

/// <summary>A Bible read from a file the user owns, before it is stored.</summary>
public sealed record ParsedVerse(string Book, int Chapter, int Verse, IReadOnlyList<Segment> Segments);
public sealed record ParsedBible(string Name, string ShortName, string LanguageCode, IReadOnlyList<ParsedVerse> Verses, IReadOnlyList<(string Book, int Chapter, Footnote Note)> Footnotes,
    IReadOnlyList<(string Book, int Chapter, int Verse, string Text)>? Headings = null);

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
            CREATE TABLE IF NOT EXISTS headings(translation TEXT, book TEXT, chapter INT, verse INT, ord INT, text TEXT, level INT);
            CREATE INDEX IF NOT EXISTS headings_pos ON headings(translation, book, chapter);
            """);
        // which import made a translation and which file it came from (an older database has neither)
        foreach (var column in new[] { "source TEXT", "importer INT" })
            try { Exec("ALTER TABLE translations ADD COLUMN " + column); } catch (SqliteException) { /* already there */ }
    }

    /// <summary>
    /// The version of the import code that made a stored translation. 2 = since 1.6.12 (better PDF layout, no markers from HTML, headings). Older copies
    /// (nothing stored) can be read again from their file for a better result. 3 = since 1.9.21: a PDF keeps the dash of a left-out part ("Schlacht- und Speisopfer");
    /// footnotes, whole psalm titles and no split words from a PDF, every paragraph of a verse from csv-bibel.de: only PDF and HTML copies of version 2 are read again.
    /// </summary>
    public const int CurrentImporter = 3;

    /// <summary>A stored translation that was made by an older import.</summary>
    public sealed record Outdated(string Id, string Name, string Language, string? Source);

    public List<Outdated> GetOutdated() => Query("SELECT id,name,language,source FROM translations WHERE COALESCE(importer,0) < 2 OR (COALESCE(importer,0) < $v AND (lower(COALESCE(source,'')) LIKE '%.pdf' OR lower(COALESCE(source,'')) LIKE '%.htm%')) ORDER BY name", c => c.Parameters.AddWithValue("$v", CurrentImporter),
        r => new Outdated(r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));

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
        var headings = Query("SELECT verse,text,level FROM headings WHERE translation=$t AND book=$b AND chapter=$c ORDER BY verse, ord", c =>
        {
            c.Parameters.AddWithValue("$t", translationId); c.Parameters.AddWithValue("$b", bookId); c.Parameters.AddWithValue("$c", chapter);
        }, r => new ChapterHeading(r.GetInt32(0), r.GetString(1), r.GetInt32(2)));
        // some files put the parallel passages of a section at the start of its first verse ("2Kö 20,12-19; 2Chr 32,25-26 Zu jener Zeit …"):
        // they become a footnote of the verse, so the verse reads as the text it is
        var allNotes = notes.ToList();
        for (var i = 0; i < verses.Count; i++)
        {
            var v = verses[i];
            if (v.Segments.Count == 0 || v.Segments[0].NoteId is not null || LeadingReferences(v.Segments[0].Text) is not { } split) continue;
            var id = 100_000 + v.Number;
            verses[i] = v with { Segments = [new Segment("", false, null, id), v.Segments[0] with { Text = split.Text }, .. v.Segments.Skip(1)] };
            allNotes.Add(new Footnote(id, v.Number, split.References));
        }
        return Task.FromResult(new Chapter(translationId, bookId, chapter, verses, allNotes, headings.Count > 0 ? headings : null));
    }

    /// <summary>
    /// Bible references at the very start of a verse, before its text ("2Kö 20,12-19; 2Chr 32,25-26 Zu jener Zeit"): the references and the rest.
    /// Null when the verse does not start so, or nothing but references would be left.
    /// </summary>
    public static (string References, string Text)? LeadingReferences(string text)
    {
        var pos = 0;
        foreach (var span in Models.DocumentLinks.Spans(text))
        {
            if (span.Index != pos) break;
            pos = span.Index + span.Length;
            var next = pos;
            while (next < text.Length && text[next] is ';' or ',' or ' ') next++;
            if (next == pos) break;
            pos = next;
        }
        if (pos == 0) return null;
        var refs = text[..pos].TrimEnd(' ', ';', ',');
        var rest = text[pos..].TrimStart();
        // the text of the verse has to follow: a word beginning with a capital letter (or a quotation mark)
        if (rest.Length == 0 || !(char.IsUpper(rest[0]) || rest[0] is '„' or '"' or '«' or '»' or '‚' or '(')) return null;
        return (refs, rest);
    }

    public async Task<IReadOnlyList<VerseText>> GetAllVersesAsync(string translationId, CancellationToken ct = default)
    {
        var rows = Query("SELECT book,chapter,verse,segs FROM verses WHERE translation=$t", c => c.Parameters.AddWithValue("$t", translationId),
            r => new VerseText(r.GetString(0), r.GetInt32(1), r.GetInt32(2), new Verse(r.GetInt32(2), FromJson(r.GetString(3))).Text))
            .Select(v => LeadingReferences(v.Text) is { } split ? v with { Text = split.Text } : v).ToList();   // parallel passages before a verse are no words of it (see GetChapterAsync)
        // Bible order, whatever order the imported file had (search hits and the concordance are listed in this order)
        var order = BookData.UsfmIds.Select((b, i) => (b, i)).ToDictionary(x => x.b, x => x.i);
        return await Task.FromResult<IReadOnlyList<VerseText>>(rows.OrderBy(v => order.GetValueOrDefault(v.BookId, 99)).ThenBy(v => v.Chapter).ThenBy(v => v.Number).ToList());
    }

    /// <summary>
    /// A stored translation exactly as it was saved (no parallel passages turned into footnotes, nothing added), in Bible order;
    /// null when there is none with this id. Saving the result again gives the same translation.
    /// </summary>
    public ParsedBible? Load(string translationId)
    {
        var head = Query("SELECT name,short_name,language FROM translations WHERE id=$i", c => c.Parameters.AddWithValue("$i", translationId),
            r => (Name: r.GetString(0), Short: r.IsDBNull(1) ? "" : r.GetString(1), Lang: r.IsDBNull(2) ? "" : r.GetString(2))).FirstOrDefault();
        if (head.Name is null) return null;
        var order = BookData.UsfmIds.Select((b, i) => (b, i)).ToDictionary(x => x.b, x => x.i);
        int Pos(string book) => order.GetValueOrDefault(book, 99);
        void Id(SqliteCommand c) => c.Parameters.AddWithValue("$i", translationId);
        var verses = Query("SELECT book,chapter,verse,segs FROM verses WHERE translation=$i", Id,
                r => new ParsedVerse(r.GetString(0), r.GetInt32(1), r.GetInt32(2), FromJson(r.GetString(3))))
            .OrderBy(v => Pos(v.Book)).ThenBy(v => v.Book, StringComparer.Ordinal).ThenBy(v => v.Chapter).ThenBy(v => v.Verse).ToList();
        var notes = Query("SELECT book,chapter,note_id,verse,text FROM notes WHERE translation=$i", Id,
                r => (Book: r.GetString(0), Chapter: r.GetInt32(1), Note: new Footnote(r.GetInt32(2), r.GetInt32(3), r.IsDBNull(4) ? "" : r.GetString(4))))
            .OrderBy(n => Pos(n.Book)).ThenBy(n => n.Book, StringComparer.Ordinal).ThenBy(n => n.Chapter).ThenBy(n => n.Note.NoteId).ToList();
        var headings = Query("SELECT book,chapter,verse,ord,text FROM headings WHERE translation=$i", Id,
                r => (Book: r.GetString(0), Chapter: r.GetInt32(1), Verse: r.GetInt32(2), Ord: r.GetInt32(3), Text: r.GetString(4)))
            .OrderBy(h => Pos(h.Book)).ThenBy(h => h.Book, StringComparer.Ordinal).ThenBy(h => h.Chapter).ThenBy(h => h.Verse).ThenBy(h => h.Ord)
            .Select(h => (h.Book, h.Chapter, h.Verse, h.Text)).ToList();
        return new ParsedBible(head.Name, head.Short, head.Lang, verses, notes, headings.Count > 0 ? headings : null);
    }

    static string ToJson(IReadOnlyList<Segment> segments) =>
        JsonSerializer.Serialize(segments.Select(s => new SegmentDto(s.NoteId is null ? s.Text : null, s.Strongs?.ToArray(), s.NoteId, s.WordsOfJesus ? true : null)));

    static List<Segment> FromJson(string json) =>
        JsonSerializer.Deserialize<List<SegmentDto>>(json)!.Select(d => new Segment(d.T ?? "", d.J == true, d.S, d.N)).ToList();

    /// <summary>Stores a parsed Bible (replacing one with the same id) and returns its translation id.</summary>
    public string Save(ParsedBible bible, string? sourcePath = null)
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
        Run("DELETE FROM verses WHERE translation=$i", ("$i", id)); Run("DELETE FROM notes WHERE translation=$i", ("$i", id)); Run("DELETE FROM headings WHERE translation=$i", ("$i", id));
        Run("INSERT OR REPLACE INTO translations(id,name,short_name,language,imported,source,importer) VALUES($i,$n,$s,$l,$d,COALESCE($f,(SELECT source FROM translations WHERE id=$i)),$v)",
            ("$i", id), ("$n", bible.Name), ("$s", bible.ShortName), ("$l", bible.LanguageCode), ("$d", DateTime.UtcNow.ToString("o")), ("$f", (object?)sourcePath ?? DBNull.Value), ("$v", CurrentImporter));
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
            foreach (var (book, chapter, note) in bible.Footnotes) { pb.Value = book; pc.Value = chapter; pn.Value = note.NoteId; pv.Value = note.Verse; px.Value = note.Text ?? ""; ins.ExecuteNonQuery(); }
        }
        if (bible.Headings is { Count: > 0 })
        {
            using var ins = con.CreateCommand();
            ins.CommandText = "INSERT INTO headings VALUES($t,$b,$c,$v,$o,$x,2)";
            ins.Parameters.AddWithValue("$t", id);
            var pb = ins.Parameters.Add("$b", SqliteType.Text); var pc = ins.Parameters.Add("$c", SqliteType.Integer); var pv = ins.Parameters.Add("$v", SqliteType.Integer);
            var po = ins.Parameters.Add("$o", SqliteType.Integer); var px = ins.Parameters.Add("$x", SqliteType.Text);
            var order = 0;
            foreach (var (book, chapter, verse, text) in bible.Headings) { pb.Value = book; pc.Value = chapter; pv.Value = verse; po.Value = order++; px.Value = text; ins.ExecuteNonQuery(); }
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

    static readonly (string Table, string Column)[] OwnTables = [("translations", "id"), ("verses", "translation"), ("notes", "translation"), ("headings", "translation")];

    /// <summary>Writes these translations into a small file of their own, to be taken along. The path of the source file stays here.</summary>
    public void CopyTo(IEnumerable<string> ids, string path)
    {
        _ = new LocalBibleStore(path);
        using var con = Open();
        void Do(string sql, string? id = null) { using var c = con.CreateCommand(); c.CommandText = sql; if (sql.Contains("$p")) c.Parameters.AddWithValue("$p", path); if (id is not null) c.Parameters.AddWithValue("$i", id); c.ExecuteNonQuery(); }
        Do("ATTACH DATABASE $p AS t");
        using (var tx = con.BeginTransaction())
        {
            foreach (var id in ids)
                foreach (var (table, column) in OwnTables) Do($"INSERT INTO t.{table} SELECT * FROM main.{table} WHERE {column}=$i", id);
            Do("UPDATE t.translations SET source=NULL");
            tx.Commit();
        }
        Do("DETACH DATABASE t");
    }

    /// <summary>Adds the translations of such a file that are not here yet (by id or by name); returns the ids that were added.</summary>
    public List<string> AddFrom(string path)
    {
        var there = new LocalBibleStore(path).Query("SELECT id,name FROM translations", _ => { }, r => (Id: r.GetString(0), Name: r.GetString(1)));
        var here = Query("SELECT id,name FROM translations", _ => { }, r => (Id: r.GetString(0), Name: r.GetString(1)));
        var added = there.Where(x => Owns(x.Id) && here.All(h => h.Id != x.Id && h.Name != x.Name)).Select(x => x.Id).ToList();
        using var con = Open();
        void Do(string sql, string? id = null) { using var c = con.CreateCommand(); c.CommandText = sql; if (sql.Contains("$p")) c.Parameters.AddWithValue("$p", path); if (id is not null) c.Parameters.AddWithValue("$i", id); c.ExecuteNonQuery(); }
        Do("ATTACH DATABASE $p AS t");
        using (var tx = con.BeginTransaction())
        {
            foreach (var id in added)
                foreach (var (table, column) in OwnTables) Do($"INSERT INTO main.{table} SELECT * FROM t.{table} WHERE {column}=$i", id);
            tx.Commit();
        }
        Do("DETACH DATABASE t");
        return added;
    }

    public void Remove(string translationId)
    {
        Exec("DELETE FROM verses WHERE translation=$i", c => c.Parameters.AddWithValue("$i", translationId));
        Exec("DELETE FROM notes WHERE translation=$i", c => c.Parameters.AddWithValue("$i", translationId));
        Exec("DELETE FROM headings WHERE translation=$i", c => c.Parameters.AddWithValue("$i", translationId));
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
        if (Overlay is not null && Overlay.Has(translationId)) result = Overlay.Apply(result);
        if (JesusFromReference?.Invoke() == true && Providers.JesusTransfer.Wanted(result)) result = await WithJesusFromReferenceAsync(result, ct);
        return result;
    }

    /// <summary>When it gives true, a chapter that does not mark the words of Jesus gets them estimated from the KJV (see JesusTransfer).</summary>
    public Func<bool>? JesusFromReference { get; set; }

    async Task<Chapter> WithJesusFromReferenceAsync(Chapter chapter, CancellationToken ct)
    {
        try
        {
            // the KJV chapter comes from the disk cache after the first time; offline and not cached: the chapter stays as it is
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var reference = await web.GetChapterAsync(Providers.JesusTransfer.ReferenceId, chapter.BookId, chapter.Number, timeout.Token);
            return Providers.JesusTransfer.Apply(chapter, reference);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            ct.ThrowIfCancellationRequested();
            return chapter;
        }
    }
    public Task<IReadOnlyList<VerseText>> GetAllVersesAsync(string translationId, CancellationToken ct = default) => For(translationId).GetAllVersesAsync(translationId, ct);
}
