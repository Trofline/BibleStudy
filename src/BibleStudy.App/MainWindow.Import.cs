using Avalonia.Platform.Storage;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Import of Bible files the user owns (private use, stored only locally).</summary>
public partial class MainWindow
{
    /// <summary>Set by automated tests of the window: nothing is opened on the screen of the person who runs them.</summary>
    internal static bool NoShell;
    bool _importing;
    string? _lastImportReport, _lastImportReportName;

    void InitImport()
    {
        ImportBibleBtn.Click += async (_, _) => await ImportBibleAsync();
        RemoveBibleBtn.Click += async (_, _) => await RemoveImportedAsync();
        ImportCheckBtn.Click += (_, _) =>
        {
            if (_lastImportReport is null) return;
            var path = Path.Combine(ExportFolder, _lastImportReportName);
            File.WriteAllText(path, _lastImportReport, new System.Text.UTF8Encoding(false));
            ImportBibleInfo.Text = L("Prüfbericht gespeichert: {0}. Gib ihn zusammen mit der Quelldatei an eine KI und lade die korrigierte USFM-Datei wieder hier hinein.", path);
            if (!NoShell) try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExportFolder) { UseShellExecute = true }); } catch { }
        };
        _ = RefreshLocalBibleBoxAsync();
    }

    async Task RefreshLocalBibleBoxAsync()
    {
        var mine = await _local.GetTranslationsAsync();
        LocalBibleBox.ItemsSource = mine.ToList();
        if (mine.Count > 0 && LocalBibleBox.SelectedIndex < 0) LocalBibleBox.SelectedIndex = 0;
    }

    async Task ImportBibleAsync()
    {
        if (_importing) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("Bibeldatei wählen"), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L("Bibeldateien")) { Patterns = ["*.bblx", "*.mybible", "*.xml", "*.osis", "*.usx", "*.usfm", "*.sfm", "*.zip", "*.md", "*.markdown", "*.txt", "*.csv", "*.tsv", "*.json", "*.html", "*.htm", "*.xhtml", "*.epub", "*.docx", "*.odt", "*.rtf", "*.fb2", "*.pdf"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await ImportBibleFromAsync(path, string.IsNullOrWhiteSpace(ImportLangBox.Text) ? "de" : ImportLangBox.Text.Trim());
    }

    /// <summary>Imports a Bible file; an already imported translation with the same id is replaced and its search index rebuilt on demand.</summary>
    internal async Task<string> ImportBibleFromAsync(string path, string language)
    {
        if (_importing) return L("Ein Import läuft bereits.");
        _importing = true; ImportBibleBtn.IsEnabled = false;
        var progress = new Progress<string>(m => ImportBibleInfo.Text = m);
        try
        {
            ImportBibleInfo.Text = L("Datei wird gelesen …");
            var bible = await Task.Run(() => BibleImporters.Read(path, language, progress));
            if (bible.Verses.Count == 0) throw new InvalidDataException(L("Keine Verse gefunden."));
            ImportBibleInfo.Text = L("{0} Verse gelesen, werden gespeichert …", bible.Verses.Count);
            var existed = (await _local.GetTranslationsAsync()).Any(t => t.Name == bible.Name);
            var id = await Task.Run(() => _local.Save(bible, path));
            if (existed) await RealignMarksAsync(id);   // the text may have moved by a letter: marks keep their words
            _store.ForgetTranslation(id); _corpora.Remove(id); _strongIndexes.Remove(id);   // text changed: rebuild search index and concordance on demand
            await RefreshLocalBibleBoxAsync();
            await ReloadTranslationsAsync(id);
            ImportBibleInfo.Text = L("„{0}“ importiert: {1} Verse, {2} Fußnoten. Sie steht jetzt in der Übersetzungsliste ({3}).", bible.Name, bible.Verses.Count, bible.Footnotes.Count, bible.ShortName);
            try
            {
                var check = ImportCheck.Analyze(bible);
                _lastImportReport = ImportCheck.BuildReport(bible, check);
                _lastImportReportName = SafeName(bible.ShortName, "import") + "-import-check.md";
                ImportCheckBtn.IsVisible = true;
                ImportBibleInfo.Text += " " + (check.Clean ? L("Die Prüfung fand nichts Auffälliges.") : L("Die Prüfung fand {0} auffällige Stellen: Der Bericht für eine KI lässt sich speichern.", check.Counts.Values.Sum()));
            }
            catch { ImportCheckBtn.IsVisible = false; /* the check is a help, never a reason to fail an import */ }
            return ImportBibleInfo.Text;
        }
        catch (Exception ex) { ImportBibleInfo.Text = L("Import fehlgeschlagen: ") + ex.Message; return ImportBibleInfo.Text; }
        finally { _importing = false; ImportBibleBtn.IsEnabled = true; }
    }

    async Task RemoveImportedAsync()
    {
        if (LocalBibleBox.SelectedItem is not TranslationInfo t) return;
        _local.Remove(t.Id);
        _store.ForgetTranslation(t.Id); _corpora.Remove(t.Id); _strongIndexes.Remove(t.Id);   // its search index goes with it
        await RefreshLocalBibleBoxAsync();
        ImportBibleInfo.Text = L("„{0}“ wurde aus der Anwendung entfernt (deine Originaldatei bleibt unberührt).", t.Name);
        await ReloadTranslationsAsync(null);
    }

    /// <summary>Reloads the translation list and optionally switches to one of them.</summary>
    async Task ReloadTranslationsAsync(string? select)
    {
        _all = Named(await _provider.GetTranslationsAsync());
        _langs = _all.Select(x => x.LanguageName).Where(l => l != "").Distinct().Order().ToList();
        var target = _all.FirstOrDefault(x => x.Id == (select ?? (TranslationBox.SelectedItem as TranslationInfo)?.Id)) ?? _all.FirstOrDefault(x => x.Id == "eng_kjv");
        if (target is null) return;
        // the lists are rebuilt without the boxes reacting at every step (each step started a load of its own, and a slow one could finish last
        // and leave the book list of a half-built state: after renaming or importing only a few books were there until one switched away and back)
        _quietPickers = true;
        try
        {
            LanguageBox.ItemsSource = _langs;
            LanguageBox.SelectedItem = target.LanguageName;
            _pendingTranslation = target.Id;
            FillTranslations();
        }
        finally { _quietPickers = false; }
        UpdatePickerLabels();
        await LoadBooksAsync();
    }
}
