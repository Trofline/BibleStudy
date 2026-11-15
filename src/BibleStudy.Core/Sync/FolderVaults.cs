using BibleStudy.Core.Storage;

namespace BibleStudy.Core.Sync;

/// <summary>What the comparison needs from the program: the vaults, their fingerprint, a consistent copy of one, and the way to bring a copy into one.</summary>
public interface ISyncVaults
{
    IReadOnlyList<string> Names();
    string Fingerprint(string name);
    /// <summary>A consistent copy of the vault in a new file.</summary>
    void Snapshot(string name, string path);
    /// <summary>
    /// Brings what the copy holds into the vault (creates the vault when it does not exist yet). <paramref name="basePath"/> is the state both had after their
    /// last comparison (null = none yet): with it, changes and deletions of the other side are taken over (see <see cref="VaultStore.MergeFrom(string, string, string?)"/>);
    /// without it nothing is deleted or overwritten. The copy may be changed.
    /// </summary>
    Task MergeAsync(string name, string snapshotPath, string? basePath);
    /// <summary>When the vault was last written (for noticing a change at once; the default knows nothing).</summary>
    DateTime Stamp(string name) => DateTime.MinValue;
}

/// <summary>The vaults in vaults/NAME/vault.db below a root folder. The program overrides <see cref="MergeAsync"/> to merge into the vault that is open.</summary>
public class FolderVaults(string root, Func<string, bool>? skip = null, string copySuffix = "Sync") : ISyncVaults
{
    protected string PathOf(string name) => Path.Combine(root, "vaults", name, "vault.db");

    public IReadOnlyList<string> Names()
    {
        var dir = Path.Combine(root, "vaults");
        if (!Directory.Exists(dir)) return [];
        return [.. Directory.GetDirectories(dir).Select(Path.GetFileName).OfType<string>()
            .Where(n => MarkLogic.IsValidVaultName(n) && File.Exists(PathOf(n)) && skip?.Invoke(n) != true).Order(StringComparer.OrdinalIgnoreCase)];
    }

    public virtual string Fingerprint(string name) => new VaultStore(PathOf(name), name).Fingerprint();

    public virtual DateTime Stamp(string name) => WriteTime(PathOf(name));

    /// <summary>When an SQLite file was last written (also its journal files).</summary>
    protected static DateTime WriteTime(string db) =>
        new[] { "", "-wal", "-journal" }.Select(s => db + s).Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();

    public virtual void Snapshot(string name, string path)
    {
        if (File.Exists(path)) File.Delete(path);
        new VaultStore(PathOf(name), name).BackupTo(path);
    }

    public virtual Task MergeAsync(string name, string snapshotPath, string? basePath)
    {
        var target = PathOf(name);
        if (File.Exists(target)) new VaultStore(target, name).MergeFrom(snapshotPath, copySuffix, basePath);
        else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(snapshotPath, target); }
        return Task.CompletedTask;
    }
}
