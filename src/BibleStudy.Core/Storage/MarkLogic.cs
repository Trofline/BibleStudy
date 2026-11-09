using BibleStudy.Core.Models;

namespace BibleStudy.Core.Storage;

/// <summary>
/// The rules behind highlights that do not need a window: trimming a selection, grouping verses into runs, carrying a mark from one
/// translation to another, and the part of a verse a mark covers.
/// </summary>
public static class MarkLogic
{
    /// <summary>
    /// The sub-marks among these marks: a mark of a part of a verse that lies inside a verse marked as a whole and has the sub-mark color.
    /// Used to give the old sub-marks the new color when the color is changed in the settings.
    /// </summary>
    public static List<long> SubMarkIds(IReadOnlyList<Mark> marks, string subColor)
    {
        var wholes = marks.Where(m => m.WholeVerse).ToList();
        return marks.Where(m => !m.WholeVerse && string.Equals(m.Color, subColor, StringComparison.OrdinalIgnoreCase)
            && wholes.Any(w => w.Ref.Book == m.Ref.Book && w.Ref.Chapter == m.Ref.Chapter && w.Ref.Verse <= m.LastVerse && w.LastVerse >= m.Ref.Verse))
            .Select(m => m.Id).ToList();
    }

    /// <summary>
    /// Where a marked text stands in the verse after the text was imported again: where it was (the offsets are still right), else the same words found in the new
    /// text (the one nearest to the old place). Null when the words are no longer there (the import corrected them): the mark stays as it is then.
    /// </summary>
    public static (int Start, int End)? Realign(string markedText, int oldStart, string newVerseText)
    {
        if (markedText.Length == 0) return null;
        if (oldStart >= 0 && oldStart + markedText.Length <= newVerseText.Length && string.CompareOrdinal(newVerseText, oldStart, markedText, 0, markedText.Length) == 0) return (oldStart, oldStart + markedText.Length);
        var best = -1;
        for (var at = newVerseText.IndexOf(markedText, StringComparison.Ordinal); at >= 0; at = newVerseText.IndexOf(markedText, at + 1, StringComparison.Ordinal))
            if (best < 0 || Math.Abs(at - oldStart) < Math.Abs(best - oldStart)) best = at;
        return best < 0 ? null : (best, best + markedText.Length);
    }

