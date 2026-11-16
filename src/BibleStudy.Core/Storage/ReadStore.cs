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
            try
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO read_chapters(book,chapter,read_at) SELECT book,chapter,read_at FROM old.read_chapters";
                cmd.ExecuteNonQuery();
            }
            finally
            {
                // the connection goes back to the pool: still attached, the next merge could not attach again (and the file would stay locked)
                using var detach = con.CreateCommand(); detach.CommandText = "DETACH DATABASE old"; detach.ExecuteNonQuery();
            }
        }
        catch (SqliteException) { /* no reading progress in that file */ }
    }

    /// <summary>Changes as soon as a chapter is marked read or unread (for the device sync: something new to send).</summary>
    public string Fingerprint() =>
        Query("SELECT COUNT(*), IFNULL(MAX(read_at),''), IFNULL(SUM(chapter),0) FROM read_chapters", _ => { }, r => $"{r.GetInt64(0)}/{r.GetString(1)}/{r.GetInt64(2)}").FirstOrDefault() ?? "";

    /// <summary>
    /// The device sync: takes over what the other device read, and what it marked unread since the last sync (<paramref name="basePath"/>, the state both had
    /// then; null = none yet, then nothing is removed). An other side that has read nothing at all removes nothing: a new or emptied device.
    /// </summary>
    public void MergeFrom(string otherPath, string? basePath)
    {
        static HashSet<(string, int)>? Read(string? path)
        {
            if (path is null || !File.Exists(path)) return null;
            try
            {
                using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                con.Open();
                using var cmd = con.CreateCommand(); cmd.CommandText = "SELECT book,chapter FROM read_chapters";
                using var r = cmd.ExecuteReader();
                var set = new HashSet<(string, int)>();
                while (r.Read()) set.Add((r.GetString(0), r.GetInt32(1)));
                return set;
            }
            catch (SqliteException) { return null; }
        }
        var other = Read(otherPath);
        if (other is null) return;
        var mine = Chapters();
        MergeFrom(otherPath);
        if (!HasTable(otherPath, "read_shared")) return;   // an older device: its file may hold an old per-vault list, which only adds
        if (Read(basePath) is not { Count: > 0 } before || other.Count == 0) return;
        // marked unread over there since the last sync, or marked unread here (the other side still has it from before): unread on both
        var unread = before.Where(c => !other.Contains(c) || !mine.Contains(c)).ToList();
        foreach (var g in unread.GroupBy(c => c.Item1)) Set(g.Key, g.Select(c => c.Item2), false);
    }

    static bool HasTable(string path, string table)
    {
        try
        {
            using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            con.Open();
            using var cmd = con.CreateCommand(); cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n"; cmd.Parameters.AddWithValue("$n", table);
            return cmd.ExecuteScalar() is not null;
        }
        catch (SqliteException) { return false; }
    }

    /// <summary>Writes the list into the read-chapters table of a vault file (an export: the vault then carries the progress along).</summary>
    public void CopyInto(string vaultDbPath)
    {
        using var con = Open();
        using (var attach = con.CreateCommand()) { attach.CommandText = "ATTACH DATABASE $p AS target"; attach.Parameters.AddWithValue("$p", vaultDbPath); attach.ExecuteNonQuery(); }
        try
        {
            using var tx = con.BeginTransaction();
            foreach (var sql in new[]
            {
                "CREATE TABLE IF NOT EXISTS target.read_chapters(book TEXT, chapter INT, read_at TEXT, PRIMARY KEY(book,chapter))",
                "CREATE TABLE IF NOT EXISTS target.read_shared(x INT)",   // marks a full copy of the shared list (an old per-vault list is only ever added)
                "DELETE FROM target.read_chapters",
                "INSERT INTO target.read_chapters(book,chapter,read_at) SELECT book,chapter,read_at FROM main.read_chapters",
            })
            {
                using var cmd = con.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        finally
        {
            // let go of the file, also after an error: the connection goes back to the pool, and an attached file would stay locked
            // (the export could not pack it, and every later copy for the sync would fail to attach)
            using var detach = con.CreateCommand(); detach.CommandText = "DETACH DATABASE target"; detach.ExecuteNonQuery();
        }
    }
}
