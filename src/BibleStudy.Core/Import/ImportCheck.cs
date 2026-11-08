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
    static readonly Regex WordRx = new(@"\p{L}+", RegexOptions.Compiled);
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

        // a word that was split ("gin g en") is found by the words it would make: the joined word occurs elsewhere in the text, the pieces hardly do
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var v in bible.Verses)
            foreach (Match w in WordRx.Matches(string.Concat(v.Segments.Where(s => s.NoteId is null).Select(s => s.Text))))
                freq[w.Value] = freq.GetValueOrDefault(w.Value) + 1;
        int SplitAt(string text)
        {
            var words = WordRx.Matches(text);
            for (var i = 0; i + 1 < words.Count; i++)
            {
                var a = words[i]; var b = words[i + 1];
                if (b.Index != a.Index + a.Length + 1 || text[a.Index + a.Length] != ' ') continue;   // only neighbours separated by one blank
                // three pieces with a single letter in the middle ("gin g en")
                if (b.Length == 1 && i + 2 < words.Count && words[i + 2].Index == b.Index + 2 && text[b.Index + 1] == ' ' && freq.GetValueOrDefault(a.Value + b.Value + words[i + 2].Value) >= 2 && freq.GetValueOrDefault(a.Value) <= 2) return a.Index;
                var joined = a.Value + b.Value;
                if (joined.Length >= 5 && freq.GetValueOrDefault(joined) >= 2 && (freq.GetValueOrDefault(a.Value) <= 2 || freq.GetValueOrDefault(b.Value) <= 2) && char.IsLower(b.Value[0])) return a.Index;
            }
            return -1;
        }

        foreach (var v in bible.Verses)
        {
            var text = string.Concat(v.Segments.Where(s => s.NoteId is null).Select(s => s.Text));
            if (string.IsNullOrWhiteSpace(text)) { Add("empty-verse", v, text); continue; }
            if (Marker.Match(text) is { Success: true } m1) Add("leftover-marker", v, text, m1.Index);
            if (Odd.Match(text) is { Success: true } m2) Add("odd-characters", v, text, m2.Index);
            if (SpaceBeforePunct.Match(text) is { Success: true } m3) Add("space-before-punctuation", v, text, m3.Index);
            if (SplitAt(text) is var split and >= 0) Add("split-word", v, text, split);
            if (LoneCap.Match(text) is { Success: true } m5 && v.Verse > 0 && text.Length < 400) Add("lone-capital", v, text, m5.Index);
            if (text.Length < 4) Add("very-short-verse", v, text);
        }

        // chapters and books that are missing altogether (a download that lost a chapter, a PDF that lost a page)
        var byBook = bible.Verses.GroupBy(v => v.Book).ToDictionary(g => g.Key, g => g.Select(v => v.Chapter).Distinct().Order().ToList());
        void Gap(string kind, string book, int chapter, string text)
        {
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
            if (findings.Count(f => f.Kind == kind) < 25) findings.Add(new Finding(kind, book, chapter, 0, text));
        }
        foreach (var (book, chapters) in byBook)
        {
            for (var c = 1; c < chapters[^1]; c++) if (!chapters.Contains(c)) Gap("missing-chapter", book, c, $"chapter {c} of {book} is not in the file");
            if (byBook.Count >= 20 && ChapterCounts.TryGetValue(book, out var expected) && chapters[^1] < expected) Gap("missing-chapter", book, chapters[^1] + 1, $"{book} ends with chapter {chapters[^1]}, the usual count is {expected}");
        }
        if (byBook.Count >= 20)
            foreach (var book in ChapterCounts.Keys.Where(b => !byBook.ContainsKey(b))) Gap("missing-book", book, 0, $"book {book} is not in the file");

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

    /// <summary>The usual number of chapters of each book (the translations that count Joel and Malachi differently are not told off for the smaller number).</summary>
    static readonly Dictionary<string, int> ChapterCounts = new()
    {
        ["GEN"] = 50, ["EXO"] = 40, ["LEV"] = 27, ["NUM"] = 36, ["DEU"] = 34, ["JOS"] = 24, ["JDG"] = 21, ["RUT"] = 4, ["1SA"] = 31, ["2SA"] = 24, ["1KI"] = 22, ["2KI"] = 25,
        ["1CH"] = 29, ["2CH"] = 36, ["EZR"] = 10, ["NEH"] = 13, ["EST"] = 10, ["JOB"] = 42, ["PSA"] = 150, ["PRO"] = 31, ["ECC"] = 12, ["SNG"] = 8, ["ISA"] = 66, ["JER"] = 52,
        ["LAM"] = 5, ["EZK"] = 48, ["DAN"] = 12, ["HOS"] = 14, ["JOL"] = 3, ["AMO"] = 9, ["OBA"] = 1, ["JON"] = 4, ["MIC"] = 7, ["NAM"] = 3, ["HAB"] = 3, ["ZEP"] = 3, ["HAG"] = 2,
        ["ZEC"] = 14, ["MAL"] = 3, ["MAT"] = 28, ["MRK"] = 16, ["LUK"] = 24, ["JHN"] = 21, ["ACT"] = 28, ["ROM"] = 16, ["1CO"] = 16, ["2CO"] = 13, ["GAL"] = 6, ["EPH"] = 6,
        ["PHP"] = 4, ["COL"] = 4, ["1TH"] = 5, ["2TH"] = 3, ["1TI"] = 6, ["2TI"] = 4, ["TIT"] = 3, ["PHM"] = 1, ["HEB"] = 13, ["JAS"] = 5, ["1PE"] = 5, ["2PE"] = 3, ["1JN"] = 5,
        ["2JN"] = 1, ["3JN"] = 1, ["JUD"] = 1, ["REV"] = 22,
    };

    static readonly Dictionary<string, string> Rules = new()
    {
        ["leftover-marker"] = "Remove codes that are not part of the Bible text: Strong numbers (G1234, H1234), edition marks (NA, TR, \"NA/TR\"), curly-brace or angle-bracket tags, HTML entities.",
        ["odd-characters"] = "Remove control characters, soft hyphens and doubled blanks.",
        ["space-before-punctuation"] = "No blank before , . ; : ! ? – join the punctuation to the word before it.",
        ["split-word"] = "Join word fragments that were split by the source (for example \"gin g en\" -> \"gingen\", \"d aß\" -> \"daß\").",
        ["lone-capital"] = "A single capital letter left alone often is the start of a word in small capitals (for example \"H\" for \"HERR\"): restore the whole word.",
        ["empty-verse"] = "A verse without text: restore it from the source if it exists, else keep the verse out of the file.",
        ["very-short-verse"] = "A verse of only a few characters: check whether the rest of the verse got lost.",
        ["missing-chapter"] = "A whole chapter is missing in the file. Do not invent it: say in your answer which chapters are missing so that the user can add them from a copy of the text they own.",
        ["missing-book"] = "A whole book is missing in the file. Do not invent it: list the missing books at the end of your answer.",
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
