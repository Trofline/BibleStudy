using System.Text;
using System.Text.RegularExpressions;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Import;

/// <summary>
/// A look at a freshly read translation for the typical faults of imports (left-over markers, split words, empty verses, missing chapters),
/// and a Markdown file that hands the findings together with the rules of the import format to an AI, which can then correct the text.
/// </summary>
public static class ImportCheck
{
    public sealed record Finding(string Kind, string Book, int Chapter, int Verse, string Excerpt);

    public sealed record Result(int Verses, int Footnotes, int Headings, int Books, IReadOnlyList<Finding> Findings, IReadOnlyDictionary<string, int> Counts)
    {
        public bool Clean => Findings.Count == 0;
    }

    static readonly Regex Marker = new(@"\b[GH]\d{3,4}\b|\{[^}\s]{1,12}\}|<[^>]{1,20}>|&[a-z]+;|\b(?:NA|TR)\b", RegexOptions.Compiled);
    static readonly Regex SpaceBeforePunct = new(@"\p{L} [,.;:!?](?!\p{L}|\d)", RegexOptions.Compiled);
    static readonly Regex SplitLetters = new(@"(?<![\p{L}\p{N}])\p{L}\p{Ll} \p{Ll}{2,}\b|\b\p{Ll}{3,} \p{Ll}\b(?! \p{L})", RegexOptions.Compiled);
    static readonly Regex LoneCap = new(@"(?<![\p{L}\p{N}])[HDKL](?=[ .,;:]|$)", RegexOptions.Compiled);
    static readonly Regex Odd = new(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F�­]|\s{2,}", RegexOptions.Compiled);

    public static Result Analyze(ParsedBible bible)
    {
        var findings = new List<Finding>();
        var counts = new Dictionary<string, int>();
        void Add(string kind, ParsedVerse v, string text, int at = 0)
        {
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
            if (findings.Count(f => f.Kind == kind) < 25)
            {
                var from = Math.Max(0, at - 25); var len = Math.Min(text.Length - from, 70);
                findings.Add(new Finding(kind, v.Book, v.Chapter, v.Verse, text.Substring(from, len)));
            }
        }

        foreach (var v in bible.Verses)
        {
            var text = string.Concat(v.Segments.Where(s => s.NoteId is null).Select(s => s.Text));
            if (string.IsNullOrWhiteSpace(text)) { Add("empty-verse", v, text); continue; }
            if (Marker.Match(text) is { Success: true } m1) Add("leftover-marker", v, text, m1.Index);
            if (Odd.Match(text) is { Success: true } m2) Add("odd-characters", v, text, m2.Index);
            if (SpaceBeforePunct.Match(text) is { Success: true } m3) Add("space-before-punctuation", v, text, m3.Index);
            if (SplitLetters.Match(text) is { Success: true } m4) Add("split-word", v, text, m4.Index);
            if (LoneCap.Match(text) is { Success: true } m5 && v.Verse > 0 && text.Length < 400) Add("lone-capital", v, text, m5.Index);
            if (text.Length < 4) Add("very-short-verse", v, text);
        }

        // chapters that skip a verse
        foreach (var ch in bible.Verses.GroupBy(v => (v.Book, v.Chapter)))
        {
            var nums = ch.Select(v => v.Verse).Distinct().Order().ToList();
            for (var i = 1; i < nums.Count; i++)
                if (nums[i] - nums[i - 1] > 1)
                {
                    var first = ch.First();
                    counts["verse-gap"] = counts.GetValueOrDefault("verse-gap") + 1;
                    if (findings.Count(f => f.Kind == "verse-gap") < 25)
                        findings.Add(new Finding("verse-gap", first.Book, first.Chapter, nums[i - 1], $"verse {nums[i - 1]} is followed by verse {nums[i]}"));
                    break;
                }
        }
        return new Result(bible.Verses.Count, bible.Footnotes.Count, bible.Headings?.Count ?? 0, bible.Verses.Select(v => v.Book).Distinct().Count(), findings, counts);
    }

    static readonly Dictionary<string, string> Rules = new()
    {
        ["leftover-marker"] = "Remove codes that are not part of the Bible text: Strong numbers (G1234, H1234), edition marks (NA, TR, \"NA/TR\"), curly-brace or angle-bracket tags, HTML entities.",
        ["odd-characters"] = "Remove control characters, soft hyphens and doubled blanks.",
        ["space-before-punctuation"] = "No blank before , . ; : ! ? – join the punctuation to the word before it.",
        ["split-word"] = "Join word fragments that were split by the source (for example \"gin g en\" -> \"gingen\", \"d aß\" -> \"daß\").",
        ["lone-capital"] = "A single capital letter left alone often is the start of a word in small capitals (for example \"H\" for \"HERR\"): restore the whole word.",
        ["empty-verse"] = "A verse without text: restore it from the source if it exists, else keep the verse out of the file.",
        ["very-short-verse"] = "A verse of only a few characters: check whether the rest of the verse got lost.",
        ["verse-gap"] = "A verse number is missing inside a chapter: check whether it was swallowed by the neighbouring verse or exists only in other editions.",
    };

    /// <summary>The Markdown file for an AI: what the file should look like, the rules, what was found and where, and how to answer.</summary>
    public static string BuildReport(ParsedBible bible, Result r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Check of the import \"{bible.Name}\" ({bible.ShortName}, language {bible.LanguageCode})").AppendLine();
        sb.AppendLine($"{r.Verses} verses in {r.Books} books, {r.Footnotes} footnotes, {r.Headings} headings were read.").AppendLine();
        sb.AppendLine("## Task").AppendLine();
        sb.AppendLine("You get the text of a Bible translation that was read from a PDF, HTML or similar file. The reading is automatic and makes typical errors.");
        sb.AppendLine("Correct the text of the translation so that it can be imported cleanly, and give it back as a USFM file (one `\\id`, `\\c` and `\\v` per book, chapter and verse; headings as `\\s1`).");
        sb.AppendLine("Change only what is wrong. Never reword, never modernise, never add words that are not in the source. If you are not sure, leave the text as it is and list the place in a short note at the end.").AppendLine();
        sb.AppendLine("## Rules").AppendLine();
        foreach (var (kind, _) in r.Counts.OrderByDescending(c => c.Value)) if (Rules.TryGetValue(kind, out var rule)) sb.AppendLine($"- **{kind}**: {rule}");
        sb.AppendLine("- Keep the divine names as in the source (for example HERR in capitals).");
        sb.AppendLine("- Keep the verse numbers and the order exactly.").AppendLine();
        sb.AppendLine("## What the check found").AppendLine();
        if (r.Clean) sb.AppendLine("Nothing noticeable. The import looks clean.");
        else
        {
            sb.AppendLine("| Kind | Number |").AppendLine("|---|---|");
            foreach (var (kind, n) in r.Counts.OrderByDescending(c => c.Value)) sb.AppendLine($"| {kind} | {n} |");
            sb.AppendLine().AppendLine("Examples (at most 25 per kind):").AppendLine();
            foreach (var g in r.Findings.GroupBy(f => f.Kind))
            {
                sb.AppendLine($"### {g.Key}").AppendLine();
                foreach (var f in g) sb.AppendLine($"- {f.Book} {f.Chapter},{f.Verse}: `{f.Excerpt.Replace('`', '\'').Replace('\n', ' ')}`");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }
}
