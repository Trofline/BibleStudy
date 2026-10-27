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

    /// <summary>True when the program was set up by the installer (an uninstaller sits next to it).</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(ProgramDir, "Uninstall.exe"));

    public static string Root => IsPortable
        ? Path.Combine(ProgramDir, "Data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BibleStudy");

    /// <summary>Default place for backups the program makes on its own (before updates).</summary>
    public static string BackupDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BibleStudy-Backup");

    public static string Version
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }
}
