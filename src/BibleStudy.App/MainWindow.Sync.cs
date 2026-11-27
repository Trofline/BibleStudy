using Avalonia.Input.Platform;
using Avalonia.Controls;
using Avalonia.Threading;
using BibleStudy.Core;
using BibleStudy.Core.Sync;

namespace BibleStudy.App;

/// <summary>
/// "Geräte synchronisieren": the user's own devices in the same network compare their vaults by themselves (see <see cref="LanSync"/>).
/// One device shows a code (as text and as a QR code), the others take it over; after that they find each other without anything else to do.
/// </summary>
public partial class MainWindow
{
    SyncConfig _syncCfg = new();
    LanSync? _lan;

    string SyncPath => Path.Combine(_root, "sync.json");

    /// <summary>The vaults of this program for the comparison: what arrives is merged into the vault that is open, on the interface thread.</summary>
    sealed class AppVaults(MainWindow w, string root) : FolderVaults(root, n => w.IsGuestVault(n), L("Synchronisation"))
    {
        public override async Task MergeAsync(string name, string snapshotPath, string? basePath)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var home = Path.Combine(root, "vaults", name);
                if (File.Exists(Path.Combine(home, "vault.db"))) w.StoreOf(name).MergeFrom(snapshotPath, L("Synchronisation"), basePath);
                else { Directory.CreateDirectory(home); File.Copy(snapshotPath, Path.Combine(home, "vault.db")); }
                if (w.IsGuestVault(name)) return;
                w._readStore.MergeFrom(snapshotPath, basePath);
                if (AppData.ReadFrom(snapshotPath) is { } theirs) w.MergeAppData(theirs, AppData.ReadFrom(basePath));
            });
        }

        // what is not in a vault travels inside each vault's copy: the reading progress (read.db, one list for all vaults) and the person's own
        // data of the program (see AppData); a change there is a change to send as well
        public override void Snapshot(string name, string path)
        {
            base.Snapshot(name, path);
            if (w.IsGuestVault(name)) return;
            w._readStore.CopyInto(path);
            AppData.WriteInto(path, Dispatcher.UIThread.Invoke(w.CollectAppData));
        }

        public override string Fingerprint(string name) =>
            base.Fingerprint(name) + (w.IsGuestVault(name) ? "" : "|read:" + w._readStore.Fingerprint() + "|app:" + AppData.Fingerprint(Dispatcher.UIThread.Invoke(w.CollectAppData)));

        public override DateTime Stamp(string name)
        {
            var own = base.Stamp(name);
            if (w.IsGuestVault(name)) return own;
            foreach (var f in new[] { "read.db", "memory.json", "learn.json", "settings.json" })
            {
                var t = WriteTime(Path.Combine(AppPaths.Root, f));
                if (t > own) own = t;
            }
            return own;
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
        SyncAutoCheck.IsCheckedChanged += (_, _) => { _syncCfg.Auto = SyncAutoCheck.IsChecked == true; _syncCfg.Save(SyncPath); ShowSyncState(); };
        SyncBiblesCheck.IsChecked = _syncCfg.Bibles;
        SyncBiblesCheck.IsCheckedChanged += (_, _) => { _syncCfg.Bibles = SyncBiblesCheck.IsChecked == true; _syncCfg.Save(SyncPath); if (_syncCfg.Bibles) _lan?.Nudge(); };
        SyncWaitCheck.IsChecked = _settings.WaitForPhoneOnClose;
        SyncWaitCheck.IsCheckedChanged += (_, _) => { _settings.WaitForPhoneOnClose = SyncWaitCheck.IsChecked == true; SaveSettings(); };
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
        SyncChip.Click += async (_, _) => await SyncNowAsync();
        SyncOffBtn.Click += (_, _) =>
        {
            _lan?.Stop(); _lan = null;
            _syncCfg.Key = null; lock (_syncCfg.Done) _syncCfg.Done.Clear();
            _syncCfg.CloudPath = null; _syncCfg.CloudBookmark = null;
            _syncCfg.Save(SyncPath);
            ShowSyncState();
        };
        SyncJoinBtn.Click += (_, _) => { JoinWithCode(SyncCodeBox.Text ?? ""); };
        CloudChooseBtn.Click += async (_, _) => await ChooseCloudAsync();
        CloudRemoveBtn.Click += (_, _) => StopCloud();
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
        if (App.TakePendingWidget() is { } widget) DispatcherTimer.RunOnce(() => OpenFromWidget(widget), TimeSpan.FromSeconds(1));   // the app was started by a tap on a widget
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
        StatusText.Text = L("Gerät gekoppelt. Die Synchronisation läuft jetzt im selben Netzwerk.");
        Notify(L("Gerät gekoppelt"), L("Suche jetzt das andere Gerät … Lass die App dort offen; sobald ihr euch erreicht, kommt hier eine Meldung."));
    }

    void StartSync()
    {
        if (_syncCfg.Key is null) return;
        try
        {
            if (_lan is null)
            {
                _syncVaults = new AppVaults(this, _root);
                _lan = new LanSync(_syncVaults, _syncCfg, SyncPath, Path.Combine(_root, "sync-tmp"));
                _lan.VaultsChanged += names => Dispatcher.UIThread.Post(() => OnSyncChanged(names));
                _lan.Finished += r => Dispatcher.UIThread.Post(() => ShowSyncResult(r));
                _lan.Contacted += name => Dispatcher.UIThread.Post(() => Greet(name));
                _lan.Bibles = new LocalBibles(_local, id => Dispatcher.UIThread.Post(() => { _store.ForgetTranslation(id); _corpora.Remove(id); _strongIndexes.Remove(id); }));
                _lan.BiblesReceived += names => Dispatcher.UIThread.Post(() => OnBiblesReceived(names));
                _ = AttachCloudAsync();
            }
            _lan.Start();
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException)
        {
            _lan = null;
            SyncStatus.Text = L("Die Synchronisation konnte nicht starten: ") + ex.Message;
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
        SaveOpenEdits();
        if (!ChangedSinceSync()) return false;   // nothing new here: the devices have it all, the program closes at once
        _syncedAtClose = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try { AutoSaveDocument(); SaveNoteDraft(); } catch (Exception ex) { ErrorLog.Write(ex); }
            try { await Task.WhenAny(Task.Run(lan.SyncKnownAsync), Task.Delay(TimeSpan.FromSeconds(8))); } catch (Exception ex) { ErrorLog.Write(ex); }
            Close();
        });
        return true;
    }

    AppVaults? _syncVaults;
    DateTime _lastSyncOkUtc = DateTime.MinValue;

    /// <summary>
    /// True when this computer has something new for the other devices: an own vault whose content (with the reading progress and the person's
    /// data of the program) is not the state the last comparison left on a device. That a file was written is not enough: the settings are written
    /// by every move of a window and opening a vault touches its file, and then there is nothing to send (the notice "Warte auf dein Handy" came anyway).
    /// </summary>
    bool ChangedSinceSync()
    {
        if (_syncVaults is null) return false;
        var since = _lastSyncOkUtc;
        if (since == DateTime.MinValue && DateTime.TryParseExact(_syncCfg.LastTime, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var last))
            since = last.ToUniversalTime().AddMinutes(1);   // the minute it was written in counts as synced
        try
        {
            foreach (var n in _syncVaults.Names())
            {
                if (IsGuestVault(n) || _syncVaults.Stamp(n) <= since.AddSeconds(5)) continue;   // nothing written since: unchanged, no need to look closer
                // written since: the content decides, by the same fingerprint the synchronisation compares (a few milliseconds for a vault)
                if (_lan?.IsOnADevice(n, _syncVaults.Fingerprint(n)) != true) return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { return false; }
    }

    /// <summary>A note or document that is still being written is saved first: it counts as something new for the other devices.</summary>
    void SaveOpenEdits()
    {
        try { AutoSaveDocument(); SaveNoteDraft(); } catch (Exception ex) { ErrorLog.Write(ex); }
    }

    bool _phoneWaiting, _phoneWaitStop;
    Border? _phoneWaitNotice;
    DateTime _phoneWaitSince;

    /// <summary>
    /// Asked first when the window is to close (before the closing animation, so the window never goes away and comes back): a phone that was
    /// synced before is not in reach and something changed here since. Then the program stays open and waits for it (the phone app only takes
    /// part while it is open), with a notice and a button to close at once. A second click on X in the first two seconds does nothing (a quick
    /// double click would end the waiting before the notice was even read); later it closes at once. True: the closing was held.
    /// </summary>
    bool HoldCloseForPhone(WindowClosingEventArgs e)
    {
        if (e.CloseReason != WindowCloseReason.WindowClosing) return false;
        if (_phoneWaiting)
        {
            if (DateTime.UtcNow - _phoneWaitSince < TimeSpan.FromSeconds(2)) { e.Cancel = true; return true; }
            _phoneWaiting = false; _phoneWaitStop = true; HideNotice(_phoneWaitNotice);
            return false;
        }
        if (_syncedAtClose || _lan is not { } lan || _syncCfg.Key is null) return false;
        if (!(_settings.WaitForPhoneOnClose && _syncCfg.Auto && !AppPlatform.IsMobile && lan.Cloud is null && _syncCfg.LastPeer is not null && lan.Peers.Count == 0)) return false;
        SaveOpenEdits();
        if (!ChangedSinceSync()) return false;   // nothing new since the last synchronisation: the phone has it all, no waiting and no notice
        e.Cancel = true;
        _syncedAtClose = true; _phoneWaiting = true; _phoneWaitStop = false; _phoneWaitSince = DateTime.UtcNow;
        Dispatcher.UIThread.Post(() => _ = WaitForPhoneThenCloseAsync(lan));
        ShowCloseLock(TimeSpan.FromSeconds(2));
        return true;
    }

    /// <summary>
    /// While a second click on X does nothing, X shows the seconds left ("2", "1"), so one knows to wait and then click again;
    /// afterwards it is the X again.
    /// </summary>
    async void ShowCloseLock(TimeSpan length)
    {
        var icon = CloseBtn.Content;
        var tip = ToolTip.GetTip(CloseBtn);
        var until = DateTime.UtcNow + length;
        var count = new TextBlock { FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 14, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        ToolTip.SetTip(CloseBtn, L("Gleich noch einmal klicken, dann schließt das Programm sofort."));
        CloseBtn.Content = count;
        try
        {
            for (var left = until - DateTime.UtcNow; left > TimeSpan.Zero && _phoneWaiting; left = until - DateTime.UtcNow)
            {
                count.Text = ((int)Math.Ceiling(left.TotalSeconds)).ToString();
                await Task.Delay(100);
            }
        }
        finally { CloseBtn.Content = icon; ToolTip.SetTip(CloseBtn, tip); }
    }

    /// <summary>Up to two minutes: every few seconds it tries the known devices; once one was synced (or "Close now" is pressed) the program closes.</summary>
    async Task WaitForPhoneThenCloseAsync(LanSync lan)
    {
        try { AutoSaveDocument(); SaveNoteDraft(); } catch (Exception ex) { ErrorLog.Write(ex); }
        var before = _lastSyncOkUtc;
        _phoneWaitNotice = Notify(L("Warte auf dein Handy …"), L("Öffne kurz BibleStudy auf dem Handy, damit es den neuesten Stand bekommt. Danach schließt sich das Programm von selbst."),
            actionLabel: L("Jetzt schließen"), action: () => _phoneWaitStop = true, stay: TimeSpan.FromMinutes(2));
        var until = DateTime.UtcNow.AddMinutes(2);
        while (!_phoneWaitStop && DateTime.UtcNow < until && _lastSyncOkUtc == before)
        {
            try { await Task.WhenAny(Task.Run(lan.SyncKnownAsync), Task.Delay(TimeSpan.FromSeconds(8))); } catch (Exception ex) { ErrorLog.Write(ex); }
            for (var i = 0; i < 6 && !_phoneWaitStop && _lastSyncOkUtc == before; i++) await Task.Delay(500);
        }
        if (!_phoneWaiting) return;   // X was clicked again: the window is closing already
        if (_lastSyncOkUtc != before) await Task.Delay(TimeSpan.FromSeconds(2));   // "Synced with …" is seen before the window goes
        _phoneWaiting = false;
        HideNotice(_phoneWaitNotice);
        Close();
    }

    void RestartSync() { _lan?.Stop(); _lan = null; StartSync(); }

    void ShowSyncState()
    {
        ShowCloudState();
        var has = _syncCfg.Key is not null;
        SyncPairPanel.IsVisible = has;
        SyncSetupBtn.IsVisible = !has;
        SyncSteps.IsVisible = !has;   // the three steps only until it is set up
        SyncNowBtn.IsVisible = has; SyncOffBtn.IsVisible = has; SyncAutoCheck.IsVisible = has;
        SyncWaitCheck.IsVisible = has && _syncCfg.Auto && !AppPlatform.IsMobile;   // only with the synchronisation switched on, and only a computer is closed
        SyncChip.IsVisible = has;
        if (has)
        {
            SyncQrHost.Content = new QrView { Size = 200, Text = SyncCode.ToUrl(_syncCfg.Key!, LanSync.LocalAddresses().Select(a => a + ":" + (_lan?.Port is > 0 and var p ? p : LanSync.DataPort))) };
            SyncCodeText.Text = SyncCode.Pretty(_syncCfg.Key!);
            SyncStatus.Text = _syncCfg.LastTime is { } t ? L("Zuletzt synchronisiert: {0} mit {1}.", t, _syncCfg.LastPeer ?? "") : L("Eingerichtet. Sobald ein anderes Gerät mit demselben Code im Netzwerk ist, synchronisiert ihr euch.");
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
        SyncNowBtn.IsEnabled = false; SyncChip.IsEnabled = false;
        SyncStatus.Text = L("Suche andere Geräte im Netzwerk …");
        SyncChipText.Text = L("Synchronisiere …");
        try
        {
            _syncAsked = true;
            var reports = await _lan.SyncAllNowAsync();
            string done;
            if (reports.Count == 0)
            {
                SyncStatus.Text = L("Kein anderes Gerät gefunden. Beide Geräte müssen im selben WLAN sein, die Synchronisation eingeschaltet und denselben Code haben.");
                done = L("Kein Gerät erreicht");
                StatusText.Text = SyncStatus.Text;
            }
            else if (reports.All(r => r.Error is not null)) { done = L("Nicht erreicht"); StatusText.Text = SyncStatus.Text; }
            else done = "✓ " + L("Synchronisiert");
            SyncChipText.Text = done;
            DispatcherTimer.RunOnce(() => SyncChipText.Text = L("Synchronisieren"), TimeSpan.FromSeconds(4));
        }
        finally { SyncNowBtn.IsEnabled = true; SyncChip.IsEnabled = true; }
    }

    void ShowSyncResult(SyncReport r)
    {
        if (r.Error == "code") SyncStatus.Text = L("{0} hat einen anderen Code.", r.PeerName);
        else if (r.Error is not null) SyncStatus.Text = L("Synchronisation mit {0} unterbrochen: {1}", r.PeerName, r.Error);
        else
        {
            _lastSyncOkUtc = DateTime.UtcNow;
            ShowSyncState();
            var first = !_greeted.Contains(r.PeerName);
            Greet(r.PeerName);
            if (r.Changed > 0) StatusText.Text = L("Synchronisiert mit {0}: Neues übernommen.", r.PeerName);
            // the end of a round that moved something (or the first one): it is all over, the app on the other device may be closed now
            if (first || r.Changed + r.Sent + r.Bibles > 0 || _syncAsked) ShowSynced(r.PeerName);
        }
    }

    /// <summary>Whole translations came from another device: they are in the translation list now.</summary>
    async void OnBiblesReceived(IReadOnlyList<string> names)
    {
        try
        {
            await RefreshLocalBibleBoxAsync();
            await ReloadTranslationsAsync(null);
            StatusText.Text = names.Count == 1
                ? L("Übersetzung „{0}“ vom anderen Gerät übernommen.", names[0])
                : L("{0} Übersetzungen vom anderen Gerät übernommen: {1}", names.Count, string.Join(", ", names));
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    bool _syncAsked;   // "sync now" was pressed: its end is shown even when nothing had to move

    void ShowSynced(string peer)
    {
        _syncAsked = false;
        // shown a moment after "connected", so both can be read
        var wait = _noticeShownAt.AddSeconds(1.5) - DateTime.UtcNow;
        void Show() => Notify(L("Synchronisiert mit {0}", peer), _phone ? L("Alles übertragen. Du kannst die App jetzt schließen.") : L("Alles übertragen."), good: true);
        if (wait > TimeSpan.Zero) DispatcherTimer.RunOnce(Show, wait); else Show();
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
        Notify(L("Seit {0} Tagen nicht synchronisiert", days), L("Öffne BibleStudy kurz auf {0} (im selben WLAN), dann synchronisiert ihr euch. Bis dahin geht nichts verloren.", _syncCfg.LastPeer ?? L("dem anderen Gerät")));
    }

    readonly HashSet<string> _greeted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The first contact with a device since the start: a short notice who it is (once per device, not at every round).</summary>
    void Greet(string peer)
    {
        if (!_greeted.Add(peer)) return;
        ShowSyncState();
        // on the PC a reminder: the phone is mostly closed while one works here, so it gets the newest state only when it is opened before the PC stops
        Notify(L("Verbunden mit {0}", peer), _phone ? "" : L("Tipp: Bevor du am PC aufhörst, öffne kurz BibleStudy auf dem Handy, damit es den neuesten Stand bekommt."), good: true);
    }

    /// <summary>Something new arrived in a vault: what is shown is read again (without closing what the person is writing).</summary>
    void OnSyncChanged(IReadOnlyList<string> names)
    {
        try
        {
            RefreshVaultUi();
            // the reading progress is shared by all vaults: shown again whichever vault brought it
            _read = _vault.GetReadChapters();
            ReadBtn.IsChecked = _read.Contains((_curBook, _curChapter));
            if (Home.IsVisible) Home.Show(_books, _read, Home.OpenBook);
            if (PlanPanel.IsEffectivelyVisible) BuildPlanUi();   // the plan shows what was read
            if (!names.Contains(_vault.Name, StringComparer.OrdinalIgnoreCase)) return;
            RefreshLists(); RefreshDocs(); RefreshQueue(); RefreshTodos(); RefreshBookmarks();
            _ = RedrawChapterAsync();   // the marks of the chapter that is open, at once
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException) { ErrorLog.Write(ex); }
    }
}
