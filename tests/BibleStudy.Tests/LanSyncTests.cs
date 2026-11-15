using System.Net;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;
using BibleStudy.Core.Sync;
using Xunit;

namespace BibleStudy.Tests;

public class LanSyncTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_sync_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    sealed class Device : IDisposable
    {
        public string Root; public SyncConfig Cfg; public LanSync Sync; public FolderVaults Vaults;
        public Device(string root, string key, string name)
        {
            Root = root; Directory.CreateDirectory(root);
            Cfg = new SyncConfig { DeviceName = name, Key = key, Auto = false };
            Vaults = new FolderVaults(root, null, "Abgleich");
            Sync = new LanSync(Vaults, Cfg, Path.Combine(root, "sync.json"), Path.Combine(root, "tmp"));
            Sync.Start();
        }
        public VaultStore Vault(string name) => new(Path.Combine(Root, "vaults", name, "vault.db"), name);
        public SyncPeer AsPeer() => new(Cfg.DeviceId, Cfg.DeviceName, IPAddress.Loopback, Sync.Port, DateTime.UtcNow);
        public void Dispose() => Sync.Dispose();
    }

    [Fact]
    public void Code_RoundTrips_AndReadsWhatIsTyped()
    {
        var key = SyncCode.NewKey();
        Assert.Equal(26, key.Length);
        Assert.NotNull(SyncCode.Decode(key));
        Assert.Equal(key, SyncCode.Parse(SyncCode.Pretty(key).ToLowerInvariant()));
        Assert.Equal(key, SyncCode.Parse(SyncCode.ToUrl(key)));
        Assert.Null(SyncCode.Parse("zu kurz"));
        var url = SyncCode.ToUrl(key, ["192.168.1.5:47822", "10.0.0.2:47822"]);
        Assert.Equal(key, SyncCode.Parse(url));
        Assert.Equal(["192.168.1.5:47822", "10.0.0.2:47822"], SyncCode.ParseHosts(url));
    }

    [Fact]
    public async Task TwoDevices_ExchangeWhatEachWrote_AndStayStable()
    {
        var key = SyncCode.NewKey();
        using var a = new Device(Path.Combine(_dir, "a"), key, "PC");
        using var b = new Device(Path.Combine(_dir, "b"), key, "Handy");
        var va = a.Vault("Standard"); var vb = b.Vault("Standard");
        va.SetNote(new VerseRef("Joh", 3, 16), "Notiz vom PC");
        vb.SetNote(new VerseRef("Ps", 23, 1), "Notiz vom Handy");
        vb.AddDocument("Predigt", "Text vom Handy", null, false, []);
        b.Vault("Nur Handy").SetNote(new VerseRef("Mt", 5, 3), "eigener Vault");

        var r = await a.Sync.SyncWithAsync(b.AsPeer());
        Assert.Null(r.Error);
        foreach (var v in new[] { a, b })
        {
            var s = v.Vault("Standard");
            Assert.Equal(2, s.GetNotes().Count);
            Assert.Single(s.GetDocuments());
        }
        Assert.Single(a.Vault("Nur Handy").GetNotes());   // a whole vault comes over too

        // again with nothing new: nothing changes, nothing is doubled
        var again = await a.Sync.SyncWithAsync(b.AsPeer());
        Assert.Equal(0, again.Changed + again.Sent);
        Assert.Single(a.Vault("Standard").GetDocuments());

        // both change the same note: both texts stay, and a third round does not grow it further
        a.Vault("Standard").SetNote(new VerseRef("Joh", 3, 16), "Alt und PC");
        b.Vault("Standard").SetNote(new VerseRef("Joh", 3, 16), "Alt und Handy");
        await a.Sync.SyncWithAsync(b.AsPeer());
        var text = a.Vault("Standard").GetNotes().First(n => n.Ref.Book == "Joh").Text;
        Assert.Contains("PC", text); Assert.Contains("Handy", text);
        await b.Sync.SyncWithAsync(a.AsPeer());
        await a.Sync.SyncWithAsync(b.AsPeer());
        Assert.Equal(2, a.Vault("Standard").GetNotes().Count);
        Assert.Single(a.Vault("Standard").GetDocuments());
        Assert.Single(b.Vault("Standard").GetDocuments());
    }

    [Fact]
    public async Task Devices_FindEachOtherByThemselves_AndSync()
    {
        var key = SyncCode.NewKey();
        using var a = new Device(Path.Combine(_dir, "a"), key, "PC");
        a.Vault("Standard").SetNote(new VerseRef("Joh", 3, 16), "vom PC");
        using var b = new Device(Path.Combine(_dir, "b"), key, "Handy");
        b.Vault("Standard").SetNote(new VerseRef("Ps", 23, 1), "vom Handy");
        // only now: a device that starts announces itself, and the other would compare before the note above is written
        a.Cfg.Auto = true; b.Cfg.Auto = true;
        var done = new TaskCompletionSource();
        var reports = new System.Collections.Concurrent.ConcurrentQueue<string>();
        a.Sync.Finished += r => { reports.Enqueue("a:" + r.Error + ":" + r.Changed + "/" + r.Sent); done.TrySetResult(); };
        b.Sync.Finished += r => { reports.Enqueue("b:" + r.Error + ":" + r.Changed + "/" + r.Sent); done.TrySetResult(); };
        a.Sync.Announce(); b.Sync.Announce();
        await Task.WhenAny(done.Task, Task.Delay(20000));
        Assert.True(done.Task.IsCompleted, "no device found the other");
        // "finished" comes from the first side that is done; the other may still be taking in what it got
        for (var i = 0; i < 100 && (a.Vault("Standard").GetNotes().Count < 2 || b.Vault("Standard").GetNotes().Count < 2); i++) await Task.Delay(100);
        Assert.True(a.Vault("Standard").GetNotes().Count == 2, string.Join(" ", reports));
        Assert.Equal(2, a.Vault("Standard").GetNotes().Count);
        Assert.Equal(2, b.Vault("Standard").GetNotes().Count);
    }

    [Fact]
    public async Task ADeviceWithAnotherCode_GetsNothing()
    {
        using var a = new Device(Path.Combine(_dir, "a"), SyncCode.NewKey(), "PC");
        using var b = new Device(Path.Combine(_dir, "b"), SyncCode.NewKey(), "Fremd");
        a.Vault("Standard").SetNote(new VerseRef("Joh", 3, 16), "privat");
        var r = await b.Sync.SyncWithAsync(a.AsPeer());
        Assert.NotNull(r.Error);
        Assert.Empty(b.Vaults.Names());
    }
}
