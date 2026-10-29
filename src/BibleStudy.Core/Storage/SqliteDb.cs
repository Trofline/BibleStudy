using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Storage;

/// <summary>Small helper base for the SQLite-backed stores.</summary>
public abstract class SqliteDb
{
    readonly string _cs;

    protected SqliteDb(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _cs = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();   // a folder name with ";" or "=" stays one path
    }

    protected SqliteConnection Open() { var c = new SqliteConnection(_cs); c.Open(); return c; }

    protected int Exec(string sql, Action<SqliteCommand>? bind = null)
    {
        using var con = Open(); using var cmd = con.CreateCommand();
        cmd.CommandText = sql; bind?.Invoke(cmd);
        return cmd.ExecuteNonQuery();
    }

    protected List<T> Query<T>(string sql, Action<SqliteCommand> bind, Func<SqliteDataReader, T> map)
    {
        using var con = Open(); using var cmd = con.CreateCommand();
        cmd.CommandText = sql; bind(cmd);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }
}
