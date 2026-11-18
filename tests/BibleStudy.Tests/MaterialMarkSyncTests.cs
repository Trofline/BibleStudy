using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

/// <summary>Marks in the materials go between PC and phone: the file lies elsewhere on each device, deletions travel, the later change counts, nothing is lost.</summary>
public class MaterialMarkSyncTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_matsync_" + Guid.NewGuid().ToString("N"));
    public MaterialMarkSyncTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    const string PcFile = @"C:\Bibelkreis\Römer 8.pdf", PhoneFile = "/data/user/0/app/files/materials/Römer 8.pdf";

    VaultStore Vault(string name) => new(Path.Combine(_dir, name + ".db"), name);
    string Copy(VaultStore s) { var p = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db"); s.BackupTo(p); return p; }
    string Base(VaultStore s) => Copy(s);

    /// <summary>One round: each side takes the other's copy (with the state of the last round as base), as the device sync does.</summary>
    static void Sync(VaultStore pc, VaultStore phone, Func<VaultStore, string> copy, string? basePath)
    {
        var fromPhone = copy(phone); var fromPc = copy(pc);
        pc.MergeFrom(fromPhone, "Handy", basePath);
        phone.MergeFrom(fromPc, "PC", basePath);
    }

    [Fact]
    public void A_mark_made_on_the_PC_shows_on_the_phone_in_the_same_file()
    {
        var pc = Vault("pc"); var phone = Vault("phone");
        pc.AddMaterialMark(PcFile, 3, 10, "Gnade", "#FFD54F", "wichtig");
        Sync(pc, phone, Copy, null);
        var there = Assert.Single(phone.GetMaterialMarks(PhoneFile));
        Assert.Equal(("Gnade", 3, 10, "#FFD54F", "wichtig"), (there.Text, there.Block, there.Start, there.Color, there.Note));
        Sync(pc, phone, Copy, null);
        Assert.Single(pc.GetAllMaterialMarks());   // no second copy when it comes back
        Assert.Single(phone.GetAllMaterialMarks());
    }

    [Fact]
    public void A_file_with_marks_of_its_own_also_shows_those_made_on_the_other_device()
    {
        var pcFile = Path.Combine(_dir, "Römer 8.pdf"); File.WriteAllText(pcFile, "x");
        var pc = Vault("pc"); var phone = Vault("phone");
        pc.AddMaterialMark(pcFile, 1, 0, "vom PC", "#FFD54F");
        phone.AddMaterialMark(PhoneFile, 2, 0, "vom Handy", "#81C784");
        Sync(pc, phone, Copy, null);
        Assert.Equal(["vom PC", "vom Handy"], pc.GetMaterialMarks(pcFile).Select(m => m.Text));
    }

    [Fact]
    public void A_deletion_travels_and_the_mark_does_not_come_back()
    {
        var pc = Vault("pc"); var phone = Vault("phone");
        pc.AddMaterialMark(PcFile, 1, 0, "Glaube", "#81C784");
        Sync(pc, phone, Copy, null);
        var basePath = Base(pc);
        phone.DeleteMaterialMark(phone.GetAllMaterialMarks().Single().Id);
        Sync(pc, phone, Copy, basePath);
        Assert.Empty(pc.GetAllMaterialMarks());
        Assert.Empty(phone.GetAllMaterialMarks());
        Sync(pc, phone, Copy, Base(pc));
        Assert.Empty(pc.GetAllMaterialMarks());
    }

    [Fact]
    public void The_later_change_counts()
    {
        var pc = Vault("pc"); var phone = Vault("phone");
        pc.AddMaterialMark(PcFile, 1, 0, "Hoffnung", "#81C784");
        Sync(pc, phone, Copy, null);
        pc.UpdateMaterialMark(pc.GetAllMaterialMarks().Single().Id, "#E57373", "vom PC");
        Thread.Sleep(20);
        phone.UpdateMaterialMark(phone.GetAllMaterialMarks().Single().Id, "#64B5F6", "vom Handy, später");
        Sync(pc, phone, Copy, Base(pc));
        foreach (var s in new[] { pc, phone })
        {
            var m = Assert.Single(s.GetAllMaterialMarks());
            Assert.Equal(("#64B5F6", "vom Handy, später"), (m.Color, m.Note));
        }
    }

    [Fact]
    public void Marks_the_phone_never_had_are_not_taken_for_deleted_by_an_old_base()
    {
        // the PC had marks before this version: the base of the last sync holds them, the phone never got them
        var pc = Vault("pc"); var phone = Vault("phone");
        pc.AddMaterialMark(PcFile, 1, 0, "eins", "#FFD54F");
        pc.AddMaterialMark(PcFile, 2, 5, "zwei", "#FFD54F");
        var oldBase = Base(pc);
        Sync(pc, phone, Copy, oldBase);
        Assert.Equal(2, pc.GetAllMaterialMarks().Count);
        Assert.Equal(2, phone.GetAllMaterialMarks().Count);
    }

    [Fact]
    public void A_file_found_at_another_path_does_not_bring_back_a_mark_deleted_elsewhere()
    {
        var pc = Vault("pc"); var phone = Vault("phone");
        pc.AddMaterialMark(PcFile, 4, 2, "Liebe", "#FFD54F");
        Sync(pc, phone, Copy, null);
        Thread.Sleep(20);
        pc.DeleteMaterialMark(pc.GetAllMaterialMarks().Single().Id);
        Thread.Sleep(20);
        var m = phone.GetAllMaterialMarks().Single();
        phone.MoveMaterialMark(m.Id, PhoneFile, m.Block, m.Start);   // the phone opens the file: the mark moves to the path there
        Sync(pc, phone, Copy, Base(pc));
        Assert.Empty(pc.GetAllMaterialMarks());
        Assert.Empty(phone.GetAllMaterialMarks());
    }

    [Fact]
    public void Marked_again_after_a_deletion_it_stays()
    {
        var pc = Vault("pc"); var phone = Vault("phone");
        var id = pc.AddMaterialMark(PcFile, 1, 0, "Friede", "#FFD54F");
        Sync(pc, phone, Copy, null);
        Thread.Sleep(20);
        pc.DeleteMaterialMark(id);
        Thread.Sleep(20);
        pc.AddMaterialMark(PcFile, 1, 0, "Friede", "#81C784");
        Sync(pc, phone, Copy, Base(pc));
        Assert.Equal("#81C784", Assert.Single(phone.GetAllMaterialMarks()).Color);
        Assert.Equal("#81C784", Assert.Single(pc.GetAllMaterialMarks()).Color);
    }

    [Fact]
    public void A_mark_in_a_file_counts_as_something_new_to_synchronise()
    {
        var pc = Vault("pc");
        var before = pc.Fingerprint();
        var id = pc.AddMaterialMark(PcFile, 1, 0, "Wort", "#FFD54F");
        var marked = pc.Fingerprint();
        Assert.NotEqual(before, marked);
        pc.MoveMaterialMark(id, @"D:\anderswo\Römer 8.pdf", 1, 0);   // only found at another path: nothing new
        Assert.Equal(marked, pc.Fingerprint());
    }
}
