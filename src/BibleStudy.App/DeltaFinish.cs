using System.Diagnostics;
using BibleStudy.Core;
using Microsoft.Win32;

namespace BibleStudy.App;

/// <summary>
/// The part of a quick update that runs when the program ends: swaps the files, starts the new version and waits for it to report that its window is up.
/// If it does not, the old files go back and the old version starts again, with a note so that the next attempt takes the full package.
/// </summary>
static class DeltaFinish
{
    const int WaitSeconds = 180;

    static string Root => DeltaUpdate.RootOf(AppPaths.ProgramDir);

    /// <summary>At the very start of the program, before anything is loaded from the program folder.</summary>
    public static void AtStart()
    {
        try { DeltaUpdate.CleanUp(Root, AppPaths.Version); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }

    /// <summary>The window of this version is up: the old process waiting for that may end. The old files are deleted once it has.</summary>
    public static void Confirm()
    {
        try
        {
            if (!DeltaUpdate.ConfirmStarted(Root, AppPaths.Version)) return;
            if (OperatingSystem.IsWindows() && AppPaths.IsInstalled)
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\BibleStudy", writable: true);
                key?.SetValue("DisplayVersion", AppPaths.Version);
            }
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
                try { DeltaUpdate.PurgeOld(Root); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }

    /// <summary>Installs a waiting update when the program has ended. Returns false when there is none (the program then just ends).</summary>
    public static bool Run()
    {
        var root = Root;
        if (!DeltaUpdate.IsReady(root)) return false;
        var exe = Environment.ProcessPath;   // read now: after the swap this process runs from a renamed file
        if (exe is null) return false;
        Process.GetCurrentProcess().Dispose();   // what is needed after the swap is loaded before it: those files may be replaced
        var version = "?";
        try
        {
            version = DeltaUpdate.WaitingVersion(root);
            var replaced = DeltaUpdate.Swap(root);
            if (OperatingSystem.IsMacOS()) SignAgain(root, replaced);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            DeltaUpdate.Rollback(root);
            DeltaUpdate.NoteFailed(AppPaths.Root, version, ex.Message);
            StartProgram(exe);
            return true;
        }
        if (!StartAndWait(exe, root, out var reason))
        {
            try { DeltaUpdate.Rollback(root); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
            DeltaUpdate.NoteFailed(AppPaths.Root, version, reason);
            StartProgram(exe);
        }
        return true;
    }

    static bool StartAndWait(string exe, string root, out string reason)
    {
        Process? process;
        try { process = StartProgram(exe); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { reason = ex.Message; return false; }
        if (process is null) { reason = "no process"; return false; }
        var until = DateTime.UtcNow.AddSeconds(WaitSeconds);
        while (DateTime.UtcNow < until)
        {
            if (DeltaUpdate.IsConfirmed(root)) { reason = ""; return true; }
            if (process.HasExited)
            {
                if (DeltaUpdate.IsConfirmed(root)) { reason = ""; return true; }
                reason = $"exit code {process.ExitCode}";
                return false;
            }
            Thread.Sleep(250);
        }
        reason = $"no window after {WaitSeconds} s";
        try { process.Kill(); process.WaitForExit(5000); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return false;
    }

    static Process? StartProgram(string exe)
    {
        try { return Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { ErrorLog.Write(ex); return null; }
    }

    /// <summary>A Mac refuses program files that are not signed: the replaced ones get a local signature (like the set-up script does), then the app itself.</summary>
    static void SignAgain(string contents, IReadOnlyList<string> replaced)
    {
        foreach (var file in replaced.Where(f => f.EndsWith(".dylib") || f is "MacOS/BibleStudy" or "MacOS/BibleStudy.Mcp" or "MacOS/createdump" or "MacOS/mcp/BibleStudy.Mcp" or "MacOS/mcp/createdump"))
            RunCodesign("--force", "--sign", "-", Path.Combine(contents, file));
        RunCodesign("--force", "--sign", "-", Path.GetDirectoryName(contents)!);
    }

    static void RunCodesign(params string[] args)
    {
        var info = new ProcessStartInfo("/usr/bin/codesign") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info);
        p?.WaitForExit(120_000);
    }
}
