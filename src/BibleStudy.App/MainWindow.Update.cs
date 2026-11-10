using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// Updates and removal. An update never touches the user's data (it lives outside the program folder), and a backup of the data is
/// made first anyway. Removal always asks whether the work done so far should be kept.
/// </summary>
public partial class MainWindow
{
    Updater? _updater;
    bool _updating;

    static readonly string[] CacheFolders = ["cache", "arc-cache"];

    void InitUpdate()
    {
        VersionText.Text = L("Version {0} · ", AppPaths.Version) + (AppPaths.IsPortable ? L("Portable (Daten in {0})", AppPaths.Root) : AppPaths.IsInstalled ? L("installiert") : L("Entwicklungsversion")) + L(" · Daten: {0}", AppPaths.Root);
        AutoUpdateCheck.IsChecked = _settings.AutoUpdate;
        AutoUpdateCheck.IsCheckedChanged += (_, _) => { _settings.AutoUpdate = AutoUpdateCheck.IsChecked == true; SaveSettings(); };
        CheckUpdateBtn.Click += async (_, _) => await CheckForUpdateAsync(manual: true);
        RemoveAppBtn.IsVisible = OperatingSystem.IsWindows() && (AppPaths.IsPortable || AppPaths.IsInstalled);
        RemoveAppBtn.Content = AppPaths.IsPortable ? L("Portable-Version entfernen …") : L("Deinstallieren …");
        RemoveAppBtn.Click += async (_, _) => await RemoveProgramAsync();
        RestartBtn.Click += (_, _) => RestartProgram();
        // older versions downloaded updates in the background and installed them without asking: nothing waiting is installed any more
        if (_settings.PendingUpdateFile is { } waiting) { try { File.Delete(waiting); } catch (IOException) { } _settings.PendingUpdateFile = null; _settings.PendingUpdateVersion = null; SaveSettings(); }
        if (DeltaUpdate.LastFailed(AppPaths.Root) is { } failedUpdate && failedUpdate.Version != AppPaths.Version && Updater.Parse(failedUpdate.Version) > Updater.Parse(AppPaths.Version))
            UpdateStatus.Text = L("Das Update auf {0} hat nicht geklappt, die bisherige Version läuft weiter: {1}", failedUpdate.Version, failedUpdate.Reason);
        InitBackup();
        InitTour();
        InitPatchNotes();
        InitTopBar();
        InitReadBarFit();
        InitReadingAids();
        InitCommentarySearch();
        InitEasyMode();
        InitAutomation();
        InitOllama();
        InitLanguageImport();
        InitFeedback(); InitSettingsLayout();
        InitPlugins();
        InitNotes();
        InitSources();
        InitHiddenXrefs();
        // once per start, right after the window is up (the check itself runs in the background and never blocks the reader)
        if (_settings.AutoUpdate)
        {
            DispatcherTimer.RunOnce(async () => await CheckForUpdateAsync(manual: false), TimeSpan.FromSeconds(2));
            var again = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            again.Tick += async (_, _) => await CheckForUpdateAsync(manual: false);
            again.Start();
        }
    }

    // ---- small dialogs -----------------------------------------------------------------------------------------------------

