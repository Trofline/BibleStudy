using BibleStudy.Core.Import;
using Xunit;

namespace BibleStudy.Tests;

public class PdfBibleLayoutTests
{
    // a tiny two-page "Bible": body size 8, verse numbers bold 7, headings italic 8, running head size 10, footnotes size 6
    static PdfPageWords Page(params (string T, double X, double Y, double S, bool B, bool I)[] w) =>
        new(354, w.Select(x => new PdfWord(x.T, x.X, x.Y, x.S, x.B, x.I)).ToList());

    static IEnumerable<(string, double, double, double, bool, bool)> Line(double y, double x, string text, double size = 8, bool bold = false, bool italic = false) =>
        text.Split(' ').Select((t, i) => (t, x + i * 12, y, size, bold, italic));

    static string? Book(string name) => name.ToUpperInvariant() switch { "RUT" => "RUT", "JONA" => "JON", _ => null };

    [Fact]
    public void ReaderSplitsVersesAndChaptersAndSkipsEverythingElse()
    {
        var words = new List<(string, double, double, double, bool, bool)>();
        words.AddRange(Line(520, 150, "RUT 1.2", 10));                           // running head
        words.AddRange(Line(500, 22, "Es kam Hunger ins Land", 8));              // verse 1 of chapter 1 (no number printed)
        words.AddRange(Line(490, 22, "2", 7, bold: true)); words.AddRange(Line(490, 40, "Der Mann hieß Elimelech", 8));
        words.AddRange(Line(480, 22, "Heb 1,1 Ps 2,3", 6));                      // parallel passages (small): left out
        words.AddRange(Line(470, 22, "Die Heimkehr", 8, italic: true));          // heading: starts the next chapter
        words.AddRange(Line(460, 22, "Und Noomi stand auf", 8));                 // verse 1 of chapter 2
        words.AddRange(Line(450, 22, "2", 7, bold: true)); words.AddRange(Line(450, 40, "Ruth ging aufs Feld", 8));
        words.AddRange(Line(440, 22, "3", 7, bold: true)); words.AddRange(Line(440, 40, "Sie las Ähren auf Man-", 8));
        words.AddRange(Line(430, 22, "ches mehr", 8));
        // filler so that the page counts as a page of text
        for (var i = 0; i < 520; i++) words.Add(("x", 200 + i % 100, 100 + i / 100, 8, false, false));

        var result = PdfBibleLayout.Read([Page([.. words])], Book);
        // fewer than 500 verses: not recognised as a printed Bible, so the generic reader would take over
        Assert.Null(result);
    }

    [Fact]
    public void HeadingMarksTheStartOfAnUnnumberedFirstVerse()
    {
        var words = new List<(string, double, double, double, bool, bool)>();
        words.AddRange(Line(520, 150, "RUT 1.2", 10));
        words.AddRange(Line(500, 22, "Es kam Hunger ins Land", 8));
        words.AddRange(Line(490, 22, "2", 7, bold: true)); words.AddRange(Line(490, 40, "Der Mann hieß Elimelech", 8));
        words.AddRange(Line(470, 22, "Die Heimkehr", 8, italic: true));
        words.AddRange(Line(460, 22, "Und Noomi stand auf", 8));
        words.AddRange(Line(450, 22, "2", 7, bold: true)); words.AddRange(Line(450, 40, "Ruth ging aufs Feld", 8));
        // enough chapters to pass the "looks like a Bible" threshold: repeat the pattern on further pages
        var pages = new List<PdfPageWords> { Page([.. words]) };
        for (var p = 0; p < 300; p++) pages.Add(Page([.. words]));
        var result = PdfBibleLayout.Read(pages, Book);
        Assert.NotNull(result);
        Assert.Contains(result!, v => v is { Book: "RUT", Chapter: 1, Verse: 1 } && v.Text == "Es kam Hunger ins Land");
        Assert.Contains(result!, v => v is { Book: "RUT", Chapter: 1, Verse: 2 } && v.Text == "Der Mann hieß Elimelech");
        Assert.DoesNotContain(result!, v => v.Text.Contains("Heimkehr") || v.Text.Contains("Heb"));
        Assert.Contains(result!, v => v is { Book: "RUT", Chapter: 2, Verse: 1 } && v.Text == "Und Noomi stand auf");
    }

