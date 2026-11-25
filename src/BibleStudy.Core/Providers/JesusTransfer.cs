using System.Text.RegularExpressions;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Providers;

/// <summary>
/// Words of Jesus for a translation that does not mark them, estimated from the KJV (which does): a verse the KJV prints all in red is red
/// here too; in a verse that is only partly red the words inside quotation marks are taken, and without quotation marks the same share
/// of the verse, moved to the nearest punctuation mark ("sprach:", "…, sagte er."). It is an estimate and can be off at the edges.
/// </summary>
public static partial class JesusTransfer
{
    /// <summary>The translation the red letters are taken from.</summary>
    public const string ReferenceId = "eng_kjv";

    /// <summary>Books in which the KJV prints words of Jesus in red (the Gospels, Acts 1, 9, 20, 22, 26, 1 Cor 11, 2 Cor 12, Revelation).</summary>
    static readonly HashSet<string> Books = ["MAT", "MRK", "LUK", "JHN", "ACT", "1CO", "2CO", "REV"];

    /// <summary>True when the chapter could get words of Jesus from the reference: a book that has them and a chapter that marks none itself.</summary>
    public static bool Wanted(Chapter chapter) =>
        Books.Contains(chapter.BookId) && !string.Equals(chapter.TranslationId, ReferenceId, StringComparison.OrdinalIgnoreCase)
        && !chapter.Verses.Any(v => v.Segments.Any(s => s.WordsOfJesus));

    /// <summary>The chapter with the words of Jesus of <paramref name="reference"/> (the same chapter in the KJV) carried over verse by verse.</summary>
    public static Chapter Apply(Chapter target, Chapter reference)
    {
        var refs = reference.Verses.GroupBy(v => v.Number).ToDictionary(g => g.Key, g => g.First());
        var changed = false;
        var verses = target.Verses.Select(v =>
        {
            if (!refs.TryGetValue(v.Number, out var r)) return v;
            var nv = Transfer(v, r);
            changed |= !ReferenceEquals(nv, v);
            return nv;
        }).ToList();
        return changed ? target with { Verses = verses } : target;
    }

    /// <summary>One verse: <paramref name="target"/> with red parts where <paramref name="reference"/> has them; the same object when nothing is red.</summary>
    public static Verse Transfer(Verse target, Verse reference)
    {
        // the words of the reference verse, each with "is it red?"
        var refWords = new List<bool>();
        foreach (var s in reference.Segments.Where(s => s.NoteId is null))
            refWords.AddRange(Enumerable.Repeat(s.WordsOfJesus, Word().Matches(s.Text).Count));
        if (!refWords.Contains(true)) return target;

        var text = string.Concat(target.Segments.Select(s => s.NoteId is null ? s.Text : ""));
        var words = Word().Matches(text).Select(m => (Start: m.Index, End: m.Index + m.Length)).ToList();
        if (words.Count == 0) return target;

        List<(int Start, int End)> ranges;
        if (!refWords.Contains(false)) ranges = [(0, text.Length)];
        else ranges = FromQuotes(text, refWords[0], refWords[^1]) ?? Proportional(text, words, refWords);
        if (ranges.Count == 0) return target;
        return target with { Segments = Split(target.Segments, ranges) };
    }

    /// <summary>The parts of the text inside quotation marks; null when the verse has none. A lone mark closes a speech begun earlier or opens one that goes on.</summary>
    static List<(int, int)>? FromQuotes(string text, bool redAtStart, bool redAtEnd)
    {
        var marks = new List<int>();
        for (var i = 0; i < text.Length; i++) if (Quotes.Contains(text[i])) marks.Add(i);
        if (marks.Count == 0) return null;
        var ranges = new List<(int, int)>();
        var at = 0;
        if (marks.Count % 2 == 1)
        {
            if (redAtStart && !redAtEnd) { ranges.Add((0, marks[0])); at = 1; }        // „… (speech from the verse before) …“ ends here
            else if (redAtEnd) { ranges.Add((marks[^1] + 1, text.Length)); marks.RemoveAt(marks.Count - 1); }   // a speech that goes on
            else return null;
        }
        for (; at + 1 < marks.Count; at += 2) ranges.Add((marks[at] + 1, marks[at + 1]));
        return ranges.Where(r => r.Item2 > r.Item1).ToList();
    }

    const string Quotes = "„“”\"«»";

    /// <summary>The red share of the reference verse placed on the same share of the words here, with each edge moved to a punctuation mark nearby.</summary>
    static List<(int, int)> Proportional(string text, List<(int Start, int End)> words, List<bool> refWords)
    {
        var n = refWords.Count; var m = words.Count;
        var ranges = new List<(int, int)>();
        for (var i = 0; i < n;)
        {
            if (!refWords[i]) { i++; continue; }
            var j = i;
            while (j < n && refWords[j]) j++;
            var from = i == 0 ? 0 : Snap(text, words, (int)Math.Round((double)i * m / n));
            var to = j == n ? m : Snap(text, words, (int)Math.Round((double)j * m / n));
            if (to > from)
            {
                var start = from == 0 ? 0 : words[from].Start;
                var end = to == m ? text.Length : words[to - 1].End;
                while (end < text.Length && Punctuation.Contains(text[end])) end++;   // "Folget mir!" keeps its "!"
                ranges.Add((start, end));
            }
            i = j;
        }
        return ranges;
    }

    const string Punctuation = ".,;:!?";

    /// <summary>A word border near <paramref name="index"/> (0..words) that follows a punctuation mark; the index itself when there is none within three words.</summary>
    static int Snap(string text, List<(int Start, int End)> words, int index)
    {
        index = Math.Clamp(index, 0, words.Count);
        bool AfterMark(int k) => k > 0 && k < words.Count && text[words[k - 1].End..words[k].Start].Any(c => Punctuation.Contains(c));
        for (var d = 0; d <= 3; d++)
        {
            if (AfterMark(index - d)) return index - d;
            if (AfterMark(index + d)) return index + d;
        }
        return index;
    }

    /// <summary>The segments cut at the edges of the ranges, the parts inside them marked as words of Jesus (footnote markers stay as they are).</summary>
    static List<Segment> Split(IReadOnlyList<Segment> segments, List<(int Start, int End)> ranges)
    {
        bool Red(int pos) => ranges.Any(r => pos >= r.Start && pos < r.End);
        var result = new List<Segment>();
        var offset = 0;
        foreach (var s in segments)
        {
            if (s.NoteId is not null || s.Text.Length == 0) { result.Add(s); continue; }
            var from = 0;
            for (var i = 1; i <= s.Text.Length; i++)
            {
                if (i < s.Text.Length && Red(offset + i) == Red(offset + from)) continue;
                result.Add(s with { Text = s.Text[from..i], WordsOfJesus = Red(offset + from) });
                from = i;
            }
            offset += s.Text.Length;
        }
        return result;
    }

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}][\p{L}\p{M}\p{N}'’\-]*")] private static partial Regex Word();
}
