using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>Adding an own language: save the template and the AI instruction, let an AI translate, import the result.</summary>
public partial class MainWindow
{
    void InitLanguageImport()
    {
        LangTemplateBtn.Click += async (_, _) => await SaveLanguageTemplateAsync();
        LangImportBtn.Click += async (_, _) => await ImportLanguageAsync();
        LangMissingBtn.Click += async (_, _) => await SaveMissingTemplateAsync();
        var (done, total) = LanguageFiles.Progress(Loc.Code);
        LangProgressText.Text = Loc.Code == "de" ? "" : L("Aktuelle Sprache: {0} % übersetzt ({1} von {2} Texten).", total == 0 ? 100 : done * 100 / total, done, total);
    }

    /// <summary>Only the texts the current language still lacks, plus the AI instruction: for completing a language after an update.</summary>
    async Task SaveMissingTemplateAsync()
    {
        try
        {
            var name = Loc.Languages.FirstOrDefault(l => l.Code == Loc.Code)?.Name ?? Loc.Code;
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BibleStudy-Sprache");
            Directory.CreateDirectory(dir);
            var utf8 = new System.Text.UTF8Encoding(false);
            await File.WriteAllTextAsync(Path.Combine(dir, Loc.Code + "-fehlend.json"), LanguageFiles.MissingTemplate(Loc.Code, name), utf8);
            await File.WriteAllTextAsync(Path.Combine(dir, "ki-anweisung.txt"), LanguageFiles.Instruction(name), utf8);
            LangImportInfo.Text = L("Fehlende Texte gespeichert in: {0}\nNach der Übersetzung als {1}.json speichern und importieren; sie werden zu den vorhandenen hinzugefügt.", dir, Loc.Code);
            try { Shell.Open(dir); } catch { /* the folder is named above */ }
        }
        catch (Exception ex) { LangImportInfo.Text = L("Fehler: ") + ex.Message; }
    }

    async Task SaveLanguageTemplateAsync()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BibleStudy-Sprache");
            Directory.CreateDirectory(dir);
            var utf8 = new System.Text.UTF8Encoding(false);
            await File.WriteAllTextAsync(Path.Combine(dir, "sprache-vorlage.json"), LanguageFiles.Template(), utf8);
            await File.WriteAllTextAsync(Path.Combine(dir, "ki-anweisung.txt"), LanguageFiles.Instruction("<language / Sprache>"), utf8);
            LangImportInfo.Text = L("Vorlage und Anweisung gespeichert in: {0}\nGib beide Dateien einer KI (z. B. Claude), speichere ihre Antwort als Datei mit dem Sprachkürzel, z. B. it.json, und importiere sie hier.", dir);
            try { Shell.Open(dir); } catch { /* the folder is named above */ }
        }
        catch (Exception ex) { LangImportInfo.Text = L("Fehler: ") + ex.Message; }
    }

    async Task ImportLanguageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("Sprachdatei wählen"), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L("Sprachdateien")) { Patterns = ["*.json"] }],
        });
        if (files.Count == 0 || await files[0].LocalPathAsync() is not { } path) return;
        try
        {
            var code = LanguageFiles.CodeFrom(path) ?? throw new FormatException(L("Der Dateiname muss das Sprachkürzel sein, z. B. it.json oder pt-br.json."));
            if (code is "de" or "en" or "es" or "fr" or "ru") throw new FormatException(L("Diese Sprache gehört schon zum Programm; wähle ein anderes Kürzel."));
            var result = LanguageFiles.Check(await File.ReadAllTextAsync(path));
            var target = Path.Combine(AppPaths.Root, "lang");
            Directory.CreateDirectory(target);
            var targetFile = Path.Combine(target, code + ".json");
            var merged = LanguageFiles.Merge(File.Exists(targetFile) ? await File.ReadAllTextAsync(targetFile) : null, result);
            await File.WriteAllTextAsync(targetFile, LanguageFiles.Serialize(merged), new System.Text.UTF8Encoding(false));
            LangImportInfo.Text = L("Sprache „{0}“ hinzugefügt: {1} Texte übernommen, {2} noch deutsch, {3} übersprungen (Platzhalter passten nicht). Sie steht nach einem Neustart in der Liste.", result.Name, result.Accepted, result.Untranslated, result.Skipped);
            var choice = await AskAsync(L("Sprache ändern"), L("Die neue Sprache gilt nach einem Neustart des Programms. Jetzt neu starten?"), L("Jetzt neu starten"), L("Später"));
            if (choice == 0) { _settings.Language = code; SaveSettings(); Restart(); }
        }
        catch (FormatException ex) { LangImportInfo.Text = L("Diese Datei ist keine brauchbare Sprachdatei: {0}", ex.Message); }
        catch (Exception ex) { LangImportInfo.Text = L("Fehler: ") + ex.Message; }
    }
}
