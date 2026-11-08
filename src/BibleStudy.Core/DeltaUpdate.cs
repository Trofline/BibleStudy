using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BibleStudy.Core;

/// <summary>One file of a program version; <see cref="Tag"/>, <see cref="Pack"/>, <see cref="Offset"/> and <see cref="Length"/> say where its gzip stream lies on GitHub.</summary>
public sealed record UpdateFile(string Path, long Size, string Sha256, bool Exec = false, string? Tag = null, string? Pack = null, long Offset = 0, long Length = 0);

/// <summary>All files of one program version on one system. Every release publishes this list; each copy of the program keeps its own as <c>update-files.json</c>.</summary>
public sealed record UpdateManifest(string Version, string Rid, List<UpdateFile> Files);

/// <summary>What has to change to get from the installed files to a version.</summary>
public sealed record DeltaPlan(UpdateManifest Target, string Root, List<UpdateFile> Download, List<string> Remove)
{
    public long DownloadBytes => Download.Sum(f => f.Length);
}

/// <summary>
/// Updates that fetch only the files that changed. A release adds one "pack" with the gzip streams of its new files; its manifest points every file
/// at the pack that holds it, also packs of older releases, so unchanged files are never uploaded again and any older version can update directly.
/// The files are fetched by HTTP range, checked by SHA-256 and swapped by renaming, which also works for the files of the running program;
/// the old files stay as <c>*.bsold</c> until the new version has started, so a failure can always be undone.
/// </summary>
public static class DeltaUpdate
{
    public const string ManifestName = "update-files.json";
    const string StagingName = ".update-staging", ReadyName = "ready.json", RestartName = "restart", JournalName = ".update-journal.json", OkName = ".update-ok", TrialName = ".update-trial", OldSuffix = ".bsold", FailSuffix = ".bsfail";
    /// <summary>How many starts of a version installed "when closing" may end without a window before its files are taken back.</summary>
    public const int TrialStarts = 3;
    const string FailedName = "update-failed.txt";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    static readonly Regex SafeName = new(@"^[A-Za-z0-9._-]+$");
    static readonly HashSet<string> NeverTouched = new(StringComparer.OrdinalIgnoreCase) { "Data", "portable.flag", "installed.flag", ManifestName, StagingName, JournalName, OkName, TrialName };

    /// <summary>The system this copy of the program was built for, as release.ps1 names it.</summary>
    public static string Rid => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    public static string ManifestAsset(string rid, string version) => $"BibleStudy-Update-{rid}-{version}.json";
    public static string PackAsset(string rid, string version) => $"BibleStudy-Pack-{rid}-{version}.bin";

