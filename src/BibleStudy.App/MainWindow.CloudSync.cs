using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Sync;

namespace BibleStudy.App;

/// <summary>
/// The sync through a cloud folder (Google Drive, OneDrive, iCloud Drive, Dropbox): every device puts a copy of its vaults there and takes over the
/// copies of the others whenever it is open, so the devices never have to be on at the same time (see <see cref="LanSync.CloudRoundAsync"/>).
/// On a computer the folder is a plain folder of the cloud app; on a phone it is a folder of the Files app, kept as a bookmark of the system.
/// </summary>
public partial class MainWindow
{
    /// <summary>A cloud folder reached through the system (a phone's Files app): the same mailbox as <see cref="PathCloudFolder"/>.</summary>
    sealed class StorageCloudFolder(IStorageFolder root) : ICloudFolder
    {
        public string Label => root.Name;

        static async Task<IStorageFolder?> Sub(IStorageFolder parent, string name, bool make)
        {
            IStorageFolder? found = null;
            try { found = await parent.GetFolderAsync(name); } catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
            return found ?? (make ? await parent.CreateFolderAsync(name) : null);
        }

        async Task<IStorageFolder?> Device(string device, bool make) =>
            await Sub(root, CloudSync.FolderName, make) is { } top ? await Sub(top, device, make) : null;

        public async Task<IReadOnlyList<string>> DevicesAsync()
        {
            var list = new List<string>();
            if (await Sub(root, CloudSync.FolderName, false) is not { } top) return list;
            await foreach (var item in top.GetItemsAsync()) if (item is IStorageFolder) list.Add(item.Name);
            return list;
        }

        public async Task<IReadOnlyList<CloudFile>> FilesAsync(string device)
        {
            var list = new List<CloudFile>();
            if (await Device(device, false) is not { } dir) return list;
            await foreach (var item in dir.GetItemsAsync())
                if (item is IStorageFile)
                {
                    var p = await item.GetBasicPropertiesAsync();
                    list.Add(new CloudFile(item.Name, p.DateModified ?? DateTimeOffset.MinValue, (long)(p.Size ?? 0)));
                }
            return list;
        }

        public async Task DownloadAsync(string device, string name, string localPath)
        {
            var dir = await Device(device, false) ?? throw new IOException(device);
            var file = await dir.GetFileAsync(name) ?? throw new IOException(name);
            await using var from = await file.OpenReadAsync();
            await using var to = File.Create(localPath);
            await from.CopyToAsync(to);
        }

        public async Task UploadAsync(string device, string name, string localPath)
        {
            var dir = await Device(device, true) ?? throw new IOException(device);
            IStorageFile? old = null;
            try { old = await dir.GetFileAsync(name); } catch (Exception ex) when (ex is IOException or ArgumentException) { }
            if (old is not null) await old.DeleteAsync();
            var file = await dir.CreateFileAsync(name) ?? throw new IOException(name);
            await using var to = await file.OpenWriteAsync();
            await using var from = File.OpenRead(localPath);
            await from.CopyToAsync(to);
        }
    }

    /// <summary>The chosen cloud folder, opened again (null = none, or it cannot be reached now).</summary>
    async Task<ICloudFolder?> OpenCloudAsync()
    {
        try
        {
            if (_syncCfg.CloudPath is { } path) return Directory.Exists(path) ? new PathCloudFolder(path) : null;
            if (_syncCfg.CloudBookmark is { } mark && TopLevel.GetTopLevel(this)?.StorageProvider is { } sp && await sp.OpenFolderBookmarkAsync(mark) is { } folder)
                return new StorageCloudFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { ErrorLog.Write(ex); }
        return null;
    }

    async Task AttachCloudAsync()
    {
        if (_lan is not { } lan) return;
        lan.Cloud = await OpenCloudAsync();
        if (_syncCfg.CloudPath is not null || _syncCfg.CloudBookmark is not null)
            CloudFolderText.Text = lan.Cloud is { } c ? L("Cloud-Ordner: {0}", _syncCfg.CloudPath ?? c.Label) : L("Der Cloud-Ordner ist gerade nicht erreichbar.");
    }

    async Task ChooseCloudAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var picked = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L("Cloud-Ordner wählen"), AllowMultiple = false });
        if (picked.FirstOrDefault() is not { } folder) return;
        if (!AppPlatform.IsMobile && folder.TryGetLocalPath() is { } local) { _syncCfg.CloudPath = local; _syncCfg.CloudBookmark = null; }
        else
        {
            var mark = folder.CanBookmark ? await folder.SaveBookmarkAsync() : null;
            if (mark is null) { SyncStatus.Text = L("Diesen Ordner kann sich das System nicht merken. Wähle einen anderen."); return; }
            _syncCfg.CloudBookmark = mark; _syncCfg.CloudPath = null;
        }
        _syncCfg.Key ??= SyncCode.NewKey();   // the cloud folder is a way of the sync: it runs with it
        ForgetCloudDone();
        _syncCfg.Save(SyncPath);
        StartSync();
        await AttachCloudAsync();
        ShowSyncState();
        await SyncNowAsync();
    }

    void StopCloud()
    {
        _syncCfg.CloudPath = null; _syncCfg.CloudBookmark = null;
        ForgetCloudDone();
        _syncCfg.Save(SyncPath);
        if (_lan is { } lan) lan.Cloud = null;
        ShowSyncState();
    }

    void ForgetCloudDone()
    {
        lock (_syncCfg.Done) foreach (var k in _syncCfg.Done.Keys.Where(k => k.StartsWith("cloud|", StringComparison.Ordinal)).ToList()) _syncCfg.Done.Remove(k);
    }

    void ShowCloudState()
    {
        var has = _syncCfg.CloudPath is not null || _syncCfg.CloudBookmark is not null;
        CloudRemoveBtn.IsVisible = has;
        CloudChooseBtn.Content = has ? L("Anderen Cloud-Ordner wählen …") : L("Cloud-Ordner wählen …");
        if (!has) CloudFolderText.Text = L("Kein Cloud-Ordner gewählt.");
        else if (_syncCfg.CloudPath is { } p) CloudFolderText.Text = L("Cloud-Ordner: {0}", p);
        else if (_lan?.Cloud is { } c) CloudFolderText.Text = L("Cloud-Ordner: {0}", c.Label);
    }
}
