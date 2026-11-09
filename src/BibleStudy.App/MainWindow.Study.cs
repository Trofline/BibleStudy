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
        SpeakBtn.Click += async (_, _) => await ToggleSpeakAsync();
        ExportBtn.Click += async (_, _) => await ExportAsync();
        ExportAllBtn.Click += async (_, _) => await ExportAllAsync();
        MergeUndoBtn.Click += async (_, _) => await UndoMergeAsync();
        MergeUndoBtn.IsVisible = Directory.Exists(MergeUndoDir);
        ImportBtn.Click += async (_, _) => await ImportAsync();
        GuestImportBtn.Click += async (_, _) => await ImportAsync(guest: true);
        Closed += (_, _) => { _speech?.Dispose(); _voiceCts?.Cancel(); _voice.Dispose(); BibleStudy.Core.OllamaClient.StopStarted(); };
    }

    // ---- original text ------------------------------------------------------------------------------------------------

    async Task ShowGrundtextAsync(VerseRef r)
    {
        _grundtextRef = r;
        GrundtextPanel.Children.Clear();
        GrundtextCheckBtn.IsVisible = false;
        GrundtextInfo.Text = L("{0} · lade …", Label(r));
        try
        {
            if (!_interlinear.TryGetValue((r.Book, r.Chapter), out var chapter))
                _interlinear[(r.Book, r.Chapter)] = chapter = await _hello.GetInterlinearAsync(r.Book, r.Chapter);
            if (_grundtextRef != r) return; // the user moved on
            if (!chapter.TryGetValue(r.Verse, out var words) || words.Count == 0) { GrundtextInfo.Text = L("{0} · keine Grundtextdaten für diesen Vers.", Label(r)); return; }
            var hebrew = words[0].IsHebrew;
            GrundtextPanel.FlowDirection = hebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            foreach (var w in words) GrundtextPanel.Children.Add(WordCell(w));
            GrundtextInfo.Text = L("{0} · {1}, {2} Wörter. Wort anklicken: Wörterbuch.", Label(r), (hebrew ? L("Hebräisch (von rechts nach links)") : L("Griechisch")), words.Count);
            GrundtextSource.Text = HelloAoProvider.InterlinearAttribution + " · " + StrongsDictionary.Attribution;
            GrundtextCheckBtn.IsVisible = true;
        }
        catch (Exception ex) { GrundtextInfo.Text = L("Grundtext nicht verfügbar: ") + ex.Message; }
    }

    Control WordCell(InterlinearWord w)
    {
        var original = new TextBlock { Text = Heb(w.Text), FontSize = 22, FontFamily = UiSerif, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, FlowDirection = w.IsHebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
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
        GrundtextDetail.Children.Add(new TextBlock { Text = L("Bedeutung hier: {0}", (string.IsNullOrEmpty(w.Gloss) ? "–" : w.Gloss)), TextWrapping = TextWrapping.Wrap });
        GrundtextDetail.Children.Add(new TextBlock { Text = L("Grammatik: {0} ({1})", (string.IsNullOrEmpty(w.ParsingFull) ? w.Parsing : w.ParsingFull), w.Parsing), TextWrapping = TextWrapping.Wrap });
        if (w.Strongs.Length > 1)
        {
            try
            {
                if (await _strongs.LookupAsync(w.Strongs) is { } entry) GrundtextDetail.Children.Add(StudyCard(entry));
            }
            catch (Exception ex) { GrundtextDetail.Children.Add(new TextBlock { Text = L("Wörterbuch nicht verfügbar: ") + ex.Message, TextWrapping = TextWrapping.Wrap }); }
            var occurrences = new Button { Content = L("Alle Vorkommen"), Margin = new(0, 0, 8, 0) };
            occurrences.Click += async (_, _) => await ShowConcordanceAsync(w.Strongs, $"{w.Strongs}  {w.Text}");
            var deeper = new Button { Content = L("Mit Claude vertiefen"), Classes = { "primary" } };
            deeper.Click += async (_, _) => await AskClaudeAboutWordAsync(w);
            GrundtextDetail.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { occurrences, deeper } });
        }
    }

    async Task AskClaudeAboutWordAsync(InterlinearWord w)
    {
        if (_grundtextRef is not { } r) return;
        var t = TranslationBox.SelectedItem as TranslationInfo;
        SideTabs.SelectedIndex = AiTab;
        AiInput.Text = L("Analysiere das Wort „{0}“ ({1}, {2}, {3}) in {4} genauer: Bedeutungsspektrum, Grammatik und ihre Bedeutung hier, wie das Wort an anderen Stellen verwendet und übersetzt wird ", w.Text, w.Translit, w.Strongs, w.ParsingFull, Label(r)) +
                       L("(nutze get_strongs und get_word_occurrences mit eng_kjv) und ob {0} es hier treffend wiedergibt (get_passage, translation={1}). Genau und knapp.", t?.Name, t?.Id);
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
        StrongInfo.Text = L("Suche …");
        try
        {
            _strongResults = (await _strongs.SearchAsync(query)).ToList();
            StrongList.ItemsSource = _strongResults.Select(e => new Entry(default, L("{0}  {1}  ({2})\n{3}", e.Id, e.Lemma, e.Translit, Shorten((e.Definition.Length > 0 ? e.Definition : e.KjvUsage).Trim(), 90)))).ToList();
            StrongInfo.Text = _strongResults.Count == 0 ? L("Nichts gefunden.") : L("{0} Treffer{1} · Treffer anklicken für Details", _strongResults.Count, (_strongResults.Count >= 60 ? L(" (erste 60)") : ""));
            StrongDetail.Children.Clear();
            if (_strongResults.Count == 1) await ShowStrongDetailAsync(_strongResults[0]);
        }
        catch (Exception ex) { StrongInfo.Text = L("Wörterbuch nicht verfügbar: ") + ex.Message; }
    }

    async Task ShowStrongDetailAsync(StrongsEntry e)
    {
        StrongDetail.Children.Clear();
        StrongDetail.Children.Add(StudyCard(e));
        var stepTask = StepCardAsync(e.Id);
        var padded = $"{e.Id[0]}{int.Parse(e.Id[1..]):D4}";
        var occurrences = new Button { Content = L("Alle Vorkommen"), Margin = new(0, 0, 8, 0) };
        occurrences.Click += async (_, _) => await ShowConcordanceAsync(padded, $"{e.Id}  {e.Lemma}");
        var deeper = new Button { Content = L("Mit Claude vertiefen") };
        deeper.Click += async (_, _) =>
        {
            SideTabs.SelectedIndex = AiTab;
            AiInput.Text = L("Erkläre {0} ({1}, {2}) ausführlich: Bedeutungsspektrum, wichtige Stellen (get_word_occurrences mit eng_kjv) und wie es sich zu verwandten Wörtern verhält. Zitiere Verse nur aus den Werkzeugen.", e.Id, e.Lemma, e.Translit);
            await AiSendOrStopAsync();
        };
        StrongDetail.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { occurrences, deeper } });
        if (await stepTask is { } step && StrongDetail.Children.Count > 0) StrongDetail.Children.Insert(1, step);
        if (e.Id[0] == 'H' && await BdbCardAsync(e.Id) is { } bdbCard && StrongDetail.Children.Count > 1) StrongDetail.Children.Add(bdbCard);   // right under the dictionary card
        if (e.Id[0] == 'G' && await LsjCardAsync(e.Id) is { } lsjCard && StrongDetail.Children.Count > 1) StrongDetail.Children.Add(lsjCard);
    }

    async Task AskClaudeToCheckAsync()
    {
        if (_grundtextRef is not { } r) return;
        var t = TranslationBox.SelectedItem as TranslationInfo;
        SideTabs.SelectedIndex = AiTab;
        AiInput.Text = L("Prüfe Wort für Wort, ob die Übersetzung {0} ({1}) in {2} den Grundtext richtig und vollständig wiedergibt. ", t?.Name, t?.Id, Label(r)) +
                       L("Nutze get_interlinear und get_passage (translation={0}) und bei Bedarf get_strongs. Nenne Abweichungen, Nuancen und mögliche andere Wiedergaben, genau und knapp.", t?.Id);
        await AiSendOrStopAsync();
    }

    // ---- backup -------------------------------------------------------------------------------------------------------

    async Task ExportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L("Vault exportieren"), SuggestedFileName = $"BibleStudy-{_vault.Name}-{DateTime.Now:yyyyMMdd}.zip",
            FileTypeChoices = [new FilePickerFileType("ZIP-Archiv") { Patterns = ["*.zip"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        ExportToPath(path);
    }

    internal void ExportToPath(string path)
    {
        var snapshot = Path.Combine(Path.GetTempPath(), $"bs_{Guid.NewGuid():N}.db");
        try
        {
            _vault.BackupTo(snapshot);
            if (!IsGuestVault(_vault.Name)) _readStore.CopyInto(snapshot);   // the shared reading progress travels with the vault
            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshot, "vault.db");
                // only the settings the import takes over: a vault is shared with other people, the relay password, the name for studying
                // together, the allowed plugins or the AI instruction must not go along
                var shared = new AppSettings { CustomColors = _settings.CustomColors, ColorNames = _settings.ColorNames, DefaultColor = _settings.DefaultColor, AutoTagBook = _settings.AutoTagBook, AutoMarkRead = _settings.AutoMarkRead, SubMarkColor = _settings.SubMarkColor, CustomSubColors = _settings.CustomSubColors };
                using (var s = new StreamWriter(zip.CreateEntry("settings.json").Open())) s.Write(JsonSerializer.Serialize(shared));
                using (var n = new StreamWriter(zip.CreateEntry("name.txt").Open(), new System.Text.UTF8Encoding(false))) n.Write(_vault.Name);   // the import gives the vault its name again
                var info = zip.CreateEntry("info.txt");
                using var writer = new StreamWriter(info.Open());
                writer.Write(L("BibleStudy vault \"{0}\", exported {1:yyyy-MM-dd HH:mm}", _vault.Name, DateTime.Now));
            }
            BackupInfo.Text = L("Exportiert nach {0}", path);
        }
        catch (Exception ex) { BackupInfo.Text = L("Export fehlgeschlagen: ") + ex.Message; }
        finally { try { File.Delete(snapshot); } catch (IOException) { } }
    }

    async Task ImportAsync(bool guest = false)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = guest ? L("Vault als Gast importieren") : L("Vault importieren"), AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("ZIP-Archiv") { Patterns = ["*.zip"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        (string Text, bool IsGuestWork, bool SameName) look;
        try { look = ImportPreview(path); }
        catch (Exception ex) { BackupInfo.Text = L("Import fehlgeschlagen: ") + ex.Message; return; }
        if ((look.IsGuestWork || look.SameName) && !guest)
        {
            var how = await AskAsync(look.IsGuestWork ? L("Gast-Arbeit einmischen?") : L("In vorhandene Vaults einmischen?"),
                look.Text + "\n\n" + (look.IsGuestWork ? L("Diese Datei wurde am Ende einer Gast-Sitzung gesichert. Beim Einmischen kommt alles Neue in deine Vaults mit demselben Namen. Es wird nichts gelöscht oder überschrieben; wo sich beide unterscheiden, bleibt beides erhalten. Vorher wird eine Sicherung angelegt.") : L("Vaults mit diesem Namen gibt es hier schon. Beim Einmischen kommt alles Neue zu ihnen dazu; es wird nichts gelöscht oder überschrieben, und vorher wird eine Sicherung angelegt.")),
                L("Einmischen"), L("Als neue Vaults"), L("Abbrechen"));
            if (how == 0) { await MergeGuestWorkAsync(path); await ReviewMergeAsync(); }
            else if (how == 1) await ImportFromPathAsync(path, false);
            return;
        }
        if (await AskAsync(guest ? L("Vault als Gast importieren") : L("Vault importieren"), look.Text, L("Importieren"), L("Abbrechen")) != 0) return;
        await ImportFromPathAsync(path, guest);
    }

    internal async Task ImportFromPathAsync(string path, bool guest)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.Entries.Any(e => e.FullName.StartsWith(BundleDir, StringComparison.Ordinal))) { await ImportAllAsync(zip, guest); return; }   // made by "export all vaults"
            var vaultEntry = zip.GetEntry("vault.db") ?? throw new InvalidDataException(L("Die Datei enthält keinen Vault (vault.db)."));
            // the vault keeps the name it had when it was exported; an export without a name (or an unusable one) gets a name of the time
            string? Read(string entry) { if (zip.GetEntry(entry) is not { } e) return null; using var r = new StreamReader(e.Open()); return r.ReadToEnd(); }
            var given = MarkLogic.VaultNameFromExport(Read("name.txt"), Read("info.txt")) is { } g && MarkLogic.IsValidVaultName(g) ? g : null;
            var stem = given is not null ? (guest ? L("{0} (Gast)", given) : given)
                : guest ? L("Gast {0:yyyy-MM-dd HH-mm}", DateTime.Now) : L("Import {0:yyyy-MM-dd HH-mm}", DateTime.Now);
            var name = stem;
            for (var n = 2; Directory.Exists(Path.Combine(_root, "vaults", name)); n++) name = stem + $" ({n})";   // a second import in the same minute
            var target = Path.Combine(_root, "vaults", name, "vault.db");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            vaultEntry.ExtractToFile(target);
            if (guest) RememberGuest(name, given ?? name);
            else _readStore.MergeFrom(target);   // what the file says was read counts here too (nothing already read is removed)
            // a guest import leaves the settings of this computer alone (colors, switches)
            if (!guest) ApplyImportedSettings(zip);
            await SwitchVaultAsync(name);
            BackupInfo.Text = guest ? L("Als Gast importiert: „{0}“. Er verschwindet, wenn du das Programm schließt; deine eigenen Daten bleiben unberührt.", name) : L("Importiert als neuer Vault „{0}“.", name);
        }
        catch (Exception ex) { BackupInfo.Text = L("Import fehlgeschlagen: ") + ex.Message; }
    }

    void ApplyImportedSettings(ZipArchive zip)
    {
        {
            if (zip.GetEntry("settings.json") is { } settingsEntry)
            {
                using var reader = new StreamReader(settingsEntry.Open());
                if (JsonSerializer.Deserialize<AppSettings>(reader.ReadToEnd()) is { } imported)
                {
                    // colours and two switches only: the AI instruction of someone else's vault does not replace your own
                    _settings.CustomColors = imported.CustomColors.Where(c => NormalizeHex(c) is not null).Take(30).ToList();
                    _settings.DefaultColor = NormalizeHex(imported.DefaultColor) ?? _settings.DefaultColor;
                    foreach (var (hex, nm) in imported.ColorNames.Take(40))   // meanings of the colors; yours stay where both name the same color
                        if (NormalizeHex(hex) is { } key && nm.Trim() is { Length: > 0 } t && !_settings.ColorNames.ContainsKey(key)) _settings.ColorNames[key] = t.Length > 30 ? t[..30] : t;
                    _settings.AutoTagBook = imported.AutoTagBook; _settings.AutoMarkRead = imported.AutoMarkRead;
                    // the color of the sub-marks travels with them, so they look the same here
                    if (NormalizeHex(imported.SubMarkColor) is { } sub) _settings.SubMarkColor = sub;
                    foreach (var hex in imported.CustomSubColors.Take(6).Select(NormalizeHex).OfType<string>())
                        if (!_settings.CustomSubColors.Contains(hex, StringComparer.OrdinalIgnoreCase)) _settings.CustomSubColors.Add(hex);
                    BuildSubMarkPanel();
                    AutoTagCheck.IsChecked = _settings.AutoTagBook; AutoReadCheck.IsChecked = _settings.AutoMarkRead;
                    BuildDefaultColorPanel();
                }
            }
        }
    }
}
