using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Marking, marking over and removing: what is marked anew replaces only that part of an older mark, a mark over several verses stays one mark,
/// the page is drawn again where it changed (no new build, so no flicker and no jump), and the last steps can be taken back with Ctrl+Z.
/// </summary>
public partial class MainWindow
{
    // ---- taking steps back --------------------------------------------------------------------------------------------

    /// <summary>
    /// Trash: the entries a removal put into the trash (thrown away again when the step is taken back, so nothing comes back twice).
    /// Jump: taking the step back opens its chapter (not for a mark deleted in the list of marks, far from the text).
    /// </summary>
    sealed record UndoStep(string Vault, string Book, int Chapter, List<Mark> Marks, List<long>? Trash = null, bool Jump = true)
    {
        /// <summary>The places corrected by hand of these marks in other translations (they would be lost with the marks put back).</summary>
        public List<(long Mark, string Translation, int Verse, int Start, int End)> Places { get; init; } = [];
    }

    readonly List<UndoStep> _markUndo = [];

    /// <summary>Remembers the marks of this chapter as they are now (call before every change of marks).</summary>
    void PushUndo() => PushUndo(_curBook, _curChapter);

    void PushUndo(string book, int chapter, bool jump = true)
    {
        var marks = _vault.GetMarks(book, chapter); var ids = marks.Select(m => m.Id).ToHashSet();
        var places = _vault.GetMarkPlaces().Where(p => ids.Contains(p.Key.Mark)).Select(p => (p.Key.Mark, p.Key.Translation, p.Key.Verse, p.Value.Start, p.Value.End)).ToList();
        _markUndo.Add(new UndoStep(_vault.Name, book, chapter, marks, Jump: jump) { Places = places });
        if (_markUndo.Count > 40) _markUndo.RemoveAt(0);
    }

    async Task UndoMarksAsync()
    {
        if (_markUndo.Count == 0) { StatusText.Text = L("Nichts rückgängig zu machen."); return; }
        var step = _markUndo[^1];
        if (step.Vault != _vault.Name) { _markUndo.Clear(); StatusText.Text = L("Nichts rückgängig zu machen."); return; }
        _markUndo.RemoveAt(_markUndo.Count - 1);
        if (step.Jump && (step.Book != _curBook || step.Chapter != _curChapter)) await GoToAsync(step.Book, step.Chapter);
        _vault.DeleteMarks(_vault.GetMarks(step.Book, step.Chapter).Select(m => m.Id));
        foreach (var m in step.Marks)
        {
            var id = _vault.AddMark(m.TranslationId, m.Ref, m.Start, m.End, m.Text, m.Color, m.Note, m.FolderId, m.Tags, m.EndVerse);
            foreach (var p in step.Places.Where(p => p.Mark == m.Id)) _vault.SetMarkPlace(id, p.Translation, p.Verse, p.Start, p.End);
        }
        foreach (var t in step.Trash ?? []) _vault.DeleteFromTrash(t);
        HideUndoToast();
        ClearTextSelection(true);
        RefreshCollection(); RefreshCells();
        StatusText.Text = L("Markierung rückgängig gemacht.");
    }

    // ---- reading the page ---------------------------------------------------------------------------------------------

    /// <summary>The plain text of one verse in one translation of this chapter ("" if it is not there).</summary>
    string RawVerse(string translationId, int verse) =>
        _markSources.TryGetValue(translationId, out var ch) && ch.Verses.FirstOrDefault(v => v.Number == verse) is { } v ? MarkLogic.RawText(v.Segments) : "";

