using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Storage;

public sealed record Note(VerseRef Ref, string Text, long? FolderId = null);
public sealed record Folder(long Id, string Name)
{
    public override string ToString() => Name;
}
public sealed record QueueItem(long Id, VerseRef Ref, string Label);
/// <summary>A note or linked file of the knowledge base. Notes have text; linked files have a path (and their text, if it could be read).</summary>
public sealed record Document(long Id, string Title, string Body, string? Path, bool IsTemplate, DateTime Updated, IReadOnlyList<string> Tags, long? FolderId = null);
public sealed record MyXref(long Id, VerseRef From, VerseRef To, int EndVerse, string Note);
/// <summary>A printed cross reference the user has judged wrong and hidden for good (it is hidden in both directions).</summary>
public sealed record HiddenXref(long Id, VerseRef From, VerseRef To);
/// <summary>One line of the checklist. A sub-task has the id of its task in ParentId; Pos is the place among its siblings.</summary>
public sealed record TodoItem(long Id, string Text, bool Done, VerseRef? Ref, long? ParentId = null, DateOnly? Due = null, long Pos = 0);
public sealed record Prayer(long Id, string Text, VerseRef? Ref, DateTime Created, DateTime? Answered, string Note);

/// <summary>A highlighted word range (Start/End are offsets into the verse text of that translation) or a whole verse (Start = -1).</summary>
/// <summary>A verse in the plain "Merkliste" (bookmark list): the place and the text as it was when saved.</summary>
public sealed record Bookmark(long Id, VerseRef Ref, string Text, string Created);

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
            CREATE TABLE IF NOT EXISTS documents(id INTEGER PRIMARY KEY, title TEXT NOT NULL, body TEXT, path TEXT, template INT, created TEXT, updated TEXT);
            CREATE TABLE IF NOT EXISTS doc_tags(doc_id INT, tag TEXT, PRIMARY KEY(doc_id, tag));
            CREATE TABLE IF NOT EXISTS todos(id INTEGER PRIMARY KEY, text TEXT, done INT, book TEXT, chapter INT, verse INT, created TEXT);
            CREATE TABLE IF NOT EXISTS my_xrefs(id INTEGER PRIMARY KEY, fbook TEXT, fchapter INT, fverse INT, tbook TEXT, tchapter INT, tverse INT, tend INT, note TEXT, created TEXT);
            CREATE TABLE IF NOT EXISTS hidden_xrefs(id INTEGER PRIMARY KEY, fbook TEXT, fchapter INT, fverse INT, tbook TEXT, tchapter INT, tverse INT, hidden TEXT);
            CREATE TABLE IF NOT EXISTS bookmarks(id INTEGER PRIMARY KEY, book TEXT, chapter INT, verse INT, text TEXT, created TEXT, UNIQUE(book, chapter, verse));
            CREATE TABLE IF NOT EXISTS last_position(id INTEGER PRIMARY KEY CHECK(id=1), book TEXT, chapter INT);
            CREATE TABLE IF NOT EXISTS prayers(id INTEGER PRIMARY KEY, text TEXT, book TEXT, chapter INT, verse INT, created TEXT, answered TEXT, note TEXT);
            """);
        // documents can live in the same folders as marks (added later; older vaults are migrated here)
        // notes can be put into folders as well (older vaults are migrated here)
        if (Query("SELECT 1 FROM pragma_table_info('notes') WHERE name='folder_id'", _ => { }, _ => 1).Count == 0)
            Exec("ALTER TABLE notes ADD COLUMN folder_id INT");
        if (Query("SELECT 1 FROM pragma_table_info('documents') WHERE name='folder_id'", _ => { }, _ => 1).Count == 0)
            Exec("ALTER TABLE documents ADD COLUMN folder_id INT");
        // the checklist got sub-tasks, a due date and an order of its own
        foreach (var (column, type) in new[] { ("parent_id", "INT"), ("due", "TEXT"), ("pos", "INT") })
            if (Query($"SELECT 1 FROM pragma_table_info('todos') WHERE name='{column}'", _ => { }, _ => 1).Count == 0)
                Exec($"ALTER TABLE todos ADD COLUMN {column} {type}");
    }

    // ---- reading progress -------------------------------------------------------------------------------------------

    /// <summary>Where the user last read in this vault (shown as "continue" on the start page); null in a vault never read in.</summary>
    public (string Book, int Chapter)? GetLastPosition() =>
        Query("SELECT book,chapter FROM last_position WHERE id=1", _ => { }, r => (r.GetString(0), r.GetInt32(1))).Cast<(string, int)?>().FirstOrDefault();

    public void SetLastPosition(string book, int chapter) =>
        Exec("INSERT OR REPLACE INTO last_position(id,book,chapter) VALUES(1,$b,$c)", c => { c.Parameters.AddWithValue("$b", book); c.Parameters.AddWithValue("$c", chapter); });

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
        Query("SELECT book,chapter,verse,text,folder_id FROM notes ORDER BY book,chapter,verse", _ => { },
            r => new Note(new VerseRef(r.GetString(0), r.GetInt32(1), r.GetInt32(2)), r.GetString(3), r.IsDBNull(4) ? null : r.GetInt64(4)));

    /// <summary>Saves the note; empty text deletes it. The folder of an existing note stays as it is.</summary>
    public void SetNote(VerseRef v, string text)
    {
        void P(SqliteCommand c) { c.Parameters.AddWithValue("$b", v.Book); c.Parameters.AddWithValue("$c", v.Chapter); c.Parameters.AddWithValue("$v", v.Verse); }
        if (string.IsNullOrWhiteSpace(text)) { Exec("DELETE FROM notes WHERE book=$b AND chapter=$c AND verse=$v", P); return; }
        Exec("INSERT INTO notes(book,chapter,verse,text,updated) VALUES($b,$c,$v,$x,$t) ON CONFLICT(book,chapter,verse) DO UPDATE SET text=excluded.text, updated=excluded.updated",
            c => { P(c); c.Parameters.AddWithValue("$x", text); c.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o")); });
    }

    /// <summary>Puts a note into a folder (null = out of every folder).</summary>
    public void SetNoteFolder(VerseRef v, long? folderId) =>
        Exec("UPDATE notes SET folder_id=$f WHERE book=$b AND chapter=$c AND verse=$v", c =>
        {
            c.Parameters.AddWithValue("$f", (object?)folderId ?? DBNull.Value);
            c.Parameters.AddWithValue("$b", v.Book); c.Parameters.AddWithValue("$c", v.Chapter); c.Parameters.AddWithValue("$v", v.Verse);
        });

    // ---- folders ----------------------------------------------------------------------------------------------------

    public List<Folder> GetFolders() => Query("SELECT id,name FROM folders ORDER BY name COLLATE NOCASE", _ => { }, r => new Folder(r.GetInt64(0), r.GetString(1)));

    public long AddFolder(string name)
    {
        using var con = Open(); using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO folders(name) VALUES($n); SELECT last_insert_rowid()";
        cmd.Parameters.AddWithValue("$n", name.Trim());
        return (long)cmd.ExecuteScalar()!;
    }

    public void RenameFolder(long id, string name) =>
        Exec("UPDATE folders SET name=$n WHERE id=$i", c => { c.Parameters.AddWithValue("$n", name.Trim()); c.Parameters.AddWithValue("$i", id); });

    /// <summary>Moves a document into a folder (null = out of every folder) without touching anything else.</summary>
    public void SetDocumentFolder(long documentId, long? folderId) =>
        Exec("UPDATE documents SET folder_id=$f WHERE id=$i", c => { c.Parameters.AddWithValue("$f", (object?)folderId ?? DBNull.Value); c.Parameters.AddWithValue("$i", documentId); });

    /// <summary>Deletes the folder; its marks and documents are kept (without folder).</summary>
    public void DeleteFolder(long id)
    {
        Exec("UPDATE marks SET folder_id=NULL WHERE folder_id=$i", c => c.Parameters.AddWithValue("$i", id));
        Exec("UPDATE documents SET folder_id=NULL WHERE folder_id=$i", c => c.Parameters.AddWithValue("$i", id));
        Exec("UPDATE notes SET folder_id=NULL WHERE folder_id=$i", c => c.Parameters.AddWithValue("$i", id));
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

    // ---- own cross references ---------------------------------------------------------------------------------------

    /// <summary>A cross reference the user wrote: from a verse to a verse or passage (To … EndVerse), with an optional note.</summary>
    public List<MyXref> GetMyXrefs(string? book = null, int? chapter = null) =>
        Query("SELECT id,fbook,fchapter,fverse,tbook,tchapter,tverse,tend,note FROM my_xrefs WHERE ($b IS NULL OR (fbook=$b AND fchapter=$c) OR (tbook=$b AND tchapter=$c)) ORDER BY id",
            c => { c.Parameters.AddWithValue("$b", (object?)book ?? DBNull.Value); c.Parameters.AddWithValue("$c", (object?)chapter ?? DBNull.Value); },
            r => new MyXref(r.GetInt64(0), new VerseRef(r.GetString(1), r.GetInt32(2), r.GetInt32(3)), new VerseRef(r.GetString(4), r.GetInt32(5), r.GetInt32(6)), r.GetInt32(7), r.IsDBNull(8) ? "" : r.GetString(8)));

    public void AddMyXref(VerseRef from, VerseRef to, int endVerse, string note)
    {
        if (GetMyXrefs(from.Book, from.Chapter).Any(x => x.From == from && x.To == to && x.EndVerse == endVerse)) return;
        Exec("INSERT INTO my_xrefs(fbook,fchapter,fverse,tbook,tchapter,tverse,tend,note,created) VALUES($fb,$fc,$fv,$tb,$tc,$tv,$te,$n,$t)", c =>
        {
            c.Parameters.AddWithValue("$fb", from.Book); c.Parameters.AddWithValue("$fc", from.Chapter); c.Parameters.AddWithValue("$fv", from.Verse);
            c.Parameters.AddWithValue("$tb", to.Book); c.Parameters.AddWithValue("$tc", to.Chapter); c.Parameters.AddWithValue("$tv", to.Verse);
            c.Parameters.AddWithValue("$te", Math.Max(to.Verse, endVerse)); c.Parameters.AddWithValue("$n", note); c.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        });
    }

    public void DeleteMyXref(long id) => Exec("DELETE FROM my_xrefs WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));

    // ---- hidden cross references ------------------------------------------------------------------------------------

    public List<HiddenXref> GetHiddenXrefs() =>
        Query("SELECT id,fbook,fchapter,fverse,tbook,tchapter,tverse FROM hidden_xrefs ORDER BY id", _ => { },
            r => new HiddenXref(r.GetInt64(0), new VerseRef(r.GetString(1), r.GetInt32(2), r.GetInt32(3)), new VerseRef(r.GetString(4), r.GetInt32(5), r.GetInt32(6))));

    /// <summary>Hides the pair for good (once; the other direction counts as the same pair).</summary>
    public void HideXref(VerseRef from, VerseRef to)
    {
        if (GetHiddenXrefs().Any(h => (h.From == from && h.To == to) || (h.From == to && h.To == from))) return;
        Exec("INSERT INTO hidden_xrefs(fbook,fchapter,fverse,tbook,tchapter,tverse,hidden) VALUES($fb,$fc,$fv,$tb,$tc,$tv,$t)", c =>
        {
            c.Parameters.AddWithValue("$fb", from.Book); c.Parameters.AddWithValue("$fc", from.Chapter); c.Parameters.AddWithValue("$fv", from.Verse);
            c.Parameters.AddWithValue("$tb", to.Book); c.Parameters.AddWithValue("$tc", to.Chapter); c.Parameters.AddWithValue("$tv", to.Verse);
            c.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        });
    }

    public void UnhideXref(long id) => Exec("DELETE FROM hidden_xrefs WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));

    // ---- bookmarks: a plain list of verses to jump back to (no notes, colors or folders) -----------------------------------

    public List<Bookmark> GetBookmarks() =>
        Query("SELECT id,book,chapter,verse,text,created FROM bookmarks ORDER BY id DESC", _ => { },
            r => new Bookmark(r.GetInt64(0), new VerseRef(r.GetString(1), r.GetInt32(2), r.GetInt32(3)), r.IsDBNull(4) ? "" : r.GetString(4), r.IsDBNull(5) ? "" : r.GetString(5)));

    public HashSet<int> GetBookmarkedVerses(string book, int chapter) =>
        Query("SELECT verse FROM bookmarks WHERE book=$b AND chapter=$c", c => { c.Parameters.AddWithValue("$b", book); c.Parameters.AddWithValue("$c", chapter); }, r => r.GetInt32(0)).ToHashSet();

    /// <summary>Bookmarks the verse, or removes the bookmark if it is there already. Returns true if it is bookmarked now.</summary>
    public bool ToggleBookmark(VerseRef v, string text)
    {
        var exists = Query("SELECT 1 FROM bookmarks WHERE book=$b AND chapter=$c AND verse=$v", c => Bind(c, v), _ => 1).Count > 0;
        if (exists) { Exec("DELETE FROM bookmarks WHERE book=$b AND chapter=$c AND verse=$v", c => Bind(c, v)); return false; }
        Exec("INSERT INTO bookmarks(book,chapter,verse,text,created) VALUES($b,$c,$v,$t,$d)", c => { Bind(c, v); c.Parameters.AddWithValue("$t", text); c.Parameters.AddWithValue("$d", DateTime.UtcNow.ToString("o")); });
        return true;
    }

    static void Bind(SqliteCommand c, VerseRef v) { c.Parameters.AddWithValue("$b", v.Book); c.Parameters.AddWithValue("$c", v.Chapter); c.Parameters.AddWithValue("$v", v.Verse); }

    public void RemoveBookmark(long id) => Exec("DELETE FROM bookmarks WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));

    // ---- delete everything in this vault ---------------------------------------------------------------------------------------

    /// <summary>Empties the vault completely (notes, highlights, folders, documents, lists, prayers, reading progress). Cannot be undone.</summary>
    public void Wipe()
    {
        foreach (var table in new[] { "notes", "read_chapters", "folders", "marks", "mark_tags", "queue", "documents", "doc_tags", "todos", "my_xrefs", "hidden_xrefs", "prayers", "bookmarks", "last_position" })
            Exec($"DELETE FROM {table}");
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

    /// <summary>All lines in the order the user put them (older vaults have no order yet: then the order of creation counts).</summary>
    public List<TodoItem> GetTodos() =>
        Query("SELECT id,text,done,book,chapter,verse,parent_id,due,COALESCE(pos,id) FROM todos ORDER BY COALESCE(pos,id), id", _ => { },
            r => new TodoItem(r.GetInt64(0), r.GetString(1), r.GetInt32(2) != 0, r.IsDBNull(3) ? null : new VerseRef(r.GetString(3), r.GetInt32(4), r.GetInt32(5)),
                r.IsDBNull(6) ? null : r.GetInt64(6), !r.IsDBNull(7) && DateOnly.TryParseExact(r.GetString(7), "yyyy-MM-dd", out var due) ? due : null, r.GetInt64(8)));

    /// <summary>Adds a line at the end of its list (or of the sub-tasks of <paramref name="parentId"/>).</summary>
    public void AddTodo(string text, VerseRef? r, long? parentId = null, DateOnly? due = null) =>
        Exec("INSERT INTO todos(text,done,book,chapter,verse,created,parent_id,due,pos) VALUES($x,0,$b,$c,$v,$t,$p,$u,(SELECT COALESCE(MAX(COALESCE(pos,id)),0)+1 FROM todos))", c =>
        {
            c.Parameters.AddWithValue("$x", text.Trim());
            c.Parameters.AddWithValue("$b", r is { } v ? v.Book : DBNull.Value); c.Parameters.AddWithValue("$c", r is { } v2 ? v2.Chapter : DBNull.Value);
            c.Parameters.AddWithValue("$v", r is { } v3 ? v3.Verse : DBNull.Value); c.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            c.Parameters.AddWithValue("$p", (object?)parentId ?? DBNull.Value);
            c.Parameters.AddWithValue("$u", due is { } d ? d.ToString("yyyy-MM-dd") : DBNull.Value);
        });

    /// <summary>Ticking a task ticks its sub-tasks too; taking the tick away leaves them as they are.</summary>
    public void SetTodoDone(long id, bool done) =>
        Exec("UPDATE todos SET done=$d WHERE id=$i OR (parent_id=$i AND $d=1)", c => { c.Parameters.AddWithValue("$d", done ? 1 : 0); c.Parameters.AddWithValue("$i", id); });

    public void SetTodoText(long id, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Exec("UPDATE todos SET text=$x WHERE id=$i", c => { c.Parameters.AddWithValue("$x", text.Trim()); c.Parameters.AddWithValue("$i", id); });
    }

    public void SetTodoDue(long id, DateOnly? due) =>
        Exec("UPDATE todos SET due=$u WHERE id=$i", c => { c.Parameters.AddWithValue("$u", due is { } d ? d.ToString("yyyy-MM-dd") : DBNull.Value); c.Parameters.AddWithValue("$i", id); });

    public void SetTodoRef(long id, VerseRef r) =>
        Exec("UPDATE todos SET book=$b,chapter=$c,verse=$v WHERE id=$i", c =>
        {
            c.Parameters.AddWithValue("$b", r.Book); c.Parameters.AddWithValue("$c", r.Chapter); c.Parameters.AddWithValue("$v", r.Verse); c.Parameters.AddWithValue("$i", id);
        });

    /// <summary>Moves a line one place up (-1) or down (+1) among the lines that are shown together with it (same parent, same state).</summary>
    public void MoveTodo(long id, int direction)
    {
        var all = GetTodos();
        if (all.FirstOrDefault(t => t.Id == id) is not { } me) return;
        var siblings = all.Where(t => t.ParentId == me.ParentId && t.Done == me.Done).ToList();
        var i = siblings.FindIndex(t => t.Id == id); var j = i + Math.Sign(direction);
        if (i < 0 || j < 0 || j >= siblings.Count) return;
        var places = siblings.Select(t => t.Pos).Order().ToList();   // the same set of places, handed out in the new order
        (siblings[i], siblings[j]) = (siblings[j], siblings[i]);
        using var con = Open(); using var tx = con.BeginTransaction(); using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE todos SET pos=$p WHERE id=$i";
        var pp = cmd.Parameters.Add("$p", SqliteType.Integer); var pi = cmd.Parameters.Add("$i", SqliteType.Integer);
        for (var k = 0; k < siblings.Count; k++) { pp.Value = places[k]; pi.Value = siblings[k].Id; cmd.ExecuteNonQuery(); }
        tx.Commit();
    }

    /// <summary>Deletes a line together with its sub-tasks.</summary>
    public void DeleteTodo(long id) => Exec("DELETE FROM todos WHERE id=$i OR parent_id=$i", c => c.Parameters.AddWithValue("$i", id));

    /// <summary>Removes everything that is ticked (and the sub-tasks of ticked tasks).</summary>
    public void ClearDoneTodos() => Exec("DELETE FROM todos WHERE done=1 OR parent_id IN (SELECT id FROM todos WHERE done=1)");


    // ---- prayer list --------------------------------------------------------------------------------------------------

    public List<Prayer> GetPrayers() =>
        Query("SELECT id,text,book,chapter,verse,created,answered,note FROM prayers ORDER BY (answered IS NOT NULL), id DESC", _ => { },
            r => new Prayer(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : new VerseRef(r.GetString(2), r.GetInt32(3), r.GetInt32(4)),
                DateTime.TryParse(r.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind, out var c) ? c.ToLocalTime() : DateTime.Now,
                r.IsDBNull(6) || !DateTime.TryParse(r.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind, out var a) ? null : a.ToLocalTime(),
                r.IsDBNull(7) ? "" : r.GetString(7)));

    public void AddPrayer(string text, VerseRef? r) =>
        Exec("INSERT INTO prayers(text,book,chapter,verse,created) VALUES($x,$b,$c,$v,$t)", c =>
        {
            c.Parameters.AddWithValue("$x", text.Trim());
            c.Parameters.AddWithValue("$b", r is { } v ? v.Book : DBNull.Value); c.Parameters.AddWithValue("$c", r is { } v2 ? v2.Chapter : DBNull.Value);
            c.Parameters.AddWithValue("$v", r is { } v3 ? v3.Verse : DBNull.Value); c.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        });

    public void SetPrayerAnswered(long id, bool answered, string note = "") =>
        Exec("UPDATE prayers SET answered=$a, note=$n WHERE id=$i", c =>
        {
            c.Parameters.AddWithValue("$a", answered ? DateTime.UtcNow.ToString("o") : DBNull.Value);
            c.Parameters.AddWithValue("$n", note); c.Parameters.AddWithValue("$i", id);
        });

    public void DeletePrayer(long id) => Exec("DELETE FROM prayers WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));

    /// <summary>Writes a consistent copy of the whole vault to a new file (which must not exist yet).</summary>
    public void BackupTo(string path) => Exec("VACUUM INTO $p", c => c.Parameters.AddWithValue("$p", path));

    // ---- documents (knowledge base) -----------------------------------------------------------------------------------

    public long AddDocument(string title, string body, string? path, bool template, IEnumerable<string> tags, long? folderId = null)
    {
        using var con = Open();
        using var tx = con.BeginTransaction();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO documents(title,body,path,template,created,updated,folder_id) VALUES($t,$b,$p,$m,$n,$n,$f); SELECT last_insert_rowid()";
        cmd.Parameters.AddWithValue("$f", (object?)folderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", title.Trim()); cmd.Parameters.AddWithValue("$b", body);
        cmd.Parameters.AddWithValue("$p", (object?)path ?? DBNull.Value); cmd.Parameters.AddWithValue("$m", template ? 1 : 0);
        cmd.Parameters.AddWithValue("$n", DateTime.UtcNow.ToString("o"));
        var id = (long)cmd.ExecuteScalar()!;
        SetDocTags(con, id, tags);
        tx.Commit();
        return id;
    }

    public void UpdateDocument(long id, string title, string body, string? path, bool template, IEnumerable<string> tags, long? folderId = null)
    {
        using var con = Open();
        using var tx = con.BeginTransaction();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "UPDATE documents SET title=$t, body=$b, path=$p, template=$m, updated=$n, folder_id=$f WHERE id=$i";
            cmd.Parameters.AddWithValue("$f", (object?)folderId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", title.Trim()); cmd.Parameters.AddWithValue("$b", body);
            cmd.Parameters.AddWithValue("$p", (object?)path ?? DBNull.Value); cmd.Parameters.AddWithValue("$m", template ? 1 : 0);
            cmd.Parameters.AddWithValue("$n", DateTime.UtcNow.ToString("o")); cmd.Parameters.AddWithValue("$i", id);
            cmd.ExecuteNonQuery();
        }
        SetDocTags(con, id, tags);
        tx.Commit();
    }

    public void DeleteDocument(long id)
    {
        Exec("DELETE FROM doc_tags WHERE doc_id=$i", c => c.Parameters.AddWithValue("$i", id));
        Exec("DELETE FROM documents WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));
    }

    /// <summary>Documents, newest change first; <paramref name="text"/> searches title, body and tags.</summary>
    public List<Document> GetDocuments(string? text = null, string? tag = null, bool? templates = null)
    {
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(text)) where.Add("(title LIKE $q ESCAPE '\\' OR body LIKE $q ESCAPE '\\' OR id IN (SELECT doc_id FROM doc_tags WHERE tag LIKE $q ESCAPE '\\'))");
        if (tag is not null) where.Add("id IN (SELECT doc_id FROM doc_tags WHERE tag=$g)");
        if (templates is not null) where.Add("template=$m");
        var tags = Query("SELECT doc_id,tag FROM doc_tags ORDER BY tag", _ => { }, r => (Id: r.GetInt64(0), Tag: r.GetString(1))).ToLookup(x => x.Id, x => x.Tag);
        return Query($"SELECT id,title,body,path,template,updated,folder_id FROM documents {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")} ORDER BY updated DESC",
            c =>
            {
                if (!string.IsNullOrWhiteSpace(text)) c.Parameters.AddWithValue("$q", "%" + text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
                if (tag is not null) c.Parameters.AddWithValue("$g", tag);
                if (templates is not null) c.Parameters.AddWithValue("$m", templates.Value ? 1 : 0);
            },
            r => new Document(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetInt32(4) != 0,
                DateTime.TryParse(r.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue, tags[r.GetInt64(0)].ToList(), r.IsDBNull(6) ? null : r.GetInt64(6)));
    }

    public Document? FindDocument(string title) =>
        GetDocuments().FirstOrDefault(d => string.Equals(d.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));

    static void SetDocTags(SqliteConnection con, long id, IEnumerable<string> tags)
    {
        using (var del = con.CreateCommand()) { del.CommandText = "DELETE FROM doc_tags WHERE doc_id=$i"; del.Parameters.AddWithValue("$i", id); del.ExecuteNonQuery(); }
        foreach (var tag in tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var ins = con.CreateCommand();
            ins.CommandText = "INSERT OR IGNORE INTO doc_tags VALUES($i,$t)";
            ins.Parameters.AddWithValue("$i", id); ins.Parameters.AddWithValue("$t", tag);
            ins.ExecuteNonQuery();
        }
    }

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
