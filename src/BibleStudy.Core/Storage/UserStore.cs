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

    /// <summary>Every verse of an indexed translation in Bible order (for the search engine).</summary>
    public List<(VerseRef Ref, string Text)> GetCorpus(string translationId) =>
        Query("SELECT book,chapter,verse,text FROM fts WHERE translation=$t ORDER BY rowid", c => c.Parameters.AddWithValue("$t", translationId),
            r => (new VerseRef(r.GetString(0), r.GetInt32(1), r.GetInt32(2)), r.GetString(3)));

    public List<StrongsOccurrence> GetAllOccurrences(string translationId) =>
        Query("SELECT strong,book,chapter,verse,word FROM occ WHERE translation=$t", c => c.Parameters.AddWithValue("$t", translationId),
            r => new StrongsOccurrence(r.GetString(0), new VerseRef(r.GetString(1), r.GetInt32(2), r.GetInt32(3)), r.GetString(4)));

    /// <summary>All words must occur (prefix match). Snippet marks hits with « ».</summary>
    public List<SearchHit> Search(string translationId, string text, int limit = 200, IReadOnlyCollection<string>? books = null)
    {
        var query = BuildFtsQuery(text);
        if (query is null) return [];
        var filter = books is { Count: > 0 } ? $" AND book IN ({string.Join(',', books.Select((_, i) => "$b" + i))})" : "";
        return Query($"SELECT book,chapter,verse,snippet(fts,0,'«','»','…',24) FROM fts WHERE fts MATCH $q AND translation=$t{filter} LIMIT $n",
            c =>
            {
                c.Parameters.AddWithValue("$q", query); c.Parameters.AddWithValue("$t", translationId); c.Parameters.AddWithValue("$n", limit);
                if (books is not null) { var i = 0; foreach (var b in books) c.Parameters.AddWithValue("$b" + i++, b); }
            },
            r => new SearchHit(new VerseRef(r.GetString(0), r.GetInt32(1), r.GetInt32(2)), r.GetString(3)));
    }

    /// <summary>
    /// Turns user input into an FTS5 query: words are AND-ed and match word beginnings, "quoted text" is an exact phrase,
    /// OR between words alternates, a leading minus excludes a word. Returns null when there is nothing positive to search for.
    /// </summary>
    public static string? BuildFtsQuery(string input)
    {
        var positives = new List<string>();
        var negatives = new List<string>();
        var useOr = false;
        foreach (Match m in Tokens().Matches(input))
        {
            if (m.Groups["phrase"].Success)
            {
                var words = Words().Matches(m.Groups["phrase"].Value).Select(w => w.Value).ToList();
                if (words.Count > 0) positives.Add($"\"{string.Join(' ', words)}\"");
            }
            else if (m.Groups["neg"].Success) { if (Words().Match(m.Groups["neg"].Value) is { Success: true } w) negatives.Add($"\"{w.Value}\"*"); }
            else if (m.Groups["or"].Success) useOr = true;
            else if (Words().Match(m.Groups["word"].Value) is { Success: true } word) positives.Add($"\"{word.Value}\"*");
        }
        if (positives.Count == 0) return null;
        var query = string.Join(useOr ? " OR " : " ", positives);
        if (negatives.Count > 0) query = $"({query}) NOT ({string.Join(" OR ", negatives)})";
        return query;
    }

    [GeneratedRegex("""
        "(?<phrase>[^"]*)"|-(?<neg>[\p{L}\p{N}]+)|(?<or>\bOR\b|\bODER\b)|(?<word>[\p{L}\p{N}]+)
        """)] private static partial Regex Tokens();

    [GeneratedRegex(@"[\p{L}\p{N}]+")] private static partial Regex Words();

}
