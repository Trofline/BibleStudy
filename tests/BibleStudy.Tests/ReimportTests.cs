using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

public class ReimportTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_re_" + Guid.NewGuid().ToString("N"));
    public ReimportTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    static ParsedBible Bible(string name) => new(name, "TST", "de", [new ParsedVerse("GEN", 1, 1, [new Segment("Im Anfang schuf Gott.")])], []);

    [Fact]
    public void A_database_from_before_gets_the_new_columns_and_its_translations_count_as_old()
    {
        var path = Path.Combine(_dir, "local.db");
        using (var con = new SqliteConnection("Data Source=" + path))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE translations(id TEXT PRIMARY KEY, name TEXT, short_name TEXT, language TEXT, imported TEXT); INSERT INTO translations VALUES('local:alt','Alte Bibel','ALT','de','2026-01-01')";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var store = new LocalBibleStore(path);
        var old = store.GetOutdated();
        Assert.Single(old);
        Assert.Equal("Alte Bibel", old[0].Name);
        Assert.Null(old[0].Source);
    }

    [Fact]
    public void A_new_import_is_current_and_remembers_its_file()
    {
        var store = new LocalBibleStore(Path.Combine(_dir, "new.db"));
        var id = store.Save(Bible("Neue Bibel"), @"C:\Bibeln\neu.pdf");
        Assert.Empty(store.GetOutdated());
        // saved again without a path (the file was given in another way): the remembered one stays
        store.Save(Bible("Neue Bibel"));
        Assert.Empty(store.GetOutdated());
        Assert.Equal(id, store.GetTranslationsAsync().Result.Single().Id);
    }

    [Theory]
    [InlineData("Dies ist wurde gut", 5, "ist", 5, 8)]       // still where it was
    [InlineData("Dies ist wurde gut", 0, "ist", 5, 8)]       // moved: found again
    [InlineData("ist hier und ist dort", 18, "ist", 13, 16)] // two places: the one nearest to the old
    public void MarkedWordsAreFoundAgainInTheNewText(string text, int oldStart, string marked, int start, int end)
    {
        var at = MarkLogic.Realign(marked, oldStart, text);
        Assert.Equal((start, end), at);
    }

    [Fact]
    public void WordsThatAreNoLongerThereLeaveTheMarkAlone() => Assert.Null(MarkLogic.Realign("wurd e", 4, "Und sie wurde schwanger"));
}
