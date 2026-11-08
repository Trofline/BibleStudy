using BibleStudy.Core.Import;
using BibleStudy.Core.Models;

namespace BibleStudy.Tests;

public class ImportCheckTests
{
    static ParsedVerse V(int n, string t) => new("GEN", 1, n, [new Segment(t)]);
    static ParsedBible Bible(params ParsedVerse[] v) => new("Test", "TST", "de", v, []);

    [Fact]
    public void Clean_text_has_no_findings()
    {
        var r = ImportCheck.Analyze(Bible(V(1, "Im Anfang schuf Gott Himmel und Erde."), V(2, "Und die Erde war wüst und leer.")));
        Assert.True(r.Clean);
        Assert.Contains("Nothing noticeable", ImportCheck.BuildReport(Bible(), r));
    }

    [Fact]
    public void Finds_the_typical_faults()
    {
        var r = ImportCheck.Analyze(Bible(
            V(1, "Im Anfang G6137 NA/G1782 TR schuf Gott"),
            V(2, "Er sprach , und es ward"),
            V(3, "Da gin g en sie hinaus"),
            V(4, "Der H sprach zu ihm"),
            V(5, ""),
            V(7, "Und so weiter und so fort."), V(8, "Sie gingen heim."), V(9, "Dann gingen sie fort.")));
        foreach (var kind in new[] { "leftover-marker", "space-before-punctuation", "split-word", "lone-capital", "empty-verse", "verse-gap" })
            Assert.True(r.Counts.ContainsKey(kind), kind);
        var report = ImportCheck.BuildReport(Bible(V(1, "x")), r);
        Assert.Contains("USFM", report);
        Assert.Contains("GEN 1,1", report);
    }
}

public class MissingChapterTests
{
    static ParsedVerse V(string book, int c, int v) => new(book, c, v, [new Segment("Ein Vers im Text.")]);

    [Fact]
    public void A_missing_first_chapter_and_a_cut_off_book_are_found()
    {
        // Acts without chapter 1, Genesis only up to chapter 3, and enough other books to count as a whole Bible
        var verses = new List<ParsedVerse>();
        for (var c = 2; c <= 28; c++) verses.Add(V("ACT", c, 1));
        for (var c = 1; c <= 3; c++) verses.Add(V("GEN", c, 1));
        for (var b = 0; b < 20; b++) verses.Add(V("X" + b, 1, 1));
        var r = ImportCheck.Analyze(new ParsedBible("T", "T", "de", verses, []));
        Assert.Contains(r.Findings, f => f is { Kind: "missing-chapter", Book: "ACT", Chapter: 1 });
        Assert.Contains(r.Findings, f => f is { Kind: "missing-chapter", Book: "GEN", Chapter: 4 });
        Assert.Contains(r.Findings, f => f is { Kind: "missing-book", Book: "EXO" });
    }

    [Fact]
    public void A_single_book_is_not_told_off_for_the_other_books()
    {
        var r = ImportCheck.Analyze(new ParsedBible("T", "T", "de", [V("RUT", 1, 1), V("RUT", 2, 1), V("RUT", 3, 1), V("RUT", 4, 1)], []));
        Assert.DoesNotContain(r.Findings, f => f.Kind == "missing-book");
    }
}

public class UsfmHeadingTests
{
    [Fact]
    public void Usfm_section_headings_are_imported_before_their_verse()
    {
        var path = Path.Combine(Path.GetTempPath(), "bs_h_" + Guid.NewGuid().ToString("N") + ".usfm");
        var bs = "\\";
        var lines = new List<string> { bs + "id GEN", bs + "c 1", bs + "s1 Die Schöpfung", bs + "p", bs + "v 1 Im Anfang schuf Gott.", bs + "v 2 Und die Erde war wüst.", bs + "s1 Der zweite Tag", bs + "v 3 Da sprach Gott." };
        lines.AddRange(Enumerable.Range(4, 12).Select(i => $"{bs}v {i} Text {i}."));
        File.WriteAllLines(path, lines);
        try
        {
            var b = BibleImporters.Read(path, "de", null);
            Assert.NotNull(b.Headings);
            Assert.Contains(b.Headings!, h => h.Book == "GEN" && h.Chapter == 1 && h.Verse == 1 && h.Text == "Die Schöpfung");
            Assert.Contains(b.Headings!, h => h.Verse == 3 && h.Text == "Der zweite Tag");
        }
        finally { File.Delete(path); }
    }
}
