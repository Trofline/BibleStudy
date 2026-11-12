using System.IO.Compression;
using System.Text;
using BibleStudy.Core.Materials;

namespace BibleStudy.Tests;

/// <summary>The "Materials" tab reads the documents of a Bible study into simple blocks; these tests build small files and check what is shown.</summary>
public class MaterialReaderTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs-materials-" + Guid.NewGuid().ToString("N"));

    public MaterialReaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    string Zip(string name, params (string Entry, string Text)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, text) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            w.Write(text);
        }
        return path;
    }

    const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void Word_keeps_headings_lists_tables_and_bold_text()
    {
        var xml = $"""
            <w:document xmlns:w="{Wns}"><w:body>
              <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Römer 8</w:t></w:r></w:p>
              <w:p><w:r><w:t xml:space="preserve">Lies </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>Röm 8,28</w:t></w:r><w:r><w:t xml:space="preserve"> laut.</w:t></w:r></w:p>
              <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/></w:numPr></w:pPr><w:r><w:t>Erste Frage</w:t></w:r></w:p>
              <w:p></w:p>
              <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Vers</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Thema</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            </w:body></w:document>
            """;
        var doc = MaterialReader.Read(Zip("Studie.docx", ("word/document.xml", xml)));
        Assert.Null(doc.Problem);
        Assert.Equal([BlockKind.Heading1, BlockKind.Paragraph, BlockKind.ListItem, BlockKind.TableRow], doc.Blocks.Select(b => b.Kind));
        Assert.Equal("Lies Röm 8,28 laut.", doc.Blocks[1].Text);
        Assert.Contains(doc.Blocks[1].Spans, s => s.Bold && s.Text == "Röm 8,28");
        Assert.Contains("Vers", doc.Blocks[3].Text);
        Assert.Contains("Thema", doc.Blocks[3].Text);
    }

    [Fact]
    public void Word_pictures_are_taken_out_after_their_paragraph()
    {
        const string a = "http://schemas.openxmlformats.org/drawingml/2006/main", r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var xml = $"""<w:document xmlns:w="{Wns}" xmlns:a="{a}" xmlns:r="{r}"><w:body><w:p><w:r><w:t>Karte</w:t></w:r><w:r><w:drawing><a:blip r:embed="rId7"/></w:drawing></w:r></w:p><w:p><w:r><w:drawing><a:blip r:embed="rId8"/></w:drawing></w:r></w:p></w:body></w:document>""";
        var rels = """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId7" Target="media/karte.png"/><Relationship Id="rId8" Target="media/alt.emf"/></Relationships>""";
        var path = Zip("Bild.docx", ("word/document.xml", xml), ("word/_rels/document.xml.rels", rels), ("word/media/karte.png", "png"), ("word/media/alt.emf", "emf"));
        var media = Path.Combine(_dir, "media");
        var blocks = MaterialReader.Read(path, media).Blocks;
        Assert.Equal([BlockKind.Paragraph, BlockKind.Image], blocks.Select(b => b.Kind));   // an EMF picture cannot be shown and is left out
        Assert.True(File.Exists(blocks[1].ImagePath));
        Assert.Single(MaterialReader.Read(path).Blocks);   // without a folder for pictures only the text
    }

    [Fact]
    public void Word_table_of_contents_in_a_content_control_links_to_its_headings()
    {
        var xml = $"""
            <w:document xmlns:w="{Wns}"><w:body>
              <w:sdt><w:sdtContent>
                <w:p><w:pPr><w:pStyle w:val="Inhaltsverzeichnisberschrift"/></w:pPr><w:r><w:t>Inhalt</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Verzeichnis1"/></w:pPr><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText> TOC \o "1-3" \h </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r>
                  <w:hyperlink w:anchor="_Toc11"><w:r><w:t>1. Anfang</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t>2</w:t></w:r></w:hyperlink></w:p>
                <w:p><w:pPr><w:pStyle w:val="Verzeichnis2"/></w:pPr><w:hyperlink w:anchor="_Toc12"><w:r><w:t>Unterpunkt</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t>3</w:t></w:r></w:hyperlink></w:p>
                <w:p><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
              </w:sdtContent></w:sdt>
              <w:p><w:pPr><w:pStyle w:val="berschrift1"/></w:pPr><w:bookmarkStart w:id="0" w:name="_Toc11"/><w:r><w:t>1. Anfang</w:t></w:r><w:bookmarkEnd w:id="0"/></w:p>
              <w:p><w:pPr><w:pStyle w:val="berschrift2"/></w:pPr><w:bookmarkStart w:id="1" w:name="_Toc12"/><w:r><w:t>Unterpunkt</w:t></w:r></w:p>
            </w:body></w:document>
            """;
        var blocks = MaterialReader.Read(Zip("Mit Inhalt.docx", ("word/document.xml", xml))).Blocks;
        var toc = blocks.Where(b => b.Kind == BlockKind.TocEntry).ToList();
        Assert.Equal(2, toc.Count);
        Assert.Equal(("1. Anfang", "2", "_Toc11", 1), (toc[0].Spans[0].Text, toc[0].Spans[1].Text, toc[0].LinkTo, toc[0].Level));
        Assert.Equal(("Unterpunkt", "_Toc12", 2), (toc[1].Spans[0].Text, toc[1].LinkTo, toc[1].Level));
        Assert.Contains(blocks, b => b.Kind == BlockKind.Heading1 && b.Anchors!.Contains("_Toc11"));
        Assert.Contains(blocks, b => b.Kind == BlockKind.Heading2 && b.Anchors!.Contains("_Toc12"));
    }

    /// <summary>A small old Word file (.doc) built by hand: a compound file with the streams WordDocument and 1Table.</summary>
    static byte[] TinyDoc(string text, string bookmark, int bookmarkCp)
    {
        const uint End = 0xFFFFFFFE, Free = 0xFFFFFFFF;
        var wd = new byte[4096]; var tb = new byte[4096];
        void W16(byte[] b, int i, int v) => BitConverter.GetBytes((ushort)v).CopyTo(b, i);
        void W32(byte[] b, int i, int v) => BitConverter.GetBytes(v).CopyTo(b, i);
        W16(wd, 0, 0xA5EC); W16(wd, 0x0A, 0x0200);              // Word, tables in "1Table"
        W16(wd, 32, 14); W16(wd, 62, 22); W32(wd, 64 + 12, text.Length);   // counts: ccpText
        W16(wd, 152, 34);                                        // 34 pairs of offset and length
        void Fc(int i, int fc, int lcb) { W32(wd, 154 + i * 8, fc); W32(wd, 158 + i * 8, lcb); }
        System.Text.Encoding.Latin1.GetBytes(text).CopyTo(wd, 1024);
        // piece table: one piece, single bytes from 1024
        tb[0] = 2; W32(tb, 1, 16); W32(tb, 5, 0); W32(tb, 9, text.Length); W32(tb, 13 + 2, (1024 * 2) | 0x40000000);
        Fc(33, 0, 21);
        // the bookmark: names (extended, UTF-16) and where it starts
        W16(tb, 100, 0xFFFF); W16(tb, 102, 1); W16(tb, 104, 0); W16(tb, 106, bookmark.Length); System.Text.Encoding.Unicode.GetBytes(bookmark).CopyTo(tb, 108);
        Fc(21, 100, 8 + bookmark.Length * 2);
        W32(tb, 200, bookmarkCp); W32(tb, 204, bookmarkCp + 5); Fc(22, 200, 12);
        // the compound file: header, one sector of allocation table, one of directory, then 8 + 8 sectors of streams
        var file = new byte[512 * 19];
        BitConverter.GetBytes(0xE11AB1A1E011CFD0).CopyTo(file, 0);
        W16(file, 0x18, 0x3E); W16(file, 0x1A, 3); W16(file, 0x1C, 0xFFFE); W16(file, 0x1E, 9); W16(file, 0x20, 6);
        W32(file, 0x2C, 1); W32(file, 0x30, 1); W32(file, 0x38, 4096); W32(file, 0x3C, unchecked((int)End)); W32(file, 0x44, unchecked((int)End));
        W32(file, 0x4C, 0); for (var i = 1; i < 109; i++) W32(file, 0x4C + i * 4, unchecked((int)Free));
        var fat = 512;
        for (var i = 0; i < 128; i++) W32(file, fat + i * 4, unchecked((int)Free));
        W32(file, fat, unchecked((int)0xFFFFFFFD)); W32(file, fat + 4, unchecked((int)End));
        for (var s = 2; s < 18; s++) W32(file, fat + s * 4, s is 9 or 17 ? unchecked((int)End) : s + 1);
        var dir = 1024;
        void Entry(int n, string name, byte type, uint start, int size)
        {
            var e = dir + n * 128;
            System.Text.Encoding.Unicode.GetBytes(name).CopyTo(file, e);
            W16(file, e + 0x40, (name.Length + 1) * 2); file[e + 0x42] = type;
            W32(file, e + 0x74, unchecked((int)start)); W32(file, e + 0x78, size);
        }
        Entry(0, "Root Entry", 5, End, 0); Entry(1, "WordDocument", 2, 2, 4096); Entry(2, "1Table", 2, 10, 4096);
        wd.CopyTo(file, 512 * 3); tb.CopyTo(file, 512 * 11);
        return file;
    }

    [Fact]
    public void Old_Word_files_are_read_with_their_table_of_contents()
    {
        // a TOC field with one line (HYPERLINK to the bookmark), then the heading with that bookmark and a paragraph
        const string head = "Erstes Kapitel";
        var text = "\u0013 TOC \\o \u0014\u0013 HYPERLINK \\l \"_Toc1\" \u0014" + head + "\t3\u0015\r\u0015" + head + "\rText des Kapitels.\rZelle A\u0007Zelle B\u0007\u0007";
        var cp = text.IndexOf("\r\u0015", StringComparison.Ordinal) + 2;
        var path = Path.Combine(_dir, "alt.doc");
        File.WriteAllBytes(path, TinyDoc(text, "_Toc1", cp));
        var doc = MaterialReader.Read(path);
        Assert.Null(doc.Problem);
        var toc = Assert.Single(doc.Blocks, b => b.Kind == BlockKind.TocEntry);
        Assert.Equal((head, "3", "_Toc1"), (toc.Spans[0].Text, toc.Spans[1].Text, toc.LinkTo));
        Assert.Contains(doc.Blocks, b => b.Text == head && b.Anchors!.Contains("_Toc1"));
        Assert.Contains(doc.Blocks, b => b.Text == "Text des Kapitels.");
        Assert.Contains(doc.Blocks, b => b.Kind == BlockKind.TableRow && b.Text.Contains("Zelle A") && b.Text.Contains("Zelle B"));
        Assert.True(MaterialReader.CanShow("Mappe.DOC"));
    }

    [Theory]
    [InlineData("Heading2", 2)]
    [InlineData("berschrift3", 3)]
    [InlineData("Überschrift1", 1)]
    [InlineData("Title", 1)]
    [InlineData("Standard", 0)]
    public void Word_heading_styles_are_recognized(string style, int level) => Assert.Equal(level, MaterialReader.HeadingLevel(style));

    [Fact]
    public void PowerPoint_gives_one_part_per_slide_with_its_title()
    {
        const string a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        string Slide(string title, string point) => $"""<p:sld xmlns:p="x" xmlns:a="{a}"><a:p><a:r><a:t>{title}</a:t></a:r></a:p><a:p><a:r><a:t>{point}</a:t></a:r></a:p></p:sld>""";
        var path = Zip("Folien.pptx", ("ppt/slides/slide2.xml", Slide("Zweite", "b")), ("ppt/slides/slide10.xml", Slide("Zehnte", "c")), ("ppt/slides/slide1.xml", Slide("Erste", "a")));
        var blocks = MaterialReader.Read(path).Blocks;
        var titles = blocks.Where(b => b.Kind == BlockKind.Heading2).Select(b => b.Text).ToList();
        Assert.Equal(["Erste", "Zweite", "Zehnte"], titles);   // slide 10 after slide 2, not after slide 1
        Assert.Equal(3, blocks.Count(b => b.Kind == BlockKind.Page));
    }

    [Fact]
    public void Markdown_and_text_and_html_are_read()
    {
        var md = MaterialReader.ReadMarkdown("# Titel\n\nEin **wichtiger** Satz\nüber zwei Zeilen.\n\n- Punkt eins\n- Punkt *zwei*\n");
        Assert.Equal([BlockKind.Heading1, BlockKind.Paragraph, BlockKind.ListItem, BlockKind.ListItem], md.Select(b => b.Kind));
        Assert.Equal("Ein wichtiger Satz über zwei Zeilen.", md[1].Text);
        Assert.Contains(md[1].Spans, s => s.Bold && s.Text == "wichtiger");

        Assert.Equal(2, MaterialReader.ReadText("Absatz eins\nnoch eins\n\n\nAbsatz zwei").Count);

        var html = MaterialReader.ReadHtml("<html><head><title>x</title><style>p{}</style></head><body><h2>Frage</h2><p>Was sagt <b>Joh 3,16</b>?&nbsp;</p><ul><li>A</li></ul></body></html>");
        Assert.Equal([BlockKind.Heading2, BlockKind.Paragraph, BlockKind.ListItem], html.Select(b => b.Kind));
        Assert.Equal("Was sagt Joh 3,16?", html[1].Text);
    }

    [Fact]
    public void Scan_leaves_out_office_temp_files_and_honours_sub_folders()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "~$a.docx"), "x");
        File.WriteAllText(Path.Combine(_dir, "notiz.xyz"), "x");
        Directory.CreateDirectory(Path.Combine(_dir, "Woche 2"));
        File.WriteAllText(Path.Combine(_dir, "Woche 2", "b.pdf"), "x");
        Assert.Equal(["a.txt"], MaterialReader.Scan(_dir, false).Select(Path.GetFileName));
        Assert.Equal(["a.txt", "b.pdf"], MaterialReader.Scan(_dir, true).Select(Path.GetFileName).Order());
        Assert.Empty(MaterialReader.Scan(Path.Combine(_dir, "gibt es nicht"), true));
    }

    [Fact]
    public void A_broken_file_gives_a_problem_not_an_exception()
    {
        var path = Path.Combine(_dir, "kaputt.docx");
        File.WriteAllText(path, "kein zip");
        var doc = MaterialReader.Read(path);
        Assert.NotNull(doc.Problem);
        Assert.Empty(doc.Blocks);
        Assert.True(MaterialReader.CanShow("Studie.DOCX"));
    }
}
