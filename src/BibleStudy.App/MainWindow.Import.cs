using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Import of Bible files the user owns (private use, stored only locally).</summary>
public partial class MainWindow
{
    /// <summary>Set by automated tests of the window: nothing is opened on the screen of the person who runs them.</summary>
    public static bool NoShell;
    bool _importing;
    string? _lastImportReport, _lastImportReportName, _lastImportId;
    ParsedBible? _lastImportBible;

    void InitImport()
    {
        ImportLangBox.Text = BibleStudy.Core.Loc.Code;   // the language of the program, not always German
        ImportBibleBtn.Click += async (_, _) => await ImportBibleAsync();
        RemoveBibleBtn.Click += async (_, _) => await RemoveImportedAsync();
        ImportAiBtn.Click += async (_, _) => await CorrectWithAiAsync();
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
        await ImportBibleCoreAsync(path, string.IsNullOrWhiteSpace(ImportLangBox.Text) ? BibleStudy.Core.Loc.Code : ImportLangBox.Text.Trim(), ConfirmReplaceAsync);
    }

    /// <summary>Imports a Bible file without asking (the once-only reading again of all imports).</summary>
    internal Task<string> ImportBibleFromAsync(string path, string language) => ImportBibleCoreAsync(path, language, null);

    /// <summary>Before a Bible that is already here is replaced: what would change. Gives the Bible to save (verses that were unticked keep their old text), null = keep the old one.</summary>
    internal async Task<ParsedBible?> ConfirmReplaceAsync(string id, ParsedBible bible)
    {
        var old = await _local.GetAllVersesAsync(id);
        var diff = await Task.Run(() => ImportDiff.Compare(old, bible));
        if (diff.None) return bible;
        ParsedBible? chosen = null;
        var ticks = new List<(CheckBox Box, ImportDiff.Change Change)>();
        var win = new Window { Title = L("Bibel ersetzen?"), Width = 720, Height = 620, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new StackPanel { Spacing = 10 };
        foreach (var c in diff.Samples)
        {
            var (was, now) = ImportDiff.Focus(c.Old, c.New);
            var tick = new CheckBox { IsChecked = true, Content = new TextBlock { Text = $"{BookName(c.Book)} {c.Chapter},{c.Verse}", FontWeight = FontWeight.SemiBold } };
            ticks.Add((tick, c));
            var row = new StackPanel { Spacing = 2, Children = { tick } };
            if (c.Old.Length > 0) row.Children.Add(new TextBlock { Text = "− " + was, TextWrapping = TextWrapping.Wrap, Foreground = Res("BsMuted") });
            if (c.New.Length > 0) row.Children.Add(new TextBlock { Text = "+ " + now, TextWrapping = TextWrapping.Wrap });
            list.Children.Add(row);
        }
        var ok = new Button { Content = L("Ersetzen"), Margin = new(8, 0, 0, 0) }; ok.Classes.Add("primary");
        var cancel = new Button { Content = L("Abbrechen"), Margin = new(8, 0, 0, 0) };
        ok.Click += (_, _) => { chosen = ImportDiff.Without(bible, ticks.Where(t => t.Box.IsChecked != true).Select(t => t.Change).ToList()); win.Close(); };
        cancel.Click += (_, _) => win.Close();
        var head = new StackPanel
        {
            Spacing = 6, Margin = new(0, 0, 0, 12),
            Children =
            {
                new TextBlock { Text = L("„{0}“ ersetzen?", bible.Name), FontSize = 18, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = L("{0} Verse geändert, {1} neu, {2} fallen weg.", diff.Changed, diff.Added, diff.Removed) + (diff.Changed + diff.Added + diff.Removed > diff.Samples.Count ? " " + L("Unten stehen die ersten {0}.", diff.Samples.Count) : ""), TextWrapping = TextWrapping.Wrap },
            },
        };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0), Children = { ok, cancel } };
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(buttons, Dock.Bottom);
        win.Content = new DockPanel { Margin = new(22), Children = { head, buttons, new ScrollViewer { Content = list } } };
        await ShowModalAsync(win);
        return chosen;
    }

    /// <summary>Imports a Bible file; an already imported translation with the same id is replaced and its search index rebuilt on demand.</summary>
    async Task<string> ImportBibleCoreAsync(string path, string language, Func<string, ParsedBible, Task<ParsedBible?>>? confirm)
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
            var earlier = (await _local.GetTranslationsAsync()).FirstOrDefault(t => t.Name == bible.Name);
            var existed = earlier is not null;
            if (earlier is not null && confirm is not null)
            {
                if (await confirm(earlier.Id, bible) is not { } wanted) return ImportBibleInfo.Text = L("Nicht ersetzt: „{0}“ bleibt, wie sie war.", bible.Name);
                bible = wanted;
            }
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
                _lastImportBible = bible; _lastImportId = id;
                ImportAiBtn.IsVisible = ImportCheck.CorrectionPrompt(bible).Length > 0;
                ImportBibleInfo.Text += " " + (check.Clean ? L("Die Prüfung fand nichts Auffälliges.") : L("Die Prüfung fand {0} auffällige Stellen: Der Bericht für eine KI lässt sich speichern.", check.Counts.Values.Sum()));
            }
            catch { ImportCheckBtn.IsVisible = false; ImportAiBtn.IsVisible = false; /* the check is a help, never a reason to fail an import */ }
            return ImportBibleInfo.Text;
        }
        catch (Exception ex) { ImportBibleInfo.Text = L("Import fehlgeschlagen: ") + ex.Message; return ImportBibleInfo.Text; }
        finally { _importing = false; ImportBibleBtn.IsEnabled = true; }
    }

    /// <summary>Hands the verses with reading errors to Claude; the answer goes through the same window as any other replacement.</summary>
    async Task CorrectWithAiAsync()
    {
        if (_lastImportBible is not { } bible) return;
        if (_settings.AiEngine == "ollama") { ImportBibleInfo.Text = L("Die Korrektur geht bisher nur mit Claude Code, nicht mit Ollama. Der Prüfbericht lässt sich aber speichern."); return; }
        var exe = ClaudeCli.FindExecutable();
        if (exe is null) { ImportBibleInfo.Text = L("Claude Code wurde nicht gefunden (siehe Einstellungen)."); return; }
        ImportAiBtn.IsEnabled = false;
        try
        {
            ImportBibleInfo.Text = L("Claude korrigiert die auffälligen Verse …");
            var answer = new System.Text.StringBuilder(); string? final = null; string? error = null;
            var workDir = Path.Combine(_root, "ai", "import-check");
            Directory.CreateDirectory(workDir);
            await ClaudeCli.RunAsync(exe, workDir, ImportCheck.CorrectionPrompt(bible), null, "You correct reading errors in Bible texts and answer only in the requested form.", ClaudeCli.WriteMcpConfig(workDir, McpExe),
                string.IsNullOrWhiteSpace(_settings.AiModel) ? null : _settings.AiModel,
                ev => { if (ev is ClaudeTextDelta d) lock (answer) answer.Append(d.Text); else if (ev is ClaudeResult r) { if (r.IsError) error = r.Text; else final = r.Text; } }, CancellationToken.None);
            if (error is not null) { ImportBibleInfo.Text = L("Fehler: ") + error; return; }
            await ApplyAiAnswerAsync(final is { Length: > 0 } ? final : answer.ToString());
        }
        catch (Exception ex) { ImportBibleInfo.Text = L("Fehler: ") + ex.Message; }
        finally { ImportAiBtn.IsEnabled = true; }
    }

    /// <summary>Applies an answer of the AI: shown first, saved only when it is accepted; marks keep their words.</summary>
    internal async Task ApplyAiAnswerAsync(string answer)
    {
        if (_lastImportBible is not { } bible || _lastImportId is not { } id) return;
        var (corrected, applied, refused) = ImportCheck.ApplyCorrections(bible, answer);
        if (applied == 0) { ImportBibleInfo.Text = L("Die Antwort enthielt keine brauchbare Korrektur ({0} abgelehnt).", refused); return; }
        if (await ConfirmReplaceAsync(id, corrected) is not { } wanted) { ImportBibleInfo.Text = L("Nicht ersetzt: „{0}“ bleibt, wie sie war.", bible.Name); return; }
        await Task.Run(() => _local.Save(wanted));
        await RealignMarksAsync(id);
        _store.ForgetTranslation(id); _corpora.Remove(id); _strongIndexes.Remove(id);
        _lastImportBible = wanted;
        ImportAiBtn.IsVisible = ImportCheck.CorrectionPrompt(wanted).Length > 0;
        await ReloadTranslationsAsync(id);
        ImportBibleInfo.Text = L("{0} Verse korrigiert, {1} Vorschläge abgelehnt, weil sie zu viel geändert hätten.", applied, refused);
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
