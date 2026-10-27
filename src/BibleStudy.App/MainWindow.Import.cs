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
            Title = "Bibeldatei wählen", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Bibeldateien") { Patterns = ["*.bblx", "*.xml", "*.html", "*.htm"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        _importing = true; ImportBibleBtn.IsEnabled = false;
        var language = string.IsNullOrWhiteSpace(ImportLangBox.Text) ? "de" : ImportLangBox.Text.Trim();
        var progress = new Progress<string>(m => ImportBibleInfo.Text = m);
        try
        {
            ImportBibleInfo.Text = "Datei wird gelesen …";
            var bible = await Task.Run(() => BibleImporters.Read(path, language, progress));
            if (bible.Verses.Count == 0) throw new InvalidDataException("Keine Verse gefunden.");
            ImportBibleInfo.Text = $"{bible.Verses.Count} Verse gelesen, werden gespeichert …";
            var id = await Task.Run(() => _local.Save(bible));
            await RefreshLocalBibleBoxAsync();
            await ReloadTranslationsAsync(id);
            ImportBibleInfo.Text = $"„{bible.Name}“ importiert: {bible.Verses.Count} Verse, {bible.Footnotes.Count} Fußnoten. Sie steht jetzt in der Übersetzungsliste ({bible.ShortName}).";
        }
        catch (Exception ex) { ImportBibleInfo.Text = "Import fehlgeschlagen: " + ex.Message; }
        finally { _importing = false; ImportBibleBtn.IsEnabled = true; }
    }

    async Task RemoveImportedAsync()
    {
        if (LocalBibleBox.SelectedItem is not TranslationInfo t) return;
        _local.Remove(t.Id);
        await RefreshLocalBibleBoxAsync();
        ImportBibleInfo.Text = $"„{t.Name}“ wurde aus der Anwendung entfernt (deine Originaldatei bleibt unberührt).";
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
