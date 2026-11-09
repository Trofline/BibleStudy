using System.IO.Compression;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BibleStudy.App;

/// <summary>
/// "Take it along": one ZIP with the chosen vaults (marks, notes, documents, lists, to-dos, reading progress) and the shared settings, without
/// Bible texts or imported translations. Whoever has the program can open it with "import" or "import as guest" and keep working.
/// Work done as a guest comes back the same way and can be mixed into the vault it came from.
/// </summary>
public partial class MainWindow
{
    const string BundleDir = "vaults/";
    const string GuestWorkEntry = "guest-work.txt";

    async Task ExportAllAsync()
    {
        if (await ChooseVaultsAsync() is not { Count: > 0 } names) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L("Vaults exportieren"), SuggestedFileName = $"BibleStudy-Notizen-{DateTime.Now:yyyyMMdd}.zip",
            FileTypeChoices = [new FilePickerFileType("ZIP-Archiv") { Patterns = ["*.zip"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        WriteVaultsZip(path, names.Select(n => (n, n)).ToList(), false);
    }

    /// <summary>Which vaults go along; all are ticked at first. Null when cancelled.</summary>
    async Task<List<string>?> ChooseVaultsAsync()
    {
        var names = VaultNames().Where(n => !IsGuestVault(n)).ToList();
        List<string>? result = null;
        var win = new Window { Title = L("Vaults exportieren"), Width = 460, SizeToContent = SizeToContent.Height, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var list = new StackPanel { Spacing = 4 };
        var boxes = names.Select(n => new CheckBox { Content = n, IsChecked = true }).ToList();
        foreach (var b in boxes) list.Children.Add(b);
        var ok = new Button { Content = L("Exportieren …"), Margin = new(8, 0, 0, 0) }; ok.Classes.Add("primary");
        var cancel = new Button { Content = L("Abbrechen"), Margin = new(8, 0, 0, 0) };
        ok.Click += (_, _) => { result = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content!).ToList(); win.Close(); };
        cancel.Click += (_, _) => win.Close();
        foreach (var b in boxes) b.IsCheckedChanged += (_, _) => ok.IsEnabled = boxes.Any(x => x.IsChecked == true);
        win.Content = new StackPanel
        {
            Margin = new(22),
            Children =
            {
                new TextBlock { Text = L("Vaults exportieren"), FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 10) },
                new TextBlock { Text = L("Welche Vaults sollen in die ZIP? Bibeltexte sind nie dabei."), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) },
                new ScrollViewer { MaxHeight = 320, Content = list },
                new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 14, 0, 0), Children = { ok, cancel } },
            },
        };
        await ShowModalAsync(win);
        return result;
    }

    internal void ExportAllToPath(string path) => WriteVaultsZip(path, VaultNames().Where(n => !IsGuestVault(n)).Select(n => (n, n)).ToList(), false);

    /// <summary>Writes the vaults into one ZIP: Source is the vault here, Name the name it has in the file.</summary>
    internal void WriteVaultsZip(string path, IReadOnlyList<(string Source, string Name)> vaults, bool guestWork)
    {
        var temp = new List<string>();
        try
        {
            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var (source, name) in vaults)
                {
                    var snapshot = Path.Combine(Path.GetTempPath(), $"bs_{Guid.NewGuid():N}.db");
                    temp.Add(snapshot);
                    StoreOf(source).BackupTo(snapshot);
                    if (!IsGuestVault(source)) _readStore.CopyInto(snapshot);   // the shared reading progress travels with each vault
                    zip.CreateEntryFromFile(snapshot, $"{BundleDir}{name}/vault.db");
                }
                var shared = new AppSettings { CustomColors = _settings.CustomColors, ColorNames = _settings.ColorNames, DefaultColor = _settings.DefaultColor, AutoTagBook = _settings.AutoTagBook, AutoMarkRead = _settings.AutoMarkRead, SubMarkColor = _settings.SubMarkColor, CustomSubColors = _settings.CustomSubColors };
                using (var s = new StreamWriter(zip.CreateEntry("settings.json").Open())) s.Write(JsonSerializer.Serialize(shared));
                if (guestWork) using (var g = new StreamWriter(zip.CreateEntry(GuestWorkEntry).Open())) g.Write("made as a guest: can be mixed into the vault of the same name");
                using var info = new StreamWriter(zip.CreateEntry("info.txt").Open());
                info.Write($"BibleStudy vaults ({vaults.Count}), exported {DateTime.Now:yyyy-MM-dd HH:mm}");
            }
            BackupInfo.Text = L("Vaults ({0}) exportiert nach {1}", vaults.Count, path);
        }
        catch (Exception ex) { BackupInfo.Text = L("Export fehlgeschlagen: ") + ex.Message; }
        finally { foreach (var t in temp) try { File.Delete(t); } catch (IOException) { } }
    }

    /// <summary>Opens a ZIP with several vaults: every vault in it comes in under a name that is still free.</summary>
    async Task ImportAllAsync(ZipArchive zip, bool guest)
    {
        string? first = null; var count = 0;
        foreach (var (entry, given) in BundleVaults(zip))
        {
            var name = FreeVaultName(guest ? L("{0} (Gast)", given) : given);
            var target = Path.Combine(_root, "vaults", name, "vault.db");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
            if (guest) RememberGuest(name, given);
            else _readStore.MergeFrom(target);
            first ??= name; count++;
        }
        if (first is null) throw new InvalidDataException(L("Die Datei enthält keinen Vault (vault.db)."));
        if (!guest) ApplyImportedSettings(zip);
        await SwitchVaultAsync(first);
        BackupInfo.Text = guest ? L("{0} Vaults als Gast importiert. Sie verschwinden, wenn du das Programm schließt; deine eigenen Daten bleiben unberührt.", count) : L("{0} Vaults importiert, jeweils als neuer Vault.", count);
    }

    static IEnumerable<(ZipArchiveEntry Entry, string Name)> BundleVaults(ZipArchive zip) =>
        zip.Entries.Where(e => e.FullName.StartsWith(BundleDir, StringComparison.Ordinal) && e.FullName.EndsWith("/vault.db", StringComparison.Ordinal))
            .Select(e => (e, e.FullName[BundleDir.Length..^"/vault.db".Length]))
            .Where(x => MarkLogic.IsValidVaultName(x.Item2));   // never a path that leaves the vault folder

    string FreeVaultName(string stem)
    {
        var name = stem;
        for (var n = 2; Directory.Exists(Path.Combine(_root, "vaults", name)); n++) name = stem + $" ({n})";
        return name;
    }

    // ---- a look at the file before it is imported -----------------------------------------------------------------------

    /// <summary>What the file holds, in words for the question before the import; IsGuestWork when it was saved at the end of a guest session.</summary>
    internal (string Text, bool IsGuestWork) ImportPreview(string path)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bs_look_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entries = BundleVaults(zip).ToList();
            if (entries.Count == 0 && zip.GetEntry("vault.db") is { } single)
            {
                string? Read(string entry) { if (zip.GetEntry(entry) is not { } e) return null; using var r = new StreamReader(e.Open()); return r.ReadToEnd(); }
                entries.Add((single, MarkLogic.VaultNameFromExport(Read("name.txt"), Read("info.txt")) ?? L("Vault")));
            }
            if (entries.Count == 0) throw new InvalidDataException(L("Die Datei enthält keinen Vault (vault.db)."));
            var lines = new List<string>(); var used = new HashSet<string>();
            var i = 0;
            foreach (var (entry, name) in entries)
            {
                var db = Path.Combine(dir, $"{i++}.db");
                entry.ExtractToFile(db);
                var s = new VaultStore(db, name).Summary();
                lines.Add(L("„{0}“: {1} Markierungen, {2} Notizen, {3} Dokumente, {4} Aufgaben", name, s.Marks, s.Notes, s.Documents, s.Todos));
                used.UnionWith(s.Translations);
            }
            var missing = used.Where(id => _all.All(t => t.Id != id)).Order().ToList();
            var text = string.Join("\n", lines);
            if (missing.Count > 0) text += "\n\n" + L("Diese Übersetzungen fehlen auf diesem Computer: {0}. Ganze Verse bleiben sichtbar, Teilmarkierungen nur dort, wo ihre Wörter im angezeigten Vers stehen.", string.Join(", ", missing.Select(id => id.Replace(BibleStudy.Core.Import.LocalBibleStore.Prefix, ""))));
            return (text, zip.GetEntry(GuestWorkEntry) is not null);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a temporary folder */ }
        }
    }

    // ---- work done as a guest comes home ---------------------------------------------------------------------------------

    /// <summary>Mixes the vaults of the file into the vaults of the same name; a vault that is not here comes in as a new one. A backup is made first.</summary>
    internal async Task MergeGuestWorkAsync(string path)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bs_merge_{Guid.NewGuid():N}");
        try
        {
            await SwitchVaultAsync(_vault.Name);   // what is open is saved first
            BackupData(L("vor dem Einmischen"));
            Directory.CreateDirectory(dir);
            using var zip = ZipFile.OpenRead(path);
            var total = new MergeResult(0, 0, 0, 0, 0);
            string? first = null; var i = 0;
            foreach (var (entry, given) in BundleVaults(zip))
            {
                var db = Path.Combine(dir, $"{i++}.db");
                entry.ExtractToFile(db);
                var home = Path.Combine(_root, "vaults", given);
                string name;
                if (Directory.Exists(home) && !IsGuestDir(home))
                {
                    name = given;
                    var r = StoreOf(name).MergeFrom(db, L("Gast"));
                    total = new MergeResult(total.Marks + r.Marks, total.Notes + r.Notes, total.Documents + r.Documents, total.Others + r.Others, total.Conflicts + r.Conflicts);
                }
                else
                {
                    name = FreeVaultName(given);
                    var target = Path.Combine(_root, "vaults", name, "vault.db");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(db, target);
                }
                _readStore.MergeFrom(db);
                first ??= name;
            }
            if (first is null) throw new InvalidDataException(L("Die Datei enthält keinen Vault (vault.db)."));
            await SwitchVaultAsync(first);
            BackupInfo.Text = L("Eingemischt: {0} Markierungen, {1} Notizen, {2} Dokumente, {3} weitere Einträge. An {4} Stellen unterschieden sich beide, dort ist beides behalten.", total.Marks, total.Notes, total.Documents, total.Others, total.Conflicts);
        }
        catch (Exception ex) { BackupInfo.Text = L("Import fehlgeschlagen: ") + ex.Message; }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a temporary folder */ }
        }
    }
}
