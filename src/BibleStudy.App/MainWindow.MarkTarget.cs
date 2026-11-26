using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// A folder chosen together with the vault (in the vault menu of the top bar): every new mark goes straight into it, nothing has to be dragged.
/// When a folder that a reading path belongs to gets new verses, a note asks once whether they go onto the end of the path. They are only ever
/// appended (the path is never made anew from the folder), so steps the user moved, took out or added by hand stay as they are. The answer
/// holds for later verses of that folder too, so the note comes once per path.
/// </summary>
public partial class MainWindow
{
    // ---- the folder new marks go into ------------------------------------------------------------------------------------

    /// <summary>The folder new marks of the active vault go into (null: none chosen, or it was deleted since).</summary>
    long? MarkTargetFolder()
    {
        if (!_settings.MarkTargetFolders.TryGetValue(_vault.Name, out var id)) return null;
        try { return _vault.GetFolders().Any(f => f.Id == id) ? id : null; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>The name of that folder with the folders above it ("Paulus › Römer"), or null.</summary>
    string? MarkTargetLabel()
    {
        if (MarkTargetFolder() is not long id) return null;
        try { return _vault.FolderKey(id) is { Length: > 0 } key ? FolderLabel(key) : null; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>The vault name in the top bar, with the folder new marks go into.</summary>
    void UpdateVaultTopText()
    {
        if (VaultTopText is null) return;
        var target = MarkTargetLabel();
        VaultTopText.Text = target is null ? _vault.Name : _vault.Name + " › " + target;
        ToolTip.SetTip(VaultTopBtn, target is null
            ? L("Aktiver Vault (deine Mappe mit Markierungen, Notizen, Dokumenten). Klicken zum Wechseln.")
            : L("Aktiver Vault „{0}“. Neue Markierungen kommen direkt in den Ordner „{1}“. Klicken zum Wechseln.", _vault.Name, target));
    }

    /// <summary>Opens the vault (if it is not the active one) and makes the folder the one new marks go into (null: into none).</summary>
    async Task PickVaultAndFolderAsync(string vault, long? folder)
    {
        if (vault != _vault.Name) await SwitchVaultAsync(vault);
        if (_vault.Name != vault) return;   // the vault could not be opened
        if (folder is long f) _settings.MarkTargetFolders[vault] = f; else _settings.MarkTargetFolders.Remove(vault);
        SaveSettings();
        UpdateVaultTopText();
        StatusText.Text = MarkTargetLabel() is { } name
            ? L("Neue Markierungen kommen jetzt direkt in den Ordner „{0}“.", name)
            : L("Neue Markierungen kommen in keinen Ordner.");
    }

    /// <summary>
    /// One vault in the vault menu. Without folders a click opens it; with folders it opens a list: "no folder" and each folder (as a tree),
    /// the one new marks go into is ticked.
    /// </summary>
    MenuItem VaultMenuItem(string name)
    {
        var active = name == _vault.Name;
        List<Folder> folders = [];
        if (!IsGuestVault(name))
            try { folders = StoreOf(name).GetFolders(); }
            catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException) { /* a vault that cannot be read just shows without folders */ }
        long? target = _settings.MarkTargetFolders.TryGetValue(name, out var t) && folders.Any(f => f.Id == t) ? t : null;
        var targetName = target is long tf ? folders.First(f => f.Id == tf).Name : null;
        var header = (active ? "✓  " : "     ") + name + (targetName is null ? "" : "  › " + targetName);
        if (folders.Count == 0) return MenuAction(header, () => { if (!active) _ = SwitchVaultAsync(name); });

        var item = new MenuItem { Header = header };
        item.Items.Add(MenuAction((target is null ? "✓  " : "     ") + L("Ohne Ordner"), () => _ = PickVaultAndFolderAsync(name, null)));
        item.Items.Add(new Separator());
        void Add(long? parent, int depth)
        {
            foreach (var f in folders.Where(f => f.ParentId == parent && f.Id != parent).OrderBy(f => f.Name, Comparer<string>.Create(NaturalCompare)))
            {
                var id = f.Id;
                item.Items.Add(MenuAction((target == id ? "✓  " : "     ") + new string(' ', depth * 4) + f.Name, () => _ = PickVaultAndFolderAsync(name, id)));
                if (depth < 8) Add(f.Id, depth + 1);
            }
        }
        Add(null, 0);
        return item;
    }

    /// <summary>A vault was renamed: its folder for new marks and the answers about its reading paths go along.</summary>
    void RenameVaultInMarkTargets(string old, string name)
    {
        if (_settings.MarkTargetFolders.Remove(old, out var folder)) _settings.MarkTargetFolders[name] = folder;
        static void Move(List<string> keys, string old, string name)
        {
            for (var i = 0; i < keys.Count; i++)
                if (keys[i].StartsWith(old + "\u001f", StringComparison.Ordinal)) keys[i] = name + keys[i][old.Length..];
        }
        Move(_settings.PathGrowAsked, old, name);
        Move(_settings.PathGrowAppend, old, name);
        SaveSettings();
    }

    // ---- new verses in a folder with a reading path ----------------------------------------------------------------------

    /// <summary>The key of a path in <see cref="AppSettings.PathGrowAsked"/> and <see cref="AppSettings.PathGrowAppend"/>.</summary>
    string PathGrowKey(ReadingPath p) => _vault.Name + "\u001f" + p.Id;

    /// <summary>True when the step <paramref name="s"/> already lies inside the step <paramref name="k"/> of a path.</summary>
    static bool StepHolds(StudyPassage k, StudyPassage s) =>
        k.Book == s.Book && k.Chapter == s.Chapter && (k.From <= 0 || (s.From > 0 && k.From <= s.From && Math.Max(k.To, k.From) >= Math.Max(s.To, s.From)));

    /// <summary>The steps of a mark (all its verses).</summary>
    static StudyPassage MarkStep(Mark m) => new(m.Ref.Book, m.Ref.Chapter, m.Ref.Verse, Math.Max(m.Ref.Verse, m.LastVerse));

    /// <summary>The marks of the folder in this chapter, before something is marked (to tell the new ones afterwards).</summary>
    HashSet<long> MarkIdsHere()
    {
        try { return _vault.GetMarks(_curBook, _curChapter).Select(m => m.Id).ToHashSet(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return []; }
    }

    /// <summary>After marking in the text: the marks that are new in the target folder may belong onto its reading paths.</summary>
    void AfterMarkedIntoFolder(long folder, HashSet<long> before)
    {
        List<Mark> now;
        try { now = _vault.GetMarks(_curBook, _curChapter); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return; }
        // a verse that was in the folder before (an older mark recolored or cut) is not new
        var old = now.Where(m => before.Contains(m.Id) && m.FolderId == folder).Select(MarkStep).ToList();
        var added = now.Where(m => !before.Contains(m.Id) && m.FolderId == folder).Select(MarkStep).Where(s => !old.Any(o => StepHolds(o, s))).ToList();
        FolderGrew(folder, added);
    }

    /// <summary>
    /// New verses came into a folder. For each reading path of the folder that does not have them yet: appended at once when the user said so
    /// before, otherwise the note asks (once per path; "no" is kept too).
    /// </summary>
    void FolderGrew(long folder, IEnumerable<StudyPassage> added)
    {
        var steps = StudyPassage.Merged(added.OrderBy(s => BookOrder(s.Book)).ThenBy(s => s.Chapter).ThenBy(s => s.From));
        if (steps.Count == 0) return;
        List<ReadingPath> paths;
        try { paths = _vault.GetReadingPathsIn(folder); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return; }
        foreach (var p in paths)
        {
            var fresh = steps.Where(s => !p.Steps.Any(k => StepHolds(k, s))).ToList();
            if (fresh.Count == 0) continue;
            var key = PathGrowKey(p);
            if (_settings.PathGrowAppend.Contains(key)) { AddSteps(p, fresh); continue; }
            if (_growAsk is { } open && open.PathId == p.Id && open.Vault == _vault.Name)
            {
                // the note is still open: these verses go with the ones it already asks about
                open.Steps.AddRange(fresh.Where(s => !open.Steps.Any(k => StepHolds(k, s))));
                ShowGrowNotice();
                continue;
            }
            if (_settings.PathGrowAsked.Contains(key)) continue;
            if (_growAsk is not null) continue;   // another path is being asked about: this one asks the next time its folder grows
            _settings.PathGrowAsked.Add(key); SaveSettings();   // shown once, whatever the answer
            var folderName = FolderLabel(p.Folder);
            _growAsk = new GrowAsk(_vault.Name, p.Id, p.Name, folderName, [.. fresh]);
            ShowGrowNotice();
        }
    }

    sealed record GrowAsk(string Vault, long PathId, string PathName, string FolderName, List<StudyPassage> Steps);
    GrowAsk? _growAsk;
    Border? _growNotice;
    TextBlock? _growNoticeText;

    /// <summary>
    /// The note itself: in the reader over the text, else (the explorer open on the phone) at the bottom of the explorer. It stays until it is
    /// answered, so it cannot be missed; it wraps its text, so it fits a phone too.
    /// </summary>
    void ShowGrowNotice()
    {
        if (_growAsk is not { } ask) return;
        if (_growNotice is null)
        {
            _growNoticeText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var yes = new Button { Content = L("Anhängen"), Classes = { "primary" }, Margin = new(0, 0, 8, 0) };
            var no = new Button { Content = L("Nein, danke"), Classes = { "ghost" } };
            yes.Click += (_, _) => AnswerGrow(true);
            no.Click += (_, _) => AnswerGrow(false);
            _growNotice = new Border
            {
                CornerRadius = new(12), Padding = new(14, 10), Margin = new(12, 0, 12, 76), BorderThickness = new(1), ZIndex = 61, MaxWidth = 520,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                BoxShadow = BoxShadows.Parse("0 4 18 0 #33000000"),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children = { _growNoticeText, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { yes, no } } },
                },
            };
            _growNotice.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
            _growNotice.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsAccent"));
            Grid.SetRowSpan(_growNotice, 2);
        }
        var what = ask.Steps.Count == 1 ? StepLabel(ask.Steps[0]) : L("{0} neue Verse", ask.Steps.Count);
        _growNoticeText!.Text = L("Im Ordner „{0}“ ist neu: {1}. Sollen neue Verse dieses Ordners hinten an den Lesepfad „{2}“ angehängt werden? Was du am Lesepfad geändert hast, bleibt so. Diese Frage kommt nur einmal.",
            ask.FolderName, what, ask.PathName);
        Panel host = ReaderHost;
        if (!ReaderHost.IsEffectivelyVisible && _exRoot is { IsEffectivelyVisible: true } explorer) host = explorer;
        if (_growNotice.Parent is Panel now && now != host) now.Children.Remove(_growNotice);
        if (_growNotice.Parent is null)
        {
            if (host is DockPanel) DockPanel.SetDock(_growNotice, Dock.Bottom);
            _growNotice.Margin = host == ReaderHost ? new(12, 0, 12, 76) : new(12, 6, 12, 6);
            if (host is DockPanel) host.Children.Insert(0, _growNotice); else host.Children.Add(_growNotice);
        }
    }

    /// <summary>"Anhängen": the verses go onto the end of the path as it is now, and later ones of the folder follow by themselves. "Nein": nothing changes.</summary>
    void AnswerGrow(bool append)
    {
        var ask = _growAsk;
        _growAsk = null;
        if (_growNotice?.Parent is Panel host) host.Children.Remove(_growNotice);
        if (ask is null || !append) return;
        if (ask.Vault != _vault.Name) { StatusText.Text = L("Der Vault wurde gewechselt; der Lesepfad blieb, wie er war."); return; }
        ReadingPath? path;
        try { path = _vault.GetReadingPath(ask.PathId); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { path = null; }
        if (path is null) { StatusText.Text = L("Den Lesepfad „{0}“ gibt es nicht mehr.", ask.PathName); return; }
        _settings.PathGrowAppend.Add(PathGrowKey(path)); SaveSettings();
        var fresh = ask.Steps.Where(s => !path.Steps.Any(k => StepHolds(k, s))).ToList();   // read anew: the path may have changed while the note was open
        if (fresh.Count > 0) AddSteps(path, fresh);
        else StatusText.Text = L("„{0}“ hat diese Verse schon.", path.Name);
    }
}
