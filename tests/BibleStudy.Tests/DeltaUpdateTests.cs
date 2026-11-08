using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BibleStudy.Core;

namespace BibleStudy.Tests;

public class DeltaUpdateTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    string Folder(string name, params (string Path, string Text)[] files)
    {
        var dir = Path.Combine(_dir, name);
        Directory.CreateDirectory(dir);
        foreach (var (p, t) in files)
        {
            var full = Path.Combine(dir, p.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, t);
        }
        return dir;
    }

    /// <summary>Answers range requests from pack files on disk, like GitHub does.</summary>
    sealed class Packs(string dir, bool ignoreRange = false) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var name = request.RequestUri!.Segments[^1];
            lock (Asked) Asked.Add(request.RequestUri.ToString());
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var all = File.ReadAllBytes(path);
            if (ignoreRange) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(all) });
            var r = request.Headers.Range!.Ranges.Single();
            var part = all[(int)r.From!.Value..((int)r.To!.Value + 1)];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(part) });
        }
    }

    /// <summary>Version 1 and 2 of a program: 2 changes a.dll, adds new.dll, removes gone.dll; same.dll stays in the pack of version 1.</summary>
    (string Installed, string Out, UpdateManifest V1, UpdateManifest V2) TwoVersions()
    {
        var output = Path.Combine(_dir, "out");
        var v1 = Folder("v1", ("a.dll", "old a"), ("same.dll", new string('s', 5000)), ("gone.dll", "bye"), ("mcp/m.dll", "m"));
        var m1 = DeltaUpdate.BuildPack(v1, "win-x64", "1.0.0", null, output);
        File.WriteAllText(Path.Combine(v1, DeltaUpdate.ManifestName), DeltaUpdate.WriteManifest(m1));
        var v2 = Folder("v2", ("a.dll", "new a"), ("same.dll", new string('s', 5000)), ("new.dll", "fresh"), ("mcp/m.dll", "m"));
        var m2 = DeltaUpdate.BuildPack(v2, "win-x64", "2.0.0", m1, output);
        return (v1, output, m1, m2);
    }

    static string Read(string root, string rel) => File.ReadAllText(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Pack_HoldsOnlyWhatIsNew_AndOldFilesPointAtTheOlderPack()
    {
        var (_, _, _, m2) = TwoVersions();
        Assert.Equal("v1.0.0", m2.Files.Single(f => f.Path == "same.dll").Tag);
        Assert.Equal("v2.0.0", m2.Files.Single(f => f.Path == "a.dll").Tag);
        Assert.Equal("v2.0.0", m2.Files.Single(f => f.Path == "new.dll").Tag);
        Assert.DoesNotContain(m2.Files, f => f.Path == DeltaUpdate.ManifestName);
    }

    [Fact]
    public void Plan_FetchesOnlyChangedFiles_AndRemovesWhatIsGone()
    {
        var (root, _, m1, m2) = TwoVersions();
        var plan = DeltaUpdate.CreatePlan(m2, m1, root, trustInstalled: false);
        Assert.Equal(["a.dll", "new.dll"], plan.Download.Select(f => f.Path).Order());
        Assert.Equal(["gone.dll"], plan.Remove);
    }

    [Fact]
    public void Plan_RepairsADamagedFile_ButOnAMacTrustsTheList()
    {
        var (root, _, m1, m2) = TwoVersions();
        File.WriteAllText(Path.Combine(root, "same.dll"), "damaged or signed");
        Assert.Contains(DeltaUpdate.CreatePlan(m2, m1, root, false).Download, f => f.Path == "same.dll");
        Assert.DoesNotContain(DeltaUpdate.CreatePlan(m2, m1, root, true).Download, f => f.Path == "same.dll");
    }

    [Fact]
    public async Task FullUpdate_ReplacesAddsRemoves_AndCleansUp()
    {
        var (root, output, m1, m2) = TwoVersions();
        var plan = DeltaUpdate.CreatePlan(m2, m1, root, false);
        await DeltaUpdate.DownloadAsync(plan, new HttpClient(new Packs(output)), null);
        Assert.Equal("old a", Read(root, "a.dll"));   // nothing changes before the program has ended
        Assert.True(DeltaUpdate.IsReady(root));

        DeltaUpdate.Swap(root);
        Assert.Equal("new a", Read(root, "a.dll"));
        Assert.Equal("fresh", Read(root, "new.dll"));
        Assert.False(File.Exists(Path.Combine(root, "gone.dll")));
        Assert.Equal("old a", Read(root, "a.dll.bsold"));
        Assert.Equal("2.0.0", DeltaUpdate.LoadInstalled(root)!.Version);

        DeltaUpdate.CleanUp(root, "2.0.0");   // the new version's own start: not yet confirmed, the way back stays open
        Assert.True(File.Exists(Path.Combine(root, "a.dll.bsold")));
        Assert.True(DeltaUpdate.ConfirmStarted(root, "2.0.0"));
        DeltaUpdate.CleanUp(root, "2.0.0");
        Assert.Empty(Directory.GetFiles(root, "*.bsold", SearchOption.AllDirectories));
        Assert.False(DeltaUpdate.ConfirmStarted(root, "2.0.0"));
    }

    [Fact]
    public async Task InstalledWhenClosing_StaysTakeBackAbleUntilAWindowWasSeen()
    {
        var (root, output, m1, m2) = TwoVersions();
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText);
        await DeltaUpdate.DownloadAsync(DeltaUpdate.CreatePlan(m2, m1, root, false), new HttpClient(new Packs(output)), null);
        DeltaUpdate.Swap(root);
        Assert.True(DeltaUpdate.ConfirmStarted(root, "2.0.0"));   // the windowless check passed
        DeltaUpdate.StartTrial(root);
        for (var i = 0; i < DeltaUpdate.TrialStarts; i++)
        {
            Assert.False(DeltaUpdate.TrialFailed(root, "2.0.0"));   // a start that never shows its window
            DeltaUpdate.CleanUp(root, "2.0.0");
            Assert.True(File.Exists(Path.Combine(root, "a.dll.bsold")));   // nothing is deleted while it is not proven
        }
        Assert.True(DeltaUpdate.TrialFailed(root, "2.0.0"));
        DeltaUpdate.Rollback(root);
        Assert.Equal(before, Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText));
        Assert.False(DeltaUpdate.TrialFailed(root, "2.0.0"));
    }

    [Fact]
    public async Task InstalledWhenClosing_AWindowProvesIt()
    {
        var (root, output, m1, m2) = TwoVersions();
        await DeltaUpdate.DownloadAsync(DeltaUpdate.CreatePlan(m2, m1, root, false), new HttpClient(new Packs(output)), null);
        DeltaUpdate.Swap(root);
        DeltaUpdate.ConfirmStarted(root, "2.0.0"); DeltaUpdate.StartTrial(root);
        Assert.False(DeltaUpdate.TrialFailed(root, "2.0.0"));
        DeltaUpdate.ClearTrial(root);   // the window is up
        DeltaUpdate.CleanUp(root, "2.0.0");
        Assert.Empty(Directory.GetFiles(root, "*.bsold", SearchOption.AllDirectories));
        Assert.False(DeltaUpdate.TrialFailed(root, "2.0.0"));
    }

    [Fact]
    public async Task Rollback_RestoresEveryFile()
    {
        var (root, output, m1, m2) = TwoVersions();
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText);
        await DeltaUpdate.DownloadAsync(DeltaUpdate.CreatePlan(m2, m1, root, false), new HttpClient(new Packs(output)), null);
        DeltaUpdate.Swap(root);
        DeltaUpdate.Rollback(root);
        Assert.Equal(before, Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText));
    }

    [Fact]
    public async Task SwapThatFailsHalfway_PutsEverythingBack()
    {
        var (root, output, m1, m2) = TwoVersions();
        await DeltaUpdate.DownloadAsync(DeltaUpdate.CreatePlan(m2, m1, root, false), new HttpClient(new Packs(output)), null);
        // the staged "new.dll" (the second in the plan) is gone: the swap fails after a.dll was already replaced
        File.Delete(Path.Combine(root, ".update-staging", "new.dll"));
        Assert.ThrowsAny<IOException>(() => DeltaUpdate.Swap(root));
        Assert.Equal("old a", Read(root, "a.dll"));
        Assert.Equal("1.0.0", DeltaUpdate.LoadInstalled(root)!.Version);
        Assert.Empty(Directory.GetFiles(root, "*.bsold", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task InterruptedSwap_IsUndoneAtTheNextStart()
    {
        var (root, output, m1, m2) = TwoVersions();
        await DeltaUpdate.DownloadAsync(DeltaUpdate.CreatePlan(m2, m1, root, false), new HttpClient(new Packs(output)), null);
        // the state after a power cut in the middle: journal written, a.dll swapped, the others not
        File.Move(Path.Combine(root, "a.dll"), Path.Combine(root, "a.dll.bsold"));
        File.Move(Path.Combine(root, ".update-staging", "a.dll"), Path.Combine(root, "a.dll"));
        File.WriteAllText(Path.Combine(root, ".update-journal.json"), """{"To":"2.0.0","Finished":false,"Files":["a.dll","new.dll","update-files.json"],"Remove":["gone.dll"]}""");
        DeltaUpdate.CleanUp(root, "1.0.0");
        Assert.Equal("old a", Read(root, "a.dll"));
        Assert.Equal("1.0.0", DeltaUpdate.LoadInstalled(root)!.Version);
        Assert.False(Directory.Exists(Path.Combine(root, ".update-staging")));
    }

    [Fact]
    public async Task ServerThatIgnoresRanges_OrWrongContent_ChangesNothing()
    {
        var (root, output, m1, m2) = TwoVersions();
        var plan = DeltaUpdate.CreatePlan(m2, m1, root, false);
        await Assert.ThrowsAsync<HttpRequestException>(() => DeltaUpdate.DownloadAsync(plan, new HttpClient(new Packs(output, ignoreRange: true)), null));
        Assert.False(DeltaUpdate.IsReady(root));
        Assert.Equal("old a", Read(root, "a.dll"));
        var bad = plan with { Download = [.. plan.Download.Select(f => f with { Sha256 = new string('0', 64) })] };
        await Assert.ThrowsAsync<InvalidDataException>(() => DeltaUpdate.DownloadAsync(bad, new HttpClient(new Packs(output)), null));
        Assert.False(DeltaUpdate.IsReady(root));
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("a/../../evil.dll")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("a\\..\\evil.dll")]
    [InlineData("Data/vaults/Standard/vault.db")]
    [InlineData("portable.flag")]
    [InlineData("update-files.json")]
    public void Manifest_CannotReachOutsideTheProgramOrIntoTheData(string path)
    {
        var json = DeltaUpdate.WriteManifest(new UpdateManifest("2.0.0", "win-x64", [new UpdateFile(path, 1, "00", false, "v2.0.0", "p.bin")]));
        Assert.Throws<InvalidDataException>(() => DeltaUpdate.ParseManifest(json));
    }

    [Fact]
    public void Manifest_CannotChooseWhereFilesComeFrom()
    {
        var json = DeltaUpdate.WriteManifest(new UpdateManifest("2.0.0", "win-x64", [new UpdateFile("a.dll", 1, "00", false, "v2/../../evil", "p.bin")]));
        Assert.Throws<InvalidDataException>(() => DeltaUpdate.ParseManifest(json));
    }

    [Fact]
    public void BuildPack_RefusesAPreviousListOfTheSameVersion()
    {
        var (root, output, m1, _) = TwoVersions();
        Assert.Throws<InvalidOperationException>(() => DeltaUpdate.BuildPack(root, "win-x64", m1.Version, m1, output));
    }

    [Fact]
    public void RootOnAMac_IsTheContentsFolder()
    {
        var sep = Path.DirectorySeparatorChar;
        var mac = Path.Combine(_dir, "BibleStudy.app", "Contents", "MacOS");
        Assert.EndsWith($"BibleStudy.app{sep}Contents", DeltaUpdate.RootOf(mac));
        var plain = Path.Combine(_dir, "BibleStudy");
        Assert.Equal(plain, DeltaUpdate.RootOf(plain));
    }

    static string Release(string tag, bool pre, params string[] assets) =>
        "{\"tag_name\":\"" + tag + "\",\"prerelease\":" + (pre ? "true" : "false") + ",\"draft\":false,\"body\":\"n\",\"html_url\":\"https://page/" + tag + "\",\"assets\":[" +
        string.Join(",", assets.Select(a => "{\"name\":\"" + a + "\",\"browser_download_url\":\"https://dl/" + a + "\",\"size\":5}")) + "]}";

    static string Quick(string v) => Release("v" + v, true, $"BibleStudy-Update-win-x64-{v}.json", $"BibleStudy-Update-linux-x64-{v}.json", $"BibleStudy-Pack-win-x64-{v}.bin");
    static string Full(string v) => Release("v" + v, false, $"BibleStudy-Setup-{v}.exe", $"BibleStudy-Portable-{v}.zip", $"BibleStudy-Update-win-x64-{v}.json", $"BibleStudy-Pack-win-x64-{v}.bin");

    /// <summary>The releases as GitHub would list them: one full release, four quick ones after it, a test version on top, the older releases without lists.</summary>
    static readonly string Releases = "[" + string.Join(",",
        Release("v9.0.0", true, "BibleStudy-Setup-9.0.0.exe"), Quick("1.6.11"), Quick("1.6.10"), Quick("1.6.9"), Quick("1.6.8"), Full("1.6.7"),
        Release("v1.6.6", false, "BibleStudy-Setup-1.6.6.exe", "BibleStudy-Portable-1.6.6.zip")) + "]";

    /// <summary>What a program before 1.6.7 does: asks for "latest" (newest that is no pre-release) and looks for its installer there.</summary>
    static string? InstallerAnOldProgramFinds()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Releases);
        var latest = doc.RootElement.EnumerateArray().First(r => !r.GetProperty("prerelease").GetBoolean());
        return latest.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("name").GetString()).FirstOrDefault(n => n!.Contains("Setup"));
    }

    [Fact]
    public void AProgramBeforeTheQuickUpdates_StillFindsAnInstaller_HoweverManyQuickReleasesCameAfter() =>
        Assert.Equal("BibleStudy-Setup-1.6.7.exe", InstallerAnOldProgramFinds());

    [Theory]
    [InlineData("1.6.7")]    // the first version with quick updates
    [InlineData("1.6.8")]    // one behind
    [InlineData("1.6.10")]   // the newest but one
    public void ACopyThatMissedReleases_GoesStraightToTheNewest(string current)
    {
        var update = Updater.ParseUpdate(Releases, current, false, "win-x64");
        Assert.Equal("1.6.11", update!.Version);
        Assert.Equal("https://dl/BibleStudy-Update-win-x64-1.6.11.json", update.ManifestUrl);
        Assert.Equal("", update.DownloadUrl);   // a quick release has no installer
    }

    [Fact]
    public void Update_NothingNewerNothingOffered_AndATestVersionIsNotForEveryone()
    {
        Assert.Null(Updater.ParseUpdate(Releases, "1.6.11", false, "win-x64"));
        Assert.DoesNotContain(Updater.ParseReleases(Releases), r => r.Version == "9.0.0");
        Assert.Contains(Updater.ParseReleases(Releases), r => r.Version == "1.6.11");   // the patch notes show the quick ones
    }

    [Fact]
    public void Update_AFullReleaseHasBoth_AndOtherSystemsListIsNotTaken()
    {
        var list = "[" + Full("2.0.0") + "]";
        var full = Updater.ParseUpdate(list, "1.0.0", false, "win-x64");
        Assert.Equal("BibleStudy-Setup-2.0.0.exe", full!.AssetName);
        Assert.Equal("https://dl/BibleStudy-Update-win-x64-2.0.0.json", full.ManifestUrl);
        Assert.Equal("BibleStudy-Portable-2.0.0.zip", Updater.ParseUpdate(list, "1.0.0", true, "win-x64")!.AssetName);
        Assert.Null(Updater.ParseUpdate(list, "1.0.0", false, "linux-x64")!.ManifestUrl);
        // a quick release without a list for this system is passed over: the newest it has one for counts
        var mixed = "[" + Release("v3.0.0", true, "BibleStudy-Update-win-x64-3.0.0.json") + "," + Quick("2.5.0") + "]";
        Assert.Equal("2.5.0", Updater.ParseUpdate(mixed, "1.0.0", false, "linux-x64")!.Version);
    }

    [Fact]
    public void Update_TheFullPackageOfTheLastFullRelease_IsTheWayBackWhenAQuickUpdateFails()
    {
        var withInstaller = Updater.ParseReleases(Releases).First(r => r.Setup is not null);
        Assert.Equal("1.6.7", withInstaller.Version);
    }

    /// <summary>Five versions, as if released one after another: each pack is built on the list of the one before, as release.ps1 does.</summary>
    List<(string Folder, UpdateManifest Manifest)> FiveVersions()
    {
        var output = Path.Combine(_dir, "out");
        var versions = new (string V, (string, string)[] Files)[]
        {
            ("1.0.0", [("app.dll", "app 1"), ("lib.dll", "lib 1"), ("old.dll", "old"), ("back.dll", "back 1"), ("mcp/m.dll", "m 1"), ("same.dll", new string('s', 4000))]),
            ("1.1.0", [("app.dll", "app 2"), ("lib.dll", "lib 2"), ("old.dll", "old"), ("back.dll", "back 2"), ("mcp/m.dll", "m 1"), ("same.dll", new string('s', 4000))]),   // lib changes here and never again
            ("1.2.0", [("app.dll", "app 3"), ("lib.dll", "lib 2"), ("back.dll", "back 2"), ("mcp/m.dll", "m 3"), ("added.dll", "added"), ("same.dll", new string('s', 4000))]),   // old goes, added comes
            ("1.3.0", [("app.dll", "app 4"), ("lib.dll", "lib 2"), ("back.dll", "back 1"), ("mcp/m.dll", "m 3"), ("added.dll", "added"), ("same.dll", new string('s', 4000))]),   // back is as in the very first again
            ("1.4.0", [("app.dll", "app 5"), ("lib.dll", "lib 2"), ("back.dll", "back 1"), ("mcp/m.dll", "m 3"), ("added.dll", "added 5"), ("same.dll", new string('s', 4000))]),
        };
        var result = new List<(string, UpdateManifest)>();
        UpdateManifest? previous = null;
        foreach (var (v, files) in versions)
        {
            var folder = Folder("build-" + v, files);
            previous = DeltaUpdate.BuildPack(folder, "win-x64", v, previous, output);
            File.WriteAllText(Path.Combine(folder, DeltaUpdate.ManifestName), DeltaUpdate.WriteManifest(previous));
            result.Add((folder, previous));
        }
        return result;
    }

    static Dictionary<string, string> Program(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(DeltaUpdate.ManifestName)).ToDictionary(f => Path.GetRelativePath(root, f), File.ReadAllText);

    [Theory]
    [InlineData(0, 3)]   // a copy of the first version, four releases behind
    [InlineData(1, 3)]   // two behind ...
    [InlineData(2, 3)]
    [InlineData(3, 2)]   // the newest but one
    public async Task ACopyThatMissedReleases_EndsUpExactlyLikeTheNewest(int installedVersion, int atMostFromPacks)
    {
        var all = FiveVersions();
        var (source, manifest) = all[installedVersion];
        var (newest, target) = all[^1];
        var root = Path.Combine(_dir, "installed");
        Directory.CreateDirectory(root);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
        var user = Path.Combine(root, "Data", "meine.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(user)!);
        File.WriteAllText(user, "bleibt");

        var plan = DeltaUpdate.CreatePlan(target, manifest, root, trustInstalled: false);
        var handler = new Packs(Path.Combine(_dir, "out"));
        await DeltaUpdate.DownloadAsync(plan, new HttpClient(handler), null);
        Assert.True(handler.Asked.Select(u => u[..u.LastIndexOf('/')]).Distinct().Count() <= atMostFromPacks);   // only the packs that hold what is missing
        DeltaUpdate.Swap(root);
        Assert.True(DeltaUpdate.ConfirmStarted(root, "1.4.0"));
        DeltaUpdate.CleanUp(root, "1.4.0");

        var after = Program(root);
        Assert.Equal("bleibt", after[Path.Combine("Data", "meine.txt")]);
        after.Remove(Path.Combine("Data", "meine.txt"));
        Assert.Equal(Program(newest), after);
        Assert.Equal("1.4.0", DeltaUpdate.LoadInstalled(root)!.Version);
    }

    [Fact]
    public void Packs_HoldEachFileOnlyOnce_AcrossAllReleases()
    {
        var target = FiveVersions()[^1].Manifest;
        Assert.Equal("v1.1.0", target.Files.Single(f => f.Path == "lib.dll").Tag);       // not changed since: not uploaded again
        Assert.Equal("v1.0.0", target.Files.Single(f => f.Path == "same.dll").Tag);
    }

    [Fact]
    public async Task InstallNowOrWhenClosing_IsDecidedPerUpdate_AndForgottenAfterwards()
    {
        var (root, output, m1, m2) = TwoVersions();
        await DeltaUpdate.DownloadAsync(DeltaUpdate.CreatePlan(m2, m1, root, false), new HttpClient(new Packs(output)), null);
        Assert.False(DeltaUpdate.RestartRequested(root));   // "when closing" is what a waiting update does unless told otherwise
        DeltaUpdate.RequestRestart(root);
        Assert.True(DeltaUpdate.RestartRequested(root));
        DeltaUpdate.Swap(root);
        Assert.False(DeltaUpdate.RestartRequested(root));
        Assert.False(File.Exists(Path.Combine(root, "restart")));   // not a file of the program
    }

    [Fact]
    public async Task ADownloadWaitingFromAnEndedSession_IsThrownAwayNotInstalledUnasked()
    {
        var (root, output, m1, m2) = TwoVersions();
        await DeltaUpdate.DownloadAsync(DeltaUpdate.CreatePlan(m2, m1, root, false), new HttpClient(new Packs(output)), null);
        DeltaUpdate.CleanUp(root, "1.0.0");
        Assert.False(DeltaUpdate.IsReady(root));
        Assert.Equal("old a", Read(root, "a.dll"));
    }

    [Fact]
    public void FailedVersion_IsRemembered()
    {
        Assert.Null(DeltaUpdate.LastFailed(_dir));
        DeltaUpdate.NoteFailed(_dir, "2.0.0", "exit code 1");
        Assert.Equal(("2.0.0", "exit code 1"), DeltaUpdate.LastFailed(_dir));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
}
