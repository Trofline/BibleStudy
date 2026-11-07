using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class ReadStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Read_IsTheSameInEveryVault_AndGuestKeepsItsOwn()
    {
        var shared = new ReadStore(Path.Combine(_dir, "read.db"));
        var a = new VaultStore(Path.Combine(_dir, "a", "vault.db"), "a") { SharedRead = shared };
        var b = new VaultStore(Path.Combine(_dir, "b", "vault.db"), "b") { SharedRead = shared };
        var guest = new VaultStore(Path.Combine(_dir, "g", "vault.db"), "g");
        a.SetRead("JHN", [1, 2], true);
        Assert.Equal(2, b.GetReadChapters().Count);
        b.SetRead("JHN", [2], false);
        Assert.Single(a.GetReadChapters());
        Assert.Empty(guest.GetReadChapters());   // a guest vault neither sees nor changes the list of this computer
        guest.SetRead("GEN", [1], true);
        Assert.Single(a.GetReadChapters());
        Assert.Single(guest.GetReadChapters());
    }

    [Fact]
    public void FirstStart_TakesOverOldVaultListsAndExportCarriesTheListAlong()
    {
        var old = new VaultStore(Path.Combine(_dir, "old", "vault.db"), "old");
        old.SetRead("PSA", [23], true);
        var shared = new ReadStore(Path.Combine(_dir, "read.db"));
        Assert.True(shared.IsNew);
        shared.MergeFrom(Path.Combine(_dir, "old", "vault.db"));
        Assert.Contains(("PSA", 23), shared.Chapters());
        shared.Set("ROM", [8], true);
        var export = Path.Combine(_dir, "export.db");
        new VaultStore(Path.Combine(_dir, "x", "vault.db"), "x").BackupTo(export);
        shared.CopyInto(export);
        Assert.Equal(2, new VaultStore(export, "e").GetReadChapters().Count);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }
}
