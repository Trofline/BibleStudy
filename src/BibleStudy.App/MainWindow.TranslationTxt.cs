using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// The current translation as one .txt file in the own format (see <see cref="BibleTextFile"/>): saved with the export button, or sent to the
/// other person when studying together, who takes it over with one click. The import reads it back with everything it had.
/// </summary>
public partial class MainWindow
{
    /// <summary>The translation that is being read, with all it has: an imported one exactly as stored, another one chapter by chapter from its source.</summary>
    async Task<ParsedBible?> CurrentTranslationAsync(IProgress<string> progress)
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return null;
        if (LocalBibleStore.Owns(t.Id)) return await Task.Run(() => _local.Load(t.Id));
        BibleStudy.Core.Providers.IBibleProvider source = BibleStudy.Core.Plugins.PluginBibleProvider.Owns(t.Id) ? _pluginProvider : _hello;
        return await BibleTextFile.FromProviderAsync(source, t, progress);
    }

    async Task ExportTranslationTxtAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) { ImportBibleInfo.Text = L("Keine Übersetzung gewählt."); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L("Übersetzung als TXT speichern"), SuggestedFileName = SafeName(t.ShortName, "bibel") + ".txt", DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }],
        });
        await using var target = await file.SaveTargetAsync();
        if (target?.Path is not { } path) return;
        ExportBibleTxtBtn.IsEnabled = false;
        try
        {
            var bible = await CurrentTranslationAsync(new Progress<string>(m => ImportBibleInfo.Text = m));
            if (bible is null || bible.Verses.Count == 0) { ImportBibleInfo.Text = L("Keine Verse gefunden."); return; }
            await Task.Run(() => File.WriteAllText(path, BibleTextFile.Write(bible), new UTF8Encoding(false)));
            ImportBibleInfo.Text = L("„{0}“ exportiert: {1} Verse, {2} Fußnoten. Gespeichert: {3}", bible.Name, bible.Verses.Count, bible.Footnotes.Count, path);
        }
        catch (Exception ex) { ImportBibleInfo.Text = L("Export fehlgeschlagen: ") + ex.Message; }
        finally { ExportBibleTxtBtn.IsEnabled = true; }
    }

    // ---- studying together ------------------------------------------------------------------------------------------------

    Button BuildSendTranslationButton()
    {
        var button = new Button { Content = L("Übersetzung senden"), Margin = new(0, 0, 8, 6) };
        ToolTip.SetTip(button, L("Sendet die Übersetzung, die du gerade liest, als .txt mit allem, was dazugehört. Die andere Person übernimmt sie mit einem Klick. Sende nur Übersetzungen, die du weitergeben darfst."));
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await SendTranslationAsync(); } finally { button.IsEnabled = true; }
        };
        return button;
    }

    /// <summary>The translation in the .txt format, packed (text shrinks to about a quarter), sent in pieces like a vault.</summary>
    async Task SendTranslationAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) { AddCollabLog(L("Keine Übersetzung gewählt."), system: true); return; }
        try
        {
            AddCollabLog(L("Übersetzung „{0}“ wird vorbereitet …", t.Name), mine: true, system: true);
            var bible = await CurrentTranslationAsync(new Progress<string>(_ => { }));
            if (bible is null || bible.Verses.Count == 0) { AddCollabLog(L("Keine Verse gefunden."), system: true); return; }
            var bytes = await Task.Run(() => BibleStudy.Core.Sync.LocalBibles.Pack(bible));
            if (bytes.LongLength > MaxBlob) { AddCollabLog(L("Diese Übersetzung ist zu groß zum Senden."), system: true); return; }
            await SendBlobAsync("bible", bible.Name, bytes, L("Du hast die Übersetzung „{0}“ gesendet ({1}).", bible.Name, SizeText(bytes.LongLength)));
        }
        catch (Exception ex) { AddCollabLog(L("Fehler: ") + ex.Message, system: true); }
    }

    /// <summary>Unpacks received text, never more than <see cref="BibleStudy.Core.Sync.LocalBibles.MaxText"/>; null when it is not packed text of this format.</summary>
    static string? UnpackTranslation(byte[] data) => BibleStudy.Core.Sync.LocalBibles.Unpack(data);

    /// <summary>A translation arrived: a card; "Übernehmen" imports it through the normal import (an existing one of the same name is only replaced after asking).</summary>
    void ShowReceivedTranslation(string name, byte[] data)
    {
        if (name.Length == 0) name = L("Übersetzung");
        var who = PeerWho;
        var text = L("{0} sendet die Übersetzung „{1}“ ({2})", who, name, SizeText(data.LongLength));
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        var take = new Button { Content = L("Übernehmen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(take, L("Wird wie eine eigene .txt-Datei importiert und steht dann in der Übersetzungsliste."));
        take.Click += async (_, _) =>
        {
            take.IsEnabled = false;
            try
            {
                if (await Task.Run(() => UnpackTranslation(data)) is not { } content) { info.Text = L("Die Datei ist keine Übersetzung im Format von BibleStudy."); return; }
                // kept in the program's folder: the import remembers where a translation came from
                var folder = Path.Combine(_root, "received");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, SafeName(name, "bibel") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
                _lastSavedImportId = null;
                info.Text = await ImportBibleCoreAsync(path, BibleStudy.Core.Loc.Code, ConfirmReplaceAsync);
                if (_lastSavedImportId is not null) take.Content = L("Übernommen ✓");
                else take.IsEnabled = true;   // not replaced, or it failed: it can be tried again
            }
            catch (Exception ex) { info.Text = L("Import fehlgeschlagen: ") + ex.Message; take.IsEnabled = true; }
        };
        AddCollabItem(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                take,
                info,
            },
        }));
        NotifyChat(text);
    }
}
