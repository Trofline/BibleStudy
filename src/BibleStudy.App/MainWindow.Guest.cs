using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace BibleStudy.App;

/// <summary>Guest import: a vault from another computer that lives only for this session. It is marked by a flag file, never becomes
/// the vault that opens at the next start, is left out of backups and is deleted when the program closes (or at the next start after a crash).
/// If something was changed in it, the program offers to save that work as a ZIP before it is gone.</summary>
public partial class MainWindow
{
    const string GuestFlag = "gast.flag";
    const string GuestOrigin = "gast.origin";
    const string GuestBase = "gast.base";
    bool _guestAsked;

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

    /// <summary>Marks a freshly unpacked vault as a guest: where it came from, and how it looked, to tell later whether anything was changed.</summary>
    void RememberGuest(string name, string origin)
    {
        var dir = Path.Combine(_root, "vaults", name);
        File.WriteAllText(Path.Combine(dir, GuestFlag), "temporary: deleted when the program closes");
        File.WriteAllText(Path.Combine(dir, GuestOrigin), origin);
        File.WriteAllText(Path.Combine(dir, GuestBase), OpenVault(name).Fingerprint());
    }

    /// <summary>The guest vaults in which something was written since they were opened.</summary>
    internal List<string> ChangedGuestVaults()
    {
        var changed = new List<string>();
        foreach (var name in VaultNames().Where(IsGuestVault))
            try
            {
                var file = Path.Combine(_root, "vaults", name, GuestBase);
                if (File.Exists(file) && File.ReadAllText(file) != StoreOf(name).Fingerprint()) changed.Add(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException) { ErrorLog.Write(ex); }
        return changed;
    }

    /// <summary>Saves the changed guest vaults under the names they had at home, so that they can be mixed in there.</summary>
    internal void SaveGuestWorkTo(string path, IReadOnlyList<string> vaults)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<(string, string)>();
        foreach (var source in vaults)
        {
            var file = Path.Combine(_root, "vaults", source, GuestOrigin);
            var origin = File.Exists(file) ? File.ReadAllText(file).Trim() : source;
            var name = origin;
            for (var n = 2; !used.Add(name); n++) name = $"{origin} ({n})";
            list.Add((source, name));
        }
        WriteVaultsZip(path, list, true);
    }

    /// <summary>True when closing may go on: nothing changed, the work is saved, or the user lets it go.</summary>
    bool GuestWorkNeedsQuestion()
    {
        if (_guestAsked) return false;
        try { AutoSaveDocument(); SaveNoteDraft(); SaveSermonBeforeVaultChange(); } catch (Exception ex) { ErrorLog.Write(ex); }
        return ChangedGuestVaults().Count > 0;
    }

    async Task AskGuestWorkThenCloseAsync()
    {
        var changed = ChangedGuestVaults();
        var answer = await AskAsync(L("Gast-Arbeit sichern?"),
            L("In {0} hast du etwas geändert. Gast-Vaults werden beim Schließen gelöscht. Als ZIP gesichert kannst du deine Arbeit auf deinem eigenen Computer importieren und in deinen Vault einmischen.", string.Join(", ", changed.Select(n => "„" + n + "“"))),
            L("Als ZIP sichern …"), L("Nicht sichern"), L("Abbrechen"));
        if (answer is 2 or -1) return;   // the program stays open
        if (answer == 0)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L("Gast-Arbeit sichern?"), SuggestedFileName = $"BibleStudy-Gast-{DateTime.Now:yyyyMMdd-HHmm}.zip",
                FileTypeChoices = [new FilePickerFileType("ZIP-Archiv") { Patterns = ["*.zip"] }],
            });
            if (file?.TryGetLocalPath() is not { } path) return;   // cancelled: nothing is lost, the program stays open
            SaveGuestWorkTo(path, changed);
            if (!File.Exists(path)) return;   // the message in the settings says why
        }
        _guestAsked = true;
        Dispatcher.UIThread.Post(Close, DispatcherPriority.Background);
    }
}
