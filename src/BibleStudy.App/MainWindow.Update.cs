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
        VersionText.Text = $"Version {AppPaths.Version} · " + (AppPaths.IsPortable ? $"Portable (Daten in {AppPaths.Root})" : AppPaths.IsInstalled ? "installiert" : "Entwicklungsversion") + $" · Daten: {AppPaths.Root}";
        AutoUpdateCheck.IsChecked = _settings.AutoUpdate;
        AutoUpdateCheck.IsCheckedChanged += (_, _) => { _settings.AutoUpdate = AutoUpdateCheck.IsChecked == true; SaveSettings(); };
        CheckUpdateBtn.Click += async (_, _) => await CheckForUpdateAsync(manual: true);
        RemoveAppBtn.IsVisible = AppPaths.IsPortable || AppPaths.IsInstalled;
        RemoveAppBtn.Content = AppPaths.IsPortable ? "Portable-Version entfernen …" : "Deinstallieren …";
        RemoveAppBtn.Click += async (_, _) => await RemoveProgramAsync();
        // once per start, a few seconds after launch so that the reader is up first
        if (_settings.AutoUpdate)
            DispatcherTimer.RunOnce(async () => await CheckForUpdateAsync(manual: false), TimeSpan.FromSeconds(10));
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
    static string BackupData(string label)
    {
        Directory.CreateDirectory(AppPaths.BackupDir);
        var path = Path.Combine(AppPaths.BackupDir, $"BibleStudy-Daten-{label}-{DateTime.Now:yyyy-MM-dd_HHmm}.zip");
        var root = AppPaths.Root;
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        if (Directory.Exists(root))
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file);
                if (CacheFolders.Contains(rel.Split(Path.DirectorySeparatorChar)[0])) continue;
                try
                {
                    // the database may be open right now: read it without asking for exclusive access
                    using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var dst = zip.CreateEntry(rel, CompressionLevel.Optimal).Open();
                    src.CopyTo(dst);
                }
                catch (IOException) { /* a temporary file that vanished meanwhile */ }
            }
        return path;
    }

    // ---- updating ------------------------------------------------------------------------------------------------------------

    async Task CheckForUpdateAsync(bool manual)
    {
        if (_updating) return;
        _updater ??= new Updater(_http);
        UpdateStatus.Text = "Suche nach Updates …";
        UpdateInfo? info;
        try { info = await _updater.CheckAsync(AppPaths.Version, AppPaths.IsPortable); }
        catch (Exception ex) { UpdateStatus.Text = manual ? "Update-Suche nicht möglich: " + ex.Message : ""; return; }
        _settings.LastUpdateCheck = DateTime.Today.ToString("yyyy-MM-dd"); SaveSettings();
        if (info is null) { UpdateStatus.Text = manual ? $"Du hast die neueste Version ({AppPaths.Version})." : ""; return; }
        if (!manual && _settings.SkippedVersion == info.Version) { UpdateStatus.Text = ""; return; }
        UpdateStatus.Text = $"Neue Version {info.Version} verfügbar.";

        var canInstall = AppPaths.IsInstalled || AppPaths.IsPortable;
        var notes = info.Notes.Length > 600 ? info.Notes[..600] + " …" : info.Notes;
        var text = $"Version {info.Version} ist da (du hast {AppPaths.Version}).\n\n{(notes.Length > 0 ? notes + "\n\n" : "")}" +
                   (canInstall ? "Beim Installieren sichert das Programm zuerst deine Daten, lädt das Update, installiert es und startet neu. Deine Vaults, Notizen und Einstellungen bleiben unverändert."
                               : "Diese Entwicklungsversion aktualisierst du selbst; die Seite mit dem Download öffnet sich.");
        var choice = await AskAsync("Update verfügbar", text, canInstall ? "Jetzt installieren" : "Seite öffnen", "Später", "Diese Version überspringen");
        if (choice == 2) { _settings.SkippedVersion = info.Version; SaveSettings(); return; }
        if (choice != 0) return;
        if (!canInstall) { try { Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true }); } catch { /* no browser */ } return; }
        await InstallUpdateAsync(info);
    }

    async Task InstallUpdateAsync(UpdateInfo info)
    {
        _updating = true;
        try
        {
            UpdateStatus.Text = "Sichere deine Daten …";
            var backup = await Task.Run(() => BackupData("vor-Update-" + info.Version));
            UpdateStatus.Text = $"Daten gesichert ({Path.GetFileName(backup)}). Lade Update …";
            var progress = new Progress<double>(p => UpdateStatus.Text = $"Lade Update … {p:P0}");
            var file = await _updater!.DownloadAsync(info, progress);
            UpdateStatus.Text = "Installiere – das Programm startet gleich neu …";

            if (AppPaths.IsPortable) StartPortableUpdate(file);
            else Process.Start(new ProcessStartInfo(file, $"--update --silent --wait-pid {Environment.ProcessId}") { UseShellExecute = true });
            _exiting = true;
            Close();
        }
        catch (Exception ex)
        {
            _updating = false;
            UpdateStatus.Text = "Update fehlgeschlagen, deine Daten sind unverändert: " + ex.Message;
        }
    }

    /// <summary>Portable: unpack the zip, then a small script copies the new files over the program folder (never the Data folder).</summary>
    static void StartPortableUpdate(string zipFile)
    {
        var staging = Path.Combine(Path.GetTempPath(), "BibleStudy-Portable-Update");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        ZipFile.ExtractToDirectory(zipFile, staging);
        var exe = Path.Combine(AppPaths.ProgramDir, "BibleStudy.App.exe");
        var script = Path.Combine(Path.GetTempPath(), "BibleStudy-Portable-Update.ps1");
        File.WriteAllText(script, $$"""
            $ErrorActionPreference = 'Stop'
            Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
            Get-ChildItem -LiteralPath '{{staging}}' -Force | Where-Object { $_.Name -notin @('Data', 'portable.flag') } |
                ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination '{{AppPaths.ProgramDir}}' -Recurse -Force }
            Start-Process -FilePath '{{exe}}'
            """, Encoding.UTF8);
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"") { UseShellExecute = false, CreateNoWindow = true });
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
        if (!looksRight) { UpdateStatus.Text = "Dieser Ordner sieht nicht wie eine Portable-Version aus – nichts wurde gelöscht."; return; }

        var keep = await AskAsync("Portable-Version entfernen", $"Der Ordner „{dir}“ mit dem Programm wird gelöscht.\n\nWas soll mit deiner Arbeit passieren (Vaults, Notizen, Dokumente, Markierungen, Einstellungen)?", "Daten behalten", "Daten löschen", "Abbrechen");
        if (keep is not (0 or 1)) return;
        string? kept = null;
        if (keep == 0)
        {
            kept = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"BibleStudy-Daten-{DateTime.Now:yyyy-MM-dd_HHmm}");
            try { await Task.Run(() => CopyData(AppPaths.Root, kept)); }
            catch (Exception ex) { UpdateStatus.Text = "Die Daten konnten nicht gesichert werden, es wurde nichts gelöscht: " + ex.Message; return; }
        }
        else if (await AskAsync("Wirklich alles löschen?", "Deine Vaults, Notizen, Dokumente und Einstellungen werden unwiderruflich gelöscht.", "Doch behalten", "Alles löschen") != 1) return;

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
