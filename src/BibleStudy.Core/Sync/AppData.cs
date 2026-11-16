using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Sync;

/// <summary>
/// What the program keeps beside the vaults and that belongs to the person, not to the device (verses to learn by heart, vocabulary, document categories,
/// names of colors, saved searches, the reading plan …): it travels inside each vault's copy, as sets of key → value (JSON), and is merged in three ways
/// like the vault itself. Window sizes, the font size and the look stay on each device.
/// </summary>
public static class AppData
{
    const string Table = "app_data";

    /// <summary>Writes the sets into a vault file (a copy for the sync).</summary>
    public static void WriteInto(string dbPath, IReadOnlyDictionary<string, Dictionary<string, string>> sets)
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
        con.Open();
        using var tx = con.BeginTransaction();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {Table}(sett TEXT, key TEXT, value TEXT, PRIMARY KEY(sett, key)); DELETE FROM {Table};";
            cmd.ExecuteNonQuery();
        }
        using (var ins = con.CreateCommand())
        {
            ins.CommandText = $"INSERT INTO {Table}(sett,key,value) VALUES($s,$k,$v)";
            var s = ins.Parameters.Add("$s", SqliteType.Text); var k = ins.Parameters.Add("$k", SqliteType.Text); var v = ins.Parameters.Add("$v", SqliteType.Text);
            foreach (var (set, values) in sets)
                foreach (var (key, value) in values) { s.Value = set; k.Value = key; v.Value = value; ins.ExecuteNonQuery(); }
        }
        tx.Commit();
    }

    /// <summary>The sets in a vault file; null when it has none (a copy from an older version, or no file).</summary>
    public static Dictionary<string, Dictionary<string, string>>? ReadFrom(string? dbPath)
    {
        if (dbPath is null || !File.Exists(dbPath)) return null;
        try
        {
            using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"SELECT sett,key,value FROM {Table}";
            using var r = cmd.ExecuteReader();
            var sets = new Dictionary<string, Dictionary<string, string>>();
            while (r.Read())
            {
                if (!sets.TryGetValue(r.GetString(0), out var set)) sets[r.GetString(0)] = set = [];
                set[r.GetString(1)] = r.IsDBNull(2) ? "" : r.GetString(2);
            }
            return sets;
        }
        catch (SqliteException) { return null; }
    }

    /// <summary>A short fingerprint of the sets (a change here is something to send).</summary>
    public static string Fingerprint(IReadOnlyDictionary<string, Dictionary<string, string>> sets)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (set, values) in sets.OrderBy(s => s.Key, StringComparer.Ordinal))
            foreach (var (key, value) in values.OrderBy(v => v.Key, StringComparer.Ordinal))
                hash.AppendData(Encoding.UTF8.GetBytes(set + "\u0001" + key + "\u0001" + value + "\u0002"));
        return Convert.ToHexString(hash.GetHashAndReset())[..16];
    }

    /// <summary>
    /// One set, merged in three ways: changed only there → taken over; deleted only there → deleted here; new there → added; changed here → stays.
    /// Without a base (the first time) or when the other side has nothing at all, nothing is deleted.
    /// </summary>
    public static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> mine, IReadOnlyDictionary<string, string> theirs, IReadOnlyDictionary<string, string>? bas)
    {
        var result = new Dictionary<string, string>(mine);
        foreach (var (key, t) in theirs)
        {
            string? was = null;
            var inBase = bas is not null && bas.TryGetValue(key, out was);
            if (!mine.TryGetValue(key, out var m)) { if (!inBase) result[key] = t; continue; }   // new over there; deleted here stays deleted
            if (m == t) continue;
            if (inBase && m == was) { result[key] = t; continue; }   // changed only there
            if (inBase && t == was) continue;                        // changed only here
            result[key] = Resolve(m, t);                             // changed on both sides (or new on both): both devices must end the same
        }
        if (bas is not null && theirs.Count > 0)
            foreach (var (key, was) in bas)
                if (!theirs.ContainsKey(key) && mine.TryGetValue(key, out var m) && m == was) result.Remove(key);   // deleted only there
        return result;
    }

    /// <summary>
    /// A value changed on both devices. Lists (the days of the reading plan done, saved searches …) are joined, so nothing ticked off on either side is lost;
    /// anything else takes one of the two by a rule that does not depend on the side, so both devices keep the same and do not drift apart.
    /// </summary>
    static string Resolve(string m, string t)
    {
        var (first, second) = string.CompareOrdinal(m, t) >= 0 ? (m, t) : (t, m);
        try
        {
            using var a = System.Text.Json.JsonDocument.Parse(first);
            using var b = System.Text.Json.JsonDocument.Parse(second);
            if (a.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array && b.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var items = new List<string>();
                foreach (var e in a.RootElement.EnumerateArray().Concat(b.RootElement.EnumerateArray()))
                    if (!items.Contains(e.GetRawText())) items.Add(e.GetRawText());
                return "[" + string.Join(",", items) + "]";
            }
        }
        catch (System.Text.Json.JsonException) { /* not JSON: one of the two */ }
        return first;
    }
}
