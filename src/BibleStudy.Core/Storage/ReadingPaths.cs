using System.Globalization;

namespace BibleStudy.Core.Storage;

/// <summary>
/// A reading path: verses (or marked verses) in the order in which they are best read, one building on the other. Each step is a verse or a
/// few verses of one chapter (<see cref="StudyPassage"/> with From &gt; 0); the same verse may come twice.
/// </summary>
/// <remarks><paramref name="Folder"/> is the folder of the explorer the path belongs to, as the names from the top down (see <see cref="VaultStore.FolderKey"/>):
/// folder numbers differ from device to device, the names do not. Empty = no folder.</remarks>
public sealed record ReadingPath(long Id, string Name, string Color, IReadOnlyList<StudyPassage> Steps, string Folder = "")
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
    void InitReadingPaths()
    {
        Exec("CREATE TABLE IF NOT EXISTS reading_paths(id INTEGER PRIMARY KEY, name TEXT NOT NULL, color TEXT, steps TEXT, folder TEXT, created TEXT, updated TEXT)");
        // the folder of the explorer came with 1.9.26 (paths made before belong to none)
        if (Query("SELECT 1 FROM pragma_table_info('reading_paths') WHERE name='folder'", _ => { }, _ => 1).Count == 0)
            Exec("ALTER TABLE reading_paths ADD COLUMN folder TEXT");
    }

    const string PathColumns = "id,name,color,steps,folder";

    static ReadingPath ReadPath(Microsoft.Data.Sqlite.SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2),
        StudyPassage.ParseList(r.IsDBNull(3) ? "" : r.GetString(3)).Where(p => p.From > 0).ToList(),
        r.IsDBNull(4) ? "" : r.GetString(4));

    public List<ReadingPath> GetReadingPaths() =>
        Query($"SELECT {PathColumns} FROM reading_paths ORDER BY name COLLATE NOCASE, id", _ => { }, ReadPath);

    public ReadingPath? GetReadingPath(long id) =>
        Query($"SELECT {PathColumns} FROM reading_paths WHERE id=$i", c => c.Parameters.AddWithValue("$i", id), ReadPath).FirstOrDefault();

    /// <summary>The path that was made or changed last (for "add to the last path" with one tap).</summary>
    public ReadingPath? GetLastReadingPath() =>
        Query($"SELECT {PathColumns} FROM reading_paths ORDER BY IFNULL(updated, created) DESC, id DESC LIMIT 1", _ => { }, ReadPath).FirstOrDefault();

    /// <summary>The paths that belong to this folder of the explorer.</summary>
    public List<ReadingPath> GetReadingPathsIn(long folder)
    {
        var key = FolderKey(folder);
        return key.Length == 0 ? [] : GetReadingPaths().Where(p => p.Folder == key).ToList();
    }

    /// <summary>Puts a path into a folder of the explorer (null = into none).</summary>
    public void SetReadingPathFolder(long id, long? folder) =>
        Exec("UPDATE reading_paths SET folder=$f, updated=$t WHERE id=$i", c =>
        {
            c.Parameters.AddWithValue("$f", folder is long f ? FolderKey(f) : "");
            c.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            c.Parameters.AddWithValue("$i", id);
        });

    /// <summary>A folder as the names from the top down (a sign no one types between them); empty when there is no such folder.</summary>
    public string FolderKey(long folder) => FolderKeys(GetFolders()).GetValueOrDefault(folder, "");

    static Dictionary<long, string> FolderKeys(List<Folder> folders)
    {
        var byId = folders.ToDictionary(f => f.Id);
        var keys = new Dictionary<long, string>();
        foreach (var f in folders)
        {
            var names = new List<string>();
            for (Folder? at = f; at is not null && names.Count < 50; at = at.ParentId is long p ? byId.GetValueOrDefault(p) : null) names.Insert(0, at.Name.Trim());
            keys[f.Id] = string.Join('\u001f', names);
        }
        return keys;
    }

    /// <summary>A folder renamed, moved or deleted: the paths in it (and in the folders below) go along; those of a deleted folder belong to none.</summary>
    void KeepPathFolders(Action change)
    {
        var before = FolderKeys(GetFolders());
        change();
        var after = FolderKeys(GetFolders());
        foreach (var (id, old) in before)
        {
            var now = after.GetValueOrDefault(id, "");
            if (now != old)
                Exec("UPDATE reading_paths SET folder=$n WHERE folder=$o", c => { c.Parameters.AddWithValue("$n", now); c.Parameters.AddWithValue("$o", old); });
        }
    }

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
