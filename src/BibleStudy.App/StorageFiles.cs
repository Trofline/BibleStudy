using Avalonia.Platform.Storage;

namespace BibleStudy.App;

/// <summary>
/// The program reads and writes files by their path. On a desktop the file picker gives one; on Android it gives a content address of
/// another app (the file manager, Google Drive …). These helpers make a path for both: a file to open is copied into the cache first,
/// a file to save is written to the cache and copied to the chosen place when the work is done.
/// </summary>
static class StorageFiles
{
    static string Cache(string sub)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BibleStudy", sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>The path of a picked file; on Android a copy in the cache (with the same name, the importers look at the extension).</summary>
    public static async Task<string?> LocalPathAsync(this IStorageFile file)
    {
        if (file.TryGetLocalPath() is { } path && File.Exists(path)) return path;
        try
        {
            var dir = Path.Combine(Cache("open"), Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            var copy = Path.Combine(dir, string.Concat(file.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)));
            await using var src = await file.OpenReadAsync();
            await using var dst = File.Create(copy);
            await src.CopyToAsync(dst);
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ErrorLog.Write(ex);
            return null;
        }
    }

    /// <summary>The paths of all picked files (see <see cref="LocalPathAsync"/>); those that cannot be read are left out.</summary>
    public static async Task<List<string>> LocalPathsAsync(this IReadOnlyList<IStorageFile> files)
    {
        var list = new List<string>();
        foreach (var f in files) if (await f.LocalPathAsync() is { } p) list.Add(p);
        return list;
    }

    /// <summary>
    /// Where to write a file chosen in the save dialog. On a desktop the file itself; on Android a file in the cache that is copied to the
    /// chosen place when the target is disposed (<c>await using</c>). Null when nothing was chosen.
    /// </summary>
    public static async Task<SaveTarget?> SaveTargetAsync(this IStorageFile? file)
    {
        if (file is null) return null;
        if (file.TryGetLocalPath() is { } path) return new SaveTarget(path, null);
        await Task.CompletedTask;
        return new SaveTarget(Path.Combine(Cache("save"), Guid.NewGuid().ToString("N")[..8] + "_" + file.Name), file);
    }

    /// <summary>The path of a picked folder; null on Android, where a folder of another app has no path (the caller says so).</summary>
    public static string? LocalPath(this IStorageFolder folder) => folder.TryGetLocalPath();

    /// <summary>The message when a folder cannot be used on this system.</summary>
    public static string NoFolderHere => L("Auf Android lässt sich kein Ordner wählen. Exporte landen im Speicher der App und werden zum Teilen angeboten.");
}

/// <summary>A path to write to; on Android its content goes to the chosen file when it is disposed.</summary>
sealed class SaveTarget(string path, IStorageFile? copyTo) : IAsyncDisposable
{
    public string Path { get; } = path;

    public async ValueTask DisposeAsync()
    {
        if (copyTo is null || !File.Exists(Path)) return;
        try
        {
            await using (var src = File.OpenRead(Path))
            await using (var dst = await copyTo.OpenWriteAsync())
            {
                await src.CopyToAsync(dst);
                if (dst.CanSeek) dst.SetLength(dst.Position);
            }
            File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { ErrorLog.Write(ex); }
    }
}
