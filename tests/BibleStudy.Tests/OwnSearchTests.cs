using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

public class OwnSearchTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));
    readonly VaultStore _v;

    public OwnSearchTests()
    {
        Directory.CreateDirectory(_dir);
        _v = new VaultStore(Path.Combine(_dir, "v.db"), "T");
    }

    static readonly VerseRef Joh316 = new("JHN", 3, 16);

    [Fact]
    public void Search_FindsEverythingOwn_AndNeedsAllWords()
    {
        _v.SetNote(Joh316, "Gottes Liebe zur Welt");
        _v.AddMark("*", new VerseRef("ROM", 8, 28), -1, -1, "Denen, die Gott lieben", "#FFD54F", "Trost im Leid", null, ["Hoffnung"]);
        _v.AddDocument("Predigt Liebe", "Über die Liebe Gottes in Joh 3,16.", null, false, []);
        _v.AddPrayer("Danke für deine Liebe", null);
        _v.AddTodo("Liebe studieren", null);
        _v.AddMyXref(Joh316, new VerseRef("1JN", 4, 9), 9, "Liebe gezeigt");

        var kinds = OwnSearch.Search(_v, "liebe").Select(h => h.Kind).ToHashSet();
        Assert.Equal([OwnKind.Document, OwnKind.Note, OwnKind.Mark, OwnKind.Prayer, OwnKind.Todo, OwnKind.MyXref], kinds.Order().ToArray());   // "lieben" contains "liebe"
        Assert.Single(OwnSearch.Search(_v, "trost leid"));            // marks: note
        Assert.Single(OwnSearch.Search(_v, "hoffnung"));              // marks: tag
        Assert.Single(OwnSearch.Search(_v, "lieben hoffnung"));       // words may come from the text and the tags of one thing
        Assert.Empty(OwnSearch.Search(_v, "liebe hoffnung welt"));    // but every word must occur in the same thing
        Assert.Empty(OwnSearch.Search(_v, "   "));
    }

    [Fact]
    public void About_FindsWhatMentionsTheVerse_AlsoInRanges()
    {
        _v.SetNote(Joh316, "Mein Lieblingsvers");
        _v.SetNote(new VerseRef("ROM", 5, 8), "Siehe auch Joh 3,14-18");
        _v.AddDocument("Predigt", "Text: Joh 3,16", null, false, []);
        _v.AddDocument("Anderes", "Nur Joh 3,1", null, false, []);
        _v.AddPrayer("Herr, hilf mir bei Joh 3,16", null);
        _v.AddMyXref(new VerseRef("1JN", 4, 9), Joh316, 16, "");

        var hits = OwnSearch.About(_v, Joh316);
        Assert.Equal(1, hits.Count(h => h.Kind == OwnKind.Document && h.Title == "Predigt"));
        Assert.DoesNotContain(hits, h => h.Title == "Anderes");
        Assert.Equal(2, hits.Count(h => h.Kind == OwnKind.Note));      // on the verse, and a note elsewhere with a range
        Assert.Single(hits, h => h.Kind == OwnKind.Prayer);
        Assert.Single(hits, h => h.Kind == OwnKind.MyXref);            // the way back
    }

    [Fact]
    public void Notes_CanBeFiled_AndKeepTheirFolderWhenEdited()
    {
        var folder = _v.AddFolder("Daniel");
        _v.SetNote(Joh316, "erste Fassung");
        Assert.Null(Assert.Single(_v.GetNotes()).FolderId);

        _v.SetNoteFolder(Joh316, folder);
        _v.SetNote(Joh316, "zweite Fassung");                          // editing the text keeps the folder
        var note = Assert.Single(_v.GetNotes());
        Assert.Equal((folder, "zweite Fassung"), (note.FolderId, note.Text));

        _v.DeleteFolder(folder);                                       // the note stays, without folder
        Assert.Null(Assert.Single(_v.GetNotes()).FolderId);
    }

    [Fact]
    public void VerseRanges_KnowTheEnd()
    {
        var r = DocumentLinks.VerseRanges("Siehe Joh 3,14-18 und Röm 8,28.").ToList();
        Assert.Equal([(new VerseRef("JHN", 3, 14), 18), (new VerseRef("ROM", 8, 28), 28)], r);
    }

    [Fact]
    public void Excerpt_ShowsTheHit()
    {
        var text = string.Concat(Enumerable.Repeat("Anfang ", 30)) + "Liebe" + string.Concat(Enumerable.Repeat(" Ende", 30));
        var e = OwnSearch.Excerpt(text, "liebe");
        Assert.Contains("Liebe", e);
        Assert.True(e.Length < 110);
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }
}
