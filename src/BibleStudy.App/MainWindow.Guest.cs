using Microsoft.Data.Sqlite;

namespace BibleStudy.App;

/// <summary>Guest import: a vault from another computer that lives only for this session. It is marked by a flag file, never becomes
/// the vault that opens at the next start, is left out of backups and is deleted when the program closes (or at the next start after a crash).</summary>
public partial class MainWindow
{
    const string GuestFlag = "gast.flag";

    static bool IsGuestDir(string vaultDir) => File.Exists(Path.Combine(vaultDir, GuestFlag));

    bool IsGuestVault(string name) => IsGuestDir(Path.Combine(_root, "vaults", name));

    /// <summary>Removes every guest vault, except the one that is open (null = all). Used at the start and when closing.</summary>
    static void DeleteGuestVaults(string root, string? keepOpen = null)
    {
        var dir = Path.Combine(root, "vaults");
        if (!Directory.Exists(dir)) return;
        SqliteConnection.ClearAllPools();
        foreach (var v in Directory.GetDirectories(dir).Where(d => IsGuestDir(d) && Path.GetFileName(d) != keepOpen))
            try { Directory.Delete(v, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }
}
