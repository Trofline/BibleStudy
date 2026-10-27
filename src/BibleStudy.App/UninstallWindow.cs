using System.Diagnostics;
using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using Microsoft.Win32;

namespace BibleStudy.App;

/// <summary>
/// The uninstaller (BibleStudy.App.exe --uninstall). It always asks whether the user's own work (vaults, notes, documents,
/// settings) should be kept. Silent test mode: --silent with --keep-data or --delete-data.
/// </summary>
sealed class UninstallWindow : Window
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BibleStudy";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly StackPanel _body = new() { Margin = new(24), Spacing = 12 };
    readonly string _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BibleStudy");

    public UninstallWindow(string[] args)
    {
        Title = L("BibleStudy deinstallieren"); Width = 560; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = _body;
        if (args.Contains("--silent")) { Opened += (_, _) => Finish(args.Contains("--delete-data")); return; }
        Opened += (_, _) => StepWelcome();
    }

    static bool Running() => Process.GetProcessesByName("BibleStudy.App").Any(p => p.Id != Environment.ProcessId);

    void Show(string title, string text, params (string Label, Action Click)[] buttons)
    {
        _body.Children.Clear();
        _body.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold });
        _body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 0) };
        foreach (var (label, click) in buttons) { var b = new Button { Content = label, Margin = new(8, 0, 0, 0) }; b.Click += (_, _) => click(); row.Children.Add(b); }
        _body.Children.Add(row);
    }

    void StepWelcome()
    {
        if (Running()) { Show("BibleStudy läuft noch", "Bitte schließe BibleStudy (auch im Infobereich neben der Uhr: Rechtsklick → Beenden) und starte die Deinstallation dann noch einmal.", ("OK", Close)); return; }
        Show("BibleStudy deinstallieren", $"Das Programm wird aus „{AppPaths.ProgramDir}“ entfernt, ebenso die Verknüpfungen.\n\nIm nächsten Schritt entscheidest du, was mit deiner eigenen Arbeit passiert.", ("Weiter", StepData), ("Abbrechen", Close));
    }

    void StepData() => Show("Deine Daten behalten?",
        $"Deine Vaults, Notizen, Dokumente, Markierungen, Leselisten, Gebete und Einstellungen liegen in:\n{_dataDir}\n\nBehältst du sie, findet eine spätere Installation alles wieder vor.",
        ("Daten behalten (empfohlen)", () => Finish(false)), ("Daten sichern und löschen", () => ConfirmDelete(backup: true)), ("Daten löschen", () => ConfirmDelete(backup: false)), ("Abbrechen", Close));

    void ConfirmDelete(bool backup) => Show("Wirklich löschen?",
        backup ? "Deine Daten werden zuerst als ZIP in Dokumente\\BibleStudy-Backup gesichert und dann gelöscht."
               : "Deine Daten werden unwiderruflich gelöscht. Es gibt keine Sicherung.",
        ("Zurück", StepData), (backup ? "Sichern und löschen" : "Endgültig löschen", () =>
        {
            if (backup) { try { BackupAll(); } catch (Exception ex) { Show("Sicherung fehlgeschlagen", "Es wurde nichts gelöscht: " + ex.Message, ("OK", Close)); return; } }
            Finish(true);
        }));

    void BackupAll()
    {
        Directory.CreateDirectory(AppPaths.BackupDir);
        var path = Path.Combine(AppPaths.BackupDir, $"BibleStudy-Daten-vor-Deinstallation-{DateTime.Now:yyyy-MM-dd_HHmm}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        if (!Directory.Exists(_dataDir)) return;
        foreach (var file in Directory.EnumerateFiles(_dataDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(_dataDir, file);
            if (rel.StartsWith("cache") || rel.StartsWith("arc-cache")) continue;
            using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = zip.CreateEntry(rel, CompressionLevel.Optimal).Open();
            src.CopyTo(dst);
        }
    }

    void Finish(bool deleteData)
    {
        try
        {
            foreach (var lnk in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BibleStudy.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BibleStudy.lnk"),
            }) if (File.Exists(lnk)) File.Delete(lnk);
            if (OperatingSystem.IsWindows())
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, true);
                run?.DeleteValue("BibleStudy", false);
            }
        }
        catch { /* leftovers are harmless */ }

        // The program folder cannot be deleted while this very program runs: a helper deletes it a moment after we exit.
        var dir = AppPaths.ProgramDir;
        var safe = File.Exists(Path.Combine(dir, "installed.flag")) && File.Exists(Path.Combine(dir, "BibleStudy.App.exe")) && dir.Length > 12;
        var cmd = new List<string> { "ping 127.0.0.1 -n 4 >nul" };
        if (safe) cmd.Add($"rmdir /s /q \"{dir}\"");
        if (deleteData && Directory.Exists(_dataDir) && _dataDir.EndsWith("BibleStudy")) cmd.Add($"rmdir /s /q \"{_dataDir}\"");
        Process.Start(new ProcessStartInfo("cmd.exe", "/c " + string.Join(" & ", cmd)) { CreateNoWindow = true, UseShellExecute = false });

        if (Environment.GetCommandLineArgs().Contains("--silent")) { Close(); return; }
        Show("BibleStudy wurde entfernt", deleteData ? "Das Programm und deine Daten wurden gelöscht." : $"Das Programm wurde entfernt. Deine Daten bleiben erhalten:\n{_dataDir}", ("Fertig", Close));
    }
}
