using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class TrashTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));
    string DbPath => Path.Combine(_dir, "v.db");

    VaultStore New() { Directory.CreateDirectory(_dir); return new VaultStore(DbPath, "t"); }

    [Fact]
    public void A_mark_comes_back_with_its_id_tags_and_folder_also_after_reopening()
    {
        var s = New();
        var f = s.AddFolder("Gnade");
        var id = s.AddMark("eng_kjv", new VerseRef("ROM", 8, 28), 0, 5, "And we", "#FFD54F", "note", f, ["a", "b"], endVerse: 30);
        var t = s.TrashMark(id);
        Assert.Empty(s.GetMarks());
        var item = Assert.Single(s.GetTrash());
        Assert.Equal(("mark", "note", "ROM 8:28–30"), (item.Kind, item.Title, item.Place));

        s = new VaultStore(DbPath, "t");   // the trash survives a restart
        Assert.True(s.RestoreFromTrash(t));
        var m = Assert.Single(s.GetMarks());
        Assert.Equal((id, f, 30), (m.Id, m.FolderId, m.LastVerse));
        Assert.Equal(["a", "b"], m.Tags);
        Assert.Empty(s.GetTrash());
    }

    [Fact]
    public void A_folder_comes_back_with_what_was_in_it_but_leaves_what_was_moved_since()
    {
        var s = New();
        var top = s.AddFolder("Top");
        var f = s.AddFolder("F"); s.SetFolderParent(f, top);
        var kid = s.AddFolder("Kid"); s.SetFolderParent(kid, f);
        var m1 = s.AddMark("*", new VerseRef("GEN", 1, 1), -1, -1, "", "#111111", "", f, []);
        var m2 = s.AddMark("*", new VerseRef("GEN", 1, 2), -1, -1, "", "#111111", "", f, []);
        s.SetNote(new VerseRef("GEN", 1, 3), "n"); s.SetNoteFolder(new VerseRef("GEN", 1, 3), f);
        var d = s.AddDocument("Doc", "body", null, false, [], f);

        var t = s.TrashFolder(f);
        Assert.Equal(top, s.GetFolders().Single(x => x.Id == kid).ParentId);   // the sub-folder moved up
        var other = s.AddFolder("Other");
        s.SetMarkFolder(m2, other);                                            // moved by hand meanwhile

        Assert.True(s.RestoreFromTrash(t));
        var back = s.GetFolders().Single(x => x.Name == "F");
        Assert.Equal((f, (long?)top), (back.Id, back.ParentId));
        Assert.Equal(f, s.GetFolders().Single(x => x.Id == kid).ParentId);
        Assert.Equal(f, s.GetMarks().Single(m => m.Id == m1).FolderId);
        Assert.Equal(other, s.GetMarks().Single(m => m.Id == m2).FolderId);
        Assert.Equal(f, s.GetNotes().Single().FolderId);
        Assert.Equal(f, s.GetDocuments().Single(x => x.Id == d).FolderId);
    }

    [Fact]
    public void A_note_does_not_overwrite_a_newer_one_and_a_document_keeps_its_id()
    {
        var s = New();
        var r = new VerseRef("JHN", 3, 16);
        s.SetNote(r, "old");
        var t = s.TrashNote(r);
        s.SetNote(r, "new");
        s.RestoreFromTrash(t);
        Assert.Equal("new\n\nold", s.GetNotes().Single().Text);

        var d = s.AddDocument("Predigt", "text", null, false, ["Predigt"]);
        var td = s.TrashDocument(d);
        Assert.Empty(s.GetDocuments());
        s.RestoreFromTrash(td);
        var doc = Assert.Single(s.GetDocuments());
        Assert.Equal((d, "Predigt", "text"), (doc.Id, doc.Title, doc.Body));
        Assert.Equal(["Predigt"], doc.Tags);
    }

    [Fact]
    public void Old_entries_are_thrown_away_and_tags_can_be_given_to_many_marks()
    {
        var s = New();
        var a = s.AddMark("*", new VerseRef("GEN", 1, 1), -1, -1, "", "#111111", "", null, ["x"]);
        var b = s.AddMark("*", new VerseRef("GEN", 1, 2), -1, -1, "", "#111111", "", null, []);
        s.TagMarks([a, b], "Gnade", add: true);
        Assert.All(s.GetMarks(), m => Assert.Contains("Gnade", m.Tags));
        Assert.Contains("x", s.GetMarks().Single(m => m.Id == a).Tags);
        s.TagMarks([a], "Gnade", add: false);
        Assert.DoesNotContain("Gnade", s.GetMarks().Single(m => m.Id == a).Tags);

        s.TrashMark(a);
        Assert.Equal(0, s.PurgeTrash(TimeSpan.FromDays(30)));
        Assert.Equal(1, s.PurgeTrash(TimeSpan.Zero));
        Assert.Empty(s.GetTrash());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
