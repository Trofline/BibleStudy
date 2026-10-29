using Avalonia.Threading;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// A mistake in one button or view must not close the whole program (and lose what is open): unexpected errors are written to
/// errors.log next to the settings, shown in the status bar, and the program goes on.
/// </summary>
static class ErrorLog
{
    public static string Path => System.IO.Path.Combine(AppPaths.Root, "errors.log");

    /// <summary>Shows the message to the user (set by the main window).</summary>
    public static Action<string>? Show;

    public static void Install()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) => { Write(e.Exception); e.Handled = true; Tell(e.Exception); };
        TaskScheduler.UnobservedTaskException += (_, e) => { Write(e.Exception); e.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Write(ex); };
    }

    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var info = new FileInfo(Path);
            if (info.Exists && info.Length > 1_000_000) info.Delete();   // keep it small
            File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  BibleStudy {AppPaths.Version}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* logging must never fail the program */ }
    }

    /// <summary>The newest entry of the log (shortened), for a bug report; null when there was no error.</summary>
    public static string? Last(int max = 1500)
    {
        try
        {
            if (!File.Exists(Path)) return null;
            var all = File.ReadAllText(Path).TrimEnd();
            var cut = all.LastIndexOf(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal);
            var last = cut < 0 ? all : all[(cut + Environment.NewLine.Length * 2)..];
            // no user name in a public report
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 3) last = last.Replace(home, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            return last.Length > max ? last[..max] + " …" : last;
        }
        catch { return null; }
    }

    static void Tell(Exception ex)
    {
        try { Show?.Invoke(Loc.L("Etwas ist schiefgegangen, das Programm läuft weiter: {0}", ex.GetBaseException().Message)); } catch { /* nothing to show it in */ }
    }
}
