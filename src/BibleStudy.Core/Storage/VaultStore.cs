using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Storage;

public sealed record Note(VerseRef Ref, string Text);
public sealed record Folder(long Id, string Name)
{
    public override string ToString() => Name;
}
public sealed record QueueItem(long Id, VerseRef Ref, string Label);
public sealed record TodoItem(long Id, string Text, bool Done, VerseRef? Ref);

/// <summary>A highlighted word range (Start/End are offsets into the verse text of that translation) or a whole verse (Start = -1).</summary>
public sealed record Mark(long Id, string TranslationId, VerseRef Ref, int Start, int End, string Text, string Color, string Note,
    long? FolderId, IReadOnlyList<string> Tags)
{
    public const string AnyTranslation = "*";
    public bool WholeVerse => Start < 0;
}

/// <summary>
/// Personal data of one vault: reading progress, notes, highlights (with colors, tags, folders), queue and to-dos.
/// Every vault is one SQLite file, so a vault can be copied, backed up or shared as a single file.
/// </summary>
public sealed class VaultStore : SqliteDb
{
    public string Name { get; }

    public VaultStore(string dbPath, string name) : base(dbPath)
    {
        Name = name;
        Exec("""
            CREATE TABLE IF NOT EXISTS notes(book TEXT, chapter INT, verse INT, text TEXT, updated TEXT, PRIMARY KEY(book,chapter,verse));
            CREATE TABLE IF NOT EXISTS read_chapters(book TEXT, chapter INT, read_at TEXT, PRIMARY KEY(book,chapter));
            CREATE TABLE IF NOT EXISTS folders(id INTEGER PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS marks(id INTEGER PRIMARY KEY, translation TEXT, book TEXT, chapter INT, verse INT,
                start_off INT, end_off INT, text TEXT, color TEXT, note TEXT, folder_id INT, created TEXT);
            CREATE INDEX IF NOT EXISTS marks_pos ON marks(book, chapter);
            CREATE TABLE IF NOT EXISTS mark_tags(mark_id INT, tag TEXT, PRIMARY KEY(mark_id, tag));
            CREATE TABLE IF NOT EXISTS queue(id INTEGER PRIMARY KEY, book TEXT, chapter INT, verse INT, label TEXT);
            CREATE TABLE IF NOT EXISTS todos(id INTEGER PRIMARY KEY, text TEXT, done INT, book TEXT, chapter INT, verse INT, created TEXT);
            """);
    }

    // ---- reading progress -------------------------------------------------------------------------------------------

    public HashSet<(string Book, int Chapter)> GetReadChapters() =>
        Query("SELECT book,chapter FROM read_chapters", _ => { }, r => (r.GetString(0), r.GetInt32(1))).ToHashSet();

    /// <summary>Marks chapters of one book as read (or unread) in one transaction.</summary>
    public void SetRead(string book, IEnumerable<int> chapters, bool read)
    {
        using var con = Open();
        using var tx = con.BeginTransaction();
        using var cmd = con.CreateCommand();
        cmd.CommandText = read ? "INSERT OR IGNORE INTO read_chapters VALUES($b,$c,$t)" : "DELETE FROM read_chapters WHERE book=$b AND chapter=$c";
        cmd.Parameters.AddWithValue("$b", book);
        var pc = cmd.Parameters.Add("$c", SqliteType.Integer);
        if (read) cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        foreach (var c in chapters) { pc.Value = c; cmd.ExecuteNonQuery(); }
        tx.Commit();
    }

    // ---- notes ------------------------------------------------------------------------------------------------------

    public List<Note> GetNotes() =>
        Query("SELECT book,chapter,verse,text FROM notes ORDER BY book,chapter,verse", _ => { },
            r => new Note(new VerseRef(r.GetString(0), r.GetInt32(1), r.GetInt32(2)), r.GetString(3)));