    /// <summary>The folder the paths of the manifest start from: the program folder, on a Mac the "Contents" folder of the app (Info.plist and the icon belong to a version too).</summary>
    public static string RootOf(string programDir)
    {
        var full = Path.GetFullPath(programDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(full) == "MacOS" && Path.GetFileName(Path.GetDirectoryName(full)) == "Contents" ? Path.GetDirectoryName(full)! : full;
    }

    public static UpdateManifest ParseManifest(string json)
    {
        var m = JsonSerializer.Deserialize<UpdateManifest>(json, Json) ?? throw new InvalidDataException("manifest");
        foreach (var f in m.Files)
        {
            if (!IsSafePath(f.Path)) throw new InvalidDataException("manifest path: " + f.Path);
            if (f.Pack is not null && (f.Tag is null || !SafeName.IsMatch(f.Pack) || !SafeName.IsMatch(f.Tag))) throw new InvalidDataException("manifest pack: " + f.Pack);
        }
        return m;
    }

    public static string WriteManifest(UpdateManifest m) => JsonSerializer.Serialize(m, Json);

    public static UpdateManifest? LoadInstalled(string root)
    {
        var path = Path.Combine(root, ManifestName);
        if (!File.Exists(path)) return null;
        try { return ParseManifest(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException) { return null; }
    }

    /// <summary>A relative path below the root that is no part of the user's data or of the update's own files.</summary>
    public static bool IsSafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith('/')) return false;
        var parts = path.Split('/');
        return parts.All(p => p.Length > 0 && p != "." && p != "..") && !NeverTouched.Contains(parts[0]) && !path.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase);
    }

    static string Full(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    // ---- making a release -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Lists a finished program folder. Files that are the same in <paramref name="previous"/> keep their place in an older pack; all others go into the
    /// pack of this version (none is written when nothing changed). Returns the manifest; the caller puts it into the folder and next to the pack.
    /// </summary>
    public static UpdateManifest BuildPack(string sourceDir, string rid, string version, UpdateManifest? previous, string outDir)
    {
        if (previous?.Version == version) throw new InvalidOperationException($"The previous manifest is of the same version {version}: its pack would be overwritten.");
        var tag = "v" + version;
        var known = previous?.Rid == rid ? previous.Files.Where(f => f.Pack is not null).ToLookup(f => f.Sha256 + "|" + f.Size) : null;
        var packPath = Path.Combine(outDir, PackAsset(rid, version));
        Directory.CreateDirectory(outDir);
        var files = new List<UpdateFile>();
        long offset = 0;
        using (var pack = File.Create(packPath))
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var rel = Path.GetRelativePath(sourceDir, file).Replace(Path.DirectorySeparatorChar, '/');
                if (rel == ManifestName) continue;
                if (!IsSafePath(rel)) throw new InvalidDataException("path in the program folder: " + rel);
                var info = new FileInfo(file);
                var sha = HashFile(file);
                var exec = !rid.StartsWith("win") && IsUnixProgram(info.Name);
                if (known?[sha + "|" + info.Length].FirstOrDefault() is { } old)
                {
                    files.Add(new UpdateFile(rel, info.Length, sha, exec, old.Tag, old.Pack, old.Offset, old.Length));
                    continue;
                }
                var start = pack.Position;
                using (var gz = new GZipStream(pack, CompressionLevel.Optimal, leaveOpen: true))
                using (var src = File.OpenRead(file)) src.CopyTo(gz);
                files.Add(new UpdateFile(rel, info.Length, sha, exec, tag, PackAsset(rid, version), start, pack.Position - start));
                offset = pack.Position;
            }
        if (offset == 0) File.Delete(packPath);
        return new UpdateManifest(version, rid, files);
    }

    /// <summary>Files that need the "executable" flag on Linux and macOS (the same rule as the packages made by release.ps1).</summary>
    static bool IsUnixProgram(string name) =>
        name is "BibleStudy" or "BibleStudy.Mcp" or "createdump" or "StrongsAdder" || name.EndsWith(".sh") || name.EndsWith(".command") || name.EndsWith(".dylib") || name.EndsWith(".so");

    // ---- planning ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Compares the files on disk with a version. On a Mac the program files are re-signed after installing, so their hash differs from the manifest:
    /// there a file also counts as up to date when the installed manifest says it already is that version (<paramref name="trustInstalled"/>).
    /// </summary>
    public static DeltaPlan CreatePlan(UpdateManifest target, UpdateManifest? installed, string root, bool trustInstalled)
    {
        var before = installed?.Files.ToDictionary(f => f.Path, f => f.Sha256) ?? [];
        var download = new List<UpdateFile>();
        foreach (var f in target.Files)
        {
            var path = Full(root, f.Path);
            if (File.Exists(path))
            {
                if (trustInstalled && before.TryGetValue(f.Path, out var sha) && sha.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                if (new FileInfo(path).Length == f.Size && HashFile(path).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            }
            if (f.Pack is null) throw new InvalidDataException("no pack holds " + f.Path);
            download.Add(f);
        }
        var wanted = target.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remove = before.Keys.Where(p => !wanted.Contains(p) && File.Exists(Full(root, p))).ToList();
        return new DeltaPlan(target, root, download, remove);
    }

    /// <summary>True when the program may change its own folder (a read-only Mac app or a protected folder needs the full package).</summary>
    public static bool CanWrite(string root)
    {
        try
        {
            var probe = Path.Combine(root, ".update-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // ---- downloading ------------------------------------------------------------------------------------------------------

    public static string PackUrl(UpdateFile f) => $"https://github.com/{Updater.Repo}/releases/download/{f.Tag}/{f.Pack}";

    /// <summary>
    /// Fetches the changed files into a staging folder next to the program and checks each one. Only when all are there it leaves the note that
    /// makes <see cref="Swap"/> install them when the program ends; nothing of the program has been changed until then.
    /// </summary>
    public static async Task DownloadAsync(DeltaPlan plan, HttpClient http, IProgress<double>? progress, CancellationToken ct = default)
    {
        var staging = Path.Combine(plan.Root, StagingName);
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        try
        {
            long done = 0; var total = Math.Max(1, plan.DownloadBytes);
            await Parallel.ForEachAsync(plan.Download, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (file, token) =>
            {
                await FetchOneAsync(file, Full(staging, file.Path), http, token);
                progress?.Report((double)Interlocked.Add(ref done, file.Length) / total);
            });
            File.WriteAllText(Path.Combine(staging, ManifestName), WriteManifest(plan.Target));
            // the last step: without this note the folder is an unfinished download and gets deleted at the next start
            File.WriteAllText(Path.Combine(staging, ReadyName), JsonSerializer.Serialize(new Ready(plan.Target.Version, [.. plan.Download.Select(f => f.Path)], plan.Remove), Json));
        }
        catch
        {
            try { Directory.Delete(staging, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    static async Task FetchOneAsync(UpdateFile f, string target, HttpClient http, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, PackUrl(f));
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("BibleStudy", AppPaths.Version));
                req.Headers.Range = new RangeHeaderValue(f.Offset, f.Offset + f.Length - 1);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                // a server that ignores the range would send the whole pack: that is not this file
                if (resp.StatusCode != HttpStatusCode.PartialContent) throw new HttpRequestException($"{f.Path}: HTTP {(int)resp.StatusCode}");
                string actual;
                await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                await using (var gz = new GZipStream(src, CompressionMode.Decompress))
                await using (var dst = File.Create(target))
                using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[81920]; int n;
                    while ((n = await gz.ReadAsync(buffer, ct)) > 0) { sha.AppendData(buffer, 0, n); await dst.WriteAsync(buffer.AsMemory(0, n), ct); }
                    actual = Convert.ToHexString(sha.GetHashAndReset());
                }
                if (!actual.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L("Die Prüfsumme der Datei stimmt nicht – Update abgebrochen."));
                if (f.Exec && !OperatingSystem.IsWindows())
                    File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                return;
            }
            catch (Exception ex) when (attempt < 3 && !ct.IsCancellationRequested && ex is HttpRequestException or IOException or InvalidDataException)
            {
                await Task.Delay(500 * attempt, ct);
            }
        }
    }

    // ---- installing -------------------------------------------------------------------------------------------------------

    sealed record Ready(string Version, List<string> Files, List<string> Remove);

    sealed record Journal(string To, bool Finished, List<string> Files, List<string> Remove);

    /// <summary>True when a complete download waits to be installed.</summary>
    public static bool IsReady(string root) => File.Exists(Path.Combine(root, StagingName, ReadyName));

    /// <summary>The user chose "install now": the program starts again when the files are in place (otherwise it just ends and the new version comes at the next start).</summary>
    public static void RequestRestart(string root) => File.WriteAllText(Path.Combine(root, StagingName, RestartName), "");

    /// <summary>True when the waiting update should start the program again. Read before <see cref="Swap"/>, which clears the staging folder.</summary>
    public static bool RestartRequested(string root) => File.Exists(Path.Combine(root, StagingName, RestartName));

    /// <summary>The version waiting to be installed.</summary>
    public static string WaitingVersion(string root) =>
        (JsonSerializer.Deserialize<Ready>(File.ReadAllText(Path.Combine(root, StagingName, ReadyName)), Json) ?? throw new InvalidDataException("ready")).Version;

    /// <summary>
    /// Puts the downloaded files in place: each old file is renamed to <c>*.bsold</c> (allowed even while it is in use), the new one moved in.
    /// Any failure puts everything back and rethrows. Returns the files that were replaced.
    /// </summary>
    public static IReadOnlyList<string> Swap(string root)
    {
        var staging = Path.Combine(root, StagingName);
        var ready = JsonSerializer.Deserialize<Ready>(File.ReadAllText(Path.Combine(staging, ReadyName)), Json) ?? throw new InvalidDataException("ready");
        if (ready.Files.Concat(ready.Remove).Any(p => !IsSafePath(p))) throw new InvalidDataException("ready");
        var all = ready.Files.Append(ManifestName).ToList();
        WriteJournal(root, new Journal(ready.Version, false, all, ready.Remove));
        try
        {
            foreach (var rel in all)
            {
                var target = Full(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target)) File.Move(target, target + OldSuffix, overwrite: true);
                File.Move(Full(staging, rel), target);
            }
            foreach (var rel in ready.Remove)
            {
                var target = Full(root, rel);
                if (File.Exists(target)) File.Move(target, target + OldSuffix, overwrite: true);
            }
            WriteJournal(root, new Journal(ready.Version, true, all, ready.Remove));
        }
        catch
        {
            Rollback(root);
            throw;
        }
        try { Directory.Delete(staging, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* removed at the next start */ }
        return ready.Files;
    }

    /// <summary>Puts the old files back after a failed swap or a new version that did not start.</summary>
    public static void Rollback(string root)
    {
        if (ReadJournal(root) is not { } journal) return;
        foreach (var rel in journal.Files.Concat(journal.Remove))
        {
            var target = Full(root, rel);
            var old = target + OldSuffix;
            if (File.Exists(old)) MoveBack(old, target);
            else if (journal.Files.Contains(rel) && File.Exists(target) && (journal.Finished || !File.Exists(Full(Path.Combine(root, StagingName), rel)))) File.Delete(target);   // a file the old version did not have
        }
        foreach (var name in new[] { JournalName, OkName, TrialName }) { try { File.Delete(Path.Combine(root, name)); } catch (IOException) { } }
        try { if (Directory.Exists(Path.Combine(root, StagingName))) Directory.Delete(Path.Combine(root, StagingName), true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Puts an old file back. When the new one is in use by the running program (a version that took itself back), it is renamed aside first (allowed even then).</summary>
    static void MoveBack(string old, string target)
    {
        try { File.Move(old, target, overwrite: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var aside = target + FailSuffix;
            File.Move(target, aside, overwrite: true);
            File.Move(old, target);
        }
    }

    /// <summary>The first real start of a version that was installed when the program closed (nobody saw its window): from now on its starts are counted until a window is up.</summary>
    public static void StartTrial(string root) { try { File.WriteAllText(Path.Combine(root, TrialName), "0"); } catch (IOException) { } }

    public static void ClearTrial(string root) { try { File.Delete(Path.Combine(root, TrialName)); } catch (IOException) { } }

    /// <summary>
    /// Called first at every start. Counts the start while a trial runs; true when <see cref="TrialStarts"/> starts of this version already ended without
    /// confirming a window: the caller then takes the version back (<see cref="Rollback"/>) and starts the old one.
    /// </summary>
    public static bool TrialFailed(string root, string runningVersion)
    {
        var path = Path.Combine(root, TrialName);
        if (!File.Exists(path)) return false;
        try
        {
            if (ReadJournal(root) is not { Finished: true } journal || journal.To != runningVersion) { File.Delete(path); return false; }
            var count = int.TryParse(File.ReadAllText(path), out var n) ? n : 0;
            if (count >= TrialStarts) return true;
            File.WriteAllText(path, (count + 1).ToString());
        }
        catch (IOException) { }
        return false;
    }

    static void WriteJournal(string root, Journal journal)
    {
        var path = Path.Combine(root, JournalName);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(journal, Json));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    static Journal? ReadJournal(string root)
    {
        var path = Path.Combine(root, JournalName);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Journal>(File.ReadAllText(path), Json); }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }

    /// <summary>The new version has its window up: tells the old process it may end (the first start after an update only).</summary>
    public static bool ConfirmStarted(string root, string version)
    {
        if (ReadJournal(root) is not { Finished: true } journal || journal.To != version) return false;
        File.WriteAllText(Path.Combine(root, OkName), version);
        return true;
    }

    public static bool IsConfirmed(string root) => File.Exists(Path.Combine(root, OkName));

    /// <summary>
    /// Runs at every start, before anything else. An interrupted swap is undone; after a confirmed update the old files are deleted (those still
    /// held by the old process stay until the next start); an unfinished download is thrown away. A first start, not yet confirmed, changes nothing.
    /// </summary>
    public static void CleanUp(string root, string runningVersion)
    {
        SweepFailed(root);
        var staging = Path.Combine(root, StagingName);
        if (File.Exists(Path.Combine(root, JournalName)))
        {
            var journal = ReadJournal(root);
            if (journal is null || !journal.Finished) { Rollback(root); return; }
            if (journal.To == runningVersion && (!IsConfirmed(root) || File.Exists(Path.Combine(root, TrialName)))) return;   // not yet proven: the way back stays open
            PurgeOld(root, journal);
        }
        if (Directory.Exists(staging))
            try { Directory.Delete(staging, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Files a version that took itself back left behind (they were in use at that time).</summary>
    static void SweepFailed(string root)
    {
        try
        {
            var dirs = Directory.EnumerateDirectories(root).Where(d => !Path.GetFileName(d).Equals("Data", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(d).Equals(StagingName, StringComparison.OrdinalIgnoreCase)).Prepend(root);
            foreach (var dir in dirs)
                foreach (var f in Directory.EnumerateFiles(dir, "*" + FailSuffix, dir == root ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories))
                    try { File.Delete(f); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Deletes the old files of a confirmed update once nothing holds them any more.</summary>
    public static void PurgeOld(string root)
    {
        if (ReadJournal(root) is { Finished: true } journal && IsConfirmed(root)) PurgeOld(root, journal);
    }

    static void PurgeOld(string root, Journal journal)
    {
        var left = false;
        foreach (var rel in journal.Files.Concat(journal.Remove))
        {
            var old = Full(root, rel) + OldSuffix;
            try { if (File.Exists(old)) File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { left = true; }
        }
        if (left) return;
        foreach (var name in new[] { JournalName, OkName }) { try { File.Delete(Path.Combine(root, name)); } catch (IOException) { } }
    }

    // ---- a version that failed ---------------------------------------------------------------------------------------------

    /// <summary>Notes that the quick update to a version failed: the next attempt takes the full package instead of failing the same way.</summary>
    public static void NoteFailed(string dataDir, string version, string reason)
    {
        try { Directory.CreateDirectory(dataDir); File.WriteAllText(Path.Combine(dataDir, FailedName), version + "\n" + reason); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* then it is simply tried again */ }
    }

    /// <summary>The version and the reason of the last failed quick update; null when there is none.</summary>
    public static (string Version, string Reason)? LastFailed(string dataDir)
    {
        try
        {
            var path = Path.Combine(dataDir, FailedName);
            if (!File.Exists(path)) return null;
            var parts = File.ReadAllText(path).Split('\n', 2);
            return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "");
        }
        catch (IOException) { return null; }
    }
}