    /// <summary>The marked text from (verse, offset) to (verse, offset), the verses joined by a blank.</summary>
    string RangeText(string translationId, int firstVerse, int start, int lastVerse, int end)
    {
        var parts = new List<string>();
        for (var v = firstVerse; v <= lastVerse; v++)
        {
            var raw = RawVerse(translationId, v);
            var s = v == firstVerse ? Math.Clamp(start, 0, raw.Length) : 0;
            var e = v == lastVerse ? Math.Clamp(end, 0, raw.Length) : raw.Length;
            if (e > s) parts.Add(raw[s..e].Trim());
        }
        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    string VerseRangeText(int from, int to) => string.Join(" ", Enumerable.Range(from, to - from + 1).Select(v => VerseTextOf(v).Trim()).Where(t => t.Length > 0));

    /// <summary>The label of a mark: "Genesis 1:4" or, over several verses, "Genesis 1:4–6".</summary>
    string MarkLabel(Mark m) => m.MultiVerse ? $"{BookName(m.Ref.Book)} {m.Ref.Chapter}:{m.Ref.Verse}–{m.LastVerse}" : Label(m.Ref);

    // ---- cutting out of older marks ---------------------------------------------------------------------------------

    /// <summary>Puts back what is left of a mark after a part was cut out of it (the mark itself is deleted; note, tags and folder go to every piece).</summary>
    int KeepRemnants(Mark old, IEnumerable<MarkLogic.Spec> specs)
    {
        _vault.DeleteMark(old.Id);
        var kept = 0;
        foreach (var sp in specs)
        {
            if (sp.Whole)
            {
                var from = new VerseRef(old.Ref.Book, old.Ref.Chapter, sp.FirstVerse);
                _vault.AddMark(old.TranslationId, from, -1, -1, VerseRangeText(sp.FirstVerse, sp.LastVerse), old.Color, old.Note, old.FolderId, old.Tags, sp.LastVerse);
                kept++;
                continue;
            }
            // a piece that is only blanks is not worth keeping; the ends of a piece lose their blanks
            var (s, e, fv, lv) = (sp.Start, sp.End, sp.FirstVerse, sp.LastVerse);
            var firstRaw = RawVerse(old.TranslationId, fv); var lastRaw = RawVerse(old.TranslationId, lv);
            if (fv == lv)
            {
                if (MarkLogic.Trim(firstRaw, s, e) is not { } t) continue;
                (s, e) = t;
            }
            else
            {
                if (MarkLogic.Trim(firstRaw, s, firstRaw.Length) is { } a) s = a.Start;
                if (MarkLogic.Trim(lastRaw, 0, e) is { } b) e = b.End;
            }
            var text = RangeText(old.TranslationId, fv, s, lv, e);
            if (text.Length == 0) continue;
            _vault.AddMark(old.TranslationId, new VerseRef(old.Ref.Book, old.Ref.Chapter, fv), s, e, text, old.Color, old.Note, old.FolderId, old.Tags, lv);
            kept++;
        }
        return kept;
    }

    Func<int, int> LengthIn(string translationId) => v => RawVerse(translationId, v).Length;

    // ---- marking -------------------------------------------------------------------------------------------------------

    /// <summary>Marks the selected text, or the chosen verses, in this color.</summary>
    /// <summary>True when the selected text lies only in verses that are marked as a whole: marking it then makes a sub-mark.</summary>
    bool InsideMarkedVerse() => !_normalMarking && _sel is { Parts.Count: > 0 } s && s.Parts.All(p => _wholeMarks.ContainsKey(p.Verse));

    /// <summary>The color of a sub-mark: the one chosen in the settings, else the accent color of the program (never a color of the normal marks).</summary>
    string SubMarkColor() => NormalizeHex(_settings.SubMarkColor) ?? HexOf((Res("BsAccent") as ISolidColorBrush)?.Color ?? Colors.Goldenrod);

    Task ApplyColorAsync(string hex)
    {
        if (InsideMarkedVerse()) hex = SubMarkColor();
        string what;
        if (_sel is { Parts.Count: > 0 } sel) what = ApplyTextColor(sel, hex);
        else if (_selVerses.Count > 0 || _selected is not null) what = ApplyVerseColor(hex);
        else { StatusText.Text = L("Erst Text auswählen oder einen Vers anklicken."); return Task.CompletedTask; }
        ClearTextSelection(false);
        RefreshCollection(); RefreshCells();
        StatusText.Text = L("Markiert: {0} · Strg+Z macht es rückgängig", what);
        return Task.CompletedTask;
    }

    string ApplyTextColor(TextSel sel, string hex)
    {
        PushUndo();
        var tid = sel.TranslationId; var first = sel.Parts[0]; var last = sel.Parts[^1];
        var r = new VerseRef(_curBook, _curChapter, first.Verse);
        var touching = _chapterMarks.Where(m => !m.WholeVerse && m.TranslationId == tid && m.Ref.Verse <= last.Verse && m.LastVerse >= first.Verse).ToList();
        var same = touching.FirstOrDefault(m => m.Ref.Verse == first.Verse && m.LastVerse == last.Verse && m.Start == first.Start && m.End == last.End);
        if (same is not null) _vault.UpdateMark(same.Id, hex, same.Note, same.FolderId, same.Tags);   // marked again in another color: note and tags stay
        else
        {
            // what lies under the new mark is cut out of the older marks; the rest of them stays
            var cut = new List<MarkLogic.Piece>();
            for (var v = first.Verse; v <= last.Verse; v++)
            {
                var len = RawVerse(tid, v).Length;
                var s = v == first.Verse ? first.Start : 0; var e = v == last.Verse ? last.End : len;
                cut.Add(new MarkLogic.Piece(v, s, e));
            }
            foreach (var m in touching) KeepRemnants(m, MarkLogic.CarveText(m, cut, LengthIn(tid)));
            var text = RangeText(tid, first.Verse, first.Start, last.Verse, last.End);
            _vault.AddMark(tid, r, first.Start, last.End, text, hex, "", null, AutoTags(r), last.Verse);
        }
        return Loc.Quote(Shorten(SelectedText(), 40));
    }

    string ApplyVerseColor(string hex)
    {
        PushUndo();
        var verses = _selVerses.Count > 0 ? _selVerses.ToHashSet() : _selected is int one ? [one] : [];
        var runs = MarkLogic.Runs(verses);
        var covered = (Mark m) => Enumerable.Range(m.Ref.Verse, m.LastVerse - m.Ref.Verse + 1);
        var wholes = _chapterMarks.Where(m => m.WholeVerse && covered(m).Any(verses.Contains)).ToList();
        var done = new HashSet<long>();
        foreach (var (from, to) in runs)   // exactly the same verses again: only the color changes (note and tags stay)
            if (wholes.FirstOrDefault(m => m.Ref.Verse == from && m.LastVerse == to) is { } exact) { _vault.UpdateMark(exact.Id, hex, exact.Note, exact.FolderId, exact.Tags); done.Add(exact.Id); }
        foreach (var m in wholes.Where(m => !done.Contains(m.Id))) KeepRemnants(m, MarkLogic.CarveWhole(m, verses));
        // a verse that is only part of such a run was cut out above; the runs without a mark of their own are new
        foreach (var (from, to) in runs)
        {
            if (wholes.Any(m => done.Contains(m.Id) && m.Ref.Verse == from && m.LastVerse == to)) continue;
            var r = new VerseRef(_curBook, _curChapter, from);
            _vault.AddMark(Mark.AnyTranslation, r, -1, -1, VerseRangeText(from, to), hex, "", null, AutoTags(r), to);
        }
        return string.Join(", ", runs.Select(x => x.From == x.To ? $"{BookName(_curBook)} {_curChapter}:{x.From}" : $"{BookName(_curBook)} {_curChapter}:{x.From}–{x.To}"));
    }

    // ---- removing -------------------------------------------------------------------------------------------------------

    Task RemoveMarksAsync()
    {
        var gone = new List<Mark>();   // marks that went as a whole (a mark cut in two is no loss)
        if (_sel is { Parts.Count: > 0 } sel)
        {
            var first = sel.Parts[0]; var last = sel.Parts[^1];
            var cut = sel.Parts.Select(p => new MarkLogic.Piece(p.Verse, p.Start, p.End)).ToList();
            var touching = _chapterMarks.Where(m => !m.WholeVerse && m.Ref.Verse <= last.Verse && m.LastVerse >= first.Verse).ToList();
            // a mark of another translation shows here, but its offsets are those of that text: it goes as a whole
            var affected = touching.Where(m => m.TranslationId == sel.TranslationId || CellMarksTouch(m, sel)).ToList();
            if (affected.Count == 0) { StatusText.Text = L("Nichts zum Entfernen ausgewählt."); return Task.CompletedTask; }
            PushUndo();
            foreach (var m in affected)
            {
                if (m.TranslationId != sel.TranslationId) { _vault.DeleteMark(m.Id); gone.Add(m); }
                else if (KeepRemnants(m, MarkLogic.CarveText(m, cut, LengthIn(m.TranslationId))) == 0) gone.Add(m);
            }
        }
        else
        {
            var verses = _selVerses.Count > 0 ? _selVerses.ToHashSet() : _selected is int v ? [v] : [];
            var touching = _chapterMarks.Where(m => Enumerable.Range(m.Ref.Verse, m.LastVerse - m.Ref.Verse + 1).Any(verses.Contains)).ToList();
            if (touching.Count == 0) { StatusText.Text = L("Nichts zum Entfernen ausgewählt."); return Task.CompletedTask; }
            PushUndo();
            var cut = verses.Select(v => new MarkLogic.Piece(v, 0, int.MaxValue)).ToList();
            foreach (var m in touching)
                if (KeepRemnants(m, m.WholeVerse ? MarkLogic.CarveWhole(m, verses) : MarkLogic.CarveText(m, cut, LengthIn(m.TranslationId))) == 0) gone.Add(m);
        }
        // what went as a whole waits in the trash as well (taking the step back throws those entries away again)
        if (gone.Count > 0 && _markUndo.Count > 0) _markUndo[^1] = _markUndo[^1] with { Trash = gone.Select(_vault.KeepInTrash).ToList() };
        ClearTextSelection(false);
        RefreshCollection(); RefreshCells();
        StatusText.Text = L("Markierung entfernt · Strg+Z macht es rückgängig");
        ShowUndoToast(gone.Count > 1 ? L("{0} Markierungen entfernt.", gone.Count) : L("Markierung entfernt."), UndoMarksAsync);
        return Task.CompletedTask;
    }

    /// <summary>Whether a mark of another translation is shown under the selection (and so is meant when the selection is cleared).</summary>
    bool CellMarksTouch(Mark m, TextSel sel)
    {
        foreach (var p in sel.Parts)
        {
            if (!m.Covers(p.Verse) || m.TranslationId == Mark.AnyTranslation) continue;
            var cell = _cells.FirstOrDefault(c => c.Data.Number == p.Verse && c.Data.T.Id == sel.TranslationId && c.Data.Col == sel.Col);
            if (cell?.Tb.Tag is not CellInfo info) continue;
            if (CellMarks(cell.Data, info.RawText).Any(x => x.Shared && x.Mark.Id == m.Id && x.Start < p.End && x.End > p.Start)) return true;
        }
        return false;
    }

    // ---- redrawing where it changed ----------------------------------------------------------------------------------------

    /// <summary>
    /// Draws the verses again with the marks, notes and bookmarks of now, without building the chapter anew: the page stays where it is,
    /// nothing flickers and the window does not move.
    /// </summary>
    void RefreshCells()
    {
        if (_vault is null || _curBook == "" || _cells.Count == 0) return;
        var keep = ReaderScroll.Offset;
        var keepCols = _colScrolls.Select(s => s.Offset).ToList();
        LoadChapterMarks();
        var noted = _vault.GetNotes().Where(n => n.Ref.Book == _curBook && n.Ref.Chapter == _curChapter).Select(n => n.Ref.Verse).ToHashSet();
        foreach (var c in _cells.ToList())
        {
            if (!c.Tb.IsAttachedToVisualTree()) continue;
            c.Data = c.Data with { HasNote = c.Data.Col == 0 && noted.Contains(c.Data.Number) };
            SelPart? part = _sel is { } s && s.TranslationId == c.Data.T.Id && s.Col == c.Data.Col ? s.Parts.FirstOrDefault(p => p.Verse == c.Data.Number) : null;
            FillCell(c.Tb, c.Data, part is null ? null : (part.Start, part.End));
        }
        _hoverSpan = null;
        Paint(); ApplyWordMarks(); RebuildHeadings();
        Dispatcher.UIThread.Post(() =>
        {
            ReaderScroll.Offset = keep;
            for (var i = 0; i < Math.Min(keepCols.Count, _colScrolls.Count); i++) _colScrolls[i].Offset = keepCols[i];
        }, DispatcherPriority.Background);
    }

    /// <summary>The chapters of the translations whose marks show here (the shown ones and those other marks were made in).</summary>
    async Task LoadMarkSourcesAsync(IEnumerable<(string Id, Chapter? C)> shown)
    {
        _markSources.Clear();
        foreach (var (id, c) in shown) if (c is not null) _markSources[id] = c;
        if (!_settings.ShareMarksAcrossTranslations) return;
        var wanted = _chapterMarks.Where(m => !m.WholeVerse && m.TranslationId != Mark.AnyTranslation).Select(m => m.TranslationId).Distinct().Where(id => !_markSources.ContainsKey(id)).ToList();
        foreach (var id in wanted)
        {
            try { _markSources[id] = await _provider.GetChapterAsync(id, _curBook, _curChapter); }
            catch (Exception) { /* offline or the translation is gone: its marks are simply not shown here */ }
        }
    }
}
