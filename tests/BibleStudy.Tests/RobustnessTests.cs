using System.Text;
using BibleStudy.Core;
using BibleStudy.Core.Import;
using BibleStudy.Core.Materials;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

/// <summary>
/// Whatever people type or paste, the readers of text never fail: thousands of odd texts (always the same ones, the dice are seeded)
/// go through every parser that reads what a person wrote.
/// </summary>
public class RobustnessTests
{
    static readonly string[] Pieces =
    [
        "Joh", "1Kö", "Jes", "Ps", "1. Mose", "Offb", "Dan", "Mt", "Gen", "John", "3", "16", "0", "999", "4b", "12a", "45", "1000000000000",
        ",", ":", ".", ";", "-", "–", "—", "(", ")", "[", "]", "[[", "]]", "[[#", "#", "|", "*", "**", "`", "!", "![", "](bild:", "0123456789abcdef", ")",
        "f.", "ff.", "vgl.", "und", "s.", "<!--", "-->", "<b>", "</b>", "&amp;", "\"", "'", "\\", "{", "}", "\t", "\n", "\r\n", " ", "  ",
        "ä", "ß", "Ö", "€", "😀", "​", "é", "ﬁ", "İ", "ı", "α", "א", "ἀγάπη", "Wort", "Jesus", "AND", "OR", "NOT", "\"Wort\"", "L=", "G3056", "H1254a",
        "?", "*", "~", "^", "$", "/", "%", "+", "=", "@", "\0", "\u001f",
        "٣", "١٦", "５", "３,１６", "Joh ٣,١٦", "۱۲", "NAH٣", "G٣٠٥٦", "H١=", "NEAR", "G", "99999999999", "Joh 99999999999:1",   // digits of other scripts, numbers too big for a number
    ];

    static IEnumerable<string> OddTexts(int count = 2500)
    {
        var dice = new Random(4711);
        yield return ""; yield return " "; yield return "\n"; yield return "Joh 3,16"; yield return "[[]]"; yield return "[[#:]]"; yield return "Joh 0,0";
        for (var i = 0; i < count; i++)
        {
            var sb = new StringBuilder();
            var n = dice.Next(1, 14);
            for (var k = 0; k < n; k++) sb.Append(Pieces[dice.Next(Pieces.Length)]);
            yield return sb.ToString();
        }
    }

    static void Never(Action<string> read, string what)
    {
        var failed = new List<string>();
        foreach (var text in OddTexts())
        {
            try { read(text); }
            catch (Exception ex) { failed.Add($"{what}({System.Text.Json.JsonSerializer.Serialize(text)}): {ex.GetType().Name} {ex.Message}"); }
        }
        Assert.True(failed.Count == 0, string.Join("\n", failed.Distinct().Take(12)));
    }

    [Fact]
    public void Bible_references_in_any_text()
    {
        Never(t =>
        {
            foreach (var s in DocumentLinks.Spans(t))
            {
                Assert.InRange(s.Index, 0, t.Length - 1);
                Assert.InRange(s.Length, 1, t.Length - s.Index);
                Assert.True(s.Ref.Chapter >= 1 && s.Ref.Verse >= 1, $"no chapter or verse 0: {s.Ref}");
            }
            DocumentLinks.VerseRefs(t); DocumentLinks.VerseRanges(t); DocumentLinks.VerseSpans(t);
            DocumentLinks.ReplaceVerses(t, s => "<" + s + ">");
            LocalBibleStore.LeadingReferences(t);
            PassageParser.Parse(t, out _);
        }, "references");
    }

    [Fact]
    public void Links_in_any_text()
    {
        Never(t =>
        {
            DocumentLinks.Links(t); DocumentLinks.WikiLinks(t); DocumentLinks.WithoutNumbers(t); DocumentLinks.WithLinkTexts(t);
            DocumentLinks.NumberLinks(t, id => id == 1 ? "Eins" : null, title => title == "Eins" ? 1 : null);
            DocumentLinks.RenameLink(t, "Joh", "Johannes");
            DocumentLinks.ToMarkdownLinks(t, l => l.Id is null ? null : ("T", "t.md"), s => "bible://" + s);
            DocumentLinks.WithoutTitleHeading(t, "Joh");
            DocumentLinks.LinksTo(t, 1, "Eins");
        }, "links");
    }

