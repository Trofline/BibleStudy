using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>Scheduled backups: while the program runs it checks hourly whether a backup is due and writes a ZIP of all data into the chosen folder.</summary>
public partial class MainWindow
{
    const int AutoBackupsKept = 10;
    bool _backingUp;

    string BackupFolder => _settings.BackupFolder is { Length: > 0 } f ? f : AppPaths.BackupDir;

    static TimeSpan Interval(string schedule) => schedule switch { "daily" => TimeSpan.FromDays(1), "weekly" => TimeSpan.FromDays(7), "monthly" => TimeSpan.FromDays(30), _ => TimeSpan.Zero };

    void InitBackup()
    {
        foreach (var item in BackupScheduleBox.Items.OfType<ComboBoxItem>())
            if ((string?)item.Tag == _settings.BackupSchedule) BackupScheduleBox.SelectedItem = item;
        if (BackupScheduleBox.SelectedItem is null) BackupScheduleBox.SelectedIndex = 0;
        BackupScheduleBox.SelectionChanged += (_, _) =>
        {
            if ((BackupScheduleBox.SelectedItem as ComboBoxItem)?.Tag is not string code) return;
            _settings.BackupSchedule = code; SaveSettings();
            ShowBackupState();
            if (code != "off") _ = AutoBackupIfDueAsync();
        };
        BackupFolderBtn.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L("Sicherungsordner wählen …") });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } p) return;
            _settings.BackupFolder = p; SaveSettings(); ShowBackupState();
        };
        BackupNowBtn.Click += async (_, _) => await RunBackupAsync(manual: true);
        ShowBackupState();
        // a while after the start (the reader comes first), then every hour
        DispatcherTimer.RunOnce(async () => await AutoBackupIfDueAsync(), TimeSpan.FromSeconds(25));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        timer.Tick += async (_, _) => await AutoBackupIfDueAsync();
        timer.Start();
    }

    void ShowBackupState()
    {
        BackupFolderText.Text = L("Sicherungsordner: {0}", BackupFolder);
        AutoBackupInfo.Text = DateTime.TryParse(_settings.LastAutoBackup, out var last) ? L("Letzte Sicherung: {0:dd.MM.yyyy HH:mm}", last) : L("Noch keine Sicherung gemacht.");
    }

    async Task AutoBackupIfDueAsync()
    {
        var every = Interval(_settings.BackupSchedule);
        if (every == TimeSpan.Zero) return;
        if (DateTime.TryParse(_settings.LastAutoBackup, out var last) && DateTime.Now - last < every) return;
        await RunBackupAsync(manual: false);
    }

    async Task RunBackupAsync(bool manual)
    {
        if (_backingUp) return;
        _backingUp = true;
        BackupNowBtn.IsEnabled = false;
        AutoBackupInfo.Text = L("Sichere deine Daten …");
        try
        {
            var folder = BackupFolder;
            var path = await Task.Run(() => BackupData("Auto", folder));
            _settings.LastAutoBackup = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); SaveSettings();
            await Task.Run(() => PruneAutoBackups(folder));
            ShowBackupState();
            if (manual) AutoBackupInfo.Text += "  " + path;
            else StatusText.Text = L("Automatische Sicherung gespeichert: {0}", path);
        }
        catch (Exception ex) { AutoBackupInfo.Text = L("Sicherung fehlgeschlagen") + ": " + ex.Message; }
        finally { _backingUp = false; BackupNowBtn.IsEnabled = true; }
    }

    /// <summary>Only the newest automatic backups stay; manual ones and the ones made before updates are never touched.</summary>
    static void PruneAutoBackups(string folder)
    {
        var old = new DirectoryInfo(folder).EnumerateFiles("BibleStudy-Daten-Auto-*.zip").OrderByDescending(f => f.LastWriteTimeUtc).Skip(AutoBackupsKept);
        foreach (var f in old) { try { f.Delete(); } catch { /* in use: next time */ } }
    }
}
