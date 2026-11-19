namespace BibleStudy.Core.Storage;

/// <summary>
/// The marks in the materials travel between the devices. A file lies at another place on every device, so a mark is known by the name of its file,
/// its place in the text and its words. Merging needs no earlier state: what is new comes over, a deletion leaves a note behind ("gone") that travels
/// too, and of two changes of the same mark the later one counts. So no mark can be lost because a device had never seen it.
/// </summary>
public sealed partial class VaultStore
{
    /// <summary>How long the notes of deleted marks are kept: a device that comes back after longer may bring such a mark back.</summary>
    const int MaterialGoneDays = 400;

    void InitMaterialSync()
    {
        if (Query("SELECT 1 FROM pragma_table_info('material_marks') WHERE name='changed'", _ => { }, _ => 1).Count == 0)
            Exec("ALTER TABLE material_marks ADD COLUMN changed TEXT");
        Exec("CREATE TABLE IF NOT EXISTS material_gone(key TEXT PRIMARY KEY, deleted TEXT NOT NULL)");
        Exec("DELETE FROM material_gone WHERE deleted < $d", c => c.Parameters.AddWithValue("$d", DateTime.UtcNow.AddDays(-MaterialGoneDays).ToString("o")));
    }

    static string MaterialKey(string name, int block, int start, string text) => $"{name.ToLowerInvariant()}\u0001{block}\u0001{start}\u0001{text}";
    static string MaterialKey(MaterialMark m) => MaterialKey(m.FileName, m.Block, m.Start, m.Text);

    void NoteMaterialGone(MaterialMark m) =>
        Exec("INSERT INTO material_gone(key,deleted) VALUES($k,$d) ON CONFLICT(key) DO UPDATE SET deleted=MAX(deleted, excluded.deleted)", c =>
        {
            c.Parameters.AddWithValue("$k", MaterialKey(m)); c.Parameters.AddWithValue("$d", DateTime.UtcNow.ToString("o"));
        });

    sealed record MaterialRow(long Id, string Path, string Name, int Block, int Start, string Text, string Color, string Note, string Created, string Changed)
    {
        public string Key => MaterialKey(Name, Block, Start, Text);
    }

    List<MaterialRow> MaterialRows() =>
        Query("SELECT id,path,name,block,start_off,text,color,note,created,changed FROM material_marks", _ => { }, r =>
        {
            var created = r.IsDBNull(8) ? "" : r.GetString(8);
            return new MaterialRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? 0 : r.GetInt32(3), r.IsDBNull(4) ? 0 : r.GetInt32(4), r.GetString(5),
                r.IsDBNull(6) ? "" : r.GetString(6), r.IsDBNull(7) ? "" : r.GetString(7), created, r.IsDBNull(9) ? created : r.GetString(9));
        });

    Dictionary<string, string> MaterialGone() =>
        Query("SELECT key,deleted FROM material_gone", _ => { }, r => (r.GetString(0), r.GetString(1))).ToDictionary(x => x.Item1, x => x.Item2);

    /// <summary>
    /// Brings the marks in the materials of another copy into this one: the notes of deleted marks of both are joined; a mark deleted after it was
    /// made goes on both sides; one that is not here comes in; of one that both have, the later change counts. Returns how many changed here.
    /// </summary>
    int MergeMaterialMarks(VaultStore other)
    {
        var gone = MaterialGone();
        foreach (var (key, when) in other.MaterialGone())
            if (!gone.TryGetValue(key, out var mine) || string.CompareOrdinal(when, mine) > 0)
            {
                gone[key] = when;
                Exec("INSERT INTO material_gone(key,deleted) VALUES($k,$d) ON CONFLICT(key) DO UPDATE SET deleted=excluded.deleted", c => { c.Parameters.AddWithValue("$k", key); c.Parameters.AddWithValue("$d", when); });
            }
        bool Deleted(MaterialRow m) => gone.TryGetValue(m.Key, out var when) && string.CompareOrdinal(when, m.Created) >= 0;

        var changed = 0;
        var here = new Dictionary<string, MaterialRow>();
        foreach (var m in MaterialRows())
        {
            if (Deleted(m)) { Exec("DELETE FROM material_marks WHERE id=$i", c => c.Parameters.AddWithValue("$i", m.Id)); changed++; continue; }
            if (!here.TryAdd(m.Key, m)) { Exec("DELETE FROM material_marks WHERE id=$i", c => c.Parameters.AddWithValue("$i", m.Id)); changed++; }   // a second copy of the same mark
        }
        foreach (var o in other.MaterialRows())
        {
            if (Deleted(o)) continue;
            if (!here.TryGetValue(o.Key, out var m))
            {
                here[o.Key] = o with { Id = InsertMaterialMark(o.Path, o.Block, o.Start, o.Text, o.Color, o.Note, o.Created, o.Changed) };
                changed++;
            }
            else if ((m.Color != o.Color || m.Note != o.Note) && string.CompareOrdinal(o.Changed, m.Changed) > 0)
            {
                Exec("UPDATE material_marks SET color=$c, note=$o, changed=$g WHERE id=$i", c =>
                {
                    c.Parameters.AddWithValue("$c", o.Color); c.Parameters.AddWithValue("$o", o.Note); c.Parameters.AddWithValue("$g", o.Changed); c.Parameters.AddWithValue("$i", m.Id);
                });
                changed++;
            }
        }
        return changed;
    }
}
