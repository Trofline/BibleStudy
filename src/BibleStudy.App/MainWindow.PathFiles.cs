using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Giving reading paths to others: exported to a file (with the marks on their verses, if wished), read in by someone else either to keep or only
/// to look at once (until the program closes), and the marks either taken over into the vault or only used to tint the verses for now.
/// </summary>
public partial class MainWindow
{
    // paths and marks only looked at: in memory, never in a vault (ids below zero: nothing saves them by mistake)
    readonly List<ReadingPath> _lookPaths = [];
    readonly List<Mark> _lookMarks = [];
    long _lookSerial;

    static bool LookedAt(ReadingPath p) => p.Id < 0;

    /// <summary>A changed path: into the vault, or (looked at only) into memory.</summary>
    void StorePath(ReadingPath p)
    {
        if (!LookedAt(p)) { _vault.UpdateReadingPath(p); return; }
        var i = _lookPaths.FindIndex(x => x.Id == p.Id);
        if (i >= 0) _lookPaths[i] = p;
    }

    void DropPath(long id)
    {
        if (id < 0) _lookPaths.RemoveAll(p => p.Id == id);
        else _vault.DeleteReadingPath(id);
    }

    /// <summary>The marks only looked at that lie in this chapter (they show as the marks of another vault do: to look at, not to change).</summary>
    IEnumerable<Mark> LookMarksIn(string book, int chapter) => _lookMarks.Where(m => m.Ref.Book == book && m.Ref.Chapter == chapter);

    /// <summary>A path looked at becomes one of the vault after all.</summary>
    long KeepLookedPath(ReadingPath p)
    {
        var id = ReadingPathFile.KeepPaths(_vault, [p])[0];
        _lookPaths.RemoveAll(x => x.Id == p.Id);
        return id;
    }

    // ---- export -------------------------------------------------------------------------------------------------------

    /// <summary>Export: this path or all of them, with the marks on their verses or without.</summary>
    void AskExportPaths(Control anchor, ReadingPath? current)
    {
        var all = ReadingPaths();
        if (all.Count == 0) { StatusText.Text = L("Noch kein Lesepfad zum Exportieren."); return; }
        var onlyThis = new RadioButton { GroupName = "pathexport", Content = current is null ? "" : L("Nur „{0}“", current.Name), IsChecked = current is not null, IsVisible = current is not null };
        var everything = new RadioButton { GroupName = "pathexport", Content = L("Alle Lesepfade ({0})", all.Count), IsChecked = current is null };
        var withMarks = new CheckBox { Content = L("Mit den Markierungen auf diesen Versen"), IsChecked = true };
        var help = new TextBlock { Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 300, Text = L("Dann steht in der Datei genau, welche Stellen markiert sind und in welcher Farbe. Wer sie einliest, kann die Verse damit einfärben oder die Markierungen übernehmen.") };
        var save = new Button { Content = L("Speichern …"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var fly = new Flyout { Content = new StackPanel { Spacing = 8, MaxWidth = 320, Children = { new TextBlock { Text = L("Lesepfade exportieren"), FontWeight = Avalonia.Media.FontWeight.SemiBold }, onlyThis, everything, withMarks, help, save } } };
        save.Click += async (_, _) =>
        {
            fly.Hide();
            var paths = onlyThis.IsChecked == true && current is not null ? [current] : all;
            await ExportPathsAsync(anchor, paths, withMarks.IsChecked == true);
        };
        fly.ShowAt(anchor);
    }

    async Task ExportPathsAsync(Control from, List<ReadingPath> paths, bool withMarks)
    {
        var provider = TopLevel.GetTopLevel(from)?.StorageProvider ?? StorageProvider;   // the window of the paths on the PC; the phone has one screen
        var stem = paths.Count == 1 ? paths[0].Name : L("Lesepfade");
        foreach (var c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L("Lesepfade exportieren"), SuggestedFileName = stem + ReadingPathFile.Extension,
            FileTypeChoices = [new FilePickerFileType(L("Lesepfade")) { Patterns = ["*" + ReadingPathFile.Extension] }],
        });
        await using var target = await file.SaveTargetAsync();
        if (target?.Path is not { } path) return;
        try
        {
            var text = ReadingPathFile.Write(_vault, paths, withMarks);
            await File.WriteAllTextAsync(path, text, new System.Text.UTF8Encoding(false));
            StatusText.Text = withMarks ? L("{0} Lesepfade mit ihren Markierungen exportiert: {1}", paths.Count, path) : L("{0} Lesepfade exportiert: {1}", paths.Count, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = L("Export fehlgeschlagen: ") + ex.Message; }
    }

    // ---- import -------------------------------------------------------------------------------------------------------

    async Task ImportPathsAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("Lesepfade importieren"), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L("Lesepfade")) { Patterns = ["*" + ReadingPathFile.Extension, "*.json"] }, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || await files[0].LocalPathAsync() is not { } path) return;
        List<ReadingPathFile.Imported> found;
        try { found = ReadingPathFile.Read(await File.ReadAllTextAsync(path)); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { StatusText.Text = L("Diese Datei enthält keine Lesepfade."); return; }
        if (found.Count == 0) { StatusText.Text = L("Diese Datei enthält keine Lesepfade."); return; }
        await ImportPathsAsync(found);
    }

