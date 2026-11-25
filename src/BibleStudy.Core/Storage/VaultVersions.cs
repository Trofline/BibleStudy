using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Storage;

/// <summary>One earlier state of a document or a note. Title is the document's title then ("" for a note).</summary>
public sealed record TextVersion(long Id, string Title, string Body, DateTime Saved);

/// <summary>
/// Earlier versions of documents and notes. Before a text is overwritten, the text as it was is kept, at most once every
/// <see cref="VersionGap"/> (documents save themselves every few seconds while one writes: one writing session gives one version,
/// not hundreds). The newest <see cref="VersionsKept"/> versions of each text stay. Versions live in the vault file, are not synchronized
/// (every device keeps its own); a document from the trash comes back with them, and they go when it is gone for good.
/// </summary>
public sealed partial class VaultStore
{
    public static readonly TimeSpan VersionGap = TimeSpan.FromMinutes(10);
    public const int VersionsKept = 50;

    void InitVersions()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS versions(id INTEGER PRIMARY KEY, kind TEXT NOT NULL, item TEXT NOT NULL, title TEXT, body TEXT NOT NULL, saved TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS versions_item ON versions(kind, item);
            """);
        // the versions of documents that are gone for good (not in the vault, not in the trash any more) go too
        try
        {
            Exec("""
                DELETE FROM versions WHERE kind='doc'
                  AND CAST(item AS INTEGER) NOT IN (SELECT id FROM documents)
                  AND CAST(item AS INTEGER) NOT IN (SELECT CAST(json_extract(payload,'$.Id') AS INTEGER) FROM trash WHERE kind='doc' AND json_valid(payload))
                """);
        }
        catch (SqliteException) { /* tidied up next time */ }
    }

    static string DocItem(long id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    static string NoteItem(VerseRef r) => $"{r.Book} {r.Chapter}:{r.Verse}";

    /// <summary>Earlier versions of a document, newest first.</summary>
    public List<TextVersion> GetDocumentVersions(long documentId) => GetVersions("doc", DocItem(documentId));

    /// <summary>Earlier versions of the note on a verse, newest first.</summary>
    public List<TextVersion> GetNoteVersions(VerseRef r) => GetVersions("note", NoteItem(r));

    List<TextVersion> GetVersions(string kind, string item) =>
        Query("SELECT id,title,body,saved FROM versions WHERE kind=$k AND item=$i ORDER BY saved DESC, id DESC",
            c => { c.Parameters.AddWithValue("$k", kind); c.Parameters.AddWithValue("$i", item); },
            r => new TextVersion(r.GetInt64(0), r.IsDBNull(1) ? "" : r.GetString(1), r.GetString(2), ParseTime(r.GetString(3))));

    static DateTime ParseTime(string s) =>
        DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime() : DateTime.MinValue;

    /// <summary>
    /// Keeps <paramref name="oldBody"/> as a version before the text becomes <paramref name="newBody"/>. Nothing is kept when nothing changes,
    /// when the old text was empty, or when the last version is younger than <see cref="VersionGap"/> (unless <paramref name="always"/>).
    /// </summary>
    static void KeepVersion(SqliteConnection con, string kind, string item, string oldTitle, string oldBody, string newTitle, string newBody, bool always = false)
    {
        if (oldBody == newBody && oldTitle == newTitle) return;
        if (string.IsNullOrWhiteSpace(oldBody) && string.IsNullOrWhiteSpace(oldTitle)) return;
        if (!always)
        {
            using var last = con.CreateCommand();
            last.CommandText = "SELECT saved FROM versions WHERE kind=$k AND item=$i ORDER BY saved DESC LIMIT 1";
            last.Parameters.AddWithValue("$k", kind); last.Parameters.AddWithValue("$i", item);
            if (last.ExecuteScalar() is string s && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)
                && DateTime.UtcNow - at.ToUniversalTime() < VersionGap) return;
        }
        using (var add = con.CreateCommand())
        {
            add.CommandText = "INSERT INTO versions(kind,item,title,body,saved) VALUES($k,$i,$t,$b,$s)";
            add.Parameters.AddWithValue("$k", kind); add.Parameters.AddWithValue("$i", item);
            add.Parameters.AddWithValue("$t", oldTitle); add.Parameters.AddWithValue("$b", oldBody);
            add.Parameters.AddWithValue("$s", DateTime.UtcNow.ToString("o"));
            add.ExecuteNonQuery();
        }
        using var trim = con.CreateCommand();
        trim.CommandText = "DELETE FROM versions WHERE kind=$k AND item=$i AND id NOT IN (SELECT id FROM versions WHERE kind=$k AND item=$i ORDER BY saved DESC, id DESC LIMIT $n)";
        trim.Parameters.AddWithValue("$k", kind); trim.Parameters.AddWithValue("$i", item); trim.Parameters.AddWithValue("$n", VersionsKept);
        trim.ExecuteNonQuery();
    }

    /// <summary>Keeps the document as it is in the vault before it changes (within the open transaction of the change).</summary>
    static void KeepDocumentVersion(SqliteConnection con, long id, string newTitle, string newBody, bool always = false)
    {
        string? title = null, body = null;
        using (var read = con.CreateCommand())
        {
            read.CommandText = "SELECT title,body FROM documents WHERE id=$i";
            read.Parameters.AddWithValue("$i", id);
            using var r = read.ExecuteReader();
            if (r.Read()) { title = r.GetString(0); body = r.IsDBNull(1) ? "" : r.GetString(1); }
        }
        if (title is null || body is null) return;
        // sermons are written anew from their own data on every save: their text says nothing a version could bring back
        if (body.Contains("<!--sermon:", StringComparison.Ordinal)) return;
        KeepVersion(con, "doc", DocItem(id), title, body, newTitle.Trim(), newBody, always);
    }

    void KeepNoteVersion(VerseRef v, string newText)
    {
        if (GetNote(v) is not { } old) return;
        using var con = Open();
        KeepVersion(con, "note", NoteItem(v), "", old.Text, "", newText);
    }

    /// <summary>Puts a document back to an earlier version; the text it has now is kept as a version first, so going back can be undone.</summary>
    public bool RestoreDocumentVersion(long documentId, long versionId)
    {
        var version = GetDocumentVersions(documentId).FirstOrDefault(x => x.Id == versionId);
        var doc = GetDocuments().FirstOrDefault(d => d.Id == documentId);
        if (version is null || doc is null) return false;
        using (var con = Open()) KeepDocumentVersion(con, documentId, version.Title, version.Body, always: true);
        UpdateDocument(documentId, version.Title.Length > 0 ? version.Title : doc.Title, version.Body, doc.Path, doc.IsTemplate, doc.Tags, doc.FolderId);
        return true;
    }

    /// <summary>Puts a note back to an earlier version; the text it has now is kept as a version first.</summary>
    public bool RestoreNoteVersion(VerseRef r, long versionId)
    {
        var version = GetNoteVersions(r).FirstOrDefault(x => x.Id == versionId);
        if (version is null) return false;
        if (GetNote(r) is { } now)
        {
            using var con = Open();
            KeepVersion(con, "note", NoteItem(r), "", now.Text, "", version.Body, always: true);
        }
        SetNote(r, version.Body);
        return true;
    }

    /// <summary>
    /// How two texts differ, line by line, for the list of versions: the number of lines that are new and that are gone.
    /// Long texts are compared roughly (lines as a set) so the list stays fast.
    /// </summary>
    public static (int Added, int Removed) LineChanges(string before, string after)
    {
        var a = before.ReplaceLineEndings("\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        var b = after.ReplaceLineEndings("\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if ((long)a.Count * b.Count > 400_000)
        {
            var sa = a.ToHashSet(); var sb = b.ToHashSet();
            return (b.Count(l => !sa.Contains(l)), a.Count(l => !sb.Contains(l)));
        }
        var len = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                len[i, j] = a[i] == b[j] ? len[i + 1, j + 1] + 1 : Math.Max(len[i + 1, j], len[i, j + 1]);
        var same = len[0, 0];
        return (b.Count - same, a.Count - same);
    }
}
