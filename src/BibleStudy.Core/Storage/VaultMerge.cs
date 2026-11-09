using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Storage;

/// <summary>What was brought into a vault by <see cref="VaultStore.MergeFrom"/>. Conflicts = places where both sides differ and both were kept.</summary>
public sealed record MergeResult(int Marks, int Notes, int Documents, int Others, int Conflicts);

/// <summary>What a vault holds, for the look at a file before it is imported.</summary>
public sealed record VaultSummary(int Marks, int Notes, int Documents, int Todos, IReadOnlyList<string> Translations);

public sealed partial class VaultStore
{
    // ---- where a mark lies in another translation, set by hand ----------------------------------------------------------

    public Dictionary<(long Mark, string Translation, int Verse), (int Start, int End)> GetMarkPlaces() =>
        Query("SELECT mark_id,translation,verse,start_off,end_off FROM mark_places", _ => { }, r => (Key: (r.GetInt64(0), r.GetString(1), r.GetInt32(2)), Value: (r.GetInt32(3), r.GetInt32(4))))
            .ToDictionary(x => x.Key, x => x.Value);

    public void SetMarkPlace(long markId, string translation, int verse, int start, int end) =>
        Exec("INSERT OR REPLACE INTO mark_places(mark_id,translation,verse,start_off,end_off) VALUES($m,$t,$v,$s,$e)", c =>
        {
            c.Parameters.AddWithValue("$m", markId); c.Parameters.AddWithValue("$t", translation); c.Parameters.AddWithValue("$v", verse);
            c.Parameters.AddWithValue("$s", start); c.Parameters.AddWithValue("$e", end);
        });

    // ---- a look at a vault ------------------------------------------------------------------------------------------------

    public VaultSummary Summary()
    {
        var marks = GetMarks();
        return new VaultSummary(marks.Count, GetNotes().Count, GetDocuments().Count, GetTodos().Count,
            marks.Where(m => !m.WholeVerse && m.TranslationId != Mark.AnyTranslation).Select(m => m.TranslationId).Distinct().Order().ToList());
    }

    static readonly string[] ContentTables = ["notes", "read_chapters", "folders", "marks", "mark_tags", "mark_places", "queue", "documents", "doc_tags", "todos", "my_xrefs", "hidden_xrefs", "bookmarks", "prayers", "headings"];

