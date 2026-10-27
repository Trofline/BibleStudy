using System.Globalization;
using System.IO.Compression;
using System.Speech.Synthesis;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Original text (interlinear), reading aloud, and backup of the vault.</summary>
public partial class MainWindow
{
    const int GrundtextTab = 8, StrongTab = 9, KnowledgeTab = 10, AiTab = 11;

    readonly Dictionary<(string Book, int Chapter), Dictionary<int, List<InterlinearWord>>> _interlinear = [];
    VerseRef? _grundtextRef;
    SpeechSynthesizer? _speech;

    void InitStudy()
    {
        GrundtextCheckBtn.Click += async (_, _) => await AskClaudeToCheckAsync();
        SideTabs.SelectionChanged += async (_, _) =>
        {
            if (SideTabs.SelectedIndex == GrundtextTab && _selected is int v) await ShowGrundtextAsync(new VerseRef(_curBook, _curChapter, v));
        };
        SpeakBtn.Click += (_, _) => ToggleSpeak();
        ExportBtn.Click += async (_, _) => await ExportAsync();
        ImportBtn.Click += async (_, _) => await ImportAsync();
        Closing += (_, _) => _speech?.Dispose();
    }

    // ---- original text ------------------------------------------------------------------------------------------------

    async Task ShowGrundtextAsync(VerseRef r)
    {
        _grundtextRef = r;
        GrundtextPanel.Children.Clear();
        GrundtextCheckBtn.IsVisible = false;
        GrundtextInfo.Text = $"{Label(r)} · lade …";
        try
        {
            if (!_interlinear.TryGetValue((r.Book, r.Chapter), out var chapter))
                _interlinear[(r.Book, r.Chapter)] = chapter = await _hello.GetInterlinearAsync(r.Book, r.Chapter);
            if (_grundtextRef != r) return; // the user moved on
            if (!chapter.TryGetValue(r.Verse, out var words) || words.Count == 0) { GrundtextInfo.Text = $"{Label(r)} · keine Grundtextdaten für diesen Vers."; return; }
            var hebrew = words[0].IsHebrew;
            GrundtextPanel.FlowDirection = hebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            foreach (var w in words) GrundtextPanel.Children.Add(WordCell(w));
            GrundtextInfo.Text = $"{Label(r)} · {(hebrew ? "Hebräisch (von rechts nach links)" : "Griechisch")}, {words.Count} Wörter. Wort anklicken: Wörterbuch.";
            GrundtextSource.Text = HelloAoProvider.InterlinearAttribution + " · " + StrongsDictionary.Attribution;
            GrundtextCheckBtn.IsVisible = true;
        }
        catch (Exception ex) { GrundtextInfo.Text = "Grundtext nicht verfügbar: " + ex.Message; }
    }