    /// <summary>Saves the note; empty text deletes it.</summary>
    public void SetNote(VerseRef v, string text)
    {
        void P(SqliteCommand c) { c.Parameters.AddWithValue("$b", v.Book); c.Parameters.AddWithValue("$c", v.Chapter); c.Parameters.AddWithValue("$v", v.Verse); }
        if (string.IsNullOrWhiteSpace(text)) { Exec("DELETE FROM notes WHERE book=$b AND chapter=$c AND verse=$v", P); return; }
        Exec("INSERT OR REPLACE INTO notes VALUES($b,$c,$v,$x,$t)", c => { P(c); c.Parameters.AddWithValue("$x", text); c.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o")); });
    }

    // ---- folders ----------------------------------------------------------------------------------------------------

    public List<Folder> GetFolders() => Query("SELECT id,name FROM folders ORDER BY name COLLATE NOCASE", _ => { }, r => new Folder(r.GetInt64(0), r.GetString(1)));

    public long AddFolder(string name)
    {
        using var con = Open(); using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO folders(name) VALUES($n); SELECT last_insert_rowid()";
        cmd.Parameters.AddWithValue("$n", name.Trim());
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Deletes the folder; its marks are kept (without folder).</summary>
    public void DeleteFolder(long id)
    {
        Exec("UPDATE marks SET folder_id=NULL WHERE folder_id=$i", c => c.Parameters.AddWithValue("$i", id));
        Exec("DELETE FROM folders WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));
    }

    // ---- marks ------------------------------------------------------------------------------------------------------

    public long AddMark(string translationId, VerseRef r, int start, int end, string text, string color, string note, long? folderId, IEnumerable<string> tags)
    {
        using var con = Open();
        using var tx = con.BeginTransaction();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT INTO marks(translation,book,chapter,verse,start_off,end_off,text,color,note,folder_id,created)
            VALUES($tr,$b,$c,$v,$s,$e,$x,$col,$n,$f,$t); SELECT last_insert_rowid()
            """;
        cmd.Parameters.AddWithValue("$tr", translationId); cmd.Parameters.AddWithValue("$b", r.Book);
        cmd.Parameters.AddWithValue("$c", r.Chapter); cmd.Parameters.AddWithValue("$v", r.Verse);
        cmd.Parameters.AddWithValue("$s", start); cmd.Parameters.AddWithValue("$e", end);
        cmd.Parameters.AddWithValue("$x", text); cmd.Parameters.AddWithValue("$col", color); cmd.Parameters.AddWithValue("$n", note);
        cmd.Parameters.AddWithValue("$f", (object?)folderId ?? DBNull.Value); cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        var id = (long)cmd.ExecuteScalar()!;
        SetTags(con, id, tags);
        tx.Commit();
        return id;
    }

    public void UpdateMark(long id, string color, string note, long? folderId, IEnumerable<string> tags)
    {
        using var con = Open();
        using var tx = con.BeginTransaction();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "UPDATE marks SET color=$c, note=$n, folder_id=$f WHERE id=$i";
            cmd.Parameters.AddWithValue("$c", color); cmd.Parameters.AddWithValue("$n", note);
            cmd.Parameters.AddWithValue("$f", (object?)folderId ?? DBNull.Value); cmd.Parameters.AddWithValue("$i", id);
            cmd.ExecuteNonQuery();
        }
        SetTags(con, id, tags);
        tx.Commit();
    }

    public void DeleteMark(long id)
    {
        Exec("DELETE FROM mark_tags WHERE mark_id=$i", c => c.Parameters.AddWithValue("$i", id));
        Exec("DELETE FROM marks WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));
    }

    /// <summary>Marks filtered by chapter, folder (<paramref name="noFolder"/> = only unfiled) and/or tag; in Bible order.</summary>
    public List<Mark> GetMarks(string? book = null, int? chapter = null, long? folderId = null, bool noFolder = false, string? tag = null)
    {
        var where = new List<string>();
        if (book is not null) where.Add("book=$b");
        if (chapter is not null) where.Add("chapter=$c");
        if (folderId is not null) where.Add("folder_id=$f");
        if (noFolder) where.Add("folder_id IS NULL");
        if (tag is not null) where.Add("id IN (SELECT mark_id FROM mark_tags WHERE tag=$t)");
        var tags = Query("SELECT mark_id,tag FROM mark_tags ORDER BY tag", _ => { }, r => (Id: r.GetInt64(0), Tag: r.GetString(1)))
            .ToLookup(x => x.Id, x => x.Tag);
        return Query($"SELECT id,translation,book,chapter,verse,start_off,end_off,text,color,note,folder_id FROM marks {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")} ORDER BY rowid",
            c =>
            {
                if (book is not null) c.Parameters.AddWithValue("$b", book);
                if (chapter is not null) c.Parameters.AddWithValue("$c", chapter);
                if (folderId is not null) c.Parameters.AddWithValue("$f", folderId);
                if (tag is not null) c.Parameters.AddWithValue("$t", tag);
            },
            r => new Mark(r.GetInt64(0), r.GetString(1), new VerseRef(r.GetString(2), r.GetInt32(3), r.GetInt32(4)), r.GetInt32(5), r.GetInt32(6),
                r.GetString(7), r.GetString(8), r.GetString(9), r.IsDBNull(10) ? null : r.GetInt64(10), tags[r.GetInt64(0)].ToList()));
    }

    public List<(string Tag, int Count)> GetTags() =>
        Query("SELECT tag, COUNT(*) FROM mark_tags GROUP BY tag ORDER BY tag COLLATE NOCASE", _ => { }, r => (r.GetString(0), r.GetInt32(1)));

    static void SetTags(SqliteConnection con, long markId, IEnumerable<string> tags)
    {
        using (var del = con.CreateCommand())
        {
            del.CommandText = "DELETE FROM mark_tags WHERE mark_id=$i";
            del.Parameters.AddWithValue("$i", markId);
            del.ExecuteNonQuery();
        }
        foreach (var tag in tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var ins = con.CreateCommand();
            ins.CommandText = "INSERT OR IGNORE INTO mark_tags VALUES($i,$t)";
            ins.Parameters.AddWithValue("$i", markId); ins.Parameters.AddWithValue("$t", tag);
            ins.ExecuteNonQuery();
        }
    }

    // ---- queue ------------------------------------------------------------------------------------------------------

    public List<QueueItem> GetQueue() =>
        Query("SELECT id,book,chapter,verse,label FROM queue ORDER BY id", _ => { },
            r => new QueueItem(r.GetInt64(0), new VerseRef(r.GetString(1), r.GetInt32(2), r.GetInt32(3)), r.GetString(4)));

    public void AddToQueue(IEnumerable<(VerseRef Ref, string Label)> items)
    {
        using var con = Open();
        using var tx = con.BeginTransaction();
        foreach (var (r, label) in items)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "INSERT INTO queue(book,chapter,verse,label) VALUES($b,$c,$v,$l)";
            cmd.Parameters.AddWithValue("$b", r.Book); cmd.Parameters.AddWithValue("$c", r.Chapter);
            cmd.Parameters.AddWithValue("$v", r.Verse); cmd.Parameters.AddWithValue("$l", label);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void RemoveFromQueue(long id) => Exec("DELETE FROM queue WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));

    public void ClearQueue() => Exec("DELETE FROM queue");

    // ---- to-dos -----------------------------------------------------------------------------------------------------

    public List<TodoItem> GetTodos() =>
        Query("SELECT id,text,done,book,chapter,verse FROM todos ORDER BY done, id", _ => { },
            r => new TodoItem(r.GetInt64(0), r.GetString(1), r.GetInt32(2) != 0, r.IsDBNull(3) ? null : new VerseRef(r.GetString(3), r.GetInt32(4), r.GetInt32(5))));

    public void AddTodo(string text, VerseRef? r) =>
        Exec("INSERT INTO todos(text,done,book,chapter,verse,created) VALUES($x,0,$b,$c,$v,$t)", c =>
        {
            c.Parameters.AddWithValue("$x", text.Trim());
            c.Parameters.AddWithValue("$b", r is { } v ? v.Book : DBNull.Value); c.Parameters.AddWithValue("$c", r is { } v2 ? v2.Chapter : DBNull.Value);
            c.Parameters.AddWithValue("$v", r is { } v3 ? v3.Verse : DBNull.Value); c.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        });

    public void SetTodoDone(long id, bool done) =>
        Exec("UPDATE todos SET done=$d WHERE id=$i", c => { c.Parameters.AddWithValue("$d", done ? 1 : 0); c.Parameters.AddWithValue("$i", id); });

    public void DeleteTodo(long id) => Exec("DELETE FROM todos WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));

    // ---- migration --------------------------------------------------------------------------------------------------

    /// <summary>Takes over bookmarks (as whole-verse marks), notes and reading progress of the pre-vault database.</summary>
    public void ImportLegacy(string legacyDbPath, string bookmarkColor)
    {
        if (!File.Exists(legacyDbPath)) return;
        using var con = Open();
        using (var attach = con.CreateCommand()) { attach.CommandText = "ATTACH DATABASE $p AS old"; attach.Parameters.AddWithValue("$p", legacyDbPath); attach.ExecuteNonQuery(); }
        foreach (var sql in new[]
        {
            "INSERT OR IGNORE INTO notes SELECT book,chapter,verse,text,updated FROM old.notes",
            "INSERT OR IGNORE INTO read_chapters SELECT book,chapter,read_at FROM old.read_chapters",
            $"INSERT INTO marks(translation,book,chapter,verse,start_off,end_off,text,color,note,folder_id,created) SELECT '{Mark.AnyTranslation}',book,chapter,verse,-1,-1,'','{bookmarkColor}','',NULL,created FROM old.bookmarks",
        })
        {
            try { using var cmd = con.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
            catch (SqliteException) { /* table missing in a very old database: nothing to import */ }
        }
    }
}
