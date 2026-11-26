using System.IO.Compression;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

/// <summary>Words of Jesus that an imported file marks itself come back as red-letter segments.</summary>
public class JesusWordsImportTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_wj_" + Guid.NewGuid().ToString("N"));
    public JesusWordsImportTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    string Write(string name, string text) { var p = Path.Combine(_dir, name); File.WriteAllText(p, text); return p; }

    static string Red(ParsedVerse v) => string.Concat(v.Segments.Where(s => s.WordsOfJesus).Select(s => s.Text)).Trim();
    static string All(ParsedVerse v) => string.Join(" ", v.Segments.Where(s => s.NoteId is null).Select(s => s.Text.Trim()));
    static ParsedVerse V(ParsedBible b, int verse) => b.Verses.First(v => v.Book == "JHN" && v.Chapter == 14 && v.Verse == verse);

    // ten plain verses, so that the readers accept the file as a Bible
    static string Filler(Func<int, string> verse) => string.Concat(Enumerable.Range(10, 10).Select(verse));

    [Fact]
    public void Osis_container_and_milestone_quotes_are_red()
    {
        var path = Write("b.osis", $"""
            <osis><osisText osisIDWork="Test"><div type="book" osisID="John"><chapter osisID="John.14">
            <verse osisID="John.14.6">Jesus spricht zu ihm: <q who="Jesus" marker="">Ich bin der Weg</q> und mehr.</verse>
            <verse sID="John.14.7" osisID="John.14.7"/>Er sagte: <q who="Jesus" sID="q1"/>Wenn ihr mich erkannt hättet,<verse eID="John.14.7"/>
            <verse sID="John.14.8" osisID="John.14.8"/>so würdet ihr auch meinen Vater kennen.<q eID="q1"/> Philippus sprach<verse eID="John.14.8"/>
            {Filler(i => $"<verse osisID=\"John.14.{i}\">Vers {i}</verse>")}
            </chapter></div></osisText></osis>
            """);
        var b = BibleImporters.Read(path, "de");
        Assert.Equal("Ich bin der Weg", Red(V(b, 6)));
        Assert.Equal("Jesus spricht zu ihm: Ich bin der Weg und mehr.", All(V(b, 6)));
        Assert.Equal("Wenn ihr mich erkannt hättet,", Red(V(b, 7)));
        Assert.Equal("so würdet ihr auch meinen Vater kennen.", Red(V(b, 8)));
        Assert.DoesNotContain(V(b, 10).Segments, s => s.WordsOfJesus);
    }

    [Fact]
    public void Usfm_wj_is_red()
    {
        var path = Write("b.usfm", "\\id JHN\n\\c 14\n\\p\n\\v 6 Jesus spricht: \\wj Ich bin der Weg\\wj* und mehr.\n" + Filler(i => $"\\v {i} Vers {i}\n"));
        var b = BibleImporters.Read(path, "de");
        Assert.Equal("Ich bin der Weg", Red(V(b, 6)));
        Assert.DoesNotContain(BibleImporters.JesusOpen, All(V(b, 6)));
        Assert.DoesNotContain(V(b, 10).Segments, s => s.WordsOfJesus);
    }

    [Fact]
    public void Usx_char_wj_is_red()
    {
        var path = Write("b.usx", $"""
            <usx><book code="JHN" style="id"/><chapter number="14" style="c"/><para style="p">
            <verse number="6" style="v"/>Jesus spricht: <char style="wj">Ich bin der Weg</char> und mehr.
            {Filler(i => $"<verse number=\"{i}\" style=\"v\"/>Vers {i} ")}</para></usx>
            """);
        var b = BibleImporters.Read(path, "de");
        Assert.Equal("Ich bin der Weg", Red(V(b, 6)));
    }

    [Fact]
    public void Zefania_red_style_is_red()
    {
        var path = Write("z.xml", """<XMLBIBLE biblename="Z"><BIBLEBOOK bnumber="43"><CHAPTER cnumber="14"><VERS vnumber="6">Jesus spricht: <STYLE css="color:#FF0000">Ich bin der Weg</STYLE> und mehr.</VERS><VERS vnumber="7">Ohne <STYLE css="font-weight:bold">Farbe</STYLE></VERS></CHAPTER></BIBLEBOOK></XMLBIBLE>""");
        var b = BibleImporters.ReadZefania(path, "de");
        Assert.Equal("Ich bin der Weg", Red(V(b, 6)));
        Assert.Equal("Jesus spricht: Ich bin der Weg und mehr.", All(V(b, 6)));
        Assert.Equal("", Red(V(b, 7)));
    }

    [Fact]
    public void Esword_and_MySword_red_letters_are_red()
    {
        var bblx = Path.Combine(_dir, "t.bblx");
        using (var con = new SqliteConnection($"Data Source={bblx}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE Bible(Book INT, Chapter INT, Verse INT, Scripture TEXT);
                INSERT INTO Bible VALUES(43,14,6,'Jesus spricht: {\cf15 Ich bin der Weg} und mehr.');
                INSERT INTO Bible VALUES(43,14,7,'Er sagte: <FR>Wenn ihr mich erkannt hättet<Fr>.');
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var b = BibleImporters.ReadEsword(bblx, "de");
        Assert.Equal("Ich bin der Weg", Red(V(b, 6)));
        Assert.Equal("Wenn ihr mich erkannt hättet", Red(V(b, 7)));
        Assert.StartsWith("Er sagte: Wenn ihr", All(V(b, 7)));
    }

    [Fact]
    public void Html_red_spans_are_red_also_over_verse_numbers()
    {
        var path = Write("b.html", "<h1>Johannes 14</h1><p><sup>6</sup>Jesus spricht: <span class=\"wj\">Ich bin der Weg. <sup>7</sup>Wenn ihr mich <b>erkannt</b> hättet,</span> sagte er. "
            + string.Concat(Enumerable.Range(8, 12).Select(i => $"<sup>{i}</sup>Vers {i} ")) + "</p>");
        var b = BibleImporters.Read(path, "de");
        Assert.Equal("Ich bin der Weg.", Red(V(b, 6)));
        Assert.Equal("Wenn ihr mich erkannt hättet,", Red(V(b, 7)));
        Assert.DoesNotContain(V(b, 8).Segments, s => s.WordsOfJesus);
    }

    [Fact]
    public void Docx_red_runs_are_red()
    {
        var path = Path.Combine(_dir, "b.docx");
        static string Run(string t, string? color = null, bool sup = false) =>
            $"<w:r><w:rPr>{(color is null ? "" : $"<w:color w:val=\"{color}\"/>")}{(sup ? "<w:vertAlign w:val=\"superscript\"/>" : "")}</w:rPr><w:t xml:space=\"preserve\">{t}</w:t></w:r>";
        var body = "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>" + Run("Johannes 14") + "</w:p><w:p>" + Run("6", sup: true) + Run("Jesus spricht: ") + Run("Ich bin der Weg", "C00000") + Run(" und mehr.")
            + string.Concat(Enumerable.Range(7, 12).Select(i => Run($"{i}", sup: true) + Run($"Vers {i} ", "000000"))) + "</w:p>";
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            w.Write($"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{body}</w:body></w:document>");
        var b = BibleImporters.Read(path, "de");
        Assert.Equal("Ich bin der Weg", Red(V(b, 6)));
        Assert.DoesNotContain(V(b, 7).Segments, s => s.WordsOfJesus);
    }

    [Theory]
    [InlineData("red", true)]
    [InlineData("#f00", true)]
    [InlineData("#C00000", true)]
    [InlineData("rgb(204, 0, 0)", true)]
    [InlineData("#000000", false)]
    [InlineData("#ff8800", false)]
    [InlineData("blue", false)]
    [InlineData("auto", false)]
    public void Red_colors_are_told_apart(string color, bool red) => Assert.Equal(red, BibleImporters.IsRedColor(color));
}
