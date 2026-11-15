using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

/// <summary>The reading progress in the device sync: what was read on either side arrives on both, "unread" travels too, an empty device removes nothing.</summary>
public class ReadSyncTests
{
    readonly string _dir;
    public ReadSyncTests() { _dir = Path.Combine(Path.GetTempPath(), "bs_readsync_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }

    ReadStore Store(string name) => new(Path.Combine(_dir, name + ".db"));

    /// <summary>The copy one device sends: its vault file with the shared list in it.</summary>
    string Copy(ReadStore s, string name)
    {
        var path = Path.Combine(_dir, name + "-" + Guid.NewGuid().ToString("N") + ".db");
        new VaultStore(path, "v").GetNotes();
        s.CopyInto(path);
        return path;
    }

    [Fact]
    public void Chapters_read_apart_on_both_devices_arrive_on_both()
    {
        var pc = Store("pc"); var phone = Store("phone");
        pc.Set("JHN", [1], true);
        var basePath = Copy(pc, "base");
        phone.MergeFrom(Copy(pc, "pc"), null);
        pc.Set("JHN", [2, 3], true);
        phone.Set("GEN", [1], true);

        pc.MergeFrom(Copy(phone, "phone"), basePath);
        phone.MergeFrom(Copy(pc, "pc"), basePath);

        HashSet<(string, int)> all = [("JHN", 1), ("JHN", 2), ("JHN", 3), ("GEN", 1)];
        Assert.True(pc.Chapters().SetEquals(all));
        Assert.True(phone.Chapters().SetEquals(all));
    }

    [Fact]
    public void A_chapter_marked_unread_stays_unread_on_the_other_device()
    {
        var pc = Store("pc"); var phone = Store("phone");
        pc.Set("JHN", [1, 2], true);
        phone.MergeFrom(Copy(pc, "pc"), null);
        var basePath = Copy(pc, "base");
        phone.Set("JHN", [2], false);

        pc.MergeFrom(Copy(phone, "phone"), basePath);

        Assert.True(pc.Chapters().SetEquals([("JHN", 1)]));
    }

    [Fact]
    public void An_empty_device_or_an_old_copy_removes_nothing()
    {
        var pc = Store("pc");
        pc.Set("JHN", [1, 2, 3], true);
        var basePath = Copy(pc, "base");

        pc.MergeFrom(Copy(Store("new"), "new"), basePath);   // a new phone that has read nothing
        Assert.Equal(3, pc.Chapters().Count);

        var old = Path.Combine(_dir, "old.db");   // a device before 1.9.7: an old per-vault list without the mark of a full copy
        using (var con = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + old + ";Pooling=False"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE read_chapters(book TEXT, chapter INT, read_at TEXT, PRIMARY KEY(book,chapter)); INSERT INTO read_chapters VALUES('GEN',5,NULL)";
            cmd.ExecuteNonQuery();
        }
        pc.MergeFrom(old, basePath);
        Assert.True(pc.Chapters().SetEquals([("JHN", 1), ("JHN", 2), ("JHN", 3), ("GEN", 5)]));
    }

    [Fact]
    public void Marking_a_chapter_read_changes_the_fingerprint()
    {
        var s = Store("pc");
        var before = s.Fingerprint();
        s.Set("JHN", [1], true);
        var read = s.Fingerprint();
        s.Set("JHN", [1], false);
        Assert.NotEqual(before, read);
        Assert.NotEqual(read, s.Fingerprint());
    }
}
