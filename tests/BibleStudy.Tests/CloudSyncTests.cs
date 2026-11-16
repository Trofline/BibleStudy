using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;
using BibleStudy.Core.Sync;
using Xunit;

namespace BibleStudy.Tests;

/// <summary>The sync through a cloud folder: two devices that never see each other, only the shared folder, at different times.</summary>
public class CloudSyncTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_cloud_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    sealed class Device
    {
        public string Root; public LanSync Sync;
        public Device(string root, string name, ICloudFolder cloud)
        {
            Root = root; Directory.CreateDirectory(root);
            var cfg = new SyncConfig { DeviceName = name, Key = SyncCode.NewKey(), Auto = false };
            Sync = new LanSync(new FolderVaults(root, null, "Abgleich"), cfg, Path.Combine(root, "sync.json"), Path.Combine(root, "tmp")) { Cloud = cloud };
        }
        public VaultStore Vault(string name = "Standard") => new(Path.Combine(Root, "vaults", name, "vault.db"), name);
    }

    static VerseRef R(int v) => new("JHN", 1, v);

    [Fact]
    public async Task Changes_travel_through_the_folder_without_both_being_on()
    {
        var cloud = new PathCloudFolder(Path.Combine(_dir, "GoogleDrive"));
        var pc = new Device(Path.Combine(_dir, "pc"), "PC", cloud);
        var phone = new Device(Path.Combine(_dir, "phone"), "iPhone", cloud);

        pc.Vault().SetNote(R(1), "vom PC");
        Assert.Null((await pc.Sync.CloudRoundAsync())!.Error);        // the PC puts its vault there and is switched off
        var r = await phone.Sync.CloudRoundAsync();                     // later the phone opens
        Assert.Null(r!.Error);
        Assert.Equal("PC", r.PeerName);
        Assert.Equal("vom PC", phone.Vault().GetNotes().Single().Text);
        await pc.Sync.CloudRoundAsync();   // the PC has seen the phone's copy once: from now on deletions travel
        // (before that a deletion cannot be told from something new on the other side: it stays, nothing is lost)

        // both work apart: the phone adds, the PC deletes the old note and adds another
        phone.Vault().SetNote(R(2), "vom Handy");
        await phone.Sync.CloudRoundAsync();
        pc.Vault().SetNote(R(1), "");
        pc.Vault().SetNote(R(3), "noch vom PC");
        await pc.Sync.CloudRoundAsync();
        await phone.Sync.CloudRoundAsync();
        await pc.Sync.CloudRoundAsync();

        foreach (var d in new[] { pc, phone })
            Assert.Equal([2, 3], d.Vault().GetNotes().Select(n => n.Ref.Verse).Order());
    }

    [Fact]
    public async Task What_was_added_here_meanwhile_stays_when_an_older_copy_of_the_other_comes()
    {
        var cloud = new PathCloudFolder(Path.Combine(_dir, "OneDrive"));
        var pc = new Device(Path.Combine(_dir, "pc"), "PC", cloud);
        var phone = new Device(Path.Combine(_dir, "phone"), "iPhone", cloud);
        pc.Vault().SetNote(R(1), "eins");
        await pc.Sync.CloudRoundAsync();
        await phone.Sync.CloudRoundAsync();

        // the PC adds something and puts it there; the phone changes something else before it has seen that
        pc.Vault().SetNote(R(5), "neu am PC");
        await pc.Sync.CloudRoundAsync();
        phone.Vault().SetNote(R(1), "eins, am Handy geändert");
        // the phone takes the PC's copy, then the PC takes the phone's (which does not have the new note yet)
        await phone.Sync.CloudRoundAsync();
        await pc.Sync.CloudRoundAsync();

        var notes = pc.Vault().GetNotes().ToDictionary(n => n.Ref.Verse, n => n.Text);
        Assert.Equal("neu am PC", notes[5]);
        Assert.Equal("eins, am Handy geändert", notes[1]);
    }

    [Fact]
    public async Task Nothing_is_sent_again_when_nothing_changed()
    {
        var cloud = new PathCloudFolder(Path.Combine(_dir, "Dropbox"));
        var pc = new Device(Path.Combine(_dir, "pc"), "PC", cloud);
        pc.Vault().SetNote(R(1), "eins");
        Assert.Equal(1, (await pc.Sync.CloudRoundAsync())!.Sent);
        Assert.Equal(0, (await pc.Sync.CloudRoundAsync())!.Sent);
    }
}
