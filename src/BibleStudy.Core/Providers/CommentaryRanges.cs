using System.Text.RegularExpressions;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Providers;

/// <summary>
/// Which verses a note of a commentary is about. The source gives each note only the verse it starts at; some works comment verse
/// by verse (Gill, Clarke, Jamieson-Fausset-Brown), others a whole section at once (Matthew Henry, Calvin, Keil-Delitzsch), and some
/// say the section at the start of the note ("John 3:1-6", "3:1-21"). Before, every note was shown as "verse 1", and a verse without
/// a note of its own was given the note of the verse before it.
/// </summary>
public static partial class CommentaryRanges
{
    /// <summary>Works whose notes run on to the next note (they comment sections, not single verses).</summary>
    static readonly HashSet<string> Sectional = new(StringComparer.OrdinalIgnoreCase) { "matthew-henry", "keil-delitzsch", "john-calvin" };

    [GeneratedRegex(@"^\s*(?:[1-3]?\s?\p{L}+\.?\s+)?(\d+):(\d+)\s*[-–—]\s*(?:(\d+):)?(\d+)")]
    private static partial Regex StartsWithRange();

    /// <summary>The first and last verse of every note (in the order of the notes). <paramref name="lastVerse"/> is the last verse of the chapter (0: unknown).</summary>
    public static List<(int From, int To)> Of(string commentaryId, IReadOnlyList<CommentaryNote> notes, int chapter, int lastVerse)
    {
        var list = new List<(int, int)>(notes.Count);
        for (var i = 0; i < notes.Count; i++)
        {
            var from = notes[i].Verse;
            var next = notes.Skip(i + 1).Select(n => n.Verse).Where(v => v > from).DefaultIfEmpty(0).Min();
            int to;
            if (StartsWithRange().Match(notes[i].Text) is { Success: true } m && int.Parse(m.Groups[1].Value) == chapter && int.Parse(m.Groups[2].Value) == from)
                to = m.Groups[3].Success && int.Parse(m.Groups[3].Value) != chapter ? (lastVerse > 0 ? lastVerse : from) : int.Parse(m.Groups[4].Value);
            else if (Sectional.Contains(commentaryId)) to = next > 0 ? next - 1 : lastVerse > 0 ? lastVerse : from;
            else to = from;
            list.Add((from, Math.Max(from, to)));
        }
        return list;
    }

    /// <summary>"Vers 3" or "Verse 1–21".</summary>
    public static string Label(int from, int to) => from == to ? Loc.L("Vers {0}", from) : Loc.L("Verse {0}–{1}", from, to);

    /// <summary>
    /// True when a paragraph of a note speaks of this verse ("v. 16", "ver. 16", "verse 16", "Joh 3:16", "(16)"). A paragraph that begins
    /// with "16." counts only where the work numbers the verses so (<paramref name="numbered"/>, Calvin); elsewhere "1." is an outline point.
    /// Short lines such as the heading "John 3:1-6" are not a place to show.
    /// </summary>
    public static bool Mentions(string paragraph, int chapter, int verse, bool numbered = false) =>
        paragraph.Length >= 20 &&
        Regex.IsMatch(paragraph, $@"(?:\b(?:v|vs|ver|vv|verse|verses)\.?\s*{verse}\b|\b{chapter}:{verse}\b|\({verse}\)" + (numbered ? $@"|^\s*{verse}[.:]\s" : "") + ")", RegexOptions.IgnoreCase);
}
