using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

public class ImportTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));
    public ImportTests() => Directory.CreateDirectory(_dir);

    const string CsvVerse = """
        <p class="vb-paragraph"><span class="bible-verse"><span class="btn-group btn-verse-button"><button type="button" class="btn" data-reference="1. Mo 1,{0}" data-url="u" data-player-start="0">{0}</button></span> <span class="bible-verse-text">Im<span class="cp-ref cp-hidden"></span> <a href="s" class="strong-link strong-H7225" data-position="H7225@1@1@1">Anfang<span class="strong-no cp-sup"> H7225</span></a> <a href="s" class="strong-link" data-position="H8064@1@1@1">Himmel<span class="strong-no"> <span class="fas"></span>H8064</span></a><sup class="footnote" data-footnote="Im Hebr. &quot;Himmel&quot; im Plural.">a</sup> und <small>die</small> Erde.</span></span></p>
        """;

    [Fact]
    public void CsvOfflineHtml_ReadsVersesStrongsAndFootnotes()
    {
        var path = Path.Combine(_dir, "csv.html");
        File.WriteAllText(path, "<html>" + string.Concat(Enumerable.Range(1, 1100).Select(i => string.Format(CsvVerse, (i % 30) + 1))) + "</html>");
        var bible = BibleImporters.ReadCsvOfflineHtml(path);
        var v = bible.Verses[0];
        Assert.Equal(("GEN", 1, 2), (v.Book, v.Chapter, v.Verse));
        Assert.Equal("Im Anfang Himmel und die Erde.", new Verse(v.Verse, v.Segments).Text.Replace("  ", " "));
        Assert.Equal(["H7225"], v.Segments.First(s => s.Text == "Anfang").Strongs);
        Assert.Equal(["H8064"], v.Segments.First(s => s.Text == "Himmel").Strongs);
        Assert.Contains(v.Segments, s => s.NoteId is not null);
        Assert.Contains("Himmel", bible.Footnotes[0].Note.Text);
    }

    [Fact]
    public void LocalStore_RoundTripsSegmentsAndServesChapters()
    {
        var store = new LocalBibleStore(Path.Combine(_dir, "local.db"));
        var id = store.Save(new ParsedBible("Test Bibel", "TST", "de",
            [new("JHN", 1, 1, [new Segment("Im Anfang ", false, null), new Segment("Wort", false, ["G3056"]), new Segment("", false, null, 0)])],
            [("JHN", 1, new Footnote(0, 1, "Fußnote"))]));
        Assert.Equal("local:tst", id);
        var chapter = store.GetChapterAsync(id, "JHN", 1).Result;
        Assert.Equal(["G3056"], chapter.Verses[0].Segments[1].Strongs);
        Assert.Equal("Fußnote", chapter.Footnotes![0].Text);
        Assert.Equal("Johannes", store.GetBooksAsync(id).Result.Single().CommonName);
        Assert.Single(store.GetTranslationsAsync().Result);
        store.Remove(id);
        Assert.Empty(store.GetTranslationsAsync().Result);
    }

    [Fact]
    public void Esword_And_Zefania_AreRead()
    {
        var bblx = Path.Combine(_dir, "t.bblx");
        using (var con = new SqliteConnection($"Data Source={bblx}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE Details(Title TEXT, Abbreviation TEXT); INSERT INTO Details VALUES('Meine Bibel','MB'); CREATE TABLE Bible(Book INT, Chapter INT, Verse INT, Scripture TEXT); INSERT INTO Bible VALUES(43,3,16,'Denn so <FI>Hinweis<Fi>hat Gott &amp; die Welt geliebt');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var e = BibleImporters.ReadEsword(bblx, "de");
        Assert.Equal(("JHN", 3, 16), (e.Verses[0].Book, e.Verses[0].Chapter, e.Verses[0].Verse));
        Assert.Equal("Denn so hat Gott & die Welt geliebt", e.Verses[0].Segments[0].Text);

        var xml = Path.Combine(_dir, "z.xml");
        File.WriteAllText(xml, "<XMLBIBLE biblename=\"Zef Bibel\"><BIBLEBOOK bnumber=\"1\"><CHAPTER cnumber=\"1\"><VERS vnumber=\"1\">Im Anfang <NOTE>x</NOTE>schuf Gott</VERS></CHAPTER></BIBLEBOOK></XMLBIBLE>");
        var z = BibleImporters.ReadZefania(xml, "de");
        Assert.Equal("Zef Bibel", z.Name);
        Assert.Equal("Im Anfang schuf Gott", z.Verses[0].Segments[0].Text);
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }
}