    /// <summary>The name of a vault written in the file of an export: the file "name.txt", else the quoted name in "info.txt" of older exports.</summary>
    public static string? VaultNameFromExport(string? nameFile, string? infoFile)
    {
        var name = nameFile?.Trim();
        if (string.IsNullOrEmpty(name) && infoFile is not null)
        {
            var open = infoFile.IndexOf('"'); var close = infoFile.IndexOf('"', open + 1);
            if (open >= 0 && close > open) name = infoFile[(open + 1)..close].Trim();
        }
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>A vault name that is allowed as a folder name on Windows, Linux and macOS (the same rules as for a new vault).</summary>
    public static bool IsValidVaultName(string name)
    {
        var device = name.Split('.')[0].Trim().ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4" or "LPT1" or "LPT2" or "LPT3";
        return name.Length > 0 && name.Length <= 80 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.Trim('.').Length > 0 && !name.EndsWith('.') && !name.EndsWith(' ') && !device;
    }

    /// <summary>The text of a verse as marks count it: the segments without footnote markers, exactly as written.</summary>
    public static string RawText(IReadOnlyList<Segment> segments) => string.Concat(segments.Where(s => s.NoteId is null).Select(s => s.Text));

    /// <summary>
    /// A selection without the blanks at its two ends (only the ends: the blanks between the words stay in, so a mark over several
    /// words is one piece). An empty or blank selection gives null.
    /// </summary>
    public static (int Start, int End)? Trim(string text, int start, int end)
    {
        start = Math.Clamp(start, 0, text.Length); end = Math.Clamp(end, 0, text.Length);
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        return end > start ? (start, end) : null;
    }

    /// <summary>Verse numbers as runs of neighbours: 3, 5, 6, 7 gives (3,3) and (5,7). Several verses of a run become one mark.</summary>
    public static List<(int From, int To)> Runs(IEnumerable<int> verses)
    {
        var runs = new List<(int From, int To)>();
        foreach (var v in verses.Distinct().Order())
        {
            if (runs.Count > 0 && runs[^1].To == v - 1) runs[^1] = (runs[^1].From, v);
            else runs.Add((v, v));
        }
        return runs;
    }

    /// <summary>
    /// The part of one verse that a mark covers, as offsets into that verse's raw text; null when the mark does not touch the verse.
    /// A verse in the middle of a mark is covered completely, the first verse from the start of the mark, the last up to its end.
    /// </summary>
    public static (int Start, int End)? PartIn(Mark m, int verse, int verseLength)
    {
        if (m.WholeVerse || !m.Covers(verse)) return null;
        var first = verse == m.Ref.Verse; var last = verse == m.LastVerse;
        var s = first ? m.Start : 0; var e = last ? m.End : verseLength;
        s = Math.Clamp(s, 0, verseLength); e = Math.Clamp(e, 0, verseLength);
        return e > s ? (s, e) : null;
    }

    /// <summary>
    /// Where the marked text of a mark made in another translation stands in this translation. First the same words are looked for (translations
    /// that read alike); then the Strong numbers of the marked words are followed when both translations carry them. Null when neither is certain:
    /// a mark must never wander to the wrong words.
    /// </summary>
    public static (int Start, int End)? Project(IReadOnlyList<Segment> source, int start, int end, IReadOnlyList<Segment> target) =>
        ProjectChecked(source, start, end, target) is { } p ? (p.Start, p.End) : null;

    /// <summary>Like <see cref="Project"/>, and says whether the place is only an estimate (found by its position in the verse).</summary>
    public static (int Start, int End, bool Estimated)? ProjectChecked(IReadOnlyList<Segment> source, int start, int end, IReadOnlyList<Segment> target)
    {
        var sourceText = RawText(source); var targetText = RawText(target);
        if (Trim(sourceText, start, end) is not { } t) return null;
        var wanted = sourceText[t.Start..t.End];

        var at = targetText.IndexOf(wanted, StringComparison.OrdinalIgnoreCase);
        if (at >= 0) return (at, at + wanted.Length, false);

        if (ProjectByStrong(source, t, target, targetText, wanted, out var evidence) is { } byStrong) return (byStrong.Start, byStrong.End, false);
        if (evidence) return null;   // the numbers speak, and against a guess: no mark rather than a wrong one
        return ProjectByPosition(sourceText, t, targetText) is { } byPlace ? (byPlace.Start, byPlace.End, true) : null;
    }

    /// <summary>
    /// Where a mark stands when the translation it was made in is not on this computer (a vault taken along): only its stored words can be looked for, the
    /// occurrence nearest to its old place. Null for a mark over several verses (its words cannot be divided among them) and when the words are not in this verse.
    /// </summary>
    public static (int Start, int End)? ProjectByStoredText(Mark m, string targetText, int? verse = null)
    {
        if (m.WholeVerse) return null;
        var text = m.Text.Trim();
        if (!m.MultiVerse) return Realign(text, m.Start, targetText);
        // over several verses: the first verse must end with the start of the words, the last begin with their end, one between lie inside them
        var v = verse ?? m.Ref.Verse;
        if (v == m.Ref.Verse)
        {
            for (var s = 0; s <= targetText.Length - MinStoredPiece; s++)
            {
                var piece = targetText[s..].TrimEnd();
                if (piece.Length >= MinStoredPiece && text.StartsWith(piece, StringComparison.Ordinal)) return (s, s + piece.Length);
            }
            return null;
        }
        if (v == m.LastVerse)
        {
            for (var e = targetText.Length; e >= MinStoredPiece; e--)
            {
                var piece = targetText[..e].TrimStart();
                if (piece.Length >= MinStoredPiece && text.EndsWith(piece, StringComparison.Ordinal)) return (targetText.Length - targetText.TrimStart().Length, e);
            }
            return null;
        }
        return targetText.Trim() is { Length: >= MinStoredPiece } whole && text.Contains(whole, StringComparison.Ordinal) ? (0, targetText.Length) : null;
    }

    const int MinStoredPiece = 3;

    /// <summary>Strong numbers: the numbers of the marked words, found again in the other text.</summary>
    static (int Start, int End)? ProjectByStrong(IReadOnlyList<Segment> source, (int Start, int End) t, IReadOnlyList<Segment> target, string targetText, string wanted, out bool evidence)
    {
        var ids = SegmentsIn(source, t.Start, t.End).SelectMany(x => x.Seg.Strongs ?? []).ToHashSet();
        evidence = false;
        if (ids.Count == 0 || ids.Count > 14) return null;
        var hits = SegmentsIn(target, 0, targetText.Length).Where(x => x.Seg.Strongs is { Count: > 0 } && x.Seg.Strongs.Any(ids.Contains)).ToList();
        if (hits.Count == 0) return null;
        evidence = true;
        var found = hits.SelectMany(h => h.Seg.Strongs!).Where(ids.Contains).ToHashSet();
        if (found.Count * 2 < ids.Count) return null;   // fewer than half of the words found again
        var from = hits.Min(h => h.Start); var to = hits.Max(h => h.End);
        if (to - from > wanted.Length * 1.5 + 12) return null;   // the hits are spread over the verse: not the same passage
        return Trim(targetText, from, to);
    }

    public static List<(int Start, int End)> WordsOf(string text) => System.Text.RegularExpressions.Regex.Matches(text, @"[\p{L}\p{N}]+").Select(m => (m.Index, m.Index + m.Length)).ToList();

    /// <summary>
    /// The last resort for texts that carry no Strong numbers (imported translations): a few marked words are looked for at the same relative place of the other
    /// verse. Only when both verses are about equally long and the mark is short, because a long mark would drift. It is a good guess, not a certainty.
    /// </summary>
    public static (int Start, int End)? ProjectByPosition(string sourceText, (int Start, int End) t, string targetText)
    {
        var sw = WordsOf(sourceText); var tw = WordsOf(targetText);
        if (sw.Count < 3 || tw.Count < 3) return null;
        var ratio = (double)tw.Count / sw.Count;
        if (ratio < 0.65 || ratio > 1.55) return null;
        var first = sw.FindIndex(w => w.End > t.Start && w.Start < t.End);
        var last = sw.FindLastIndex(w => w.End > t.Start && w.Start < t.End);
        if (first < 0 || last - first + 1 > 5) return null;
        var from = Math.Clamp((int)Math.Floor((double)first / sw.Count * tw.Count + 0.0001), 0, tw.Count - 1);
        var to = Math.Clamp((int)Math.Ceiling((double)(last + 1) / sw.Count * tw.Count - 0.0001) - 1, from, tw.Count - 1);
        if (to - from > (last - first) + 1) to = from + (last - first) + 1;
        return (tw[from].Start, tw[to].End);
    }

    // ---- cutting a part out of existing marks -----------------------------------------------------------------------

    /// <summary>A stretch of text of one verse: the part of a mark, or a part to cut out of it.</summary>
    public readonly record struct Piece(int Verse, int Start, int End);

    /// <summary>One mark to keep or to add: from the first verse (offset Start) to the last verse (offset End); whole verses have Start = -1.</summary>
    public readonly record struct Spec(int FirstVerse, int Start, int LastVerse, int End)
    {
        public bool Whole => Start < 0;
    }

    /// <summary>
    /// What is left of a text mark when the pieces in <paramref name="cut"/> are taken out: zero, one or two stretches in one verse, or a stretch
    /// over several verses where nothing was cut in between. Marking over an old mark thus replaces only the part that is marked anew.
    /// </summary>
    public static List<Spec> CarveText(Mark m, IReadOnlyList<Piece> cut, Func<int, int> lengthOf)
    {
        var left = new List<Piece>();
        for (var v = m.Ref.Verse; v <= m.LastVerse; v++)
        {
            var len = lengthOf(v);
            if (PartIn(m, v, len) is not { } part) continue;
            var spans = new List<(int S, int E)> { part };
            foreach (var c in cut.Where(c => c.Verse == v))
            {
                var next = new List<(int S, int E)>();
                foreach (var (s, e) in spans)
                {
                    if (c.End <= s || c.Start >= e) { next.Add((s, e)); continue; }
                    if (c.Start > s) next.Add((s, c.Start));
                    if (c.End < e) next.Add((c.End, e));
                }
                spans = next;
            }
            foreach (var (s, e) in spans.OrderBy(x => x.S)) left.Add(new Piece(v, s, e));
        }
        // neighbours that join up (one ends where its verse ends, the next begins at the start of the next verse) are one mark again
        var result = new List<Spec>();
        for (var i = 0; i < left.Count; i++)
        {
            var first = left[i]; var last = first;
            while (i + 1 < left.Count && left[i + 1].Verse == last.Verse + 1 && last.End >= lengthOf(last.Verse) && left[i + 1].Start <= 0) { last = left[++i]; }
            result.Add(new Spec(first.Verse, first.Start, last.Verse, last.End));
        }
        return result;
    }

    /// <summary>What is left of a mark of whole verses when some verses are cut out: runs of the verses that remain.</summary>
    public static List<Spec> CarveWhole(Mark m, ISet<int> cutVerses) =>
        Runs(Enumerable.Range(m.Ref.Verse, m.LastVerse - m.Ref.Verse + 1).Where(v => !cutVerses.Contains(v))).Select(r => new Spec(r.From, -1, r.To, -1)).ToList();

    /// <summary>The segments (without footnote markers) that touch the range, each with its offsets in the raw text.</summary>
    public static IEnumerable<(Segment Seg, int Start, int End)> SegmentsIn(IReadOnlyList<Segment> segments, int start, int end)
    {
        var pos = 0;
        foreach (var s in segments)
        {
            if (s.NoteId is not null) continue;
            var a = pos; var b = pos + s.Text.Length; pos = b;
            if (b > start && a < end) yield return (s, a, b);
        }
    }
}
