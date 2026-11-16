namespace BibleStudy.Core.Sync;

/// <summary>A file in the shared cloud folder, with what shows that it changed.</summary>
public sealed record CloudFile(string Name, DateTimeOffset Modified, long Size);

/// <summary>
/// A folder that a cloud service (Google Drive, OneDrive, iCloud Drive, Dropbox …) keeps the same on all the user's devices. The sync uses it as a
/// mailbox: below "BibleStudy-Sync" each device has a folder of its own with a copy of each vault; the others take those copies over whenever they look.
/// So the devices never have to be on at the same time.
/// </summary>
public interface ICloudFolder
{
    /// <summary>A name to show (the folder).</summary>
    string Label { get; }
    /// <summary>The folders of the devices.</summary>
    Task<IReadOnlyList<string>> DevicesAsync();
    Task<IReadOnlyList<CloudFile>> FilesAsync(string device);
    Task DownloadAsync(string device, string name, string localPath);
    /// <summary>Puts a file into the folder of a device (made when missing), replacing one of the same name.</summary>
    Task UploadAsync(string device, string name, string localPath);
}

/// <summary>A cloud folder that is a plain folder on this computer (the Google Drive, OneDrive or Dropbox app keeps it in step).</summary>
public sealed class PathCloudFolder(string path) : ICloudFolder
{
    string Root => Path.Combine(path, CloudSync.FolderName);

    public string Label => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n ? n : path;

    public Task<IReadOnlyList<string>> DevicesAsync() =>
        Task.FromResult<IReadOnlyList<string>>(Directory.Exists(Root) ? [.. Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>()] : []);

    public Task<IReadOnlyList<CloudFile>> FilesAsync(string device)
    {
        var dir = Path.Combine(Root, device);
        if (!Directory.Exists(dir)) return Task.FromResult<IReadOnlyList<CloudFile>>([]);
        return Task.FromResult<IReadOnlyList<CloudFile>>([.. new DirectoryInfo(dir).GetFiles().Select(f => new CloudFile(f.Name, f.LastWriteTimeUtc, f.Length))]);
    }

    public Task DownloadAsync(string device, string name, string localPath)
    {
        File.Copy(Path.Combine(Root, device, name), localPath, true);
        return Task.CompletedTask;
    }

    public Task UploadAsync(string device, string name, string localPath)
    {
        var dir = Path.Combine(Root, device);
        Directory.CreateDirectory(dir);
        // written beside it first: the cloud app never sends a half-written file
        var part = Path.Combine(dir, name + ".part");
        File.Copy(localPath, part, true);
        File.Move(part, Path.Combine(dir, name), true);
        return Task.CompletedTask;
    }
}

public static class CloudSync
{
    /// <summary>The folder inside the chosen cloud folder.</summary>
    public const string FolderName = "BibleStudy-Sync";

    /// <summary>An empty file in a device's folder whose name carries the device's name (shown to the others without opening anything).</summary>
    public const string NamePrefix = "device-";

    public static string NameFile(string deviceName) =>
        NamePrefix + new string([.. deviceName.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_')]).Trim() + ".txt";
}
