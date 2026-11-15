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
        if (_syncCfg.Key is not null)
        {
            DispatcherTimer.RunOnce(StartSync, TimeSpan.FromSeconds(8));   // the reader comes first
            DispatcherTimer.RunOnce(RemindToSync, TimeSpan.FromSeconds(30));
        }
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
        Notify(L("Gerät gekoppelt"), L("Suche jetzt das andere Gerät … Lass die App dort offen; sobald ihr euch erreicht, kommt hier eine Meldung."));
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
                _lan.Contacted += name => Dispatcher.UIThread.Post(() => Greet(name));
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
            Greet(r.PeerName);
            if (r.Changed > 0) StatusText.Text = L("Abgeglichen mit {0}: Neues übernommen.", r.PeerName);
        }
    }

    /// <summary>
    /// The other device was not reached for some days (an iPhone compares only while the app is open, and that is easily forgotten): a reminder to open
    /// it once. Nothing gets lost meanwhile (both sides keep their changes), it only waits.
    /// </summary>
    void RemindToSync()
    {
        if (_syncCfg.Key is null || _greeted.Count > 0) return;   // reached since the start: nothing to remind of
        if (_syncCfg.LastTime is not { } last || !DateTime.TryParseExact(last, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when)) return;
        var days = (int)(DateTime.Now - when).TotalDays;
        if (days < 3) return;
        Notify(L("Seit {0} Tagen kein Abgleich", days), L("Öffne BibleStudy kurz auf {0} (im selben WLAN), dann gleicht ihr euch ab. Bis dahin geht nichts verloren.", _syncCfg.LastPeer ?? L("dem anderen Gerät")));
    }

    readonly HashSet<string> _greeted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The first contact with a device since the start: a notice that cannot be missed, so it is clear that the pairing works (once per device, not at every comparison).</summary>
    void Greet(string peer)
    {
        if (!_greeted.Add(peer)) return;
        ShowSyncState();
        Notify(L("Verbunden mit {0}", peer), L("Der Abgleich funktioniert. Ab jetzt gleicht ihr euch von selbst ab, sobald beide Apps im selben WLAN offen sind."), good: true);
    }

    Border? _notice;
    IDisposable? _noticeTimer;

    /// <summary>A notice over everything (phone: at the top, desktop: bottom right) that goes again after some seconds; a tap closes it at once.</summary>
    void Notify(string title, string text, bool good = false)
    {
        if (_notice is null)
        {
            // the notice lies in a grid over the whole content (the content is wrapped once)
            if (TopBar.Parent is not DockPanel dock) return;
            Panel host;
            if (dock.Parent is Grid g) host = g;
            else
            {
                host = new Grid();
                if (dock.Parent is Panel p) { var at = p.Children.IndexOf(dock); p.Children.RemoveAt(at); host.Children.Add(dock); p.Children.Insert(at, host); }
                else if (dock.Parent is ContentControl cc) { cc.Content = null; host.Children.Add(dock); cc.Content = host; }
                else return;
            }
            _notice = new Border
            {
                CornerRadius = new Avalonia.CornerRadius(12), Padding = new Avalonia.Thickness(16, 12), BoxShadow = Avalonia.Media.BoxShadows.Parse("0 4 18 0 #40000000"),
                MaxWidth = 420, Margin = new Avalonia.Thickness(12), IsVisible = false, ZIndex = 1000,
            };
            _notice.PointerPressed += (_, e) => { _notice.IsVisible = false; e.Handled = true; };
            host.Children.Add(_notice);
        }
        _notice.HorizontalAlignment = _phone ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Right;
        _notice.VerticalAlignment = _phone ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Bottom;
        _notice.Margin = _phone ? new Avalonia.Thickness(12, 60, 12, 0) : new Avalonia.Thickness(0, 0, 16, 40);
        _notice.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(good ? "#2F7D4F" : "#3B5BDB"));
        _notice.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = (good ? "✓  " : "") + title, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 15, Foreground = Avalonia.Media.Brushes.White, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = text, FontSize = 13, Foreground = Avalonia.Media.Brushes.White, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            },
        };
        _notice.IsVisible = true;
        _noticeTimer?.Dispose();
        _noticeTimer = DispatcherTimer.RunOnce(() => { if (_notice is not null) _notice.IsVisible = false; }, TimeSpan.FromSeconds(8));
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
