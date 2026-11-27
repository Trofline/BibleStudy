using System.Net;
using BibleStudy.Core.Import;
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
    public async Task AfterTheFirstRound_ChangesReplaceAndDeletionsTravel()
    {
        var key = SyncCode.NewKey();
        using var a = new Device(Path.Combine(_dir, "a"), key, "PC");
        using var b = new Device(Path.Combine(_dir, "b"), key, "Handy");
        a.Vault("Standard").SetNote(new VerseRef("Joh", 3, 16), "erste Fassung");
        a.Vault("Standard").SetNote(new VerseRef("Ps", 23, 1), "bleibt nicht");
        Assert.Null((await a.Sync.SyncWithAsync(b.AsPeer())).Error);

        b.Vault("Standard").SetNote(new VerseRef("Joh", 3, 16), "zweite Fassung vom Handy");   // changed on the phone
        a.Vault("Standard").SetNote(new VerseRef("Ps", 23, 1), "");                            // deleted on the PC
        Assert.Null((await b.Sync.SyncWithAsync(a.AsPeer())).Error);

        foreach (var d in new[] { a, b })
        {
            var notes = d.Vault("Standard").GetNotes();
            Assert.Equal("zweite Fassung vom Handy", Assert.Single(notes).Text);
        }
    }

    [Fact]
    public async Task A_deletion_does_not_come_back_after_both_got_the_entry_through_a_third_device()
    {
        var key = SyncCode.NewKey();
        using var a = new Device(Path.Combine(_dir, "a"), key, "PC");
        using var b = new Device(Path.Combine(_dir, "b"), key, "Handy");
        using var c = new Device(Path.Combine(_dir, "c"), key, "Tablet");
        a.Vault("Standard").SetNote(new VerseRef("Joh", 3, 16), "vom PC");
        Assert.Null((await a.Sync.SyncWithAsync(b.AsPeer())).Error);

        c.Vault("Standard").SetNote(new VerseRef("Ps", 23, 1), "vom Tablet");
        Assert.Null((await c.Sync.SyncWithAsync(a.AsPeer())).Error);
        Assert.Null((await c.Sync.SyncWithAsync(b.AsPeer())).Error);
        Assert.Null((await a.Sync.SyncWithAsync(b.AsPeer())).Error);   // the same on both: nothing to exchange

        a.Vault("Standard").SetNote(new VerseRef("Ps", 23, 1), "");   // deleted on the PC
        Assert.Null((await a.Sync.SyncWithAsync(b.AsPeer())).Error);

        foreach (var d in new[] { a, b }) Assert.Equal("vom PC", Assert.Single(d.Vault("Standard").GetNotes()).Text);
    }

    [Fact]
    public async Task AChange_ReachesTheOtherDevice_WithinSeconds_WithoutAsking()
    {
        var key = SyncCode.NewKey();
        using var a = new Device(Path.Combine(_dir, "a"), key, "PC");
        using var b = new Device(Path.Combine(_dir, "b"), key, "Handy");
        a.Vault("Standard").SetNote(new VerseRef("Joh", 3, 16), "erste");
        Assert.Null((await a.Sync.SyncWithAsync(b.AsPeer())).Error);   // paired once: a knows where b is
        a.Cfg.Auto = true;
        await Task.Delay(2500);   // the watch has looked at the vaults once

        a.Vault("Standard").SetNote(new VerseRef("Joh", 3, 16), "geändert am PC");
        for (var i = 0; i < 120 && b.Vault("Standard").GetNotes().Single().Text != "geändert am PC"; i++) await Task.Delay(100);
        Assert.Equal("geändert am PC", b.Vault("Standard").GetNotes().Single().Text);
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
    public async Task After_a_sync_only_new_content_counts_as_something_to_send()
    {
        // the computer asks the phone (as with an iPhone): afterwards the phone has all of it
        var key = SyncCode.NewKey();
        using var pc = new Device(Path.Combine(_dir, "pc"), key, "PC");
        using var phone = new Device(Path.Combine(_dir, "phone"), key, "Handy");
        pc.Vault("Standard").SetNote(new VerseRef("JHN", 3, 16), "vom PC");
        Assert.False(pc.Sync.IsOnADevice("Standard", pc.Vaults.Fingerprint("Standard")));   // never compared: new
        Assert.Null((await pc.Sync.SyncWithAsync(phone.AsPeer())).Error);
        bool Synced() => pc.Sync.IsOnADevice("Standard", pc.Vaults.Fingerprint("Standard"));
        Assert.True(Synced());

        // the file is written again without anything new (the same note saved once more, the vault opened): still nothing to send
        var file = Path.Combine(pc.Root, "vaults", "Standard", "vault.db");
        var written = File.GetLastWriteTimeUtc(file);
        Thread.Sleep(20);
        pc.Vault("Standard").SetNote(new VerseRef("JHN", 3, 16), "vom PC");
        Assert.True(Synced());

        // something new: to send; taken back again: nothing to send
        pc.Vault("Standard").SetNote(new VerseRef("PSA", 23, 1), "neu am PC");
        Assert.False(Synced());
        pc.Vault("Standard").SetNote(new VerseRef("PSA", 23, 1), "");
        Assert.True(Synced());
        Assert.False(pc.Sync.IsOnADevice("Anderer", pc.Vaults.Fingerprint("Standard")));   // another vault has no state on a device
    }

    [Fact]
    public async Task When_the_phone_asks_the_computer_knows_afterwards_what_the_phone_has()
    {
        // the phone starts the comparison (as an Android phone does) and brings something new: the computer merges it and knows the shared state
        var key = SyncCode.NewKey();
        using var pc = new Device(Path.Combine(_dir, "pc"), key, "PC");
        using var phone = new Device(Path.Combine(_dir, "phone"), key, "Handy");
        pc.Vault("Standard").SetNote(new VerseRef("JHN", 3, 16), "vom PC");
        phone.Vault("Standard").SetNote(new VerseRef("PSA", 23, 1), "vom Handy");
        Assert.Null((await phone.Sync.SyncWithAsync(pc.AsPeer())).Error);
        Assert.Equal(2, pc.Vault("Standard").GetNotes().Count);
        Assert.True(pc.Sync.IsOnADevice("Standard", pc.Vaults.Fingerprint("Standard")));
        pc.Vault("Standard").SetNote(new VerseRef("MAT", 5, 3), "danach am PC");
        Assert.False(pc.Sync.IsOnADevice("Standard", pc.Vaults.Fingerprint("Standard")));
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

    static ParsedBible Bible(string name, string shortName, string word) => new(name, shortName, "de",
        [
            new ParsedVerse("GEN", 1, 1, [new Segment("Im Anfang", false, ["H7225"]), new Segment(" schuf Gott " + word), new Segment("", false, null, 0)]),
            new ParsedVerse("JHN", 3, 16, [new Segment("Denn also hat Gott die Welt geliebt", true, ["G3779"])]),
        ],
        [("GEN", 1, new Footnote(0, 1, "Fußnote von " + name))],
        [("GEN", 1, 1, "Die Schöpfung")]);

    static string Canon(LocalBibleStore store, string name)
    {
        var id = store.GetTranslationsAsync().Result.Single(t => t.Name == name).Id;
        return BibleTextFile.Write(store.Load(id)!);
    }

    [Fact]
    public async Task WholeTranslations_GoToTheOtherDevice_AndADeletedOneDoesNotComeBack()
    {
        var key = SyncCode.NewKey();
        using var a = new Device(Path.Combine(_dir, "a"), key, "PC");
        using var b = new Device(Path.Combine(_dir, "b"), key, "Handy");
        var la = new LocalBibleStore(Path.Combine(a.Root, "local.db"));
        var lb = new LocalBibleStore(Path.Combine(b.Root, "local.db"));
        var imported = new List<string>();
        a.Sync.Bibles = new LocalBibles(la);
        b.Sync.Bibles = new LocalBibles(lb, id => { lock (imported) imported.Add(id); });
        la.Save(Bible("Schlachter 2000", "SCH2000", "Himmel"));
        la.Save(Bible("Elberfelder", "ELB", "Erde"));
        lb.Save(Bible("Luther 1912", "LUT", "Licht"));
        lb.Save(Bible("Elberfelder", "ELB", "anders"));   // the same name on both sides: each keeps its own

        var r = await a.Sync.SyncWithAsync(b.AsPeer());
        Assert.Null(r.Error);
        Assert.Equal(2, r.Bibles);   // one came, one went
        foreach (var name in new[] { "Schlachter 2000", "Luther 1912" }) Assert.Equal(Canon(la, name), Canon(lb, name));
        var sch = lb.Load(lb.GetTranslationsAsync().Result.Single(t => t.Name == "Schlachter 2000").Id)!;
        Assert.Equal(["H7225"], sch.Verses[0].Segments[0].Strongs);
        Assert.True(sch.Verses[1].Segments[0].WordsOfJesus);
        Assert.Single(sch.Footnotes); Assert.Single(sch.Headings!);
        Assert.Contains("anders", Canon(lb, "Elberfelder"));
        Assert.Contains("Erde", Canon(la, "Elberfelder"));
        Assert.Single(imported);

        // nothing new: nothing goes over again
        Assert.Equal(0, (await a.Sync.SyncWithAsync(b.AsPeer())).Bibles);
        Assert.Equal(0, (await b.Sync.SyncWithAsync(a.AsPeer())).Bibles);

        // deleted on the phone: the computer does not bring it back, the other way round neither
        lb.Remove(lb.GetTranslationsAsync().Result.Single(t => t.Name == "Schlachter 2000").Id);
        Assert.Equal(0, (await a.Sync.SyncWithAsync(b.AsPeer())).Bibles);
        Assert.Equal(0, (await b.Sync.SyncWithAsync(a.AsPeer())).Bibles);
        Assert.DoesNotContain(lb.GetTranslationsAsync().Result, t => t.Name == "Schlachter 2000");

        // a new one later still goes over
        lb.Save(Bible("Menge", "MEN", "Wasser"));
        Assert.Equal(1, (await a.Sync.SyncWithAsync(b.AsPeer())).Bibles);
        Assert.Equal(Canon(lb, "Menge"), Canon(la, "Menge"));
    }

    [Fact]
    public async Task Translations_StayWhereTheyAre_WhenTheOtherDeviceSendsNone()
    {
        var key = SyncCode.NewKey();
        using var a = new Device(Path.Combine(_dir, "a"), key, "PC");
        using var b = new Device(Path.Combine(_dir, "b"), key, "Handy");   // like an older version: lists no translations
        var la = new LocalBibleStore(Path.Combine(a.Root, "local.db"));
        a.Sync.Bibles = new LocalBibles(la);
        la.Save(Bible("Schlachter 2000", "SCH2000", "Himmel"));
        a.Vault("Standard").SetNote(new VerseRef("Joh", 3, 16), "Notiz");

        var r = await a.Sync.SyncWithAsync(b.AsPeer());
        Assert.Null(r.Error);
        Assert.Equal(0, r.Bibles);
        Assert.Single(b.Vault("Standard").GetNotes());   // the notes went over as always

        // switched off on the computer: nothing goes, even to a device that would take it
        var lb = new LocalBibleStore(Path.Combine(b.Root, "local.db"));
        b.Sync.Bibles = new LocalBibles(lb);
        a.Cfg.Bibles = false;
        Assert.Equal(0, (await a.Sync.SyncWithAsync(b.AsPeer())).Bibles);
        Assert.Equal(0, (await b.Sync.SyncWithAsync(a.AsPeer())).Bibles);
        Assert.Empty(await lb.GetTranslationsAsync());
    }

    [Fact]
    public void PackedTranslation_IsCheckedBeforeItIsRead()
    {
        var packed = LocalBibles.Pack(Bible("Schlachter 2000", "SCH2000", "Himmel"));
        Assert.StartsWith("#BIBLESTUDY-TXT", LocalBibles.Unpack(packed));
        Assert.Null(LocalBibles.Unpack([1, 2, 3, 4]));
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, true)) gz.Write("nur ein Text"u8);
        Assert.Null(LocalBibles.Unpack(ms.ToArray()));
    }
}
