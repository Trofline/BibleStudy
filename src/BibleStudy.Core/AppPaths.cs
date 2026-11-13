using System.Reflection;

namespace BibleStudy.Core;

/// <summary>
/// Where the program keeps the user's data. Installed: %LOCALAPPDATA%\BibleStudy (outside the program folder, so updates and
/// reinstalls never touch it). Portable: the "Data" folder next to the program, switched on by the file "portable.flag".
/// </summary>
public static class AppPaths
{
    public static string ProgramDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool IsPortable => File.Exists(Path.Combine(ProgramDir, "portable.flag"));

    /// <summary>True when the program was set up by the installer (it wrote "installed.flag" next to the program).</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(ProgramDir, "installed.flag"));

    public static string Root => IsPortable
        ? Path.Combine(ProgramDir, "Data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BibleStudy");

    /// <summary>Default place for backups the program makes on its own (before updates).</summary>
    /// <summary>The folder of the backups (the variable BIBLESTUDY_BACKUPDIR moves it: automated tests must not write into the real one).</summary>
    public static string BackupDir => Environment.GetEnvironmentVariable("BIBLESTUDY_BACKUPDIR") is { Length: > 0 } d ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BibleStudy-Backup");

    public static string Version
    {
        get
        {
            var app = Assembly.GetEntryAssembly() ?? AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "BibleStudy.App");
            var v = app?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }
}
