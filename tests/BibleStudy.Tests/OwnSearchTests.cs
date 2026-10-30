using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
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
    public void LastPosition_BelongsToItsVault()
    {
        Assert.Null(_v.GetLastPosition());   // a new vault has no "continue" place
        _v.SetLastPosition("JHN", 3);
        _v.SetLastPosition("ROM", 8);
        Assert.Equal(("ROM", 8), _v.GetLastPosition());
        var other = new VaultStore(Path.Combine(_dir, "other.db"), "O");
        Assert.Null(other.GetLastPosition());
    }

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
    public void HiddenXrefs_AreKept_InBothDirections_AndCanBeTakenBack()
    {
        var a = Joh316; var b = new VerseRef("ROM", 5, 8);
        _v.HideXref(a, b);
        _v.HideXref(b, a);                                  // the same pair the other way round: not stored twice
        var h = Assert.Single(_v.GetHiddenXrefs());
        Assert.Equal((a, b), (h.From, h.To));
        _v.UnhideXref(h.Id);
        Assert.Empty(_v.GetHiddenXrefs());
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

public class StepLexiconTests
{
    [Fact]
    public void Parse_ReadsGreekRowsWithMeaning_AndHebrewWithGlossOnly()
    {
        var greek = "Header text\nEStrong#\tx\n" +
            "G0026\tG0026 =\tG0026\tἀγάπη\tagapē\tG:N-F\tlove\t <b>ἀγάπη</b>, -ης, ἡ <BR /> <b>love, goodwill</b>. <ref='Rom.5.8'>Rom.5:8;</ref> <BR />__1. Of men\n" +
            "G0026\tG0026B\tG0026\tother\tx\tG:N\tsecond row\tignored\n" +
            "G2264G\tG2264G\tG2264G\tἩρῴδης\tHērōdēs\tN\tHerod\tHerod the Great\n";   // names with letters: not a plain Strong number
        var g = StepLexicon.Parse(greek, 'G');
        var e = g[26];
        Assert.Equal(("G26", "ἀγάπη", "agapē", "love"), (e.Id, e.Word, e.Translit, e.Gloss));
        Assert.Contains("love, goodwill", e.Meaning);
        Assert.Contains("Rom.5:8", e.Meaning);
        Assert.DoesNotContain("<", e.Meaning);
        Assert.Contains("\n", e.Meaning);
        Assert.False(g.ContainsKey(2264));

        var hebrew = "H0430\tH0430G = a Name of\tH3068G\tאֱלֹהִים\te.lo.him\tH:N-M\tGod\tThis name means \"gods\"<BR>Another name of ...\n";
        var h = StepLexicon.Parse(hebrew, 'H')[430];
        Assert.Equal("God", h.Gloss);
        Assert.Equal("", h.Meaning);   // the long Hebrew definitions need the permission of their owner and are not used
    }
}

public class BookmarkTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));
    public BookmarkTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Bookmarks_AreAPlainList_ThatTogglesAndListsNewestFirst()
    {
        var v = new VaultStore(Path.Combine(_dir, "v.db"), "T");
        var joh = new VerseRef("JHN", 3, 16); var rom = new VerseRef("ROM", 8, 28);
        Assert.True(v.ToggleBookmark(joh, "Denn also hat Gott …"));
        Assert.True(v.ToggleBookmark(rom, "Wir wissen aber …"));
        Assert.Equal([rom, joh], v.GetBookmarks().Select(b => b.Ref).ToArray());
        Assert.Equal([16], v.GetBookmarkedVerses("JHN", 3).ToArray());
        Assert.False(v.ToggleBookmark(joh, ""));                                   // a second press removes it again
        Assert.Single(v.GetBookmarks());
        Assert.Empty(v.GetMarks());                                                // a bookmark is not a highlight
    }

    [Fact]
    public void Wipe_EmptiesTheWholeVault()
    {
        var v = new VaultStore(Path.Combine(_dir, "w.db"), "W");
        var r = new VerseRef("JHN", 3, 16);
        v.SetNote(r, "Notiz"); v.ToggleBookmark(r, "x"); v.AddDocument("Doc", "Text", null, false, []); v.AddTodo("t", null); v.AddPrayer("p", null);
        v.AddMark("*", r, -1, -1, "x", "#FFD54F", "", null, []); v.SetRead("JHN", [3], true); v.AddFolder("F"); v.SetLastPosition("JHN", 3);
        v.Wipe();
        Assert.Empty(v.GetNotes()); Assert.Empty(v.GetBookmarks()); Assert.Empty(v.GetDocuments()); Assert.Empty(v.GetTodos()); Assert.Empty(v.GetPrayers());
        Assert.Empty(v.GetMarks()); Assert.Empty(v.GetReadChapters()); Assert.Empty(v.GetFolders()); Assert.Null(v.GetLastPosition());
    }
}
