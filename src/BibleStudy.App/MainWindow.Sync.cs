using Avalonia.Input.Platform;
using Avalonia.Controls;
using Avalonia.Threading;
using BibleStudy.Core;
using BibleStudy.Core.Sync;

namespace BibleStudy.App;

/// <summary>
/// "Geräte abgleichen": the user's own devices in the same network compare their vaults by themselves (see <see cref="LanSync"/>).
/// One device shows a code (as text and as a QR code), the others take it over; after that they find each other without anything else to do.
/// </summary>
public partial class MainWindow
{
    SyncConfig _syncCfg = new();
    LanSync? _lan;

    string SyncPath => Path.Combine(_root, "sync.json");

    /// <summary>The vaults of this program for the comparison: what arrives is merged into the vault that is open, on the interface thread.</summary>
    sealed class AppVaults(MainWindow w, string root) : FolderVaults(root, n => w.IsGuestVault(n), L("Abgleich"))
    {
        public override async Task MergeAsync(string name, string snapshotPath, string? basePath)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var home = Path.Combine(root, "vaults", name);
                if (File.Exists(Path.Combine(home, "vault.db"))) w.StoreOf(name).MergeFrom(snapshotPath, L("Abgleich"), basePath);
                else { Directory.CreateDirectory(home); File.Copy(snapshotPath, Path.Combine(home, "vault.db")); }
                w._readStore.MergeFrom(snapshotPath);
            });
        }
    }

    void InitSync()
    {
        var fresh = !File.Exists(SyncPath);
        _syncCfg = SyncConfig.Load(SyncPath);
#if ANDROID
        if (fresh) _syncCfg.DeviceName = Android.OS.Build.Model ?? "Android";
#elif IOS
        if (fresh) _syncCfg.DeviceName = UIKit.UIDevice.CurrentDevice.Name;
#endif
        if (fresh) _syncCfg.Save(SyncPath);
        SyncNameBox.Text = _syncCfg.DeviceName;
        SyncAutoCheck.IsChecked = _syncCfg.Auto;
        SyncAutoCheck.IsCheckedChanged += (_, _) => { _syncCfg.Auto = SyncAutoCheck.IsChecked == true; _syncCfg.Save(SyncPath); };
        SyncNameBox.LostFocus += (_, _) =>
        {
            var name = SyncNameBox.Text?.Trim() ?? "";
            if (name.Length == 0) { SyncNameBox.Text = _syncCfg.DeviceName; return; }
            if (name != _syncCfg.DeviceName) { _syncCfg.DeviceName = name; _syncCfg.Save(SyncPath); RestartSync(); }
        };
        SyncSetupBtn.Click += (_, _) =>
        {
            _syncCfg.Key ??= SyncCode.NewKey();
            _syncCfg.Save(SyncPath);
            StartSync();
            ShowSyncState();
        };
        SyncNowBtn.Click += async (_, _) => await SyncNowAsync();
        SyncOffBtn.Click += (_, _) =>
        {
            _lan?.Stop(); _lan = null;
            _syncCfg.Key = null; lock (_syncCfg.Done) _syncCfg.Done.Clear();
            _syncCfg.Save(SyncPath);
            ShowSyncState();
        };
        SyncJoinBtn.Click += (_, _) => { JoinWithCode(SyncCodeBox.Text ?? ""); };
        // the whole pairing link, not only the code: it carries the address of this device, which an iPhone needs (it often does not hear the call-out)
        SyncCopyBtn.Click += async (_, _) =>
        {
            if (_syncCfg.Key is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(SyncCode.ToUrl(_syncCfg.Key, LanSync.LocalAddresses().Select(a => a + ":" + (_lan?.Port is > 0 and var p ? p : LanSync.DataPort))));
            SyncStatus.Text = L("Kopplungs-Link kopiert. Auf dem anderen Gerät ins Feld „Code von einem anderen Gerät“ einfügen.");
        };
        ShowSyncState();
        if (_syncCfg.Key is not null) DispatcherTimer.RunOnce(StartSync, TimeSpan.FromSeconds(8));   // the reader comes first
#if MOBILE
        if (App.TakePendingPair() is { } link) DispatcherTimer.RunOnce(() => JoinWithCode(link), TimeSpan.FromSeconds(1));
#endif
    }

    /// <summary>Takes over a code (typed, or from the address of a QR code that the camera app opened) and starts the comparison.</summary>
    internal void JoinWithCode(string text)
    {
        if (SyncCode.Parse(text) is not { } key) { SyncStatus.Text = L("Dieser Code ist nicht gültig. Er hat 26 Zeichen (Buchstaben und Ziffern)."); return; }
        _lan?.Stop(); _lan = null;
        _syncCfg.Key = key; lock (_syncCfg.Done) _syncCfg.Done.Clear();
        foreach (var h in SyncCode.ParseHosts(text)) { var at = h.LastIndexOf(':'); if (int.TryParse(h[(at + 1)..], out var port)) _syncCfg.AddHost(h[..at], port); }
        _syncCfg.Save(SyncPath);
        SyncCodeBox.Text = "";
        StartSync();
        ShowSyncState();
        StatusText.Text = L("Gerät gekoppelt. Der Abgleich läuft jetzt im selben Netzwerk.");
    }

    void StartSync()
    {
        if (_syncCfg.Key is null) return;
        try
        {
            if (_lan is null)
            {
                _lan = new LanSync(new AppVaults(this, _root), _syncCfg, SyncPath, Path.Combine(_root, "sync-tmp"));
                _lan.VaultsChanged += names => Dispatcher.UIThread.Post(() => OnSyncChanged(names));
                _lan.Finished += r => Dispatcher.UIThread.Post(() => ShowSyncResult(r));
            }
            _lan.Start();
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException)
        {
            _lan = null;
            SyncStatus.Text = L("Der Abgleich konnte nicht starten: ") + ex.Message;
        }
    }

    /// <summary>Compares soon with the devices in reach (the app came to the front again).</summary>
    internal void SyncSoon() { if (_lan is null) StartSync(); _lan?.Nudge(); }

    bool _syncedAtClose;

    /// <summary>
    /// The program closes: what was written in the last seconds goes to the devices in reach first (eight seconds at the most), so the other device
    /// is up to date without this one staying open. True when the closing has to wait for it.
    /// </summary>
    bool SyncBeforeClose()
    {
        if (_syncedAtClose || _lan is not { } lan || _syncCfg.Key is null) return false;
        _syncedAtClose = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try { AutoSaveDocument(); SaveNoteDraft(); } catch (Exception ex) { ErrorLog.Write(ex); }
            try { await Task.WhenAny(Task.Run(lan.SyncKnownAsync), Task.Delay(TimeSpan.FromSeconds(8))); } catch (Exception ex) { ErrorLog.Write(ex); }
            Close();
        });
        return true;
    }

    void RestartSync() { _lan?.Stop(); _lan = null; StartSync(); }

    void ShowSyncState()
    {
        var has = _syncCfg.Key is not null;
        SyncPairPanel.IsVisible = has;
        SyncSetupBtn.IsVisible = !has;
        SyncNowBtn.IsVisible = has; SyncOffBtn.IsVisible = has; SyncAutoCheck.IsVisible = has;
        if (has)
        {
            SyncQrHost.Content = new QrView { Size = 200, Text = SyncCode.ToUrl(_syncCfg.Key!, LanSync.LocalAddresses().Select(a => a + ":" + (_lan?.Port is > 0 and var p ? p : LanSync.DataPort))) };
            SyncCodeText.Text = SyncCode.Pretty(_syncCfg.Key!);
            SyncStatus.Text = _syncCfg.LastTime is { } t ? L("Zuletzt abgeglichen: {0} mit {1}.", t, _syncCfg.LastPeer ?? "") : L("Eingerichtet. Sobald ein anderes Gerät mit demselben Code im Netzwerk ist, gleicht ihr euch ab.");
        }
        else
        {
            SyncQrHost.Content = null;
            SyncStatus.Text = L("Noch nicht eingerichtet.");
        }
    }

    async Task SyncNowAsync()
    {
        if (_lan is null) StartSync();
        if (_lan is null) return;
        SyncNowBtn.IsEnabled = false;
        SyncStatus.Text = L("Suche andere Geräte im Netzwerk …");
        try
        {
            var reports = await _lan.SyncAllNowAsync();
            if (reports.Count == 0) SyncStatus.Text = L("Kein anderes Gerät gefunden. Beide Geräte müssen im selben WLAN sein, den Abgleich eingeschaltet und denselben Code haben.");
        }
        finally { SyncNowBtn.IsEnabled = true; }
    }

    void ShowSyncResult(SyncReport r)
    {
        if (r.Error == "code") SyncStatus.Text = L("{0} hat einen anderen Code.", r.PeerName);
        else if (r.Error is not null) SyncStatus.Text = L("Abgleich mit {0} unterbrochen: {1}", r.PeerName, r.Error);
        else
        {
            ShowSyncState();
            if (r.Changed > 0) StatusText.Text = L("Abgeglichen mit {0}: Neues übernommen.", r.PeerName);
        }
    }

    /// <summary>Something new arrived in a vault: what is shown is read again (without closing what the person is writing).</summary>
    void OnSyncChanged(IReadOnlyList<string> names)
    {
        try
        {
            RefreshVaultUi();
            if (!names.Contains(_vault.Name, StringComparer.OrdinalIgnoreCase)) return;
            _read = _vault.GetReadChapters();
            RefreshLists(); RefreshDocs(); RefreshQueue(); RefreshTodos(); RefreshBookmarks();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException) { ErrorLog.Write(ex); }
    }
}
