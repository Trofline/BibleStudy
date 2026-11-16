namespace BibleStudy.Core.Storage;

public sealed partial class VaultStore
{
    /// <summary>
    /// A plain list of a vault for the three-way merge: <paramref name="Key"/> says which rows are the same entry (an SQL expression over the row "t"),
    /// <paramref name="Columns"/> are what can change about it.
    /// </summary>
    sealed record ListSpec(string Table, string Key, string[] Columns);

    static readonly ListSpec[] Lists =
    [
        // a to-do is its text under its parent task; ticking off, the date and the verse can change
        new("todos", "IFNULL((SELECT p.text FROM todos p WHERE p.id=t.parent_id),'')||char(1)||IFNULL(t.text,'')", ["done", "due", "book", "chapter", "verse"]),
        new("bookmarks", "t.book||'|'||t.chapter||'|'||IFNULL(t.verse,'')", ["text"]),
        new("queue", "t.book||'|'||t.chapter||'|'||IFNULL(t.verse,'')", ["label"]),
        new("prayers", "IFNULL(t.text,'')||char(1)||IFNULL(t.created,'')", ["answered", "note", "book", "chapter", "verse"]),
        new("headings", "t.book||'|'||t.chapter||'|'||IFNULL(t.verse,'')||'|'||t.text", ["level"]),
        new("my_xrefs", "t.fbook||'|'||t.fchapter||'|'||IFNULL(t.fverse,'')||'>'||t.tbook||'|'||t.tchapter||'|'||IFNULL(t.tverse,'')", ["tend", "note"]),
        new("hidden_xrefs", "t.fbook||'|'||t.fchapter||'|'||IFNULL(t.fverse,'')||'>'||t.tbook||'|'||t.tchapter||'|'||IFNULL(t.tverse,'')", ["hidden"]),
        new("studies", "t.day||char(1)||t.title", ["passages", "notes", "questions"]),
    ];

    sealed record ListRow(long Id, object?[] Values)
    {
        public bool Same(ListRow o) => Values.Select(v => v?.ToString() ?? "").SequenceEqual(o.Values.Select(v => v?.ToString() ?? ""));
    }

    Dictionary<string, ListRow> ListRows(ListSpec spec)
    {
        var rows = new Dictionary<string, ListRow>();
        try
        {
            foreach (var (key, row) in Query($"SELECT t.id, {spec.Key}, {string.Join(",", spec.Columns.Select(c => "t." + c))} FROM {spec.Table} t", _ => { },
                         r => (r.IsDBNull(1) ? "" : r.GetValue(1).ToString() ?? "", new ListRow(r.GetInt64(0), [.. Enumerable.Range(2, spec.Columns.Length).Select(i => r.IsDBNull(i) ? null : r.GetValue(i))]))))
                rows.TryAdd(key, row);
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { /* an older copy without this list (or a column of it) */ }
        return rows;
    }

    void SetListRow(ListSpec spec, long id, object?[] values) =>
        Exec($"UPDATE {spec.Table} SET {string.Join(",", spec.Columns.Select((c, i) => $"{c}=$v{i}"))} WHERE id=$id", c =>
        {
            for (var i = 0; i < values.Length; i++) c.Parameters.AddWithValue("$v" + i, values[i] ?? DBNull.Value);
            c.Parameters.AddWithValue("$id", id);
        });

    void DeleteListRow(ListSpec spec, long id) => Exec($"DELETE FROM {spec.Table} WHERE id=$id", c => c.Parameters.AddWithValue("$id", id));

    /// <summary>
    /// The plain lists in the three-way merge (see <see cref="MergeFrom(string, string, string?)"/>): an entry changed only there is taken over, deleted only there
    /// is deleted here (through <paramref name="deletions"/>, which may be held back), changed or deleted only here is set in the other copy so that the
    /// plain merge afterwards does not bring the old state back. Returns how many entries there were here (for the guard against mass deletions).
    /// </summary>
    int MergeLists(VaultStore other, VaultStore bas, List<Action> deletions, ref int taken)
    {
        var total = 0;
        foreach (var spec in Lists)
        {
            var mine = ListRows(spec); var theirs = other.ListRows(spec);
            total += mine.Count;
            foreach (var (key, b) in bas.ListRows(spec))
            {
                mine.TryGetValue(key, out var m); theirs.TryGetValue(key, out var t);
                var mineSame = m is not null && m.Same(b); var theirSame = t is not null && t.Same(b);
                if (mineSame && t is null) { var id = m!.Id; var s = spec; deletions.Add(() => DeleteListRow(s, id)); }
                else if (mineSame && !theirSame) { SetListRow(spec, m!.Id, t!.Values); taken++; }
                else if (theirSame && m is null) other.DeleteListRow(spec, t!.Id);          // deleted here: it must not come back
                else if (theirSame && !mineSame) other.SetListRow(spec, t!.Id, m!.Values);   // changed here: the old state there must not win
            }
        }
        return total;
    }
}
