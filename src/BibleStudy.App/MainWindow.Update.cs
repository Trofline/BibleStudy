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
        InitBackup();
        InitFeedback();
        InitPlugins();
        InitNotes();
        InitSources();
        InitHiddenXrefs();
        // once per start, a few seconds after launch so that the reader is up first
        if (_settings.AutoUpdate)
        {
            DispatcherTimer.RunOnce(async () => await CheckForUpdateAsync(manual: false), TimeSpan.FromSeconds(10));
            // programs that stay open for days (tray) look again now and then; a program hidden in the tray applies a waiting update
            var again = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            again.Tick += async (_, _) => await CheckForUpdateAsync(manual: false);
            again.Start();
            var tray = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            tray.Tick += (_, _) => { if (!IsVisible && UpdateApply.HasPending(_settings) && ApplyPendingUpdate(launch: true)) { _exiting = true; Close(); } };
            tray.Start();
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
            b.Click += (_, _) => { result = k; win.Close(); };
            row.Children.Add(b);
        }
        win.Content = new StackPanel { Margin = new(22), Children = { new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 10) }, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, row } };
        await win.ShowDialog(this);
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
    /// Updating needs no question: the check runs by itself (at start and every few hours), a newer version is downloaded in the background
    /// after the data was backed up, and installed at the next exit or start (see <see cref="UpdateApply"/>).
    /// The "Check now" button installs at once and restarts.
    /// </summary>
    async Task CheckForUpdateAsync(bool manual)
    {
        if (_updating) return;
        _updater ??= new Updater(_http);
        if (!manual && UpdateApply.HasPending(_settings)) { UpdateStatus.Text = L("Update {0} ist geladen und wird beim nächsten Beenden oder Start installiert.", _settings.PendingUpdateVersion); return; }
        UpdateStatus.Text = L("Suche nach Updates …");
        UpdateInfo? info;
        try { info = await _updater.CheckAsync(AppPaths.Version, AppPaths.IsPortable); }
        catch (Exception ex) { UpdateStatus.Text = manual ? L("Update-Suche nicht möglich: ") + ex.Message : ""; return; }
        _settings.LastUpdateCheck = DateTime.Today.ToString("yyyy-MM-dd"); SaveSettings();
        if (info is null) { UpdateStatus.Text = manual ? L("Du hast die neueste Version ({0}).", AppPaths.Version) : ""; return; }
        UpdateStatus.Text = L("Neue Version {0} verfügbar.", info.Version);

        var canInstall = OperatingSystem.IsWindows() && (AppPaths.IsInstalled || AppPaths.IsPortable);   // the self-installing update is Windows only; elsewhere the download page opens
        if (!canInstall)
        {
            if (manual && !OperatingSystem.IsWindows()) { try { Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true }); } catch { /* no browser */ } }
            return;
        }
        if (manual) { await InstallUpdateAsync(info); return; }
        await DownloadUpdateInBackgroundAsync(info);
    }

    async Task DownloadUpdateInBackgroundAsync(UpdateInfo info)
    {
        _updating = true;
        try
        {
            await Task.Run(() => BackupData("vor-Update-" + info.Version));
            var progress = new Progress<double>(p => UpdateStatus.Text = L("Lade Update {0} … {1:P0}", info.Version, p));
            var file = await _updater!.DownloadAsync(info, progress);
            _settings.PendingUpdateFile = file; _settings.PendingUpdateVersion = info.Version; SaveSettings();
            UpdateStatus.Text = L("Update {0} ist geladen und wird beim nächsten Beenden oder Start installiert.", info.Version);
        }
        catch (Exception ex) { UpdateStatus.Text = L("Update fehlgeschlagen, deine Daten sind unverändert: ") + ex.Message; }
        finally { _updating = false; }
    }

    async Task InstallUpdateAsync(UpdateInfo info)
    {
        _updating = true;
        try
        {
            UpdateStatus.Text = L("Sichere deine Daten …");
            var backup = await Task.Run(() => BackupData("vor-Update-" + info.Version));
            UpdateStatus.Text = L("Daten gesichert ({0}). Lade Update …", Path.GetFileName(backup));
            var progress = new Progress<double>(p => UpdateStatus.Text = L("Lade Update … {0:P0}", p));
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

    /// <summary>Called when the program ends or has been sitting in the tray: installs a downloaded update. Returns true when it started.</summary>
    bool ApplyPendingUpdate(bool launch) => UpdateApply.TryApply(_settings, launch);

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
