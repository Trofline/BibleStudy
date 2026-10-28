using System.IO.Compression;
using System.Text;
using BibleStudy.Core.Import;
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
    }

    [Fact]
    public void Markdown_HeadingsAndNumberedLines()
    {
        var verses = string.Join("\n\n", Verses.Select(v => v % 3 == 0 ? $"<sup>{v}</sup> Vers {v} Text mit **Wörtern**." : v % 3 == 1 ? $"**{v}** Vers {v} Text mit Wörtern." : $"{v}. Vers {v} Text mit Wörtern."));
        Check(BibleImporters.Read(File_("g.md", $"# 1. Mose\n\n## Kapitel 1\n\n{verses}\n"), "de"));
        Check(BibleImporters.Read(File_("h.md", $"# Genesis 1\n\n{verses}\n"), "de"));
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
