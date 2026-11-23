using System.Globalization;

namespace BibleStudy.Core.Storage;

/// <summary>
/// A reading path: verses (or marked verses) in the order in which they are best read, one building on the other. Each step is a verse or a
/// few verses of one chapter (<see cref="StudyPassage"/> with From &gt; 0); the same verse may come twice.
/// </summary>
public sealed record ReadingPath(long Id, string Name, string Color, IReadOnlyList<StudyPassage> Steps)
{
    /// <summary>The steps that lie in this chapter, with their number in the path (1 is the first).</summary>
    public IEnumerable<(int Number, StudyPassage Step)> StepsIn(string book, int chapter) =>
        Steps.Select((s, i) => (i + 1, s)).Where(x => x.s.Covers(book, chapter));

    /// <summary>The steps from the end of one to the next where both lie in this chapter: each is one arrow beside the text.</summary>
    public IEnumerable<(int Number, int From, int To)> ArrowsIn(string book, int chapter)
    {
        for (var i = 0; i + 1 < Steps.Count; i++)
            if (Steps[i].Covers(book, chapter) && Steps[i + 1].Covers(book, chapter) && Steps[i].From != Steps[i + 1].From)
                yield return (i + 1, Steps[i].From, Steps[i + 1].From);
    }
}

/// <summary>
/// The arrows of the reading paths beside the text, laid out as the jumps beside the code in a disassembler: each arrow runs in a lane of its own,
/// the shorter ones nearer to the text, so arrows that overlap never share a line.
/// </summary>
public static class PathArrows
{
    /// <summary>The lane of each arrow (0 = next to the text). Arrows whose spans touch or overlap get different lanes.</summary>
    public static int[] Lanes(IReadOnlyList<(int From, int To)> arrows)
    {
        var lanes = new int[arrows.Count];
        var taken = new List<List<(int Lo, int Hi)>>();
        foreach (var i in Enumerable.Range(0, arrows.Count).OrderBy(i => Math.Abs(arrows[i].To - arrows[i].From)).ThenBy(i => Math.Min(arrows[i].From, arrows[i].To)))
        {
            var lo = Math.Min(arrows[i].From, arrows[i].To); var hi = Math.Max(arrows[i].From, arrows[i].To);
            var lane = 0;
            while (lane < taken.Count && taken[lane].Any(s => s.Lo <= hi && lo <= s.Hi)) lane++;
            if (lane == taken.Count) taken.Add([]);
            taken[lane].Add((lo, hi));
            lanes[i] = lane;
        }
        return lanes;
    }
}

public sealed partial class VaultStore
{
    void InitReadingPaths() =>
        Exec("CREATE TABLE IF NOT EXISTS reading_paths(id INTEGER PRIMARY KEY, name TEXT NOT NULL, color TEXT, steps TEXT, created TEXT, updated TEXT)");

    static ReadingPath ReadPath(Microsoft.Data.Sqlite.SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2),
        StudyPassage.ParseList(r.IsDBNull(3) ? "" : r.GetString(3)).Where(p => p.From > 0).ToList());

    public List<ReadingPath> GetReadingPaths() =>
        Query("SELECT id,name,color,steps FROM reading_paths ORDER BY name COLLATE NOCASE, id", _ => { }, ReadPath);

    public ReadingPath? GetReadingPath(long id) =>
        Query("SELECT id,name,color,steps FROM reading_paths WHERE id=$i", c => c.Parameters.AddWithValue("$i", id), ReadPath).FirstOrDefault();

    public long AddReadingPath(string name, string color, IEnumerable<StudyPassage> steps)
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO reading_paths(name,color,steps,created,updated) VALUES($n,$c,$s,$t,$t); SELECT last_insert_rowid()";
        cmd.Parameters.AddWithValue("$n", name.Trim()); cmd.Parameters.AddWithValue("$c", color);
        cmd.Parameters.AddWithValue("$s", StudyPassage.Join(steps)); cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        return (long)cmd.ExecuteScalar()!;
    }

    public void UpdateReadingPath(ReadingPath p) =>
        Exec("UPDATE reading_paths SET name=$n, color=$c, steps=$s, updated=$t WHERE id=$i", c =>
        {
            c.Parameters.AddWithValue("$n", p.Name.Trim()); c.Parameters.AddWithValue("$c", p.Color);
            c.Parameters.AddWithValue("$s", StudyPassage.Join(p.Steps)); c.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            c.Parameters.AddWithValue("$i", p.Id);
        });

    public void DeleteReadingPath(long id) => Exec("DELETE FROM reading_paths WHERE id=$i", c => c.Parameters.AddWithValue("$i", id));
}
