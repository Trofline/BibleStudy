using Avalonia.Platform.Storage;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Import of Bible files the user owns (private use, stored only locally).</summary>
public partial class MainWindow
{
    bool _importing;

    void InitImport()
    {
        ImportBibleBtn.Click += async (_, _) => await ImportBibleAsync();
        RemoveBibleBtn.Click += async (_, _) => await RemoveImportedAsync();
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
            FileTypeFilter = [new FilePickerFileType(L("Bibeldateien")) { Patterns = ["*.bblx", "*.mybible", "*.xml", "*.osis", "*.usx", "*.usfm", "*.sfm", "*.zip", "*.md", "*.markdown", "*.txt", "*.csv", "*.tsv", "*.json", "*.html", "*.htm"] }],
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
            var id = await Task.Run(() => _local.Save(bible));
            _store.ForgetTranslation(id); _corpora.Remove(id); _strongIndexes.Remove(id);   // text changed: rebuild search index and concordance on demand
            await RefreshLocalBibleBoxAsync();
            await ReloadTranslationsAsync(id);
            ImportBibleInfo.Text = L("„{0}“ importiert: {1} Verse, {2} Fußnoten. Sie steht jetzt in der Übersetzungsliste ({3}).", bible.Name, bible.Verses.Count, bible.Footnotes.Count, bible.ShortName);
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
        _all = await _provider.GetTranslationsAsync();
        _langs = _all.Select(x => x.LanguageName).Where(l => l != "").Distinct().Order().ToList();
        LanguageBox.ItemsSource = _langs;
        var target = _all.FirstOrDefault(x => x.Id == (select ?? (TranslationBox.SelectedItem as TranslationInfo)?.Id)) ?? _all.FirstOrDefault(x => x.Id == "eng_kjv");
        if (target is null) return;
        _pendingTranslation = target.Id;
        if ((string?)LanguageBox.SelectedItem == target.LanguageName) FillTranslations(); else LanguageBox.SelectedItem = target.LanguageName;
    }
}
