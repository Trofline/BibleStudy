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
