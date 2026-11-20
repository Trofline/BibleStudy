using BibleStudy.Core.Models;

namespace BibleStudy.Core.Storage;

public sealed partial class VaultStore
{
    /// <summary>
    /// Copies marks of this vault into <paramref name="target"/>: color, note, tags, the hand-corrected places and the folder (the same chain of folder names,
    /// made there when it is missing). A mark that is there already (the same words of the same verses) is not copied again.
    /// Returns the ids of the marks copied, those already there counted as copied too (so a move may delete them here).
    /// </summary>
    public List<long> CopyMarksTo(VaultStore target, IEnumerable<long> ids)
    {
        var mine = GetMarks().ToDictionary(m => m.Id);
        var there = target.GetMarks().Select(MarkKey).ToHashSet();
        var places = GetMarkPlaces();
        var folders = new FolderCopier(this, target);
        var done = new List<long>();
        foreach (var id in ids.Distinct())
        {
            if (!mine.TryGetValue(id, out var m)) continue;
            done.Add(id);
            if (!there.Add(MarkKey(m))) continue;
            var made = target.AddMark(m.TranslationId, m.Ref, m.Start, m.End, m.Text, m.Color, m.Note, folders.Map(m.FolderId), m.Tags, m.EndVerse);
            foreach (var ((mark, translation, verse), (start, end)) in places)
                if (mark == id) target.SetMarkPlace(made, translation, verse, start, end);
        }
        return done;
    }

    /// <summary>Copies notes into <paramref name="target"/> (with their folder). A note on a verse that has one there already is joined to it, nothing written is lost. Returns the verses copied.</summary>
    public List<VerseRef> CopyNotesTo(VaultStore target, IEnumerable<VerseRef> refs)
    {
        var mine = GetNotes().ToDictionary(n => n.Ref);
        var there = target.GetNotes().ToDictionary(n => n.Ref);
        var folders = new FolderCopier(this, target);
        var done = new List<VerseRef>();
        foreach (var r in refs.Distinct())
        {
            if (!mine.TryGetValue(r, out var n)) continue;
            done.Add(r);
            var text = there.TryGetValue(r, out var t) ? MergeText(t.Text, n.Text, out _) : n.Text;
            target.SetNote(r, text);
            if (t?.FolderId is null && folders.Map(n.FolderId) is long f) target.SetNoteFolder(r, f);
        }
        return done;
    }

    /// <summary>A folder of one vault to the folder of the same names in another, made there when it is missing.</summary>
    sealed class FolderCopier(VaultStore from, VaultStore to)
    {
        readonly Dictionary<long, Folder> _from = from.GetFolders().ToDictionary(f => f.Id);
        readonly List<Folder> _to = to.GetFolders();
        readonly Dictionary<long, long> _done = [];

        public long? Map(long? id)
        {
            if (id is not long f || !_from.TryGetValue(f, out var folder)) return null;
            if (_done.TryGetValue(f, out var known)) return known;
            var parent = folder.ParentId is long p && p != f ? Map(p) : null;
            var match = _to.FirstOrDefault(x => x.ParentId == parent && string.Equals(x.Name.Trim(), folder.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            long made;
            if (match is not null) made = match.Id;
            else
            {
                made = to.AddFolder(folder.Name);
                if (parent is long pp) to.SetFolderParent(made, pp);
                _to.Add(new Folder(made, folder.Name.Trim(), parent));
            }
            return _done[f] = made;
        }
    }
}