    /// <summary>A fingerprint of what the user wrote in this vault. Reading (the last position) and the time stamps of a save without a change do not count.</summary>
    public string Fingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var con = Open();
        foreach (var table in ContentTables)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {table} ORDER BY rowid";
            using var r = cmd.ExecuteReader();
            hash.AppendData(Encoding.UTF8.GetBytes("#" + table));
            while (r.Read())
                for (var i = 0; i < r.FieldCount; i++)
                    if (r.GetName(i) is not ("updated" or "created" or "read_at"))
                        hash.AppendData(Encoding.UTF8.GetBytes(r.GetValue(i) + "\u0001"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // ---- bringing the work of another copy into this vault ----------------------------------------------------------------

    /// <summary>
    /// Two texts of the same place: the longer one when one contains the other, else both, one after the other. Nothing that was written is lost.
    /// </summary>
    public static string MergeText(string mine, string theirs, out bool conflict)
    {
        conflict = false;
        var a = mine.Trim(); var b = theirs.Trim();
        if (b.Length == 0 || a == b || a.Contains(b, StringComparison.Ordinal)) return mine;
        if (a.Length == 0 || b.Contains(a, StringComparison.Ordinal)) return theirs;
        conflict = true;
        return a + "\n\n" + b;
    }

    // links carry the number of a document of the other copy: without it they find the document by its title, and get their number here
    static string Unnumbered(string text) => Regex.Replace(text, @"\[\[#\d+:", "[[");

    /// <summary>
    /// Adds what another copy of a vault holds and this one does not. Nothing is deleted or overwritten here: where both differ, both are kept
    /// (a note gets both texts, a document a second copy with <paramref name="copySuffix"/> in its title). What was deleted in the other copy stays here.
    /// </summary>
    public MergeResult MergeFrom(string otherDbPath, string copySuffix)
    {
        var other = new VaultStore(otherDbPath, "merge");
        int marks = 0, notes = 0, documents = 0, others = 0, conflicts = 0;

        // folders: the same name at the same place of the tree is the same folder
        var mineFolders = GetFolders();
        var theirFolders = other.GetFolders().ToDictionary(f => f.Id);
        var folderMap = new Dictionary<long, long>();
        long MapFolder(Folder f)
        {
            if (folderMap.TryGetValue(f.Id, out var known)) return known;
            long? parent = f.ParentId is long p && theirFolders.TryGetValue(p, out var pf) ? MapFolder(pf) : null;
            var hit = mineFolders.FirstOrDefault(m => m.ParentId == parent && string.Equals(m.Name, f.Name, StringComparison.OrdinalIgnoreCase));
            long id;
            if (hit is not null) id = hit.Id;
            else
            {
                id = AddFolder(f.Name);
                if (parent is not null) SetFolderParent(id, parent);
                mineFolders.Add(new Folder(id, f.Name.Trim(), parent));
            }
            return folderMap[f.Id] = id;
        }
        foreach (var f in theirFolders.Values) MapFolder(f);
        long? MappedFolder(long? id) => id is long x && folderMap.TryGetValue(x, out var m) ? m : null;

        // marks: the same words of the same verses are the same mark (a whole verse is the same in every translation)
        static string Key(Mark m) => $"{(m.WholeVerse ? "" : m.TranslationId)}|{m.Ref.Book}|{m.Ref.Chapter}|{m.Ref.Verse}|{m.LastVerse}|{m.Start}|{m.End}";
        var mineMarks = new Dictionary<string, Mark>();
        foreach (var m in GetMarks()) mineMarks.TryAdd(Key(m), m);
        var markMap = new Dictionary<long, long>();
        foreach (var o in other.GetMarks())
        {
            if (mineMarks.TryGetValue(Key(o), out var hit))
            {
                var note = MergeText(hit.Note, o.Note, out var c); if (c) conflicts++;
                var tags = hit.Tags.Union(o.Tags).ToList();
                var folder = hit.FolderId ?? MappedFolder(o.FolderId);
                if (note != hit.Note || tags.Count != hit.Tags.Count || folder != hit.FolderId) UpdateMark(hit.Id, hit.Color, note, folder, tags);
                markMap[o.Id] = hit.Id;
            }
            else
            {
                markMap[o.Id] = AddMark(o.TranslationId, o.Ref, o.Start, o.End, o.Text, o.Color, o.Note, MappedFolder(o.FolderId), o.Tags, o.EndVerse);
                marks++;
            }
        }
        var places = GetMarkPlaces();
        foreach (var ((mark, translation, verse), at) in other.GetMarkPlaces())
            if (markMap.TryGetValue(mark, out var id) && !places.ContainsKey((id, translation, verse))) SetMarkPlace(id, translation, verse, at.Start, at.End);

        // notes
        var mineNotes = GetNotes().ToDictionary(n => n.Ref);
        foreach (var o in other.GetNotes())
        {
            var theirs = Unnumbered(o.Text);
            if (!mineNotes.TryGetValue(o.Ref, out var hit)) { SetNote(o.Ref, theirs); if (MappedFolder(o.FolderId) is { } f) SetNoteFolder(o.Ref, f); notes++; continue; }
            var mine = Unnumbered(hit.Text);
            var merged = MergeText(mine, theirs, out var c); if (c) conflicts++;
            if (merged != mine) { SetNote(o.Ref, merged); notes++; }
        }

        // documents
        var mineDocs = GetDocuments();
        foreach (var o in other.GetDocuments())
        {
            var body = Unnumbered(o.Body);
            bool Same(Document d) => Unnumbered(d.Body).Trim() == body.Trim();
            var hit = mineDocs.FirstOrDefault(d => string.Equals(d.Title, o.Title, StringComparison.OrdinalIgnoreCase));
            if (hit is not null && Same(hit)) continue;
            var title = o.Title;
            if (hit is not null)
            {
                title = $"{o.Title} ({copySuffix})";
                if (mineDocs.Any(d => string.Equals(d.Title, title, StringComparison.OrdinalIgnoreCase) && Same(d))) continue;   // brought in earlier
                conflicts++;
            }
            AddDocument(title, body, o.Path, o.IsTemplate, o.Tags, MappedFolder(o.FolderId));
            documents++;
        }

        // the plain lists: what is not here yet
        var theirTodos = other.GetTodos();
        var mineTodos = GetTodos();
        var byId = theirTodos.ToDictionary(t => t.Id);
        using (var con = Open())
        {
            using (var attach = con.CreateCommand()) { attach.CommandText = "ATTACH DATABASE $p AS g"; attach.Parameters.AddWithValue("$p", otherDbPath); attach.ExecuteNonQuery(); }
            using (var tx = con.BeginTransaction())
            {
                foreach (var sql in new[]
                {
                    "INSERT OR IGNORE INTO main.bookmarks(book,chapter,verse,text,created) SELECT book,chapter,verse,text,created FROM g.bookmarks",
                    "INSERT INTO main.queue(book,chapter,verse,label) SELECT book,chapter,verse,label FROM g.queue o WHERE NOT EXISTS(SELECT 1 FROM main.queue m WHERE m.book IS o.book AND m.chapter IS o.chapter AND m.verse IS o.verse)",
                    "INSERT INTO main.headings(book,chapter,verse,text,level,created) SELECT book,chapter,verse,text,level,created FROM g.headings o WHERE NOT EXISTS(SELECT 1 FROM main.headings m WHERE m.book IS o.book AND m.chapter IS o.chapter AND m.verse IS o.verse AND m.text IS o.text)",
                    "INSERT INTO main.my_xrefs(fbook,fchapter,fverse,tbook,tchapter,tverse,tend,note,created) SELECT fbook,fchapter,fverse,tbook,tchapter,tverse,tend,note,created FROM g.my_xrefs o WHERE NOT EXISTS(SELECT 1 FROM main.my_xrefs m WHERE m.fbook IS o.fbook AND m.fchapter IS o.fchapter AND m.fverse IS o.fverse AND m.tbook IS o.tbook AND m.tchapter IS o.tchapter AND m.tverse IS o.tverse)",
                    "INSERT INTO main.hidden_xrefs(fbook,fchapter,fverse,tbook,tchapter,tverse,hidden) SELECT fbook,fchapter,fverse,tbook,tchapter,tverse,hidden FROM g.hidden_xrefs o WHERE NOT EXISTS(SELECT 1 FROM main.hidden_xrefs m WHERE m.fbook IS o.fbook AND m.fchapter IS o.fchapter AND m.fverse IS o.fverse AND m.tbook IS o.tbook AND m.tchapter IS o.tchapter AND m.tverse IS o.tverse)",
                    "INSERT INTO main.prayers(text,book,chapter,verse,created,answered,note) SELECT text,book,chapter,verse,created,answered,note FROM g.prayers o WHERE NOT EXISTS(SELECT 1 FROM main.prayers m WHERE m.text IS o.text AND m.created IS o.created)",
                })
                {
                    using var cmd = con.CreateCommand(); cmd.CommandText = sql; others += cmd.ExecuteNonQuery();
                }

                // to-dos: a sub-task goes under the same task here; ticking off counts, an open one does not untick
                var todoMap = new Dictionary<long, long>();
                long MapTodo(TodoItem t)
                {
                    if (todoMap.TryGetValue(t.Id, out var known)) return known;
                    long? parent = t.ParentId is long p && byId.TryGetValue(p, out var pt) ? MapTodo(pt) : null;
                    long id;
                    if (mineTodos.FirstOrDefault(m => m.ParentId == parent && m.Text == t.Text) is { } hit)
                    {
                        id = hit.Id;
                        if (t.Done && !hit.Done) { using var up = con.CreateCommand(); up.CommandText = "UPDATE main.todos SET done=1 WHERE id=$i"; up.Parameters.AddWithValue("$i", id); up.ExecuteNonQuery(); others++; }
                    }
                    else
                    {
                        using var ins = con.CreateCommand();
                        ins.CommandText = "INSERT INTO main.todos(text,done,book,chapter,verse,created,parent_id,due,pos) SELECT text,done,book,chapter,verse,created,$p,due,pos FROM g.todos WHERE id=$i; SELECT last_insert_rowid()";
                        ins.Parameters.AddWithValue("$p", (object?)parent ?? DBNull.Value); ins.Parameters.AddWithValue("$i", t.Id);
                        id = (long)ins.ExecuteScalar()!;
                        others++;
                    }
                    return todoMap[t.Id] = id;
                }
                foreach (var t in theirTodos) MapTodo(t);
                tx.Commit();
            }
            using var detach = con.CreateCommand(); detach.CommandText = "DETACH DATABASE g"; detach.ExecuteNonQuery();   // let go of the file
        }
        return new MergeResult(marks, notes, documents, others, conflicts);
    }
}
