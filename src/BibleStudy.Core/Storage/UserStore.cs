using System.Text.RegularExpressions;
using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Storage;


public sealed record SearchHit(VerseRef Ref, string Snippet);

/// <summary>Shared SQLite index (not personal data): full-text search (FTS5) and Strong's concordance per translation.</summary>
public sealed partial class UserStore : SqliteDb
{

    public UserStore(string dbPath) : base(dbPath)
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS indexed(translation TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS occ(translation TEXT, strong TEXT, book TEXT, chapter INT, verse INT, word TEXT);
            CREATE INDEX IF NOT EXISTS occ_idx ON occ(translation, strong);
            CREATE TABLE IF NOT EXISTS occ_done(translation TEXT PRIMARY KEY);
            CREATE VIRTUAL TABLE IF NOT EXISTS fts USING fts5(text, translation UNINDEXED, book UNINDEXED, chapter UNINDEXED, verse UNINDEXED, tokenize='unicode61 remove_diacritics 2');
            """);
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

    public bool IsStrongsIndexed(string translationId) =>
        Query("SELECT 1 FROM occ_done WHERE translation=$t", c => c.Parameters.AddWithValue("$t", translationId), _ => 1).Count > 0;

    public void IndexStrongs(string translationId, IEnumerable<StrongsOccurrence> occurrences)
    {
        using var con = Open();
        using var tx = con.BeginTransaction();
        using (var del = con.CreateCommand())
        {
            del.CommandText = "DELETE FROM occ WHERE translation=$t";
            del.Parameters.AddWithValue("$t", translationId);
            del.ExecuteNonQuery();
        }
        using var ins = con.CreateCommand();
        ins.CommandText = "INSERT INTO occ VALUES($t,$s,$b,$c,$v,$w)";
        ins.Parameters.AddWithValue("$t", translationId);
        var ps = ins.Parameters.Add("$s", SqliteType.Text); var pb = ins.Parameters.Add("$b", SqliteType.Text);
        var pc = ins.Parameters.Add("$c", SqliteType.Integer); var pv = ins.Parameters.Add("$v", SqliteType.Integer);
        var pw = ins.Parameters.Add("$w", SqliteType.Text);
        foreach (var o in occurrences)
        {
            ps.Value = o.Strong; pb.Value = o.Ref.Book; pc.Value = o.Ref.Chapter; pv.Value = o.Ref.Verse; pw.Value = o.Word;
            ins.ExecuteNonQuery();
        }
        using (var mark = con.CreateCommand())
        {
            mark.CommandText = "INSERT OR REPLACE INTO occ_done VALUES($t)";
            mark.Parameters.AddWithValue("$t", translationId);
            mark.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Occurrences of a Strong's number in Bible order.</summary>
    public List<StrongsOccurrence> GetOccurrences(string translationId, string strong, int limit = 3000) =>
        Query("SELECT book,chapter,verse,word FROM occ WHERE translation=$t AND strong=$s ORDER BY rowid LIMIT $n",
            c => { c.Parameters.AddWithValue("$t", translationId); c.Parameters.AddWithValue("$s", strong); c.Parameters.AddWithValue("$n", limit); },
            r => new StrongsOccurrence(strong, new VerseRef(r.GetString(0), r.GetInt32(1), r.GetInt32(2)), r.GetString(3)));

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

}