    /// <summary>Asks how: keep the paths or only look at them; tint the verses with the marks, take the marks over, or leave them out.</summary>
    internal async Task ImportPathsAsync(List<ReadingPathFile.Imported> found, int? keepChoice = null, int? markChoice = null)
    {
        var names = string.Join("\n", found.Select(f => "• " + L("„{0}“ · {1} Schritte", f.Path.Name, f.Path.Steps.Count)));
        var keep = keepChoice ?? await AskAsync(L("Lesepfade importieren"),
            names + "\n\n" + L("Behalten: Die Lesepfade kommen in diesen Vault und bleiben. Nur ansehen: Sie gelten nur jetzt, bis das Programm geschlossen wird; nichts wird gespeichert."),
            L("Behalten"), L("Nur ansehen"), L("Abbrechen"));
        if (keep is not (0 or 1)) return;
        var marks = found.SelectMany(f => f.Marks).ToList();
        var how = 2;   // 0 = tint only, 1 = take over, 2 = leave out
        if (marks.Count > 0)
            how = markChoice ?? await AskAsync(L("Markierungen"),
                L("Die Datei bringt {0} Markierungen auf diesen Versen mit.", marks.Count) + "\n\n" +
                L("Nur einfärben: Die Verse erscheinen in ihren Farben, solange das Programm offen ist; deine Markierungen bleiben, wie sie sind. Übernehmen: Die Markierungen werden in diesem Vault gespeichert (was es schon gibt, kommt nicht doppelt)."),
                L("Nur einfärben"), L("Übernehmen und speichern"), L("Ohne Markierungen"));
        if (how < 0) return;

        long? first = null;
        if (keep == 0) { var ids = ReadingPathFile.KeepPaths(_vault, found.Select(f => f.Path)); first = ids.FirstOrDefault(); }
        else
            foreach (var f in found)
            {
                var id = --_lookSerial;
                _lookPaths.Add(f.Path with { Id = id, Name = f.Path.Name });
                first ??= id;
            }
        var added = 0;
        if (how == 0) foreach (var m in marks) _lookMarks.Add(m with { Id = long.MinValue / 2 - _lookMarks.Count });
        else if (how == 1) added = ReadingPathFile.KeepMarks(_vault, marks);

        StatusText.Text = (keep == 0 ? L("{0} Lesepfade importiert.", found.Count) : L("{0} Lesepfade zum Ansehen geöffnet (bis das Programm schließt).", found.Count))
            + (how == 0 ? " " + L("Die Verse sind eingefärbt, gespeichert ist nichts.") : how == 1 ? " " + L("{0} Markierungen übernommen.", added) : "");
        if (how == 1) RefreshCollection();
        AfterPathChange();
        if (ReadingPaths().FirstOrDefault(p => p.Id == first) is { Steps.Count: > 0 } start) GoToStep(start, 0);
    }
}
