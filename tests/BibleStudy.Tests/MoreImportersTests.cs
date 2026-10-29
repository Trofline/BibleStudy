using System.IO.Compression;
using System.Text;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

public class MoreImportersTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    string File_(string name, string content) { Directory.CreateDirectory(_dir); var p = Path.Combine(_dir, name); File.WriteAllText(p, content, new UTF8Encoding(false)); return p; }

    static IEnumerable<int> Verses => Enumerable.Range(1, 12);

    static void Check(ParsedBible b)
    {
        Assert.Equal(12, b.Verses.Count);
        var v3 = b.Verses.First(v => v.Verse == 3);
        Assert.Equal(("GEN", 1), (v3.Book, v3.Chapter));
        Assert.Equal("Vers 3 Text mit Wörtern.", string.Concat(v3.Segments.Select(s => s.Text)));
    }

    [Fact]
    public void Osis_Milestones_And_Containers_SkipNotes()
    {
        var milestones = string.Concat(Verses.Select(v => $"<verse sID=\"Gen.1.{v}\"/>Vers {v} Text <note>Fußnote</note>mit Wörtern.<verse eID=\"Gen.1.{v}\"/>"));
        Check(BibleImporters.Read(File_("m.xml", $"<osis><osisText osisIDWork=\"T\"><div type=\"book\" osisID=\"Gen\"><chapter osisID=\"Gen.1\">{milestones}</chapter></div></osisText></osis>"), "de"));
        var containers = string.Concat(Verses.Select(v => $"<verse osisID=\"Gen.1.{v}\">Vers {v} Text <note>Fußnote</note>mit Wörtern.</verse>"));
        Check(BibleImporters.Read(File_("c.xml", $"<osis><osisText><chapter osisID=\"Gen.1\">{containers}</chapter></osisText></osis>"), "de"));
    }

    [Fact]
    public void Usx_SkipsHeadingsAndNotes()
    {
        var body = string.Concat(Verses.Select(v => $"<verse number=\"{v}\" style=\"v\" />Vers {v} Text <note style=\"f\">Fußnote</note>mit Wörtern."));
        Check(BibleImporters.Read(File_("b.usx", $"<usx version=\"3.0\"><book code=\"GEN\" style=\"id\">x</book><chapter number=\"1\" style=\"c\" /><para style=\"s\">Überschrift</para><para style=\"p\">{body}</para></usx>"), "de"));
    }

    [Fact]
    public void Usfm_SingleFile_And_Zip()
    {
        var usfm = "\\id GEN Test\n\\h Genesis\n\\c 1\n\\s1 Überschrift\n\\p\n" + string.Join("\n", Verses.Select(v => $"\\v {v} Vers {v} Text \\f + \\ft Fußnote\\f*mit \\w Wörtern|lemma=\"x\"\\w*."));
        Check(BibleImporters.Read(File_("g.usfm", usfm), "de"));
        Directory.CreateDirectory(_dir);
        var zip = Path.Combine(_dir, "z.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) { var e = z.CreateEntry("01-GEN.usfm"); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(usfm); }
        Check(BibleImporters.Read(zip, "de"));
    }

    [Fact]
    public void MySword_Module_IsRead()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "t.bbl.mybible");
        using (var con = new SqliteConnection($"Data Source={path}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE Bible(Book INT, Chapter INT, Verse INT, Scripture TEXT);" + string.Concat(Verses.Select(v => $"INSERT INTO Bible VALUES(1,1,{v},'Vers {v} Text mit Wörtern.');"));
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        Check(BibleImporters.Read(path, "de"));
        File.Delete(path);   // the file is not kept locked after reading
    }

    [Fact]
    public void Esword_Rtf_IsCleaned()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "e.bblx");
        using (var con = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE Bible(Book INT, Chapter INT, Verse INT, Scripture TEXT);" + string.Concat(Verses.Select(v => $"INSERT INTO Bible VALUES(1,1,{v},'Vers {v} {{\\cf6 Text}} mit Wörtern.\\par');"));
            cmd.ExecuteNonQuery();
        }
        Check(BibleImporters.Read(path, "de"));
    }

    [Fact]
    public void Markdown_HeadingsAndNumberedLines()
    {
        var verses = string.Join("\n\n", Verses.Select(v => v % 3 == 0 ? $"<sup>{v}</sup> Vers {v} Text mit **Wörtern**." : v % 3 == 1 ? $"**{v}** Vers {v} Text mit Wörtern." : $"{v}. Vers {v} Text mit Wörtern."));
        Check(BibleImporters.Read(File_("g.md", $"# 1. Mose\n\n## Kapitel 1\n\n{verses}\n"), "de"));
        Check(BibleImporters.Read(File_("h.md", $"# Genesis 1\n\n{verses}\n"), "de"));
    }

    [Fact]
    public void Markdown_Paragraphs_ContinuationNumbers_AndChapterWithoutHeading()
    {
        // several verses in one paragraph
        var para = string.Join(" ", Verses.Select(v => $"<sup>{v}</sup> Vers {v} Text mit Wörtern."));
        Check(BibleImporters.Read(File_("p.md", $"# 1. Mose 1\n\n{para}\n"), "de"));

        // a wrapped line that starts with a number stays in its verse; verse 1 again means the next chapter
        var b = BibleImporters.Read(File_("w.md", "# Genesis\n\n## 1\n\n1 Am Anfang\n2 Da waren\n12 Stämme im Text\n3 Und Gott\n" +
            string.Join("\n", Enumerable.Range(4, 6).Select(v => $"{v} Vers {v}")) + "\n1 Neues Kapitel\n2 Weiter\n"), "de");
        Assert.Equal("Da waren 12 Stämme im Text", string.Concat(b.Verses.First(v => v.Chapter == 1 && v.Verse == 2).Segments.Select(s => s.Text)));
        Assert.Equal("Neues Kapitel", string.Concat(b.Verses.First(v => v.Chapter == 2 && v.Verse == 1).Segments.Select(s => s.Text)));
    }

    [Fact]
    public void Text_Csv_And_Json()
    {
        Check(BibleImporters.Read(File_("t.txt", string.Join("\n", Verses.Select(v => $"1. Mose 1,{v} Vers {v} Text mit Wörtern."))), "de"));
        Check(BibleImporters.Read(File_("t.csv", string.Join("\n", Verses.Select(v => $"Genesis;1;{v};\"Vers {v} Text mit Wörtern.\""))), "de"));
        Check(BibleImporters.Read(File_("a.json", "[" + string.Join(",", Verses.Select(v => $"{{\"book\":\"Gen\",\"chapter\":1,\"verse\":{v},\"text\":\"Vers {v} Text mit Wörtern.\"}}")) + "]"), "de"));
        Check(BibleImporters.Read(File_("b.json", "[{\"abbrev\":\"gn\",\"name\":\"Genesis\",\"chapters\":[[" + string.Join(",", Verses.Select(v => $"\"Vers {v} Text mit Wörtern.\"")) + "]]}]"), "de"));
        Check(BibleImporters.Read(File_("c.json", "{\"Genesis\":{\"1\":{" + string.Join(",", Verses.Select(v => $"\"{v}\":\"Vers {v} Text mit Wörtern.\"")) + "}}}"), "de"));
    }

    [Fact]
    public void LocalStore_DifferentBiblesWithSameShortName_DoNotReplaceEachOther()
    {
        Directory.CreateDirectory(_dir);
        var store = new LocalBibleStore(Path.Combine(_dir, "local.db"));
        ParsedBible Bible(string name) => new(name, "Schlachter20", "de", [new ParsedVerse("GEN", 1, 1, [new Segment("Text")])], []);
        var at = store.Save(Bible("Schlachter2000-AT"));
        var nt = store.Save(Bible("Schlachter2000-NT"));
        Assert.NotEqual(at, nt);
        Assert.Equal(at, store.Save(Bible("Schlachter2000-AT")));   // importing the same file again replaces it
        SqliteConnection.ClearAllPools();
    }

    static string HtmlBody() => "<h1>1. Mose</h1><h2>Kapitel 1</h2>" + string.Concat(Verses.Select(v => v % 2 == 0 ? $"<p><sup>{v}</sup> Vers {v} Text mit Wörtern.</p>" : $"<p><span class=\"verse\">{v}</span> Vers {v} Text mit Wörtern.</p>"));

    [Fact]
    public void Html_And_Epub_AreRead_InSpineOrder()
    {
        Check(BibleImporters.Read(File_("b.html", "<html><head><title>x</title><style>p{}</style></head><body>" + HtmlBody() + "</body></html>"), "de"));

        Directory.CreateDirectory(_dir);
        var epub = Path.Combine(_dir, "b.epub");
        using (var z = ZipFile.Open(epub, ZipArchiveMode.Create))
        {
            void Add(string name, string text) { var e = z.CreateEntry(name); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(text); }
            Add("META-INF/container.xml", "<container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
            Add("OEBPS/content.opf", "<package xmlns=\"http://www.idpf.org/2007/opf\"><manifest><item id=\"a\" href=\"a.xhtml\"/><item id=\"b\" href=\"b%20x.xhtml\"/></manifest><spine><itemref idref=\"a\"/><itemref idref=\"b\"/></spine></package>");
            var all = HtmlBody();
            var cut = all.IndexOf("<p><sup>4</sup>", StringComparison.Ordinal);
            Add("OEBPS/a.xhtml", "<html><body>" + all[..cut] + "</body></html>");
            Add("OEBPS/b x.xhtml", "<html><body>" + all[cut..] + "</body></html>");   // the second page comes later in the spine than its name suggests
        }
        Check(BibleImporters.Read(epub, "de"));
    }

    [Fact]
    public void Epub_WithCopyProtection_IsRefused()
    {
        Directory.CreateDirectory(_dir);
        foreach (var (name, entry, content) in new[] { ("rights.epub", "META-INF/rights.xml", "<rights/>"), ("enc.epub", "META-INF/encryption.xml", "<encryption xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><EncryptedData><CipherData><CipherReference URI=\"OEBPS/a.xhtml\"/></CipherData></EncryptedData></encryption>") })
        {
            var p = Path.Combine(_dir, name);
            using (var z = ZipFile.Open(p, ZipArchiveMode.Create)) { var e = z.CreateEntry(entry); using var w = new StreamWriter(e.Open()); w.Write(content); }
            Assert.Contains("kopiergeschützt", Assert.Throws<InvalidDataException>(() => BibleImporters.Read(p, "de")).Message);
        }
        // an obfuscated font is no copy protection
        var fontOnly = Path.Combine(_dir, "font.epub");
        using (var z = ZipFile.Open(fontOnly, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry("META-INF/encryption.xml"); using (var w = new StreamWriter(e.Open())) w.Write("<encryption xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><EncryptedData><CipherData><CipherReference URI=\"OEBPS/f.otf\"/></CipherData></EncryptedData></encryption>");
            var p = z.CreateEntry("OEBPS/a.xhtml"); using (var w = new StreamWriter(p.Open(), new UTF8Encoding(false))) w.Write("<html><body>" + HtmlBody() + "</body></html>");
        }
        Check(BibleImporters.Read(fontOnly, "de"));

        foreach (var ext in new[] { ".azw3", ".mobi", ".kfx", ".lbxlls" })
            Assert.Contains("kopiergeschützt", Assert.Throws<NotSupportedException>(() => BibleImporters.Read("x" + ext, "de")).Message);
    }

    [Fact]
    public void Docx_Odt_Rtf_Fb2_AreRead()
    {
        Directory.CreateDirectory(_dir);
        var w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var docx = Path.Combine(_dir, "b.docx");
        using (var z = ZipFile.Open(docx, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry("word/document.xml"); using var s = new StreamWriter(e.Open(), new UTF8Encoding(false));
            s.Write($"<w:document xmlns:w=\"{w}\"><w:body><w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>1. Mose</w:t></w:r></w:p><w:p><w:r><w:t>Kapitel 1</w:t></w:r></w:p>"
                + string.Concat(Verses.Select(v => $"<w:p><w:r><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr><w:t>{v}</w:t></w:r><w:r><w:t xml:space=\"preserve\"> Vers {v} Text mit Wörtern.</w:t></w:r></w:p>")) + "</w:body></w:document>");
        }
        Check(BibleImporters.Read(docx, "de"));

        var odt = Path.Combine(_dir, "b.odt");
        using (var z = ZipFile.Open(odt, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry("content.xml"); using var s = new StreamWriter(e.Open(), new UTF8Encoding(false));
            s.Write("<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\">"
                + "<office:automatic-styles><style:style style:name=\"T1\" style:family=\"text\"><style:text-properties style:text-position=\"super 58%\"/></style:style></office:automatic-styles><office:body><office:text><text:h>1. Mose</text:h><text:p>Kapitel 1</text:p>"
                + string.Concat(Verses.Select(v => $"<text:p><text:span text:style-name=\"T1\">{v}</text:span> Vers {v} Text <text:note><text:note-body><text:p>Fußnote</text:p></text:note-body></text:note>mit Wörtern.</text:p>")) + "</office:text></office:body></office:document-content>");
        }
        Check(BibleImporters.Read(odt, "de"));

        var rtf = @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\pard 1. Mose\par Kapitel 1\par " + string.Concat(Verses.Select(v => $@"{{\super {v}}} Vers {v} Text mit W\'f6rtern.\par ")) + "}";
        Check(BibleImporters.Read(File_("b.rtf", rtf), "de"));

        var fb2 = "<FictionBook><body><section><title><p>1. Mose</p></title><p>Kapitel 1</p>" + string.Concat(Verses.Select(v => $"<p><sup>{v}</sup> Vers {v} Text mit Wörtern.</p>")) + "</section></body></FictionBook>";
        Check(BibleImporters.Read(File_("b.fb2", fb2), "de"));
    }

    [Fact]
    public void Pdf_IsRead_ByLines()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var lines = new List<string> { "Genesis", "Kapitel 1" };
        lines.AddRange(Verses.Select(v => $"{v} Vers {v} Text mit Woertern."));
        for (var i = 0; i < lines.Count; i++) page.AddText(lines[i], 11, new UglyToad.PdfPig.Core.PdfPoint(50, 800 - 20 * i), font);
        var path = Path.Combine(_dir, "b.pdf");
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(path, builder.Build());
        var b = BibleImporters.Read(path, "de");
        Assert.Equal(12, b.Verses.Count);
        Assert.Equal("Vers 3 Text mit Woertern.", string.Concat(b.Verses.First(v => v.Verse == 3).Segments.Select(s => s.Text)));
    }

    [Theory]
    [InlineData("1. Mose", "GEN")]
    [InlineData("Joh", "JHN")]
    [InlineData("John", "JHN")]
    [InlineData("1Cor", "1CO")]
    [InlineData("Psalm", "PSA")]
    [InlineData("Off", "REV")]
    public void BookNames_AreRecognized(string name, string usfm) => Assert.Equal(usfm, BibleImporters.BookFromName(name));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}
