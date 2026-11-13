using System.Globalization;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Storage;

/// <summary>A passage of a Bible study: verses of one chapter (From 0 = the whole chapter).</summary>
public sealed record StudyPassage(string Book, int Chapter, int From = 0, int To = 0)
{
    public bool Covers(string book, int chapter) => Book == book && Chapter == chapter;

    public override string ToString() => From <= 0 ? $"{Book} {Chapter}" : To > From ? $"{Book} {Chapter}:{From}-{To}" : $"{Book} {Chapter}:{From}";

    /// <summary>"JHN 10:1-18;PSA 23" → the passages; what cannot be read is left out.</summary>
    public static List<StudyPassage> ParseList(string? s)
    {
        var list = new List<StudyPassage>();
        foreach (var part in (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sp = part.Split(' ', 2);
            if (sp.Length < 2) continue;
            var cv = sp[1].Split(':', 2);
            if (!int.TryParse(cv[0], out var c)) continue;
            int from = 0, to = 0;
            if (cv.Length == 2)
            {
                var r = cv[1].Split('-', 2);
                if (!int.TryParse(r[0], out from)) continue;
                to = r.Length == 2 && int.TryParse(r[1], out var t) ? t : from;
            }
            list.Add(new StudyPassage(sp[0].ToUpperInvariant(), c, from, to));
        }
        return list;
    }

    public static string Join(IEnumerable<StudyPassage> passages) => string.Join(";", passages.Select(p => p.ToString()));

    /// <summary>Adds verses to a list of passages: next to verses of the same chapter they make one passage, a chapter on its own stays whole.</summary>
    public static List<StudyPassage> Add(IEnumerable<StudyPassage> passages, StudyPassage add)
    {
        var list = passages.ToList();
        var i = list.FindIndex(p => p.Covers(add.Book, add.Chapter));
        if (i < 0) { list.Add(add); return list; }
        var p = list[i];
        if (p.From <= 0 || add.From <= 0) list[i] = p with { From = 0, To = 0 };
        else list[i] = p with { From = Math.Min(p.From, add.From), To = Math.Max(Math.Max(p.To, p.From), Math.Max(add.To, add.From)) };
        return list;
    }
}

/// <summary>One Bible study as it was held: the day, a title, the passages, notes and the questions that came up.</summary>
public sealed record StudySession(long Id, DateOnly Day, string Title, IReadOnlyList<StudyPassage> Passages, string Notes, string Questions, DateTime Created);

/// <summary>The archive of the Bible studies held: when which passage was read, with notes and questions.</summary>
public sealed partial class VaultStore
{
    static StudySession ReadStudy(Microsoft.Data.Sqlite.SqliteDataReader r) => new(
        r.GetInt64(0),
        DateOnly.TryParseExact(r.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateOnly.FromDateTime(DateTime.Today),
        r.GetString(2), StudyPassage.ParseList(r.IsDBNull(3) ? "" : r.GetString(3)),
        r.IsDBNull(4) ? "" : r.GetString(4), r.IsDBNull(5) ? "" : r.GetString(5),
        !r.IsDBNull(6) && DateTime.TryParse(r.GetString(6), null, DateTimeStyles.RoundtripKind, out var c) ? c.ToLocalTime() : DateTime.Now);

    /// <summary>All Bible studies, the newest first.</summary>
    public List<StudySession> GetStudies() =>
        Query("SELECT id,day,title,passages,notes,questions,created FROM studies ORDER BY day DESC, id DESC", _ => { }, ReadStudy);

    public StudySession? GetStudy(long id) =>
        Query("SELECT id,day,title,passages,notes,questions,created FROM studies WHERE id=$i", c => c.Parameters.AddWithValue("$i", id), ReadStudy).FirstOrDefault();

    /// <summary>The Bible studies in which this chapter was read, the newest first.</summary>
    public List<StudySession> StudiesOf(string book, int chapter) =>
        GetStudies().Where(s => s.Passages.Any(p => p.Covers(book, chapter))).ToList();

    public long AddStudy(DateOnly day, string title, IEnumerable<StudyPassage> passages, string notes = "", string questions = "")
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO studies(day,title,passages,notes,questions,created) VALUES($d,$t,$p,$n,$q,$c); SELECT last_insert_rowid()";
        cmd.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$t", title.Trim()); cmd.Parameters.AddWithValue("$p", StudyPassage.Join(passages));
        cmd.Parameters.AddWithValue("$n", notes); cmd.Parameters.AddWithValue("$q", questions);
        cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
        return (long)cmd.ExecuteScalar()!;
    }

    public void UpdateStudy(StudySession s) =>
        Exec("UPDATE studies SET day=$d, title=$t, passages=$p, notes=$n, questions=$q WHERE id=$i", c =>
        {
            c.Parameters.AddWithValue("$d", s.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            c.Parameters.AddWithValue("$t", s.Title.Trim()); c.Parameters.AddWithValue("$p", StudyPassage.Join(s.Passages));
            c.Parameters.AddWithValue("$n", s.Notes); c.Parameters.AddWithValue("$q", s.Questions); c.Parameters.AddWithValue("$i", s.Id);
        });

    public void DeleteStudy(long id) => Exec("DELETE FROM studies WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));
}
