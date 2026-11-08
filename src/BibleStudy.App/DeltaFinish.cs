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
        try
        {
            // a version installed when the program closed was never seen with a window: when it keeps failing to show one, it is taken back here
            if (DeltaUpdate.TrialFailed(Root, AppPaths.Version))
            {
                var exe = Environment.ProcessPath; var failed = AppPaths.Version;
                DeltaUpdate.Rollback(Root);
                DeltaUpdate.NoteFailed(AppPaths.Root, failed, $"no window after {DeltaUpdate.TrialStarts} starts");
                if (exe is not null && StartProgram(exe) is not null) Environment.Exit(0);
                return;
            }
            DeltaUpdate.CleanUp(Root, AppPaths.Version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }

    /// <summary>The window of this version is up: the old process waiting for that may end. The old files are deleted once it has.</summary>
    public static void Confirm(bool trialOnly = false)
    {
        try
        {
            if (!DeltaUpdate.ConfirmStarted(Root, AppPaths.Version)) return;
            if (trialOnly) { DeltaUpdate.StartTrial(Root); }   // nobody sees a window now: the proof comes at the first real start
            else DeltaUpdate.ClearTrial(Root);
            if (OperatingSystem.IsWindows() && AppPaths.IsInstalled)
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\BibleStudy", writable: true);
                key?.SetValue("DisplayVersion", AppPaths.Version);
            }
            if (trialOnly) return;
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
        var restart = DeltaUpdate.RestartRequested(root);
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
            if (restart) StartProgram(exe);
            return true;
        }
        string reason;
        if (restart ? !StartAndWait(exe, root, out reason) : !CheckWithoutWindow(exe, root, out reason))
        {
            try { DeltaUpdate.Rollback(root); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
            DeltaUpdate.NoteFailed(AppPaths.Root, version, reason);
            if (restart) StartProgram(exe);
        }
        return true;
    }

    /// <summary>
    /// "Install when closing": nobody is there to see a window, and at the next start the old process that could take the update back is gone. So the new
    /// version is tried now, without a window: it loads its parts and reports back (<see cref="CheckAndExit"/>).
    /// </summary>
    static bool CheckWithoutWindow(string exe, string root, out string reason)
    {
        try
        {
            using var check = Process.Start(new ProcessStartInfo(exe, CheckArgument) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
            if (check is null) { reason = "no process"; return false; }
            if (!check.WaitForExit(60_000)) { check.Kill(); reason = "check timed out"; return false; }
            reason = $"check: exit code {check.ExitCode}";
            return check.ExitCode == 0 && DeltaUpdate.IsConfirmed(root);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { reason = ex.Message; return false; }
    }

    public const string CheckArgument = "--update-check";

    /// <summary>Runs in the new version, without a window: its own parts and what they use directly must load. Returns the exit code; not 0 sends the old files back.</summary>
    public static int CheckAndExit()
    {
        try
        {
            foreach (var part in new[] { typeof(DeltaFinish).Assembly, typeof(AppPaths).Assembly })
                foreach (var name in part.GetReferencedAssemblies()) System.Reflection.Assembly.Load(name);
            _ = Program.BuildAvaloniaApp();
            Confirm(trialOnly: true);
            return 0;
        }
        catch (Exception ex) { ErrorLog.Write(ex); return 1; }
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
