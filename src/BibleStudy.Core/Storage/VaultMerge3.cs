using BibleStudy.Core.Models;

namespace BibleStudy.Core.Storage;

/// <summary>What a three-way merge did besides the plain merge: Taken = changes of the other side taken over, Deleted = items deleted here because the other side deleted them, Held = deletions that were not done because too many at once looked like a lost copy.</summary>
public sealed record ThreeWayResult(MergeResult Merge, int Taken, int Deleted, int Held);

public sealed partial class VaultStore
{
    /// <summary>
    /// Merges another copy with the help of the state both had after their last comparison (<paramref name="basePath"/>), so that it is clear which side changed what:
    /// changed only there → taken over here; changed only here → stays (the old text of the other side is not added to it); deleted only there and unchanged here →
    /// deleted here too (into the trash). Changed on both sides → both kept, as <see cref="MergeFrom(string, string)"/> does.
    /// A side that never had an item never deletes it: an empty or new copy only adds. When more than a few items, or more than a quarter, would be deleted
    /// at once, nothing is deleted (a reset or reinstalled device must not empty the other one).
    /// <paramref name="otherDbPath"/> must be a copy that may be changed.
    /// </summary>
    public ThreeWayResult MergeFrom(string otherDbPath, string copySuffix, string? basePath)
    {
        if (basePath is null || !File.Exists(basePath)) return new ThreeWayResult(MergeFrom(otherDbPath, copySuffix), 0, 0, 0);
        // before the copies are opened (opening makes the lists they lack): which lists does an older version's copy or the old state not have at all?
        var skipLists = ListsMissingIn(otherDbPath); skipLists.UnionWith(ListsMissingIn(basePath));
        var bas = new VaultStore(basePath, "base");
        var other = new VaultStore(otherDbPath, "other");
        int taken = 0;
        var deletions = new List<Action>();

        // marks: the same words of the same verses; what can change is the color, the note and the tags
        static Dictionary<string, Mark> Marks(VaultStore s) { var d = new Dictionary<string, Mark>(); foreach (var m in s.GetMarks()) d.TryAdd(MarkKey(m), m); return d; }
        static bool SameMark(Mark a, Mark b) => a.Color == b.Color && a.Note.Trim() == b.Note.Trim() && a.Tags.Order().SequenceEqual(b.Tags.Order());
        var mineMarks = Marks(this); var theirMarks = Marks(other);
        foreach (var (key, b) in Marks(bas))
        {
            mineMarks.TryGetValue(key, out var m); theirMarks.TryGetValue(key, out var t);
            var mineSame = m is not null && SameMark(m, b); var theirSame = t is not null && SameMark(t, b);
            if (mineSame && t is null) { var id = m!.Id; deletions.Add(() => TrashMark(id)); }
            else if (mineSame && !theirSame) { UpdateMark(m!.Id, t!.Color, t.Note, m.FolderId, t.Tags); taken++; }
            else if (theirSame && m is null) other.DeleteMark(t!.Id);                                   // deleted here: it must not come back
            else if (theirSame && !mineSame) other.UpdateMark(t!.Id, m!.Color, m.Note, t.FolderId, m.Tags);   // the old state there must not be added to the new one here
        }

        // notes: one per verse
        var mineNotes = GetNotes().ToDictionary(n => n.Ref); var theirNotes = other.GetNotes().ToDictionary(n => n.Ref);
        foreach (var b in bas.GetNotes())
        {
            mineNotes.TryGetValue(b.Ref, out var m); theirNotes.TryGetValue(b.Ref, out var t);
            var was = Unnumbered(b.Text).Trim();
            var mineSame = m is not null && Unnumbered(m.Text).Trim() == was; var theirSame = t is not null && Unnumbered(t.Text).Trim() == was;
            if (mineSame && t is null) { var r = b.Ref; deletions.Add(() => TrashNote(r)); }
            else if (mineSame && !theirSame) { SetNote(b.Ref, Unnumbered(t!.Text)); taken++; }
            else if (theirSame && m is null) other.SetNote(b.Ref, "");
            else if (theirSame && !mineSame) other.SetNote(b.Ref, Unnumbered(m!.Text));
        }

        // documents: the same title is the same document
        static Dictionary<string, Document> Docs(VaultStore s) { var d = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase); foreach (var x in s.GetDocuments()) d.TryAdd(x.Title, x); return d; }
        var mineDocs = Docs(this); var theirDocs = Docs(other);
        foreach (var (title, b) in Docs(bas))
        {
            mineDocs.TryGetValue(title, out var m); theirDocs.TryGetValue(title, out var t);
            var was = Unnumbered(b.Body).Trim();
            var mineSame = m is not null && Unnumbered(m.Body).Trim() == was; var theirSame = t is not null && Unnumbered(t.Body).Trim() == was;
            if (mineSame && t is null) { var id = m!.Id; deletions.Add(() => TrashDocument(id)); }
            else if (mineSame && !theirSame) { UpdateDocument(m!.Id, m.Title, Unnumbered(t!.Body), m.Path, m.IsTemplate, m.Tags, m.FolderId); taken++; }
            else if (theirSame && m is null) other.DeleteDocument(t!.Id);
            else if (theirSame && !mineSame) other.UpdateDocument(t!.Id, t.Title, Unnumbered(m!.Body), t.Path, t.IsTemplate, t.Tags, t.FolderId);
        }

        // the plain lists: to-dos, bookmarks, the reading queue, prayers, own headings and cross references, the archive of the Bible studies
        var listCount = MergeLists(other, bas, deletions, ref taken, skipLists);

        var total = mineMarks.Count + mineNotes.Count + mineDocs.Count + listCount;
        var held = 0;
        if (deletions.Count > Math.Max(5, total / 4)) held = deletions.Count;   // looks like a lost or reset copy over there: keep everything
        else foreach (var d in deletions) d();
        var merge = MergeFrom(otherDbPath, copySuffix);
        return new ThreeWayResult(merge, taken, held > 0 ? 0 : deletions.Count, held);
    }
}
