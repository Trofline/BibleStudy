using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using BibleStudy.Core;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Applies a downloaded update without asking: at the start of the program (before any window), when the program is closed, or
/// when it has been sitting in the tray. The user's data is never touched (it was backed up before the download).
/// </summary>
static class UpdateApply
{
    public static string SettingsPath => Path.Combine(AppPaths.Root, "settings.json");

    public static bool HasPending(AppSettings st) =>
        st.PendingUpdateFile is { Length: > 0 } f && File.Exists(f) && Updater.Parse(st.PendingUpdateVersion ?? "") is { } v && Updater.Parse(AppPaths.Version) is { } cur && v > cur
        && OperatingSystem.IsWindows() && (AppPaths.IsInstalled || AppPaths.IsPortable);

    /// <summary>Starts the installer (or the portable copy script) for the downloaded update; true when it was started.
    /// <paramref name="launch"/>: start the program again afterwards.</summary>
    public static bool TryApply(AppSettings st, bool launch)
    {
        if (!HasPending(st)) return false;
        var file = st.PendingUpdateFile!;
        st.PendingUpdateFile = null; st.PendingUpdateVersion = null;   // cleared first: a failing install must not loop
        try { st.Save(SettingsPath); } catch { /* settings are a convenience */ }
        try
        {
            if (AppPaths.IsPortable) StartPortableUpdate(file, launch);
            else Process.Start(new ProcessStartInfo(file, $"--update --silent {(launch ? "" : "--no-launch ")}--wait-pid {Environment.ProcessId}") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>Portable: unpack the zip, then a small script copies the new files over the program folder (never the Data folder).</summary>
    public static void StartPortableUpdate(string zipFile, bool launch)
    {
        var staging = Path.Combine(Path.GetTempPath(), "BibleStudy-Portable-Update");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        ZipFile.ExtractToDirectory(zipFile, staging);
        // the portable zip holds the program in a folder "BibleStudy": copy that folder's content, not the folder itself
        // (otherwise the update lands in ProgramDir\BibleStudy and the old program stays)
        var source = File.Exists(Path.Combine(staging, "BibleStudy.App.exe")) ? staging
            : Directory.GetDirectories(staging).FirstOrDefault(d => File.Exists(Path.Combine(d, "BibleStudy.App.exe")))
              ?? throw new InvalidDataException("BibleStudy.App.exe");
        var exe = Path.Combine(AppPaths.ProgramDir, "BibleStudy.App.exe");
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";   // a quote in a folder name must not break the script
        var script = Path.Combine(Path.GetTempPath(), "BibleStudy-Portable-Update.ps1");
        File.WriteAllText(script, $$"""
            $ErrorActionPreference = 'Stop'
            Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
            Get-ChildItem -LiteralPath {{Q(source)}} -Force | Where-Object { $_.Name -notin @('Data', 'portable.flag') } |
                ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination {{Q(AppPaths.ProgramDir)}} -Recurse -Force }
            {{(launch ? $"Start-Process -FilePath {Q(exe)}" : "")}}
            """, Encoding.UTF8);
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"") { UseShellExecute = false, CreateNoWindow = true });
    }
}
