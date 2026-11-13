using Avalonia.Platform.Storage;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Once after an update of the import code (1.6.12): the Bibles the user imported earlier were read by the older code. The program offers to read
/// them again from their files, with the reasons, and asks twice before it lets the old copy be. Marks stay where their words are.
/// </summary>
public partial class MainWindow
{
    async Task OfferReimportAsync()
    {
        if (_settings.ImportRefreshAsked >= LocalBibleStore.CurrentImporter) return;
        var old = _local.GetOutdated();
        if (old.Count == 0) { _settings.ImportRefreshAsked = LocalBibleStore.CurrentImporter; SaveSettings(); return; }
        // another window (notice, update question) is open: wait for it
        for (var i = 0; i < 30 && (OwnedWindows.Count > 0 || SplashWindow.IsShowing); i++) await Task.Delay(2000);
        if (!IsVisible || OwnedWindows.Count > 0) return;   // next start
        var names = string.Join(", ", old.Select(o => "„" + o.Name + "“"));
        var answer = await AskAsync(L("Importierte Bibeln neu einlesen?"),
            L("Das Einlesen eigener Dateien ist in diesem Update deutlich besser geworden (PDF-Bibeln: fehlende Kapitel und Verse, doppelte Seiten, zerteilte Wörter; Webseiten: Nummern und Marker im Text; dazu Überschriften). Deine bisher importierten Bibeln sind noch nach der alten Art eingelesen: {0}. Sollen sie jetzt neu eingelesen werden? Du wählst dafür jeweils die Datei, aus der du sie damals importiert hast. Deine Markierungen bleiben bei ihren Wörtern, vor dem Neu-Einlesen wird eine Sicherung angelegt.", names),
            L("Ja, neu einlesen"), L("Nein"));
        if (answer != 0)
        {
            var sure = await AskAsync(L("Wirklich nicht neu einlesen?"),
                L("Ohne das Neu-Einlesen bleiben die Fehler der alten Art in deinen importierten Bibeln (zum Beispiel fehlende erste Verse, doppelter Text oder Reste von Nummern). Diese Frage kommt nicht wieder; du kannst eine Bibel jederzeit in den Einstellungen unter „Eigene Übersetzung importieren“ erneut einlesen."),
                L("Doch neu einlesen"), L("Ja, so lassen"));
            if (sure != 0) { _settings.ImportRefreshAsked = LocalBibleStore.CurrentImporter; SaveSettings(); return; }
        }
        _settings.ImportRefreshAsked = LocalBibleStore.CurrentImporter; SaveSettings();
        await ReimportAsync(old);
    }

    /// <summary>Reads each of these Bibles again from its file (the remembered one, else the user is asked for it).</summary>
    internal async Task<int> ReimportAsync(List<LocalBibleStore.Outdated> list, Func<LocalBibleStore.Outdated, Task<string?>>? ask = null)
    {
        var label = L("vor dem Neu-Einlesen");
        try { await Task.Run(() => BackupData(label)); } catch { /* a backup that fails must not stop the better import; the old text stays until the new one is saved */ }
        var done = 0;
        foreach (var o in list)
        {
            var path = o.Source is { Length: > 0 } s && File.Exists(s) ? s : await FindMovedFileAsync(o) ?? await (ask ?? AskForFileAsync)(o);
            if (path is null) continue;
            await ImportBibleFromAsync(path, o.Language);
            done++;
        }
        ImportBibleInfo.Text = L("{0} von {1} Bibeln neu eingelesen.", done, list.Count);
        return done;
    }

    /// <summary>A file that is no longer where it was: the same name is taken, a similar name only after asking (it could be another Bible).</summary>
    async Task<string?> FindMovedFileAsync(LocalBibleStore.Outdated o)
    {
        if (o.Source is not { Length: > 0 } source) return null;
        string Known(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var places = new[] { Known(Environment.SpecialFolder.MyDocuments), Path.Combine(Known(Environment.SpecialFolder.UserProfile), "Downloads"), Known(Environment.SpecialFolder.DesktopDirectory) };
        var hit = await Task.Run(() => ImportFileFinder.Find(source, places));
        if (hit is null) return null;
        if (hit.Exact) return hit.Path;
        return await AskAsync(L("Ist das die richtige Datei?"), L("Die Datei von „{0}“ liegt nicht mehr an ihrem Platz. Gefunden wurde diese ähnliche Datei:\n{1}", o.Name, hit.Path), L("Ja, diese Datei"), L("Nein")) == 0 ? hit.Path : null;
    }

    async Task<string?> AskForFileAsync(LocalBibleStore.Outdated o)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L("Datei für „{0}“ wählen (Abbrechen: diese Bibel überspringen)", o.Name), AllowMultiple = false });
        return files.Count > 0 ? await files[0].LocalPathAsync() : null;
    }

    /// <summary>After a Bible was read again: marks in it keep their words, also where the text moved (a letter more or less before them).</summary>
    async Task RealignMarksAsync(string translationId)
    {
        foreach (var name in VaultNames().Where(n => !IsGuestVault(n)))
        {
            VaultStore store;
            try { store = StoreOf(name); } catch { continue; }
            foreach (var m in store.GetMarks().Where(m => m.TranslationId == translationId && !m.WholeVerse && m.EndVerse <= m.Ref.Verse && m.Text.Length > 0))
            {
                try
                {
                    var chapter = await _local.GetChapterAsync(translationId, m.Ref.Book, m.Ref.Chapter);
                    if (chapter.Verses.FirstOrDefault(v => v.Number == m.Ref.Verse) is not { } verse) continue;
                    if (MarkLogic.Realign(m.Text, m.Start, MarkLogic.RawText(verse.Segments)) is { } at && (at.Start != m.Start || at.End != m.End)) store.SetMarkRange(m.Id, at.Start, at.End);
                }
                catch { /* one mark that cannot be moved stays where it is */ }
            }
        }
    }
}
