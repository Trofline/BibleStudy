using Avalonia.Controls;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Marks of whole verses that follow each other can be put together into one mark (one entry in the lists, one block in the text), and such a mark can be cut
/// into single verses again, so that e.g. only the middle verse can be deleted. In the explorer and in the text.
/// </summary>
public partial class MainWindow
{
    /// <summary>The text of every verse of a chapter (the shown translation), for the single marks of a mark that is cut up.</summary>
    async Task<Func<int, string>> VerseTextsAsync(string book, int chapter)
    {
        if (book == _curBook && chapter == _curChapter && _curChapterData is not null) return VerseTextOf;
        try
        {
            if (TranslationBox.SelectedItem is TranslationInfo t)
            {
                var ch = await _provider.GetChapterAsync(t.Id, book, chapter);
                return v => ch.Verses.FirstOrDefault(x => x.Number == v) is { } verse ? string.Concat(verse.Segments.Where(s => s.NoteId is null).Select(s => s.Text)) : "";
            }
        }
        catch { /* the text of the verses is only the short text kept with the mark */ }
        return _ => "";
    }

    // ---- in the explorer ---------------------------------------------------------------------------------------------

    /// <summary>"Zusammenfassen" and "Trennen" for the chosen marks. Looked up when the menu opens (the menu is made for every row).</summary>
    void AddMarkMergeItems(ContextMenu menu, List<ExItem> targets)
    {
        var added = new List<Control>();
        menu.Opening += (_, _) =>
        {
            foreach (var x in added) menu.Items.Remove(x);
            added.Clear();
            var ids = targets.Where(i => i.Kind == ExKind.Mark && !Foreign(i)).Select(i => i.Id).ToHashSet();
            if (ids.Count == 0 || ids.Count != targets.Count) return;
            var marks = _vault.GetMarks().Where(m => ids.Contains(m.Id)).ToList();
            var at = menu.Items.OfType<Separator>().Select(s => menu.Items.IndexOf(s)).DefaultIfEmpty(menu.Items.Count).First();
            if (VaultStore.CanMergeMarks(marks)) { var i = MenuAction(L("Zu einer Markierung zusammenfassen"), () => _ = ExMergeMarksAsync(marks)); menu.Items.Insert(at++, i); added.Add(i); }
            if (marks.Count == 1 && marks[0].WholeVerse && marks[0].MultiVerse) { var i = MenuAction(L("In einzelne Verse trennen"), () => _ = ExSplitMarkAsync(marks[0])); menu.Items.Insert(at++, i); added.Add(i); }
        };
    }

    async Task ExMergeMarksAsync(List<Mark> marks)
    {
        if (_vault.MergeMarks(marks) is not { } made) { ExNote(L("Diese Markierungen lassen sich nicht zusammenfassen."), false); return; }
        ExRemember(L("Markierungen zusammengefasst"), () => { _vault.DeleteMark(made.Id); foreach (var m in marks) _vault.RestoreMark(m); }, true);
        _exSel.Clear();
        AfterExplorerChange();
        ExNote(L("{0} Markierungen sind jetzt eine: {1}.", marks.Count, MarkLabel(made)), true);
        LoadChapterMarks(); await RedrawChapterAsync(); RefreshCells();
    }

    async Task ExSplitMarkAsync(Mark mark)
    {
        var texts = await VerseTextsAsync(mark.Ref.Book, mark.Ref.Chapter);
        var pieces = _vault.SplitMark(mark, texts);
        if (pieces.Count == 0) return;
        ExRemember(L("Markierung getrennt"), () => { foreach (var p in pieces) _vault.DeleteMark(p.Id); _vault.RestoreMark(mark); }, true);
        _exSel.Clear();
        AfterExplorerChange();
        ExNote(L("{0} ist jetzt in {1} einzelne Verse getrennt.", MarkLabel(mark), pieces.Count), true);
        LoadChapterMarks(); await RedrawChapterAsync(); RefreshCells();
    }

    // ---- in the text ---------------------------------------------------------------------------------------------------

    /// <summary>The whole-verse marks of this chapter under the verses that are chosen (or the one clicked).</summary>
    List<Mark> MarksUnder(int clicked)
    {
        var verses = _selVerses.Contains(clicked) && _selVerses.Count > 1 ? _selVerses.ToHashSet() : [clicked];
        return [.. _chapterMarks.Where(m => m.WholeVerse && Enumerable.Range(m.Ref.Verse, m.LastVerse - m.Ref.Verse + 1).Any(verses.Contains))];
    }

    /// <summary>Menu entries of the verse menu: put together the marks under the chosen verses, or cut up the multi-verse mark under this one.</summary>
    IEnumerable<MenuItem> MarkMergeMenuItems(int verse)
    {
        var under = MarksUnder(verse);
        if (VaultStore.CanMergeMarks(under))
            yield return MarkMergeEntry(L("Markierungen zu einer zusammenfassen"), async () =>
            {
                PushUndo();
                if (_vault.MergeMarks(under) is null) return;
                await AfterTextMarkChangeAsync(L("Markierungen zusammengefasst."));
            });
        if (under.FirstOrDefault(m => m.MultiVerse) is { } multi)
            yield return MarkMergeEntry(L("Markierung in einzelne Verse trennen"), async () =>
            {
                var texts = await VerseTextsAsync(multi.Ref.Book, multi.Ref.Chapter);
                PushUndo();
                if (_vault.SplitMark(multi, texts).Count == 0) return;
                await AfterTextMarkChangeAsync(L("Markierung in einzelne Verse getrennt."));
            });
    }

    MenuItem MarkMergeEntry(string text, Func<Task> act)
    {
        var i = new MenuItem { Header = text };
        i.Click += async (_, _) => await act();
        return i;
    }

    async Task AfterTextMarkChangeAsync(string message)
    {
        _selVerses.Clear(); _selected = null; _anchorVerse = null; ThreadPane.SelectedVerse = null;
        LoadChapterMarks(); await RedrawChapterAsync();
        RefreshCollection(); RefreshCells();
        ShowUndoToast(message, UndoMarksAsync);
    }
}
