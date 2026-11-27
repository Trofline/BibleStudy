using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// The commentaries the user brings along. The program ships none and loads none from the net: what a commentary teaches is for the user to judge.
/// Modules of e-Sword, MySword, MyBible, theWord and SWORD, OSIS files and documents (also the exports of Logos) are read into the commentary store (see <see cref="OwnCommentaries"/>).
/// </summary>
public partial class MainWindow
{
    LocalCommentaryStore _ownCommentaries = null!;   // made with the other stores

    void InitOwnCommentaries()
    {
        CommentaryImportBtn.Click += async (_, _) => await ImportCommentaryAsync();
        CommentaryImportSettingsBtn.Click += async (_, _) => await ImportCommentaryAsync();
        CommentaryRemoveBtn.Click += async (_, _) => await RemoveCommentaryAsync();
        CommentaryRemoveBtn.IsEnabled = _ownCommentaries.List().Count > 0;
    }

    /// <summary>Reads the list of the commentaries from the store into the box (the commentary <paramref name="select"/>, else the one chosen last, else the first).</summary>
    void LoadCommentaryList(string? select = null)
    {
        _commentaries = _ownCommentaries.List();
        _libraryLoading = true;
        CommentaryBox.ItemsSource = _commentaries;
        CommentaryBox.SelectedItem = _commentaries.FirstOrDefault(c => c.Id == (select ?? _settings.Commentary)) ?? _commentaries.FirstOrDefault();
        _libraryLoading = false;
        CommentaryRemoveBtn.IsEnabled = _commentaries.Count > 0;
    }

    async Task ImportCommentaryAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("Kommentar zum Einlesen wählen"), AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(L("Kommentare")) { Patterns = OwnCommentaries.Patterns }],
        });
        if (files.Count == 0) return;
        var done = new List<string>(); var failed = new List<string>(); string? lastId = null; var entries = 0;
        foreach (var file in files)
        {
            if (await file.LocalPathAsync() is not { } path) continue;
            try
            {
                var parsed = await Task.Run(() => OwnCommentaries.Read(path));
                lastId = await Task.Run(() => _ownCommentaries.Save(parsed, path));
                _commentaryIndex.Remove(lastId);
                done.Add(parsed.Name); entries += parsed.Entries.Count;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or System.Xml.XmlException or Microsoft.Data.Sqlite.SqliteException)
            {
                failed.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        if (lastId is not null)
        {
            _settings.Commentary = lastId;
            if (!_settings.ShowCommentaries) CommentariesCheck.IsChecked = true;   // reading one in means wanting to see it
            SaveSettings();
            LoadCommentaryList(lastId);
            _shownCommentary = null;
            if (TabActive(CommentaryTab)) await ShowCommentaryAsync();
        }
        var text = done.Count > 0 ? L("Eingelesen: {0} ({1} Einträge). Sie ist nur auf deinem Computer.", string.Join(", ", done), entries) : "";
        if (failed.Count > 0) text += (text.Length > 0 ? " " : "") + L("Nicht gelesen: {0}", string.Join("; ", failed));
        if (text.Length > 0) StatusText.Text = text;
    }

    async Task RemoveCommentaryAsync()
    {
        if (CommentaryBox.SelectedItem is not CommentaryInfo c) return;
        if (await AskAsync(L("Kommentar entfernen?"), L("„{0}“ wird aus BibleStudy entfernt. Deine Datei bleibt, wo sie ist; du kannst den Kommentar jederzeit wieder einlesen.", c.Name), L("Entfernen"), L("Abbrechen")) != 0) return;
        _ownCommentaries.Remove(c.Id); _commentaryIndex.Remove(c.Id);
        if (_settings.Commentary == c.Id) { _settings.Commentary = ""; SaveSettings(); }
        _shownCommentary = null;
        LoadCommentaryList();
        await ShowCommentaryAsync();
    }
}
