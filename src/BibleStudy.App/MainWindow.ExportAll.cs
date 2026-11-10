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
    const string BiblesEntry = "bibles.db";
    const string BaseEntry = "/base.db";
    string MergeUndoDir => Path.Combine(_root, "merge-undo");
    MergeReview? _lastMerge;

    sealed record MergeReview(List<(string Vault, MergeConflict Item)> Conflicts, List<(string Vault, MergeDeletion Item)> Deletions);

    async Task ExportAllAsync()
    {
        if (await ChooseVaultsAsync() is not { Vaults.Count: > 0 } choice) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L("Vaults exportieren"), SuggestedFileName = $"BibleStudy-Notizen-{DateTime.Now:yyyyMMdd}.zip",
            FileTypeChoices = [new FilePickerFileType("ZIP-Archiv") { Patterns = ["*.zip"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        WriteVaultsZip(path, choice.Vaults.Select(n => (n, n)).ToList(), false, choice.Bibles);
    }

    /// <summary>Which vaults go along; all are ticked at first. Null when cancelled.</summary>
    internal async Task<(List<string> Vaults, List<string> Bibles)?> ChooseVaultsAsync()
    {
        var names = VaultNames().Where(n => !IsGuestVault(n)).ToList();
        (List<string>, List<string>)? result = null;
        var bibles = (await _local.GetTranslationsAsync()).Select(t => new CheckBox { Content = t.Name, Tag = t.Id, IsChecked = false }).ToList();
        var win = new Window { Title = L("Vaults exportieren"), Width = 460, SizeToContent = SizeToContent.Height, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var list = new StackPanel { Spacing = 4 };
        var boxes = names.Select(n => new CheckBox { Content = n, IsChecked = true }).ToList();
        foreach (var b in boxes) list.Children.Add(b);
        if (bibles.Count > 0)
        {
            list.Children.Add(new TextBlock { Text = L("Importierte Bibeln mitnehmen (nur eigene Kopien für den privaten Gebrauch; beachte die Rechte der Texte):"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 2) });
            foreach (var b in bibles) list.Children.Add(b);
        }
        var ok = new Button { Content = L("Exportieren …"), Margin = new(8, 0, 0, 0) }; ok.Classes.Add("primary");
        var cancel = new Button { Content = L("Abbrechen"), Margin = new(8, 0, 0, 0) };
        ok.Click += (_, _) => { result = (boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content!).ToList(), bibles.Where(b => b.IsChecked == true).Select(b => (string)b.Tag!).ToList()); win.Close(); };
        cancel.Click += (_, _) => win.Close();
        foreach (var b in boxes) b.IsCheckedChanged += (_, _) => ok.IsEnabled = boxes.Any(x => x.IsChecked == true);
        win.Content = new StackPanel
        {
            Margin = new(22),
            Children =
            {
                new TextBlock { Text = L("Vaults exportieren"), FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 10) },
                new TextBlock { Text = L("Welche Vaults sollen in die ZIP?"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) },
                new ScrollViewer { MaxHeight = 320, Content = list },
                new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 14, 0, 0), Children = { ok, cancel } },
            },
        };
        await ShowModalAsync(win);
        return result;
    }

    internal void ExportAllToPath(string path) => WriteVaultsZip(path, VaultNames().Where(n => !IsGuestVault(n)).Select(n => (n, n)).ToList(), false);

    /// <summary>Writes the vaults into one ZIP: Source is the vault here, Name the name it has in the file.</summary>
    internal void WriteVaultsZip(string path, IReadOnlyList<(string Source, string Name)> vaults, bool guestWork, IReadOnlyList<string>? bibles = null)
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
                    if (guestWork && File.Exists(Path.Combine(_root, "vaults", source, GuestOriginal))) zip.CreateEntryFromFile(Path.Combine(_root, "vaults", source, GuestOriginal), BundleDir + name + BaseEntry);
                }
                if (bibles is { Count: > 0 })
                {
                    var db = Path.Combine(Path.GetTempPath(), $"bs_{Guid.NewGuid():N}.db");
                    temp.Add(db);
                    _local.CopyTo(bibles, db);
                    SqliteConnection.ClearAllPools();
                    zip.CreateEntryFromFile(db, BiblesEntry);
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
        await TakeBiblesAsync(zip, guest);
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

    /// <summary>The vaults of a file of either kind (several, or the export of one vault).</summary>
    static List<(ZipArchiveEntry Entry, string Name)> ZipVaults(ZipArchive zip)
    {
        var list = BundleVaults(zip).ToList();
        if (list.Count == 0 && zip.GetEntry("vault.db") is { } single)
        {
            string? Read(string entry) { if (zip.GetEntry(entry) is not { } e) return null; using var r = new StreamReader(e.Open()); return r.ReadToEnd(); }
            if (MarkLogic.VaultNameFromExport(Read("name.txt"), Read("info.txt")) is { } n && MarkLogic.IsValidVaultName(n)) list.Add((single, n));
        }
        return list;
    }

    /// <summary>Bibles that came along are added; those of a guest import are noted, so that they go when the session ends.</summary>
    async Task TakeBiblesAsync(ZipArchive zip, bool guest)
    {
        if (zip.GetEntry(BiblesEntry) is not { } entry) return;
        var db = Path.Combine(Path.GetTempPath(), $"bs_{Guid.NewGuid():N}.db");
        try
        {
            entry.ExtractToFile(db);
            var added = _local.AddFrom(db);
            if (guest && added.Count > 0) File.AppendAllLines(Path.Combine(_root, GuestBibles), added);
            if (added.Count > 0) { await RefreshLocalBibleBoxAsync(); await ReloadTranslationsAsync(null); }
        }
        finally { SqliteConnection.ClearAllPools(); try { File.Delete(db); } catch (IOException) { } }
    }

    string FreeVaultName(string stem)
    {
        var name = stem;
        for (var n = 2; Directory.Exists(Path.Combine(_root, "vaults", name)); n++) name = stem + $" ({n})";
        return name;
    }

    // ---- a look at the file before it is imported -----------------------------------------------------------------------

    /// <summary>What the file holds, in words for the question before the import; IsGuestWork when it was saved at the end of a guest session.</summary>
    internal (string Text, bool IsGuestWork, bool SameName) ImportPreview(string path)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bs_look_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entries = ZipVaults(zip);
            if (entries.Count == 0 && zip.GetEntry("vault.db") is { } single) entries.Add((single, L("Vault")));
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
            if (zip.GetEntry(BiblesEntry) is { } bibleEntry)
            {
                var db = Path.Combine(dir, "bibles.db");
                bibleEntry.ExtractToFile(db);
                var came = new BibleStudy.Core.Import.LocalBibleStore(db).GetTranslationsAsync().GetAwaiter().GetResult();
                used.ExceptWith(came.Select(t => t.Id));
                lines.Add(L("Bibeln: {0}", string.Join(", ", came.Select(t => t.Name))));
            }
            var missing = used.Where(id => _all.All(t => t.Id != id)).Order().ToList();
            var text = string.Join("\n", lines);
            if (missing.Count > 0) text += "\n\n" + L("Diese Übersetzungen fehlen auf diesem Computer: {0}. Ganze Verse bleiben sichtbar, Teilmarkierungen nur dort, wo ihre Wörter im angezeigten Vers stehen.", string.Join(", ", missing.Select(id => id.Replace(BibleStudy.Core.Import.LocalBibleStore.Prefix, ""))));
            var same = entries.Any(e => Directory.Exists(Path.Combine(_root, "vaults", e.Name)) && !IsGuestVault(e.Name));
            return (text, zip.GetEntry(GuestWorkEntry) is not null, same);
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
            var label = L("vor dem Einmischen");
            await Task.Run(() => BackupData(label));   // a big vault takes a while: the window must not freeze
            Directory.CreateDirectory(dir);
            if (Directory.Exists(MergeUndoDir)) Directory.Delete(MergeUndoDir, true);
            Directory.CreateDirectory(MergeUndoDir);
            var undo = new List<string>();
            var review = new MergeReview([], []);
            using var zip = ZipFile.OpenRead(path);
            var total = new MergeResult(0, 0, 0, 0, 0);
            string? first = null; var i = 0;
            await TakeBiblesAsync(zip, false);
            foreach (var (entry, given) in ZipVaults(zip))
            {
                var db = Path.Combine(dir, $"{i++}.db");
                entry.ExtractToFile(db);
                var home = Path.Combine(_root, "vaults", given);
                string name;
                if (Directory.Exists(home) && !IsGuestDir(home))
                {
                    name = given;
                    var snap = Path.Combine(MergeUndoDir, $"{undo.Count}.db");
                    StoreOf(name).BackupTo(snap);
                    undo.Add($"restore\t{name}\t{snap}");
                    if (zip.GetEntry(BundleDir + given + BaseEntry) is { } baseEntry)
                    {
                        var basePath = db + ".base";
                        baseEntry.ExtractToFile(basePath);
                        review.Deletions.AddRange(StoreOf(name).FindDeletions(basePath, db).Select(x => (name, x)));
                    }
                    var r = StoreOf(name).MergeFrom(db, L("Gast"));
                    review.Conflicts.AddRange((r.Items ?? []).Select(x => (name, x)));
                    total = new MergeResult(total.Marks + r.Marks, total.Notes + r.Notes, total.Documents + r.Documents, total.Others + r.Others, total.Conflicts + r.Conflicts);
                }
                else
                {
                    name = FreeVaultName(given);
                    var target = Path.Combine(_root, "vaults", name, "vault.db");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(db, target);
                    undo.Add($"new\t{name}");
                }
                _readStore.MergeFrom(db);
                first ??= name;
            }
            if (first is null) throw new InvalidDataException(L("Die Datei enthält keinen Vault (vault.db)."));
            File.WriteAllLines(Path.Combine(MergeUndoDir, "list.txt"), undo);
            _lastMerge = review;
            MergeUndoBtn.IsVisible = true;
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

    /// <summary>The vaults go back to how they were right before the last merge; vaults it brought in are removed. Reading progress stays.</summary>
    internal async Task UndoMergeCoreAsync()
    {
        var file = Path.Combine(MergeUndoDir, "list.txt");
        if (!File.Exists(file)) return;
        await SwitchVaultAsync(_vault.Name);
        var lines = File.ReadAllLines(file).Select(l => l.Split('\t')).ToList();
        var restored = lines.Where(p => p[0] == "restore").Select(p => p[1]).ToList();
        var fresh = lines.Where(p => p[0] == "new").Select(p => p[1]).ToList();
        var stay = restored.FirstOrDefault() ?? VaultNames().FirstOrDefault(n => !fresh.Contains(n)) ?? "Standard";
        await SwitchVaultAsync(stay, alreadySaved: true);
        _otherVaults.Clear();
        SqliteConnection.ClearAllPools();
        foreach (var p in lines.Where(p => p[0] == "restore")) File.Copy(p[2], Path.Combine(_root, "vaults", p[1], "vault.db"), true);
        foreach (var n in fresh) { var d = Path.Combine(_root, "vaults", n); if (Directory.Exists(d)) Directory.Delete(d, true); }
        SqliteConnection.ClearAllPools();
        Directory.Delete(MergeUndoDir, true);
        MergeUndoBtn.IsVisible = false;
        _lastMerge = null;
        await SwitchVaultAsync(stay, alreadySaved: true);
        BackupInfo.Text = L("Das letzte Einmischen wurde rückgängig gemacht.");
    }

    async Task UndoMergeAsync()
    {
        if (await AskAsync(L("Einmischen rückgängig"), L("Die Vaults werden so wiederhergestellt, wie sie direkt vor dem letzten Einmischen waren. Was du seitdem in ihnen geändert hast, geht dabei verloren."), L("Rückgängig machen"), L("Abbrechen")) == 0)
            await UndoMergeCoreAsync();
    }

    /// <summary>After a merge: for each place where both differ mine, theirs or both; and what was deleted as a guest can be deleted here too (nothing is ticked at first).</summary>
    internal async Task ReviewMergeAsync()
    {
        if (_lastMerge is not { } review || review.Conflicts.Count + review.Deletions.Count == 0) return;
        var win = new Window { Title = L("Einmischen prüfen"), Width = 760, Height = 640, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new StackPanel { Spacing = 12 };
        var picks = new List<(string Vault, MergeConflict Item, ComboBox Box)>();
        foreach (var (vault, c) in review.Conflicts)
        {
            var box = new ComboBox { ItemsSource = new[] { L("Meins"), L("Deins"), L("Beides") }, SelectedIndex = 2 };
            picks.Add((vault, c, box));
            var where = c.Ref is { } r ? $"{BookName(r.Book)} {r.Chapter},{r.Verse}" : c.Label;
            list.Children.Add(new StackPanel { Spacing = 3, Children =
            {
                new TextBlock { Text = $"{vault}: {where}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = L("Meins: ") + Shorten(c.Mine.ReplaceLineEndings(" "), 220), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = L("Deins: ") + Shorten(c.Theirs.ReplaceLineEndings(" "), 220), TextWrapping = TextWrapping.Wrap },
                box,
            } });
        }
        var gone = new List<(string Vault, MergeDeletion Item, CheckBox Box)>();
        if (review.Deletions.Count > 0)
        {
            list.Children.Add(new TextBlock { Text = L("Als Gast gelöscht, hier noch vorhanden. Angehakte werden auch hier gelöscht (sie bleiben eine Weile im Papierkorb):"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            foreach (var (vault, d) in review.Deletions)
            {
                var where = d.Ref is { } r ? $"{BookName(r.Book)} {r.Chapter},{r.Verse}: " : "";
                var box = new CheckBox { IsChecked = false, Content = new TextBlock { Text = $"{vault}: {where}{Shorten(d.Label.ReplaceLineEndings(" "), 120)}", TextWrapping = TextWrapping.Wrap } };
                gone.Add((vault, d, box));
                list.Children.Add(box);
            }
        }
        var apply = false;
        var ok = new Button { Content = L("Übernehmen"), Margin = new(8, 0, 0, 0) }; ok.Classes.Add("primary");
        var cancel = new Button { Content = L("Alles so lassen"), Margin = new(8, 0, 0, 0) };
        ok.Click += (_, _) => { apply = true; win.Close(); };
        cancel.Click += (_, _) => win.Close();
        var head = new TextBlock { Text = L("Einmischen prüfen"), FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 12) };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0), Children = { ok, cancel } };
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(buttons, Dock.Bottom);
        win.Content = new DockPanel { Margin = new(22), Children = { head, buttons, new ScrollViewer { Content = list } } };
        await ShowModalAsync(win);
        _lastMerge = null;
        if (!apply) return;
        foreach (var (vault, item, box) in picks) StoreOf(vault).ResolveConflict(item, box.SelectedIndex);
        foreach (var (vault, item, box) in gone) if (box.IsChecked == true) StoreOf(vault).ApplyDeletion(item);
        await SwitchVaultAsync(_vault.Name, alreadySaved: true);
    }
}