    Control WordCell(InterlinearWord w)
    {
        var original = new TextBlock { Text = w.Text, FontSize = 22, FontFamily = UiSerif, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, FlowDirection = w.IsHebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        original.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        var translit = new TextBlock { Text = w.Translit, FontSize = 11, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        translit.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        var gloss = new TextBlock { Text = w.Gloss, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 110, TextAlignment = TextAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var strongs = new TextBlock { Text = w.Strongs, FontSize = 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        strongs.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsMuted"));
        var cell = new Border
        {
            Padding = new(10, 6), Margin = new(0, 0, 6, 6), CornerRadius = new(10), Cursor = new Cursor(StandardCursorType.Hand), FlowDirection = FlowDirection.LeftToRight,
            Child = new StackPanel { Spacing = 1, Children = { original, translit, gloss, strongs } },
        };
        cell.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
        ToolTip.SetTip(cell, string.IsNullOrEmpty(w.ParsingFull) ? w.Parsing : w.ParsingFull);
        cell.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
            foreach (var other in GrundtextPanel.Children.OfType<Border>()) other.ClearValue(Border.BorderBrushProperty);
            cell.BorderThickness = new(1.5);
            cell.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsAccent"));
            await ShowGrundtextDetailAsync(w);
        };
        cell.BorderThickness = new(1.5); // reserve the space of the selection frame
        cell.BorderBrush = Brushes.Transparent;
        return cell;
    }

    /// <summary>Detail view of one original word: grammar, dictionary entry, and ways to dig deeper.</summary>
    async Task ShowGrundtextDetailAsync(InterlinearWord w)
    {
        GrundtextDetail.Children.Clear();
        var head = new TextBlock { Text = $"{w.Text}  ·  {w.Translit}", FontSize = 20, FontFamily = UiSerif, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight };
        head.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        GrundtextDetail.Children.Add(head);
        GrundtextDetail.Children.Add(new TextBlock { Text = $"Bedeutung hier: {(string.IsNullOrEmpty(w.Gloss) ? "–" : w.Gloss)}", TextWrapping = TextWrapping.Wrap });
        GrundtextDetail.Children.Add(new TextBlock { Text = $"Grammatik: {(string.IsNullOrEmpty(w.ParsingFull) ? w.Parsing : w.ParsingFull)} ({w.Parsing})", TextWrapping = TextWrapping.Wrap });
        if (w.Strongs.Length > 1)
        {
            try
            {
                if (await _strongs.LookupAsync(w.Strongs) is { } entry) GrundtextDetail.Children.Add(StudyCard(entry));
            }
            catch (Exception ex) { GrundtextDetail.Children.Add(new TextBlock { Text = "Wörterbuch nicht verfügbar: " + ex.Message, TextWrapping = TextWrapping.Wrap }); }
            var occurrences = new Button { Content = "Alle Vorkommen", Margin = new(0, 0, 8, 0) };
            occurrences.Click += async (_, _) => await ShowConcordanceAsync(w.Strongs, $"{w.Strongs}  {w.Text}");
            var deeper = new Button { Content = "Mit Claude vertiefen", Classes = { "accent" } };
            deeper.Click += async (_, _) => await AskClaudeAboutWordAsync(w);
            GrundtextDetail.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { occurrences, deeper } });
        }
    }

    async Task AskClaudeAboutWordAsync(InterlinearWord w)
    {
        if (_grundtextRef is not { } r) return;
        var t = TranslationBox.SelectedItem as TranslationInfo;
        SideTabs.SelectedIndex = AiTab;
        AiInput.Text = $"Analysiere das Wort „{w.Text}“ ({w.Translit}, {w.Strongs}, {w.ParsingFull}) in {Label(r)} genauer: Bedeutungsspektrum, Grammatik und ihre Bedeutung hier, wie das Wort an anderen Stellen verwendet und übersetzt wird " +
                       $"(nutze get_strongs und get_word_occurrences mit eng_kjv) und ob {t?.Name} es hier treffend wiedergibt (get_passage, translation={t?.Id}). Genau und knapp.";
        await AiSendOrStopAsync();
    }

    // ---- Strong's search ----------------------------------------------------------------------------------------------

    List<StrongsEntry> _strongResults = [];

    void InitStrongSearch()
    {
        StrongSearchBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await SearchStrongAsync(); };
        StrongList.SelectionChanged += async (_, _) =>
        {
            if (StrongList.SelectedIndex is var i and >= 0 && i < _strongResults.Count) await ShowStrongDetailAsync(_strongResults[i]);
        };
    }

