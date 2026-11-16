using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

/// <summary>The comparison of two devices with the state after their last comparison: the newer side wins, deletions travel, an empty copy deletes nothing.</summary>
public class ThreeWayMergeTests
{
    readonly string _dir;
    public ThreeWayMergeTests() { _dir = Path.Combine(Path.GetTempPath(), "bs_3way_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
    static VerseRef R(int v) => new("JHN", 1, v);

    /// <summary>PC and phone after a comparison: both hold the same, and the base is that state.</summary>
    (VaultStore Pc, string PhonePath, string BasePath) Synced(Action<VaultStore> fill)
    {
        var pc = new VaultStore(Path.Combine(_dir, "pc.db"), "pc");
        fill(pc);
        var basePath = Path.Combine(_dir, "base.db"); pc.BackupTo(basePath);
        var phonePath = Path.Combine(_dir, "phone.db"); pc.BackupTo(phonePath);
        return (pc, phonePath, basePath);
    }

    string CopyOf(string path) { var c = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db"); File.Copy(path, c); return c; }

    [Fact]
    public void A_change_on_one_side_replaces_the_old_text_instead_of_doubling_it()
    {
        var (pc, phonePath, basePath) = Synced(s => { s.SetNote(R(1), "alt"); s.AddDocument("Predigt", "Entwurf eins", null, false, []); });
        new VaultStore(phonePath, "phone").SetNote(R(1), "neu vom Handy");
        pc.UpdateDocument(pc.GetDocuments().Single().Id, "Predigt", "Entwurf zwei", null, false, []);   // the PC changed something else

        var r = pc.MergeFrom(CopyOf(phonePath), "Handy", basePath);

        Assert.Equal("neu vom Handy", pc.GetNotes().Single().Text);
        Assert.Equal("Entwurf zwei", pc.GetDocuments().Single().Body);   // the old draft of the phone did not come back into it
        Assert.Single(pc.GetDocuments());
        Assert.Equal(0, r.Merge.Conflicts);

        // the phone, the other way round: it gets the new draft and keeps its note
        var phone = new VaultStore(phonePath, "phone");
        phone.MergeFrom(CopyOf(Path.Combine(_dir, "pc.db")), "PC", basePath);
        Assert.Equal("Entwurf zwei", phone.GetDocuments().Single().Body);
        Assert.Equal("neu vom Handy", phone.GetNotes().Single().Text);
    }

    [Fact]
    public void A_deletion_travels_and_does_not_come_back()
    {
        var (pc, phonePath, basePath) = Synced(s => { s.SetNote(R(1), "eins"); s.SetNote(R(2), "zwei"); s.AddMark("t", R(3), 0, 4, "Wort", "#ffcc00", "", null, []); });
        var phone = new VaultStore(phonePath, "phone");
        phone.SetNote(R(1), "");
        phone.DeleteMark(phone.GetMarks().Single().Id);

        var r = pc.MergeFrom(CopyOf(phonePath), "Handy", basePath);
        Assert.Equal(2, r.Deleted);
        Assert.Equal([2], pc.GetNotes().Select(n => n.Ref.Verse));
        Assert.Empty(pc.GetMarks());

        // the phone gets nothing back of what it deleted
        phone.MergeFrom(CopyOf(Path.Combine(_dir, "pc.db")), "PC", basePath);
        Assert.Equal([2], phone.GetNotes().Select(n => n.Ref.Verse));
        Assert.Empty(phone.GetMarks());
    }

    [Fact]
    public void An_empty_copy_never_empties_the_other()
    {
        var (pc, _, basePath) = Synced(s => { for (var v = 1; v <= 12; v++) s.SetNote(R(v), "Notiz " + v); });
        var empty = Path.Combine(_dir, "reinstalled.db"); new VaultStore(empty, "phone").GetNotes();   // a phone that lost its data but still has the old base

        var r = pc.MergeFrom(empty, "Handy", basePath);

        Assert.Equal(12, pc.GetNotes().Count);
        Assert.Equal(12, r.Held);
        Assert.Equal(0, r.Deleted);
    }

    [Fact]
    public void Without_a_base_nothing_is_deleted_or_replaced()
    {
        var pc = new VaultStore(Path.Combine(_dir, "pc.db"), "pc");
        pc.SetNote(R(1), "PC");
        var phonePath = Path.Combine(_dir, "phone.db");
        new VaultStore(phonePath, "phone").SetNote(R(2), "Handy");

        pc.MergeFrom(phonePath, "Handy", null);

        Assert.Equal(2, pc.GetNotes().Count);
    }

    [Fact]
    public void New_marks_made_apart_on_both_devices_all_arrive_on_both()
    {
        // synced once, then the phone stayed closed for days: both got their own new marks
        var (pc, phonePath, basePath) = Synced(s => s.AddMark("t", R(1), 0, 4, "Wort", "#ffcc00", "", null, []));
        pc.AddMark("t", R(2), 0, 4, "PC-Wort", "#ff0000", "", null, []);
        pc.AddMark("t", R(3), 0, 4, "PC-Wort2", "#ff0000", "", null, []);
        var phone = new VaultStore(phonePath, "phone");
        phone.AddMark("t", R(4), 0, 4, "Handy-Wort", "#00ff00", "", null, []);
        phone.AddMark("t", R(2), 5, 9, "Handy-Wort2", "#00ff00", "", null, []);

        pc.MergeFrom(CopyOf(phonePath), "Handy", basePath);
        phone.MergeFrom(CopyOf(Path.Combine(_dir, "pc.db")), "PC", basePath);

        string[] all = ["Handy-Wort", "Handy-Wort2", "PC-Wort", "PC-Wort2", "Wort"];
        Assert.Equal(all, pc.GetMarks().Select(m => m.Text).Order());
        Assert.Equal(all, phone.GetMarks().Select(m => m.Text).Order());
    }

    [Fact]
    public void Todos_bookmarks_and_prayers_change_and_disappear_on_both_devices()
    {
        var (pc, phonePath, basePath) = Synced(s =>
        {
            s.AddTodo("Röm 8 lesen", null); s.AddTodo("Predigt schreiben", null); s.AddTodo("Lieder aussuchen", null);
            s.ToggleBookmark(R(1), "Anfang"); s.ToggleBookmark(R(2), "Wort");
            s.AddPrayer("Für die Gemeinde", null);
        });
        var phone = new VaultStore(phonePath, "phone");
        var todos = phone.GetTodos().ToDictionary(t => t.Text, t => t.Id);
        phone.SetTodoDone(todos["Röm 8 lesen"], true);            // ticked off on the phone
        phone.DeleteTodo(todos["Lieder aussuchen"]);               // deleted on the phone
        phone.SetTodoText(todos["Predigt schreiben"], "Predigt zu Ende schreiben");
        phone.AddTodo("Neu vom Handy", null);
        phone.RemoveBookmark(phone.GetBookmarks().Single(b => b.Ref.Verse == 2).Id);
        phone.SetPrayerAnswered(phone.GetPrayers().Single().Id, true, "erhört");
        pc.AddTodo("Neu vom PC", null);

        pc.MergeFrom(CopyOf(phonePath), "Handy", basePath);

        var pcTodos = pc.GetTodos().ToDictionary(t => t.Text, t => t.Done);
        Assert.Equal(["Neu vom Handy", "Neu vom PC", "Predigt zu Ende schreiben", "Röm 8 lesen"], pcTodos.Keys.Order());
        Assert.True(pcTodos["Röm 8 lesen"]);
        Assert.Equal([1], pc.GetBookmarks().Select(b => b.Ref.Verse));
        Assert.Equal("erhört", pc.GetPrayers().Single().Note);
        Assert.NotNull(pc.GetPrayers().Single().Answered);

        // the other way round, after the PC unticked it again: the phone follows and keeps nothing old
        var basePc = CopyOf(Path.Combine(_dir, "pc.db"));
        phone.MergeFrom(CopyOf(Path.Combine(_dir, "pc.db")), "PC", basePath);
        pc.SetTodoDone(pc.GetTodos().Single(t => t.Text == "Röm 8 lesen").Id, false);
        phone.MergeFrom(CopyOf(Path.Combine(_dir, "pc.db")), "PC", basePc);
        Assert.False(phone.GetTodos().Single(t => t.Text == "Röm 8 lesen").Done);
        Assert.Equal(4, phone.GetTodos().Count);
    }

    [Fact]
    public void Changes_on_both_sides_are_both_kept()
    {
        var (pc, phonePath, basePath) = Synced(s => s.SetNote(R(1), "Anfang"));
        pc.SetNote(R(1), "PC schrieb das");
        new VaultStore(phonePath, "phone").SetNote(R(1), "Handy schrieb dies");

        var r = pc.MergeFrom(CopyOf(phonePath), "Handy", basePath);

        var text = pc.GetNotes().Single().Text;
        Assert.Contains("PC schrieb das", text); Assert.Contains("Handy schrieb dies", text);
        Assert.Equal(1, r.Merge.Conflicts);
    }
}
