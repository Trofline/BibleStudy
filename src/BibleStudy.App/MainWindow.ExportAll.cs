using System.IO.Compression;
using System.Text.Json;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// "Take everything along": one ZIP with all vaults (marks, notes, documents, lists, to-dos, reading progress) and the shared settings, without
/// Bible texts or imported translations. Whoever has the program can open it with "import" or "import as guest" and keep working.
/// </summary>
public partial class MainWindow
{
    const string BundleDir = "vaults/";

    async Task ExportAllAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L("Alle Vaults exportieren"), SuggestedFileName = $"BibleStudy-Notizen-{DateTime.Now:yyyyMMdd}.zip",
            FileTypeChoices = [new FilePickerFileType("ZIP-Archiv") { Patterns = ["*.zip"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        ExportAllToPath(path);
    }

    internal void ExportAllToPath(string path)
    {
        var temp = new List<string>();
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var count = 0;
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var name in VaultNames().Where(n => !IsGuestVault(n)))
                {
                    var snapshot = Path.Combine(Path.GetTempPath(), $"bs_{Guid.NewGuid():N}.db");
                    temp.Add(snapshot);
                    (name == _vault.Name ? _vault : OpenVault(name)).BackupTo(snapshot);
                    _readStore.CopyInto(snapshot);   // the shared reading progress travels with each vault
                    zip.CreateEntryFromFile(snapshot, $"{BundleDir}{name}/vault.db");
                    count++;
                }
                var shared = new AppSettings { CustomColors = _settings.CustomColors, ColorNames = _settings.ColorNames, DefaultColor = _settings.DefaultColor, AutoTagBook = _settings.AutoTagBook, AutoMarkRead = _settings.AutoMarkRead, SubMarkColor = _settings.SubMarkColor, CustomSubColors = _settings.CustomSubColors };
                using (var s = new StreamWriter(zip.CreateEntry("settings.json").Open())) s.Write(JsonSerializer.Serialize(shared));
                using var info = new StreamWriter(zip.CreateEntry("info.txt").Open());
                info.Write($"BibleStudy all vaults ({count}), exported {DateTime.Now:yyyy-MM-dd HH:mm}");
            }
            BackupInfo.Text = L("Alle Vaults ({0}) exportiert nach {1}", count, path);
        }
        catch (Exception ex) { BackupInfo.Text = L("Export fehlgeschlagen: ") + ex.Message; }
        finally { foreach (var t in temp) try { File.Delete(t); } catch (IOException) { } }
    }

    /// <summary>Opens a ZIP made by "export all vaults": every vault in it comes in under a name that is still free.</summary>
    async Task ImportAllAsync(ZipArchive zip, bool guest)
    {
        string? first = null; var count = 0;
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(BundleDir, StringComparison.Ordinal) && e.FullName.EndsWith("/vault.db", StringComparison.Ordinal)))
        {
            var given = entry.FullName[BundleDir.Length..^"/vault.db".Length];
            if (!MarkLogic.IsValidVaultName(given)) continue;   // never a path that leaves the vault folder
            var stem = guest ? L("{0} (Gast)", given) : given;
            var name = stem;
            for (var n = 2; Directory.Exists(Path.Combine(_root, "vaults", name)); n++) name = stem + $" ({n})";
            var target = Path.Combine(_root, "vaults", name, "vault.db");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
            if (guest) File.WriteAllText(Path.Combine(Path.GetDirectoryName(target)!, GuestFlag), "temporary: deleted when the program closes");
            else _readStore.MergeFrom(target);
            first ??= name; count++;
        }
        if (first is null) throw new InvalidDataException(L("Die Datei enthält keinen Vault (vault.db)."));
        if (!guest) ApplyImportedSettings(zip);
        await SwitchVaultAsync(first);
        BackupInfo.Text = guest ? L("{0} Vaults als Gast importiert. Sie verschwinden, wenn du das Programm schließt; deine eigenen Daten bleiben unberührt.", count) : L("{0} Vaults importiert, jeweils als neuer Vault.", count);
    }
}