    async Task SearchStrongAsync()
    {
        var query = StrongSearchBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(query)) return;
        StrongInfo.Text = "Suche …";
        try
        {
            _strongResults = (await _strongs.SearchAsync(query)).ToList();
            StrongList.ItemsSource = _strongResults.Select(e => new Entry(default, $"{e.Id}  {e.Lemma}  ({e.Translit})\n{Shorten((e.Definition.Length > 0 ? e.Definition : e.KjvUsage).Trim(), 90)}")).ToList();
            StrongInfo.Text = _strongResults.Count == 0 ? "Nichts gefunden." : $"{_strongResults.Count} Treffer{(_strongResults.Count >= 60 ? " (erste 60)" : "")} · Treffer anklicken für Details";
            StrongDetail.Children.Clear();
            if (_strongResults.Count == 1) await ShowStrongDetailAsync(_strongResults[0]);
        }
        catch (Exception ex) { StrongInfo.Text = "Wörterbuch nicht verfügbar: " + ex.Message; }
    }

    Task ShowStrongDetailAsync(StrongsEntry e)
    {
        StrongDetail.Children.Clear();
        StrongDetail.Children.Add(StudyCard(e));
        var padded = $"{e.Id[0]}{int.Parse(e.Id[1..]):D4}";
        var occurrences = new Button { Content = "Alle Vorkommen", Margin = new(0, 0, 8, 0) };
        occurrences.Click += async (_, _) => await ShowConcordanceAsync(padded, $"{e.Id}  {e.Lemma}");
        var deeper = new Button { Content = "Mit Claude vertiefen" };
        deeper.Click += async (_, _) =>
        {
            SideTabs.SelectedIndex = AiTab;
            AiInput.Text = $"Erkläre {e.Id} ({e.Lemma}, {e.Translit}) ausführlich: Bedeutungsspektrum, wichtige Stellen (get_word_occurrences mit eng_kjv) und wie es sich zu verwandten Wörtern verhält. Zitiere Verse nur aus den Werkzeugen.";
            await AiSendOrStopAsync();
        };
        StrongDetail.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { occurrences, deeper } });
        return Task.CompletedTask;
    }

    async Task AskClaudeToCheckAsync()
    {
        if (_grundtextRef is not { } r) return;
        var t = TranslationBox.SelectedItem as TranslationInfo;
        SideTabs.SelectedIndex = AiTab;
        AiInput.Text = $"Prüfe Wort für Wort, ob die Übersetzung {t?.Name} ({t?.Id}) in {Label(r)} den Grundtext richtig und vollständig wiedergibt. " +
                       $"Nutze get_interlinear und get_passage (translation={t?.Id}) und bei Bedarf get_strongs. Nenne Abweichungen, Nuancen und mögliche andere Wiedergaben, genau und knapp.";
        await AiSendOrStopAsync();
    }

    // ---- reading aloud ------------------------------------------------------------------------------------------------

    void ToggleSpeak()
    {
        if (!OperatingSystem.IsWindows()) { StatusText.Text = "Vorlesen ist derzeit nur unter Windows verfügbar."; SpeakBtn.IsChecked = false; return; }
        if (_speech is { State: SynthesizerState.Speaking }) { _speech.SpeakAsyncCancelAll(); SpeakBtn.IsChecked = false; return; }
        if (_curChapterData is null || TranslationBox.SelectedItem is not TranslationInfo t) { SpeakBtn.IsChecked = false; return; }
        var verses = _selected is int v ? _curChapterData.Verses.Where(x => x.Number == v) : _curChapterData.Verses;
        var text = string.Join(" ", verses.Select(x => x.Text));
        if (string.IsNullOrWhiteSpace(text)) { SpeakBtn.IsChecked = false; return; }
        try
        {
            _speech ??= new SpeechSynthesizer();
            _speech.SpeakCompleted -= OnSpeakCompleted;
            _speech.SpeakCompleted += OnSpeakCompleted;
            var culture = CultureInfo.GetCultures(CultureTypes.NeutralCultures).FirstOrDefault(c => c.ThreeLetterISOLanguageName == t.Language || c.Name == t.Language);
            if (culture is not null)
                try { _speech.SelectVoiceByHints(VoiceGender.NotSet, VoiceAge.NotSet, 0, culture); }
                catch (ArgumentException) { StatusText.Text = $"Keine Windows-Stimme für {t.LanguageName} installiert; es wird die Standardstimme genutzt."; }
            _speech.SpeakAsync(text);
            SpeakBtn.IsChecked = true;
        }
        catch (Exception ex) { StatusText.Text = "Vorlesen nicht möglich: " + ex.Message; SpeakBtn.IsChecked = false; }
    }

    void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(() => SpeakBtn.IsChecked = false);

    // ---- backup -------------------------------------------------------------------------------------------------------

    async Task ExportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Vault exportieren", SuggestedFileName = $"BibleStudy-{_vault.Name}-{DateTime.Now:yyyyMMdd}.zip",
            FileTypeChoices = [new FilePickerFileType("ZIP-Archiv") { Patterns = ["*.zip"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var snapshot = Path.Combine(Path.GetTempPath(), $"bs_{Guid.NewGuid():N}.db");
        try
        {
            _vault.BackupTo(snapshot);
            SaveSettings();
            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshot, "vault.db");
                if (File.Exists(_settingsPath)) zip.CreateEntryFromFile(_settingsPath, "settings.json");
                var info = zip.CreateEntry("info.txt");
                using var writer = new StreamWriter(info.Open());
                writer.Write($"BibleStudy vault \"{_vault.Name}\", exported {DateTime.Now:yyyy-MM-dd HH:mm}");
            }
            BackupInfo.Text = $"Exportiert nach {path}";
        }
        catch (Exception ex) { BackupInfo.Text = "Export fehlgeschlagen: " + ex.Message; }
        finally { try { File.Delete(snapshot); } catch (IOException) { } }
    }

    async Task ImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Vault importieren", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("ZIP-Archiv") { Patterns = ["*.zip"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var vaultEntry = zip.GetEntry("vault.db") ?? throw new InvalidDataException("Die Datei enthält keinen Vault (vault.db).");
            var name = $"Import {DateTime.Now:yyyy-MM-dd HH-mm}";
            var target = Path.Combine(_root, "vaults", name, "vault.db");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            vaultEntry.ExtractToFile(target);
            if (zip.GetEntry("settings.json") is { } settingsEntry)
            {
                using var reader = new StreamReader(settingsEntry.Open());
                if (JsonSerializer.Deserialize<AppSettings>(reader.ReadToEnd()) is { } imported)
                {
                    _settings.CustomColors = imported.CustomColors; _settings.DefaultColor = imported.DefaultColor; _settings.AutoTagBook = imported.AutoTagBook;
                    _settings.AutoMarkRead = imported.AutoMarkRead; _settings.AiContext = imported.AiContext; _settings.AiModel = imported.AiModel;
                    AiContextBox.Text = _settings.AiContext; AiModelBox.Text = _settings.AiModel; AutoTagCheck.IsChecked = _settings.AutoTagBook; AutoReadCheck.IsChecked = _settings.AutoMarkRead;
                    BuildDefaultColorPanel();
                }
            }
            await SwitchVaultAsync(name);
            BackupInfo.Text = $"Importiert als neuer Vault „{name}“.";
        }
        catch (Exception ex) { BackupInfo.Text = "Import fehlgeschlagen: " + ex.Message; }
    }
}