    [Fact]
    public void Search_queries_of_any_kind()
    {
        Never(t =>
        {
            if (SearchQuery.Parse(t, out _) is null) return;
            if (SearchQuery.HasWildcard(t)) SearchQuery.WildcardRegex(t).IsMatch("wort");
        }, "search");
    }

    [Fact]
    public void Searching_the_Bible_with_any_query_and_any_option()
    {
        var corpus = new List<CorpusVerse>
        {
            new(new VerseRef("JHN", 3, 16), "Denn also hat Gott die Welt geliebt, dass er seinen eingeborenen Sohn gab."),
            new(new VerseRef("JHN", 3, 17), "Denn Gott hat seinen Sohn nicht in die Welt gesandt, dass er die Welt richte."),
            new(new VerseRef("GEN", 1, 1), "Im Anfang schuf Gott Himmel und Erde."),
            new(new VerseRef("PSA", 23, 1), "Der HERR ist mein Hirte; mir wird nichts mangeln."),
            new(new VerseRef("ISA", 39, 1), "Zu jener Zeit sandte Merodach-Baladan … ٣ ١٦ 😀 ἀγάπη א"),
        };
        var options = new[]
        {
            new SearchOptions(), new SearchOptions(WordMatch.Whole, CaseSensitive: true), new SearchOptions(WordMatch.Suffix, Range: 2),
            new SearchOptions(WordMatch.Contains, Original: true, Range: 5), new SearchOptions(WordMatch.Prefix, Range: 99),
        };
        Never(t =>
        {
            foreach (var o in options) SearchEngine.Run(corpus, t, null, o);
            SearchEngine.Run(corpus, t, null, exactWords: true, books: ["JHN"]);
        }, "search engine");
    }

    [Fact]
    public void Markdown_text_and_web_pages_of_any_kind()
    {
        Never(t =>
        {
            MaterialReader.ReadMarkdown(t); MaterialReader.ReadText(t); MaterialReader.ReadHtml(t); MaterialReader.MarkdownSpans(t);
            var blocks = MaterialReader.ReadText(t + "\nJoh 3,16 und mehr");
            MaterialSearch.Find("x.txt", blocks, t.Length > 0 ? t[..Math.Min(4, t.Length)] : "a");
            MaterialSearch.VerseHits("x.txt", blocks, new VerseRef("JHN", 3, 16));
            MaterialSearch.Locate(blocks, 0, 0, t);
            for (var i = 0; i <= t.Length; i += Math.Max(1, t.Length / 4)) MaterialSearch.Snippet(t, i, Math.Min(3, t.Length - i), 20);
        }, "materials");
    }

    [Fact]
    public void Marks_moved_to_a_changed_verse_of_any_kind()
    {
        Never(t =>
        {
            for (var i = -1; i <= t.Length + 1; i += Math.Max(1, t.Length / 3))
            {
                MarkLogic.Realign(t, i, "Und " + t + " Ende");
                MarkLogic.Trim(t, Math.Max(0, i), Math.Min(t.Length, i + 4));
            }
        }, "marks");
    }

    [Fact]
    public void Other_text_readers()
    {
        Never(t =>
        {
            BibleImporters.HtmlToLines(t).ToList(); BibleImporters.RtfToLines(t).ToList();
            NaturalVoice.Chunks(t, 20);
            GospelRange.Parse(t); Synopsis.Words(t).ToList();
            Updater.Parse(t);
            BibleStudy.Core.Sync.SyncCode.ParseHosts(t);
            BibleStudy.Core.Collab.Media.Ffmpeg.ParseDshow(t); BibleStudy.Core.Collab.Media.Ffmpeg.ParseAvfoundation(t);
            ImportDiff.Focus(t, t + "x");
            WordAnalyzer.Normalize(t); WordAnalyzer.Normalize("G" + t); WordAnalyzer.Normalize("H" + t);
            AudioBible.ChapterOf(t + ".mp3"); AudioBible.ChapterOf("Chapter " + t + ".mp3");
            MaterialReader.HeadingLevel(t); MaterialReader.HeadingLevel("Heading" + t);
        }, "readers");
    }
}
