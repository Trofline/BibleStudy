using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Storage;

/// <summary>
/// Which chapters were read: one list for the whole program, the same in every vault (one person reads the Bible once, whatever project
/// they study in). Lives in its own file (<c>read.db</c>) next to the vaults, so it is part of every backup.
/// </summary>
public sealed class ReadStore : SqliteDb
{
    readonly string _path;

    /// <summary>True when the file was created just now (the caller then takes over the old per-vault lists once).</summary>
    public bool IsNew { get; }

    public ReadStore(string dbPath) : base(dbPath)
    {
        _path = dbPath;
        IsNew = !File.Exists(dbPath);
        Exec("CREATE TABLE IF NOT EXISTS read_chapters(book TEXT, chapter INT, read_at TEXT, PRIMARY KEY(book,chapter))");
    }

    public HashSet<(string Book, int Chapter)> Chapters() =>
        Query("SELECT book,chapter FROM read_chapters", _ => { }, r => (r.GetString(0), r.GetInt32(1))).ToHashSet();

    public List<(string Book, int Chapter, DateTime At)> Dates() =>
        Query("SELECT book,chapter,read_at FROM read_chapters WHERE read_at IS NOT NULL", _ => { },
            r => (r.GetString(0), r.GetInt32(1), DateTime.TryParse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime() : DateTime.MinValue))
        .Where(x => x.Item3 != DateTime.MinValue).ToList();

    public void Set(string book, IEnumerable<int> chapters, bool read)
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

    /// <summary>Adds the chapters read in a vault file (nothing is removed; what is read stays read). A file without the table adds nothing.</summary>
    public void MergeFrom(string vaultDbPath)
    {
        if (!File.Exists(vaultDbPath)) return;
        try
        {
            using var con = Open();
            using (var attach = con.CreateCommand()) { attach.CommandText = "ATTACH DATABASE $p AS old"; attach.Parameters.AddWithValue("$p", vaultDbPath); attach.ExecuteNonQuery(); }
            using var cmd = con.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO read_chapters(book,chapter,read_at) SELECT book,chapter,read_at FROM old.read_chapters";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException) { /* no reading progress in that file */ }
    }

    /// <summary>Writes the list into the read-chapters table of a vault file (an export: the vault then carries the progress along).</summary>
    public void CopyInto(string vaultDbPath)
    {
        using var con = Open();
        using (var attach = con.CreateCommand()) { attach.CommandText = "ATTACH DATABASE $p AS target"; attach.Parameters.AddWithValue("$p", vaultDbPath); attach.ExecuteNonQuery(); }
        using var tx = con.BeginTransaction();
        foreach (var sql in new[]
        {
            "CREATE TABLE IF NOT EXISTS target.read_chapters(book TEXT, chapter INT, read_at TEXT, PRIMARY KEY(book,chapter))",
            "DELETE FROM target.read_chapters",
            "INSERT INTO target.read_chapters(book,chapter,read_at) SELECT book,chapter,read_at FROM main.read_chapters",
        })
        {
            using var cmd = con.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
        }
        tx.Commit();
        // let go of the file: the connection goes back to the pool, and an attached file would stay locked (the export could not pack it)
        using (var detach = con.CreateCommand()) { detach.CommandText = "DETACH DATABASE target"; detach.ExecuteNonQuery(); }
    }
}
