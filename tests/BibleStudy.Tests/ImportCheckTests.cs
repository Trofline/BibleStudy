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
            V(7, "Und so weiter und so fort.")));
        foreach (var kind in new[] { "leftover-marker", "space-before-punctuation", "split-word", "lone-capital", "empty-verse", "verse-gap" })
            Assert.True(r.Counts.ContainsKey(kind), kind);
        var report = ImportCheck.BuildReport(Bible(V(1, "x")), r);
        Assert.Contains("USFM", report);
        Assert.Contains("GEN 1,1", report);
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
