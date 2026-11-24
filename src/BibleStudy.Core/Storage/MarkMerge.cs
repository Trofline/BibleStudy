namespace BibleStudy.Core.Storage;

public sealed partial class VaultStore
{
    /// <summary>
    /// Whether these marks can become one: at least two, all marks of whole verses, in one chapter, and the verses follow each other without a gap.
    /// </summary>
    public static bool CanMergeMarks(IReadOnlyList<Mark> marks)
    {
        if (marks.Count < 2 || marks.Any(m => !m.WholeVerse)) return false;
        var first = marks[0];
        if (marks.Any(m => m.Ref.Book != first.Ref.Book || m.Ref.Chapter != first.Ref.Chapter)) return false;
        var reach = 0;
        foreach (var m in marks.OrderBy(m => m.Ref.Verse).ThenBy(m => m.LastVerse))
        {
            if (reach > 0 && m.Ref.Verse > reach + 1) return false;   // a verse in between that is not marked
            reach = Math.Max(reach, m.LastVerse);
        }
        return true;
    }

    /// <summary>
    /// Makes one mark of several that follow each other (see <see cref="CanMergeMarks"/>): it takes the colour and folder of the first, the notes and tags of all.
    /// The old marks go. Returns the new mark, or null when they cannot be put together.
    /// </summary>
    public Mark? MergeMarks(IReadOnlyList<Mark> marks)
    {
        if (!CanMergeMarks(marks)) return null;
        var sorted = marks.OrderBy(m => m.Ref.Verse).ThenBy(m => m.LastVerse).ToList();
        var first = sorted[0];
        var last = sorted.Max(m => m.LastVerse);
        var notes = sorted.Select(m => m.Note.Trim()).Where(n => n.Length > 0).Distinct().ToList();
        var tags = sorted.SelectMany(m => m.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var text = string.Join(" ", sorted.Select(m => m.Text.Trim()).Where(t => t.Length > 0));
        foreach (var m in sorted) DeleteMark(m.Id);
        var id = AddMark(first.TranslationId, first.Ref, -1, -1, text, first.Color, string.Join("\n\n", notes), first.FolderId, tags, last);
        return GetMarks(first.Ref.Book, first.Ref.Chapter).FirstOrDefault(m => m.Id == id);
    }

    /// <summary>
    /// Cuts a mark that covers several whole verses into one mark per verse (colour, note, folder and tags go to each). <paramref name="verseText"/> gives the text of a verse.
    /// Returns the new marks, or an empty list when the mark covers only one verse or is not a whole-verse mark.
    /// </summary>
    public List<Mark> SplitMark(Mark mark, Func<int, string> verseText)
    {
        if (!mark.WholeVerse || !mark.MultiVerse) return [];
        DeleteMark(mark.Id);
        var made = new HashSet<long>();
        for (var v = mark.Ref.Verse; v <= mark.LastVerse; v++)
            made.Add(AddMark(mark.TranslationId, new Models.VerseRef(mark.Ref.Book, mark.Ref.Chapter, v), -1, -1, verseText(v).Trim(), mark.Color, mark.Note, mark.FolderId, mark.Tags));
        return [.. GetMarks(mark.Ref.Book, mark.Ref.Chapter).Where(m => made.Contains(m.Id))];
    }
}
