using Microsoft.Data.Sqlite;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class VaultMarkTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    VaultStore New() { Directory.CreateDirectory(_dir); return new VaultStore(Path.Combine(_dir, "v.db"), "t"); }

    [Fact]
    public void A_mark_over_several_verses_stays_one_entry()
    {
        var s = New();
        var id = s.AddMark("eng_kjv", new VerseRef("GEN", 1, 4), 10, 7, "text of four to six", "#FFD54F", "", null, ["tag"], endVerse: 6);
        var m = Assert.Single(s.GetMarks("GEN", 1));
        Assert.Equal(id, m.Id);
        Assert.Equal((4, 6), (m.Ref.Verse, m.LastVerse));
        Assert.True(m.Covers(5)); Assert.False(m.Covers(7));
        s.AddMark("eng_kjv", new VerseRef("GEN", 1, 9), 0, 4, "x", "#FFD54F", "", null, []);
        Assert.Equal(9, s.GetMarks("GEN", 1).Last().LastVerse);   // a single verse ends where it starts
    }

    [Fact]
    public void Several_marks_can_be_recolored_and_deleted_in_one_step()
    {
        var s = New();
        var a = s.AddMark("*", new VerseRef("GEN", 1, 1), -1, -1, "a", "#111111", "", null, []);
        var b = s.AddMark("*", new VerseRef("GEN", 1, 2), -1, -1, "b", "#111111", "", null, []);
        s.RecolorMarks([a, b], "#222222");
        Assert.All(s.GetMarks(), m => Assert.Equal("#222222", m.Color));
        s.DeleteMarks([a, b]);
        Assert.Empty(s.GetMarks());
    }

    [Fact]
    public void Headings_are_kept_per_chapter_and_can_be_changed()
    {
        var s = New();
        var id = s.AddHeading(new VerseRef("PSA", 23, 1), "  Der gute Hirte ", 2);
        s.AddHeading(new VerseRef("PSA", 24, 1), "Anderes Kapitel");
        var h = Assert.Single(s.GetHeadings("PSA", 23));
        Assert.Equal(("Der gute Hirte", 2, 1), (h.Text, h.Level, h.Ref.Verse));
        s.UpdateHeading(id, "Der Hirte", 9);   // the level stays within 1 to 3
        Assert.Equal(("Der Hirte", 3), (s.GetHeadings("PSA", 23)[0].Text, s.GetHeadings("PSA", 23)[0].Level));
        s.DeleteHeading(id);
        Assert.Empty(s.GetHeadings("PSA", 23));
    }

    [Fact]
    public void An_older_vault_without_the_new_column_keeps_its_marks()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "old.db");
        using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE marks(id INTEGER PRIMARY KEY, translation TEXT, book TEXT, chapter INT, verse INT, start_off INT, end_off INT, text TEXT, color TEXT, note TEXT, folder_id INT, created TEXT);
                INSERT INTO marks(translation,book,chapter,verse,start_off,end_off,text,color,note,created) VALUES('eng_kjv','JHN',3,16,0,4,'For God','#FFD54F','mein Vers','2020-01-01');
                """;
            cmd.ExecuteNonQuery();
        }
        var s = new VaultStore(path, "old");
        var m = Assert.Single(s.GetMarks("JHN", 3));
        Assert.Equal(("mein Vers", 16, 0), (m.Note, m.LastVerse, m.EndVerse));
        Assert.True(File.Exists(path + ".vor-Mehrvers.bak"));   // a copy of the old vault was kept before it was changed
    }

    [Fact]
    public void Own_search_finds_headings_and_marks_over_several_verses()
    {
        var s = New();
        s.AddHeading(new VerseRef("PSA", 23, 1), "Der gute Hirte");
        s.AddMark("eng_kjv", new VerseRef("GEN", 1, 4), 0, 5, "alpha beta", "#FFD54F", "Notiz zum Bereich", null, [], endVerse: 6);
        Assert.Contains(OwnSearch.Search(s, "Hirte"), h => h.Kind == OwnKind.Heading);
        Assert.Contains(OwnSearch.About(s, new VerseRef("GEN", 1, 5)), h => h.Kind == OwnKind.Mark);   // the middle verse of 4 to 6
        Assert.Contains(OwnSearch.About(s, new VerseRef("PSA", 23, 1)), h => h.Kind == OwnKind.Heading);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
