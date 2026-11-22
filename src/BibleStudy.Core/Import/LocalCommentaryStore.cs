using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Import;

/// <summary>The commentaries the user read in (see <see cref="OwnCommentaries"/>): one small database file; a commentary with the same name is replaced by a new import.</summary>
public sealed class LocalCommentaryStore
{
    readonly string _path;

    public LocalCommentaryStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var con = Open();
        Exec(con, "CREATE TABLE IF NOT EXISTS commentaries(id TEXT PRIMARY KEY, name TEXT NOT NULL, source TEXT, imported TEXT);"
            + "CREATE TABLE IF NOT EXISTS entries(commentary TEXT NOT NULL, book TEXT NOT NULL, chapter INTEGER NOT NULL, from_verse INTEGER NOT NULL, to_verse INTEGER NOT NULL, ord INTEGER NOT NULL, text TEXT NOT NULL);"
            + "CREATE INDEX IF NOT EXISTS entries_chapter ON entries(commentary, book, chapter);");
    }

    SqliteConnection Open() { var con = new SqliteConnection($"Data Source={_path}"); con.Open(); return con; }

    static void Exec(SqliteConnection con, string sql, params (string, object)[] args)
    {
        using var cmd = con.CreateCommand(); cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();
    }

    /// <summary>"own:calvin" from "Calvin"; the same name gives the same id.</summary>
    public static string IdFor(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return "own:" + (slug.Length > 0 ? slug : "kommentar");
    }

    public string Save(ParsedCommentary commentary, string? sourcePath = null)
    {
        var id = IdFor(commentary.Name);
        using var con = Open();
        using var tx = con.BeginTransaction();
        Exec(con, "DELETE FROM entries WHERE commentary=$i", ("$i", id));
        Exec(con, "INSERT OR REPLACE INTO commentaries(id,name,source,imported) VALUES($i,$n,$s,$d)",
            ("$i", id), ("$n", commentary.Name.Trim()), ("$s", (object?)sourcePath ?? DBNull.Value), ("$d", DateTime.UtcNow.ToString("o")));
        using var ins = con.CreateCommand();
        ins.CommandText = "INSERT INTO entries VALUES($c,$b,$ch,$f,$t,$o,$x)";
        ins.Parameters.AddWithValue("$c", id);
        var pb = ins.Parameters.Add("$b", SqliteType.Text); var pch = ins.Parameters.Add("$ch", SqliteType.Integer);
        var pf = ins.Parameters.Add("$f", SqliteType.Integer); var pt = ins.Parameters.Add("$t", SqliteType.Integer);
        var po = ins.Parameters.Add("$o", SqliteType.Integer); var px = ins.Parameters.Add("$x", SqliteType.Text);
        var order = 0;
        foreach (var e in commentary.Entries)
        {
            pb.Value = e.Book; pch.Value = e.Chapter; pf.Value = e.From; pt.Value = e.To; po.Value = order++; px.Value = e.Text;
            ins.ExecuteNonQuery();
        }
        tx.Commit();
        return id;
    }

    public IReadOnlyList<CommentaryInfo> List()
    {
        using var con = Open(); using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM commentaries ORDER BY name COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        var list = new List<CommentaryInfo>();
        while (r.Read()) list.Add(new CommentaryInfo(r.GetString(0), r.GetString(1), "", ""));
        return list;
    }

    /// <summary>The comments of one chapter: the introduction (entries about the chapter as a whole) and the notes with the verses each covers. Null when there is nothing for this chapter.</summary>
    public CommentaryChapter? GetChapter(string id, string book, int chapter)
    {
        using var con = Open(); using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT from_verse, to_verse, text FROM entries WHERE commentary=$i AND book=$b AND chapter=$c ORDER BY from_verse, ord";
        cmd.Parameters.AddWithValue("$i", id); cmd.Parameters.AddWithValue("$b", book); cmd.Parameters.AddWithValue("$c", chapter);
        using var r = cmd.ExecuteReader();
        var intro = new List<string>(); var notes = new List<CommentaryNote>(); var ranges = new List<(int From, int To)>();
        while (r.Read())
        {
            var from = r.GetInt32(0); var to = r.GetInt32(1); var text = r.GetString(2);
            if (from == 0) intro.Add(text);
            else { notes.Add(new CommentaryNote(from, text)); ranges.Add((from, Math.Max(from, to))); }
        }
        return intro.Count == 0 && notes.Count == 0 ? null : new CommentaryChapter(string.Join("\n\n", intro), notes, ranges);
    }

    /// <summary>Every entry of a commentary, for the search in it.</summary>
    public List<(string Book, int Chapter, int Verse, string Text)> All(string id)
    {
        using var con = Open(); using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT book, chapter, from_verse, text FROM entries WHERE commentary=$i ORDER BY ord";
        cmd.Parameters.AddWithValue("$i", id);
        using var r = cmd.ExecuteReader();
        var list = new List<(string, int, int, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3)));
        return list;
    }

    public void Remove(string id)
    {
        using var con = Open();
        Exec(con, "DELETE FROM entries WHERE commentary=$i", ("$i", id));
        Exec(con, "DELETE FROM commentaries WHERE id=$i", ("$i", id));
    }
}
