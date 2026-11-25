using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

public class VersionTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    public VersionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { /* a test runner still holds it */ }
    }

    /// <summary>Makes the versions look older than the gap, as if the next change came later.</summary>
    static void Age(string path)
    {
        using var con = new SqliteConnection($"Data Source={path}");
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE versions SET saved = '2000-01-01T00:00:00.0000000Z'";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Saving_a_document_keeps_the_text_it_had_once_per_writing_session()
    {
        var path = Path.Combine(_dir, "v.db");
        var s = new VaultStore(path, "t");
        var id = s.AddDocument("Gnade", "eins", null, false, []);
        Assert.Empty(s.GetDocumentVersions(id));

        s.UpdateDocument(id, "Gnade", "eins zwei", null, false, []);
        s.UpdateDocument(id, "Gnade", "eins zwei drei", null, false, []);   // autosave a few seconds later: no second version
        var v = Assert.Single(s.GetDocumentVersions(id));
        Assert.Equal("eins", v.Body);
        Assert.Equal("Gnade", v.Title);

        Age(path);
        s.UpdateDocument(id, "Gnade (neu)", "vier", null, false, []);
        Assert.Equal(["eins zwei drei", "eins"], s.GetDocumentVersions(id).Select(x => x.Body));
    }

    [Fact]
    public void Nothing_is_kept_when_nothing_changed()
    {
        var s = new VaultStore(Path.Combine(_dir, "same.db"), "t");
        var id = s.AddDocument("A", "Text", null, false, []);
        s.UpdateDocument(id, "A", "Text", null, false, ["tag"]);
        Assert.Empty(s.GetDocumentVersions(id));
    }

    [Fact]
    public void Going_back_keeps_the_current_text_so_it_can_be_undone()
    {
        var path = Path.Combine(_dir, "restore.db");
        var s = new VaultStore(path, "t");
        var id = s.AddDocument("Titel", "alt", null, false, ["x"]);
        s.UpdateDocument(id, "Titel", "neu", null, false, ["x"]);
        var old = Assert.Single(s.GetDocumentVersions(id));

        Assert.True(s.RestoreDocumentVersion(id, old.Id));
        var d = s.GetDocuments().Single(x => x.Id == id);
        Assert.Equal("alt", d.Body);
        Assert.Equal(["x"], d.Tags);
        Assert.Contains(s.GetDocumentVersions(id), x => x.Body == "neu");   // within the gap too
    }

    [Fact]
    public void Notes_have_versions_too()
    {
        var path = Path.Combine(_dir, "notes.db");
        var s = new VaultStore(path, "t");
        var r = new VerseRef("JHN", 3, 16);
        s.SetNote(r, "erste Fassung");
        s.SetNote(r, "zweite Fassung");
        Assert.Equal("erste Fassung", Assert.Single(s.GetNoteVersions(r)).Body);

        Age(path);
        s.SetNote(r, "");   // deleted: the last text is kept
        Assert.Equal("zweite Fassung", s.GetNoteVersions(r)[0].Body);

        Assert.True(s.RestoreNoteVersion(r, s.GetNoteVersions(r)[1].Id));
        Assert.Equal("erste Fassung", s.GetNote(r)!.Text);
    }

    [Fact]
    public void Only_the_newest_versions_stay()
    {
        var path = Path.Combine(_dir, "many.db");
        var s = new VaultStore(path, "t");
        var id = s.AddDocument("A", "0", null, false, []);
        for (var i = 1; i <= VaultStore.VersionsKept + 5; i++)
        {
            Age(path);
            s.UpdateDocument(id, "A", i.ToString(), null, false, []);
        }
        var versions = s.GetDocumentVersions(id);
        Assert.Equal(VaultStore.VersionsKept, versions.Count);
        Assert.Equal((VaultStore.VersionsKept + 4).ToString(), versions[0].Body);
    }

    [Fact]
    public void A_document_from_the_trash_comes_back_with_its_versions()
    {
        var path = Path.Combine(_dir, "trash.db");
        var s = new VaultStore(path, "t");
        var id = s.AddDocument("A", "alt", null, false, []);
        s.UpdateDocument(id, "A", "neu", null, false, []);
        var trash = s.TrashDocument(id);

        s = new VaultStore(path, "t");   // opening the vault tidies up: the document still waits in the trash
        Assert.Single(s.GetDocumentVersions(id));
        Assert.True(s.RestoreFromTrash(trash));
        Assert.Single(s.GetDocumentVersions(id));

        s.TrashDocument(id);
        s.EmptyTrash();
        s = new VaultStore(path, "t");
        Assert.Empty(s.GetDocumentVersions(id));
    }

    [Fact]
    public void Line_changes_count_new_and_gone_lines()
    {
        Assert.Equal((1, 1), VaultStore.LineChanges("a\nb\nc", "a\nB\nc"));
        Assert.Equal((2, 0), VaultStore.LineChanges("a", "a\nb\n\nc"));
        Assert.Equal((0, 0), VaultStore.LineChanges("a\r\nb", "a\nb"));
    }
}