    /// <summary>A modal question with buttons; returns the index of the clicked button (-1 when closed).</summary>
    async Task<int> AskAsync(string title, string text, params string[] buttons)
    {
        var result = -1;
        var win = new Window { Title = title, Width = 520, SizeToContent = SizeToContent.Height, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 14, 0, 0) };
        for (var i = 0; i < buttons.Length; i++)
        {
            var k = i;
            var b = new Button { Content = buttons[i], Margin = new(8, 0, 0, 0) };
            if (i == 0) b.Classes.Add("primary");
            // Enter answers only a window with one button: the first of several is sometimes the far-reaching one ("delete")
            if (buttons.Length == 1) b.IsDefault = true;
            b.Click += (_, _) => { result = k; win.Close(); };
            row.Children.Add(b);
        }
        win.Content = new StackPanel { Margin = new(22), Children = { new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 10) }, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, row } };
        win.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { e.Handled = true; win.Close(); } };   // Esc: no answer, as the ✕ of the window
        await ShowModalAsync(win);
        return result;
    }

    // ---- backup of the user's data -----------------------------------------------------------------------------------------

    /// <summary>Zips the user's data (vaults, settings, local translations; not the re-downloadable caches).</summary>
    static string BackupData(string label, string? folder = null)
    {
        folder ??= AppPaths.BackupDir;
        Directory.CreateDirectory(folder);
        // two backups in the same minute (e.g. one before an update and one by hand) must not collide
        var path = Path.Combine(folder, $"BibleStudy-Daten-{label}-{DateTime.Now:yyyy-MM-dd_HHmmss}.zip");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"BibleStudy-Daten-{label}-{DateTime.Now:yyyy-MM-dd_HHmmss}-{n}.zip");
        var root = AppPaths.Root;
        var backupDir = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        if (Directory.Exists(root))
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file);
                if (CacheFolders.Contains(rel.Split(Path.DirectorySeparatorChar)[0])) continue;
                var parts = rel.Split(Path.DirectorySeparatorChar);
                if (parts.Length > 2 && parts[0] == "vaults" && IsGuestDir(Path.Combine(root, parts[0], parts[1]))) continue;   // guest imports are not backed up
                if (Path.GetFullPath(file).StartsWith(backupDir, StringComparison.OrdinalIgnoreCase)) continue;   // a backup folder inside the data: not the backups themselves
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var dst = zip.CreateEntry(rel, CompressionLevel.Optimal).Open();
                    if (Path.GetExtension(file).Equals(".db", StringComparison.OrdinalIgnoreCase) && SnapshotDb(file) is { } snapshot)
                    {
                        // a database is copied through SQLite: a plain copy taken while it is being written can be unusable
                        try { using var src = File.OpenRead(snapshot); src.CopyTo(dst); }
                        finally { try { File.Delete(snapshot); } catch (IOException) { } }
                    }
                    else
                    {
                        // the file may be open right now: read it without asking for exclusive access
                        using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        src.CopyTo(dst);
                    }
                }
                catch (IOException) { /* a temporary file that vanished meanwhile */ }
            }
        return path;
    }

    /// <summary>A consistent copy of a SQLite database in a temp file; null when the file is no database (it is then copied as it is).</summary>
    static string? SnapshotDb(string file)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "bs-backup-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var src = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = file, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            using (var dst = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = tmp, Pooling = false }.ToString()))
            {
                src.Open(); dst.Open();
                src.BackupDatabase(dst);
            }
            return tmp;
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { try { File.Delete(tmp); } catch (IOException) { } return null; }
    }

    // ---- updating ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Nothing is installed without the user's say: the check runs by itself (at start and every few hours), a newer version is only offered
    /// in a question ("Install" / "Later"), and only the click on "Install" backs up the data, downloads and installs.
    /// </summary>
    string? _askedUpdate;   // the version that was offered in this run: "Later" is not asked again until the next start

    /// <summary>During the first start (setup and tour) no update is offered by itself: installing it would restart the program in the middle of the setup.</summary>
    bool UpdateMayAsk(bool manual) => manual || !_settings.TourPending;

    async Task CheckForUpdateAsync(bool manual)
    {
        if (_updating || !UpdateMayAsk(manual)) return;
        _updater ??= new Updater(_http);
        UpdateStatus.Text = L("Suche nach Updates …");
        UpdateInfo? info;
        try { info = await _updater.CheckAsync(AppPaths.Version, AppPaths.IsPortable); }
        catch (Exception ex) { UpdateStatus.Text = manual ? L("Update-Suche nicht möglich: ") + ex.Message : ""; return; }
        _settings.LastUpdateCheck = DateTime.Today.ToString("yyyy-MM-dd"); SaveSettings();
        if (info is null) { UpdateStatus.Text = L("Du hast die neueste Version ({0}).", AppPaths.Version); return; }
        UpdateStatus.Text = L("Neue Version {0} verfügbar.", info.Version);

        // a check that runs by itself asks once per version and never while the program sits in the tray
        if (!manual && (_askedUpdate == info.Version || !IsVisible)) return;
        _askedUpdate = info.Version;

        var plan = await PlanQuickUpdateAsync(info);
        var canInstall = plan is not null || (OperatingSystem.IsWindows() && (AppPaths.IsInstalled || AppPaths.IsPortable));   // the full package installs itself on Windows only; elsewhere the download page opens
        if (!canInstall)
        {
            var open = await AskAsync(L("Neue Version {0}", info.Version), L("BibleStudy {0} ist erschienen (du hast {1}). Auf diesem System lädst du das neue Paket selbst herunter. Soll die Download-Seite geöffnet werden?", info.Version, AppPaths.Version), L("Seite öffnen"), L("Später"));
            // a quick release has no packages: the last full release has them
            var downloads = info.DownloadUrl.Length > 0 ? info.PageUrl : $"https://github.com/{Updater.Repo}/releases/latest";
            if (open == 0) { try { Process.Start(new ProcessStartInfo(downloads) { UseShellExecute = true }); } catch { /* no browser */ } }
            return;
        }
        var question = L("BibleStudy {0} ist erschienen (du hast {1}). Möchtest du es jetzt installieren?\n\nVorher wird eine Sicherung deiner Daten angelegt, danach startet das Programm neu. Ohne deine Zustimmung wird nichts installiert.", info.Version, AppPaths.Version);
        if (plan is null)
        {
            if (await AskAsync(L("Neue Version {0}", info.Version), question, L("Installieren"), L("Später")) == 0) await InstallUpdateAsync(info);
            return;
        }
        question += "\n\n" + L("Es werden nur die geänderten Dateien geladen ({0:0.0} MB). Du kannst gleich weiterlesen, wo du warst, oder ungestört weiterarbeiten: Dann lädt es im Hintergrund und kommt beim nächsten Schließen drauf.", plan.DownloadBytes / 1048576.0);
        var answer = await AskAsync(L("Neue Version {0}", info.Version), question, L("Jetzt installieren"), L("Beim Schließen installieren"), L("Später"));
        if (answer is 0 or 1) await InstallUpdateAsync(info, plan, onClose: answer == 1);
    }

    /// <summary>
    /// What a quick update to this version would fetch; null when it is not possible (a release without a list of files, a copy that was not made
    /// from a package, a folder it may not write to, a quick update to this version that already failed). The full package is the way then.
    /// </summary>
    async Task<DeltaPlan?> PlanQuickUpdateAsync(UpdateInfo info)
    {
        if (info.ManifestUrl is not { } url) return null;
        if (DeltaUpdate.LastFailed(AppPaths.Root) is { } failed && failed.Version == info.Version) return null;
        var root = DeltaUpdate.RootOf(AppPaths.ProgramDir);
        if (DeltaUpdate.LoadInstalled(root) is not { } installed || installed.Rid != DeltaUpdate.Rid) return null;
        try
        {
            var target = await _updater!.GetManifestAsync(url);
            if (target.Rid != installed.Rid || target.Version != info.Version) return null;
            // hashing the program files takes a moment: not on the UI thread
            return await Task.Run(() => DeltaUpdate.CanWrite(root) ? DeltaUpdate.CreatePlan(target, installed, root, trustInstalled: OperatingSystem.IsMacOS()) : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidDataException or IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); return null; }
    }

    async Task InstallUpdateAsync(UpdateInfo info, DeltaPlan? plan = null, bool onClose = false)
    {
        _updating = true;
        try
        {
            UpdateStatus.Text = L("Sichere deine Daten …");
            var backup = await Task.Run(() => BackupData("vor-Update-" + info.Version));
            UpdateStatus.Text = L("Daten gesichert ({0}). Lade Update …", Path.GetFileName(backup));
            var progress = new Progress<double>(p => UpdateStatus.Text = L("Lade Update … {0:P0}", p));
            if (plan is not null)
            {
                try
                {
                    await DeltaUpdate.DownloadAsync(plan, _http, progress);
                    if (onClose)
                    {
                        // _updating stays on: no second offer while this one waits. The files are swapped when the program ends (Program.Main)
                        UpdateStatus.Text = L("Version {0} ist bereit und kommt beim nächsten Schließen des Programms drauf.", info.Version);
                        _trayUpdateText = UpdateStatus.Text; UpdateTrayUpdateItem();
                        StatusText.Text = UpdateStatus.Text;
                        return;
                    }
                    UpdateStatus.Text = L("Installiere – das Programm startet gleich neu …");
                    DeltaUpdate.RequestRestart(DeltaUpdate.RootOf(AppPaths.ProgramDir));
                    CaptureResume();
                    _closeAnimated = true;   // the blink: no goodbye
                    _exiting = true;
                    Close();   // the files are swapped when the program has ended (Program.Main)
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    ErrorLog.Write(ex);   // nothing of the program has changed yet: on to the full package
                    if (!OperatingSystem.IsWindows() || onClose) throw;   // in the background nothing big is fetched unasked
                }
            }
            if (info.DownloadUrl.Length == 0)
                info = await _updater!.FindFullAsync(AppPaths.Version, AppPaths.IsPortable) ?? throw new InvalidOperationException(L("Für diese Version gibt es kein vollständiges Paket. Lade es bitte von der Download-Seite."));
            var file = await _updater!.DownloadAsync(info, progress);
            UpdateStatus.Text = L("Installiere – das Programm startet gleich neu …");
            _settings.PendingUpdateFile = file; _settings.PendingUpdateVersion = info.Version; SaveSettings();
            if (!UpdateApply.TryApply(_settings, launch: true)) throw new InvalidOperationException(L("Der Installer konnte nicht gestartet werden."));
            _exiting = true;
            Close();
        }
        catch (Exception ex)
        {
            _updating = false;
            UpdateStatus.Text = L("Update fehlgeschlagen, deine Daten sind unverändert: ") + ex.Message;
        }
    }

    /// <summary>Ends the program and starts it again a moment later (this one must have ended first: both use the same files); it opens on the settings page.</summary>
    void RestartProgram()
    {
        var exe = Environment.ProcessPath;
        if (exe is null || Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) { UpdateStatus.Text = L("Neustart nicht möglich: Das Programm wurde nicht über seine eigene Startdatei gestartet."); return; }
        try
        {
            Process.Start(OperatingSystem.IsWindows()
                ? new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & start \"\" \"{exe}\" --open-settings") { CreateNoWindow = true, UseShellExecute = false }
                : new ProcessStartInfo("/bin/sh", $"-c \"sleep 2; exec '{exe.Replace("'", "'\\''")}' --open-settings\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { UpdateStatus.Text = L("Neustart nicht möglich: ") + ex.Message; return; }
        _exiting = true; Close();
    }

    // ---- removing the program --------------------------------------------------------------------------------------------------

    async Task RemoveProgramAsync()
    {
        if (AppPaths.IsInstalled)
        {
            // the uninstaller asks about the data itself
            Process.Start(new ProcessStartInfo(Path.Combine(AppPaths.ProgramDir, "BibleStudy.App.exe"), "--uninstall") { UseShellExecute = true });
            _exiting = true; Close();
            return;
        }
        if (!AppPaths.IsPortable) return;

        var dir = AppPaths.ProgramDir;
        // safety: only ever delete a folder that really is a portable BibleStudy folder
        var looksRight = File.Exists(Path.Combine(dir, "BibleStudy.App.exe")) && File.Exists(Path.Combine(dir, "portable.flag")) && dir.Length > 12;
        if (!looksRight) { UpdateStatus.Text = L("Dieser Ordner sieht nicht wie eine Portable-Version aus – nichts wurde gelöscht."); return; }

        var keep = await AskAsync(L("Portable-Version entfernen"), L("Der Ordner „{0}“ mit dem Programm wird gelöscht.\n\nWas soll mit deiner Arbeit passieren (Vaults, Notizen, Dokumente, Markierungen, Einstellungen)?", dir), L("Daten behalten"), L("Daten löschen"), L("Abbrechen"));
        if (keep is not (0 or 1)) return;
        string? kept = null;
        if (keep == 0)
        {
            kept = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"BibleStudy-Daten-{DateTime.Now:yyyy-MM-dd_HHmm}");
            try { await Task.Run(() => CopyData(AppPaths.Root, kept)); }
            catch (Exception ex) { UpdateStatus.Text = L("Die Daten konnten nicht gesichert werden, es wurde nichts gelöscht: ") + ex.Message; return; }
        }
        else if (await AskAsync(L("Wirklich alles löschen?"), L("Deine Vaults, Notizen, Dokumente und Einstellungen werden unwiderruflich gelöscht."), L("Doch behalten"), L("Alles löschen")) != 1) return;

        RemoveOwnStartMenuEntry(dir);
        var cmd = $"/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"{dir}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
        if (kept is not null) { try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{kept}\"")); } catch { /* optional */ } }
        _exiting = true; Close();
    }

    static void CopyData(string from, string to)
    {
        if (!Directory.Exists(from)) { Directory.CreateDirectory(to); return; }
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(from, file);
            if (CacheFolders.Contains(rel.Split(Path.DirectorySeparatorChar)[0])) continue;
            var target = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = File.Create(target);
            src.CopyTo(dst);
        }
    }
}
