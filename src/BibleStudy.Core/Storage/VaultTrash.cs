using System.Text.Json;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Storage;

/// <summary>Something deleted that waits in the trash (Kind: mark, note, folder, doc). Place is the Bible place or "" (folders, documents).</summary>
public sealed record TrashItem(long Id, string Kind, string Title, string Place, DateTime Deleted);

/// <summary>
/// The trash of a vault: what is deleted is kept as it was (as JSON) for <see cref="TrashDays"/> days and can be brought back, also after
/// the program was closed. Older entries are thrown away when the vault is opened.
/// </summary>
public sealed partial class VaultStore
{
    public const int TrashDays = 30;

    sealed record FolderSnapshot(long Id, string Name, long? ParentId, List<long> Kids, List<long> Marks, List<VerseRef> Notes, List<long> Docs);
    sealed record DocSnapshot(long Id, string Title, string Body, string? Path, bool IsTemplate, List<string> Tags, long? FolderId);

    void InitTrash()
    {
        Exec("CREATE TABLE IF NOT EXISTS trash(id INTEGER PRIMARY KEY, kind TEXT NOT NULL, title TEXT, place TEXT, payload TEXT NOT NULL, deleted TEXT NOT NULL)");
        PurgeTrash(TimeSpan.FromDays(TrashDays));
    }

