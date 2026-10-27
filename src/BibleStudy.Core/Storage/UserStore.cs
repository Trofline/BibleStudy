using System.Text.RegularExpressions;
using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Storage;

public sealed record Note(VerseRef Ref, string Text);
public sealed record SearchHit(VerseRef Ref, string Snippet);

/// <summary>Local SQLite storage: bookmarks, notes and a full-text index (FTS5) per translation.</summary>
public sealed partial class UserStore
{
    readonly string _cs;

    public UserStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _cs = $"Data Source={dbPath}";
        Exec("""
            CREATE TABLE IF NOT EXISTS bookmarks(book TEXT, chapter INT, verse INT, created TEXT, PRIMARY KEY(book,chapter,verse));
            CREATE TABLE IF NOT EXISTS notes(book TEXT, chapter INT, verse INT, text TEXT, updated TEXT, PRIMARY KEY(book,chapter,verse));
            CREATE TABLE IF NOT EXISTS indexed(translation TEXT PRIMARY KEY);
            CREATE VIRTUAL TABLE IF NOT EXISTS fts USING fts5(text, translation UNINDEXED, book UNINDEXED, chapter UNINDEXED, verse UNINDEXED, tokenize='unicode61 remove_diacritics 2');
            """);
    }

    public List<VerseRef> GetBookmarks() =>
        Query("SELECT book,chapter,verse FROM bookmarks ORDER BY created", _ => { }, r => new VerseRef(r.GetString(0), r.GetInt32(1), r.GetInt32(2)));

    /// <returns>true if the verse is bookmarked afterwards.</returns>
    public bool ToggleBookmark(VerseRef v)
    {
        void P(SqliteCommand c) { c.Parameters.AddWithValue("$b", v.Book); c.Parameters.AddWithValue("$c", v.Chapter); c.Parameters.AddWithValue("$v", v.Verse); }
        if (Exec("DELETE FROM bookmarks WHERE book=$b AND chapter=$c AND verse=$v", P) > 0) return false;
        Exec("INSERT INTO bookmarks VALUES($b,$c,$v,$t)", c => { P(c); c.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o")); });
        return true;
    }

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

    public bool IsIndexed(string translationId) =>
        Query("SELECT 1 FROM indexed WHERE translation=$t", c => c.Parameters.AddWithValue("$t", translationId), _ => 1).Count > 0;

    public void Index(string translationId, IEnumerable<VerseText> verses)
    {
        using var con = Open();
        using var tx = con.BeginTransaction();
        using (var del = con.CreateCommand())
        {
            del.CommandText = "DELETE FROM fts WHERE translation=$t";
            del.Parameters.AddWithValue("$t", translationId);
            del.ExecuteNonQuery();
        }
        using var ins = con.CreateCommand();
        ins.CommandText = "INSERT INTO fts(text,translation,book,chapter,verse) VALUES($x,$t,$b,$c,$v)";
        var px = ins.Parameters.Add("$x", SqliteType.Text); var pb = ins.Parameters.Add("$b", SqliteType.Text);
        var pc = ins.Parameters.Add("$c", SqliteType.Integer); var pv = ins.Parameters.Add("$v", SqliteType.Integer);
        ins.Parameters.AddWithValue("$t", translationId);
        foreach (var v in verses) { px.Value = v.Text; pb.Value = v.BookId; pc.Value = v.Chapter; pv.Value = v.Number; ins.ExecuteNonQuery(); }
        using (var mark = con.CreateCommand())
        {
            mark.CommandText = "INSERT OR REPLACE INTO indexed VALUES($t)";
            mark.Parameters.AddWithValue("$t", translationId);
            mark.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>All words must occur (prefix match). Snippet marks hits with « ».</summary>
    public List<SearchHit> Search(string translationId, string text, int limit = 200)
    {
        var terms = Words().Matches(text).Select(m => $"\"{m.Value}\"*").ToList();
        if (terms.Count == 0) return [];
        return Query("SELECT book,chapter,verse,snippet(fts,0,'«','»','…',24) FROM fts WHERE fts MATCH $q AND translation=$t LIMIT $n",
            c => { c.Parameters.AddWithValue("$q", string.Join(' ', terms)); c.Parameters.AddWithValue("$t", translationId); c.Parameters.AddWithValue("$n", limit); },
            r => new SearchHit(new VerseRef(r.GetString(0), r.GetInt32(1), r.GetInt32(2)), r.GetString(3)));
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")] private static partial Regex Words();

    SqliteConnection Open() { var c = new SqliteConnection(_cs); c.Open(); return c; }

    int Exec(string sql, Action<SqliteCommand>? bind = null)
    {
        using var con = Open(); using var cmd = con.CreateCommand();
        cmd.CommandText = sql; bind?.Invoke(cmd);
        return cmd.ExecuteNonQuery();
    }

    List<T> Query<T>(string sql, Action<SqliteCommand> bind, Func<SqliteDataReader, T> map)
    {
        using var con = Open(); using var cmd = con.CreateCommand();
        cmd.CommandText = sql; bind(cmd);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }
}