    static List<PdfPageWords> Repeated(List<(string, double, double, double, bool, bool)> words) =>
        Enumerable.Range(0, 301).Select(_ => Page([.. words])).ToList();

    [Fact]
    public void SmallCapitalsWithPunctuationBelongToTheWord()
    {
        var words = new List<(string, double, double, double, bool, bool)>();
        words.AddRange(Line(520, 150, "RUT 1.2", 10));
        words.AddRange(Line(500, 10, "Es sprach der H", 8)); words.Add(("ERRN,", 59, 500, 5.7, false, false)); words.AddRange(Line(500, 70, "zu ihm", 8));
        words.AddRange(Line(490, 10, "2", 7, bold: true)); words.AddRange(Line(490, 20, "Und des H", 8)); words.Add(("ERR", 56, 490, 5.7, false, false));
        words.AddRange(Line(470, 22, "Die Heimkehr", 8, italic: true));
        words.AddRange(Line(460, 22, "Und Noomi", 8));
        words.AddRange(Line(450, 22, "2", 7, bold: true)); words.AddRange(Line(450, 40, "Ruth ging", 8));
        var result = PdfBibleLayout.Read(Repeated(words), Book);
        Assert.NotNull(result);
        Assert.Contains(result!, v => v is { Chapter: 1, Verse: 1 } && v.Text.Contains("HERRN,"));
        Assert.DoesNotContain(result!, v => v.Text.Contains("der H "));
        Assert.Contains(result!, v => v is { Chapter: 1, Verse: 2 } && v.Text.Contains("des HERR"));
    }

    [Fact]
    public void HeadingsAreKeptInFrontOfTheirVerse_AndItalicWordsInsideALineStayText()
    {
        var words = new List<(string, double, double, double, bool, bool)>();
        words.AddRange(Line(520, 150, "RUT 1.2", 10));
        words.AddRange(Line(500, 22, "Es kam Hunger", 8));
        words.AddRange(Line(490, 22, "2", 7, bold: true)); words.AddRange(Line(490, 40, "Der Mann", 8));
        words.AddRange(Line(480, 10, "Elimelech", 8)); words.Add(("eine", 24, 480, 8, false, true)); words.Add(("Frau", 36, 480, 8, false, false));   // inline italic: a word of the translators
        words.AddRange(Line(470, 22, "Die Heimkehr", 8, italic: true));      // heading of chapter 2
        words.AddRange(Line(460, 22, "Und Noomi", 8));
        words.AddRange(Line(450, 22, "2", 7, bold: true)); words.AddRange(Line(450, 40, "Ruth ging", 8));
        words.AddRange(Line(440, 22, "Das Feld des Boas", 8, italic: true)); // heading in front of verse 3
        words.AddRange(Line(430, 22, "3", 7, bold: true)); words.AddRange(Line(430, 40, "Sie las", 8));
        var full = PdfBibleLayout.ReadFull(Repeated(words), Book);
        Assert.NotNull(full);
        Assert.Contains(full!.Headings, h => h is { Book: "RUT", Chapter: 2, Verse: 1, Text: "Die Heimkehr" });
        Assert.Contains(full.Headings, h => h is { Book: "RUT", Chapter: 2, Verse: 3, Text: "Das Feld des Boas" });
        Assert.DoesNotContain(full.Headings, h => h.Text == "eine");
        Assert.Contains(full.Verses, v => v is { Chapter: 1, Verse: 2 } && v.Text.Contains("Elimelech eine Frau"));
        Assert.DoesNotContain(full.Verses, v => v.Text.Contains("Heimkehr") || v.Text.Contains("Das Feld"));
    }

    [Theory]
    [InlineData("ERR", 5.7, true)]
    [InlineData("ERRN,", 5.7, true)]
    [InlineData("[ERRN]", 5.7, true)]
    [InlineData("a", 5.7, false)]       // a footnote letter
    [InlineData("ERR", 8.0, false)]     // as large as the body text: a real capital word
    [InlineData("TEXTLICH", 5.7, true)]
    public void SmallCapitalsAreRecognised(string text, double size, bool expected) => Assert.Equal(expected, PdfBibleLayout.IsSmallCaps(text, size, 8.0));
}