    long AddToTrash(string kind, string title, string place, object payload)
    {
        using var con = Open(); using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO trash(kind,title,place,payload,deleted) VALUES($k,$t,$p,$j,$d); SELECT last_insert_rowid()";
        cmd.Parameters.AddWithValue("$k", kind); cmd.Parameters.AddWithValue("$t", title); cmd.Parameters.AddWithValue("$p", place);
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(payload)); cmd.Parameters.AddWithValue("$d", DateTime.UtcNow.ToString("o"));
        return (long)cmd.ExecuteScalar()!;
    }

    static string Short(string s) { s = s.ReplaceLineEndings(" ").Trim(); return s.Length > 120 ? s[..120] + "…" : s; }
    static string PlaceOf(VerseRef r, int last = 0) => $"{r.Book} {r.Chapter}:{r.Verse}" + (last > r.Verse ? "–" + last : "");

    /// <summary>Puts a mark that is already gone into the trash (when it was removed in the text, for example).</summary>
    public long KeepInTrash(Mark m) => AddToTrash("mark", Short(m.Note.Length > 0 ? m.Note : m.Text), PlaceOf(m.Ref, m.LastVerse), m);

    /// <summary>Deletes the mark and keeps it in the trash; returns the id of the trash entry (0 when there was no such mark).</summary>
    public long TrashMark(long id)
    {
        if (GetMarks().FirstOrDefault(m => m.Id == id) is not { } m) return 0;
        var t = KeepInTrash(m);
        DeleteMark(id);
        return t;
    }

    public long TrashNote(VerseRef r)
    {
        if (GetNotes().FirstOrDefault(n => n.Ref == r) is not { } n) return 0;
        var t = AddToTrash("note", Short(n.Text), PlaceOf(r), n);
        SetNote(r, "");
        return t;
    }

    /// <summary>Deletes the folder (its content stays, see <see cref="DeleteFolder"/>) and remembers what was in it, so all of it goes back in.</summary>
    public long TrashFolder(long id)
    {
        if (GetFolders().FirstOrDefault(f => f.Id == id) is not { } f) return 0;
        var snap = new FolderSnapshot(f.Id, f.Name, f.ParentId,
            GetFolders().Where(x => x.ParentId == id).Select(x => x.Id).ToList(),
            GetMarks(folderId: id).Select(m => m.Id).ToList(),
            GetNotes().Where(n => n.FolderId == id).Select(n => n.Ref).ToList(),
            GetDocuments().Where(d => d.FolderId == id).Select(d => d.Id).ToList());
        var t = AddToTrash("folder", f.Name, "", snap);
        DeleteFolder(id);
        return t;
    }

    public long TrashDocument(long id)
    {
        if (GetDocuments().FirstOrDefault(d => d.Id == id) is not { } d) return 0;
        var t = AddToTrash("doc", d.Title, "", new DocSnapshot(d.Id, d.Title, d.Body, d.Path, d.IsTemplate, [.. d.Tags], d.FolderId));
        DeleteDocument(id);
        return t;
    }

    /// <summary>What lies in the trash, the newest first.</summary>
    public List<TrashItem> GetTrash() =>
        Query("SELECT id,kind,title,place,deleted FROM trash ORDER BY id DESC", _ => { },
            r => new TrashItem(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3),
                DateTime.TryParse(r.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime() : DateTime.Now));

    bool FolderExists(long? id) => id is long f && Query("SELECT 1 FROM folders WHERE id=$i", c => c.Parameters.AddWithValue("$i", f), _ => 1).Count > 0;

    /// <summary>
    /// Brings an entry of the trash back where it was (a folder it lay in that is gone by now: then at the top) and takes it out of the trash.
    /// A note on a verse that has a new note meanwhile is put under it, so nothing is overwritten. Returns false when the entry is not there.
    /// </summary>
    public bool RestoreFromTrash(long trashId)
    {
        var row = Query("SELECT kind,payload FROM trash WHERE id=$i", c => c.Parameters.AddWithValue("$i", trashId), r => (Kind: r.GetString(0), Json: r.GetString(1))).FirstOrDefault();
        if (row.Kind is null) return false;
        switch (row.Kind)
        {
            case "mark":
            {
                var m = JsonSerializer.Deserialize<Mark>(row.Json)!;
                RestoreMark(m with { FolderId = FolderExists(m.FolderId) ? m.FolderId : null });
                break;
            }
            case "note":
            {
                var n = JsonSerializer.Deserialize<Note>(row.Json)!;
                var now = GetNotes().FirstOrDefault(x => x.Ref == n.Ref);
                SetNote(n.Ref, now is null ? n.Text : now.Text.TrimEnd() + "\n\n" + n.Text);
                if (now is null) SetNoteFolder(n.Ref, FolderExists(n.FolderId) ? n.FolderId : null);
                break;
            }
            case "folder":
            {
                var f = JsonSerializer.Deserialize<FolderSnapshot>(row.Json)!;
                var parent = FolderExists(f.ParentId) ? f.ParentId : null;
                var id = RestoreFolder(f.Id, f.Name, parent);
                // only what still lies where the deleting put it goes back in (whatever was moved since then stays where it was put)
                var folders = GetFolders().ToDictionary(x => x.Id);
                foreach (var k in f.Kids) if (folders.TryGetValue(k, out var kid) && kid.ParentId == f.ParentId) SetFolderParent(k, id);
                var marks = GetMarks(noFolder: true).Select(m => m.Id).ToHashSet();
                foreach (var m in f.Marks) if (marks.Contains(m)) SetMarkFolder(m, id);
                var notes = GetNotes().Where(n => n.FolderId is null).Select(n => n.Ref).ToHashSet();
                foreach (var n in f.Notes) if (notes.Contains(n)) SetNoteFolder(n, id);
                var docs = GetDocuments().Where(d => d.FolderId is null).Select(d => d.Id).ToHashSet();
                foreach (var d in f.Docs) if (docs.Contains(d)) SetDocumentFolder(d, id);
                break;
            }
            case "doc":
            {
                var d = JsonSerializer.Deserialize<DocSnapshot>(row.Json)!;
                var made = AddDocument(d.Title, d.Body, d.Path, d.IsTemplate, d.Tags, FolderExists(d.FolderId) ? d.FolderId : null);
                if (Query("SELECT 1 FROM documents WHERE id=$i", c => c.Parameters.AddWithValue("$i", d.Id), _ => 1).Count == 0)
                    Exec("UPDATE documents SET id=$o WHERE id=$n; UPDATE doc_tags SET doc_id=$o WHERE doc_id=$n", c => { c.Parameters.AddWithValue("$o", d.Id); c.Parameters.AddWithValue("$n", made); });
                break;
            }
            default: return false;
        }
        DeleteFromTrash(trashId);
        return true;
    }

    /// <summary>Throws an entry of the trash away for good.</summary>
    public void DeleteFromTrash(long trashId) => Exec("DELETE FROM trash WHERE id=$i", c => c.Parameters.AddWithValue("$i", trashId));

    public void EmptyTrash() => Exec("DELETE FROM trash");

    /// <summary>Throws away what lies in the trash longer than <paramref name="keep"/>; returns how many entries went.</summary>
    public int PurgeTrash(TimeSpan keep)
    {
        var old = GetTrash().Where(t => DateTime.Now - t.Deleted > keep).Select(t => t.Id).ToList();
        foreach (var id in old) DeleteFromTrash(id);
        return old.Count;
    }

    /// <summary>Gives several marks a tag (or takes it away) in one step; their other tags stay.</summary>
    public void TagMarks(IEnumerable<long> ids, string tag, bool add)
    {
        tag = tag.Trim();
        if (tag.Length == 0) return;
        using var con = Open();
        using var tx = con.BeginTransaction();
        foreach (var id in ids)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = add ? "INSERT OR IGNORE INTO mark_tags VALUES($i,$t)" : "DELETE FROM mark_tags WHERE mark_id=$i AND tag=$t";
            cmd.Parameters.AddWithValue("$i", id); cmd.Parameters.AddWithValue("$t", tag);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
