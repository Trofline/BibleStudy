using BibleStudy.Core.Import;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

public class OwnCommentariesTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_cmt_" + Guid.NewGuid().ToString("N"));
    public OwnCommentariesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    const string Text = "# Mein Kommentar\n\nEine Vorrede, die zu keiner Stelle gehört.\n\n## Johannes 3\n\nEinleitung zum Kapitel.\n\n## Joh 3,16-18\n\nGott liebt die Welt.\nZweite Zeile.\n\n**Joh 3,19:**\nDas Licht ist gekommen.\n\n## Römer 8,28\n\nAlles dient zum Besten.\n";

    [Fact]
    public void Markdown_references_start_entries_and_a_chapter_heading_is_the_introduction()
    {
        var c = OwnCommentaries.ReadText(Text, "Datei");
        Assert.Equal("Mein Kommentar", c.Name);
        Assert.Equal(
            [("JHN", 3, 0, 0), ("JHN", 3, 16, 18), ("JHN", 3, 19, 19), ("ROM", 8, 28, 28)],
            c.Entries.Select(e => (e.Book, e.Chapter, e.From, e.To)).ToArray());
        Assert.Equal("Gott liebt die Welt.\nZweite Zeile.", c.Entries[1].Text);
        Assert.Equal("Das Licht ist gekommen.", c.Entries[2].Text);
    }

    [Fact]
    public void A_file_without_any_reference_is_refused_with_a_reason()
    {
        var path = Path.Combine(_dir, "leer.md");
        File.WriteAllText(path, "Nur Text ohne Bibelstelle.");
        Assert.Throws<InvalidDataException>(() => OwnCommentaries.Read(path));
    }

    [Fact]
    public void An_esword_module_is_read_with_its_title_and_verse_ranges()
    {
        var path = Path.Combine(_dir, "Mein.cmtx");
        using (var con = new SqliteConnection($"Data Source={path}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE Details(Title TEXT, Abbreviation TEXT); INSERT INTO Details VALUES('Mein Auslegung','MA');"
                + "CREATE TABLE Commentary(Book INT, ChapterBegin INT, VerseBegin INT, ChapterEnd INT, VerseEnd INT, Comments TEXT);"
                + "INSERT INTO Commentary VALUES(43,3,16,3,18,'Gott liebt die <b>Welt</b>.');"
                + "INSERT INTO Commentary VALUES(43,3,0,3,0,'Zum ganzen Kapitel.');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var c = OwnCommentaries.Read(path);
        Assert.Equal("Mein Auslegung", c.Name);
        Assert.Contains(c.Entries, e => e is { Book: "JHN", Chapter: 3, From: 16, To: 18 } && e.Text == "Gott liebt die Welt.");
        Assert.Contains(c.Entries, e => e is { Book: "JHN", Chapter: 3, From: 0 } && e.Text == "Zum ganzen Kapitel.");
    }

    [Fact]
    public void The_store_gives_back_a_chapter_with_ranges_replaces_a_commentary_of_the_same_name_and_removes_it()
    {
        var store = new LocalCommentaryStore(Path.Combine(_dir, "c.db"));
        var id = store.Save(OwnCommentaries.ReadText(Text, "x"), "Mein.md");
        Assert.Equal("own:mein-kommentar", id);
        Assert.Equal(["Mein Kommentar"], store.List().Select(i => i.Name).ToArray());
        var chapter = store.GetChapter(id, "JHN", 3)!;
        Assert.Equal("Einleitung zum Kapitel.", chapter.Introduction);
        Assert.Equal([(16, 18), (19, 19)], chapter.Ranges!.ToArray());
        Assert.Equal([16, 19], chapter.Notes.Select(n => n.Verse).ToArray());
        Assert.Null(store.GetChapter(id, "GEN", 1));
        Assert.Equal(4, store.All(id).Count);

        store.Save(new ParsedCommentary("Mein Kommentar", [new CommentaryEntry("GEN", 1, 1, 1, "Neu")]));
        Assert.Single(store.List());
        Assert.Null(store.GetChapter(id, "JHN", 3));
        Assert.Equal("Neu", store.GetChapter(id, "GEN", 1)!.Notes[0].Text);

        store.Remove(id);
        Assert.Empty(store.List());
        Assert.Empty(store.All(id));
    }
}
