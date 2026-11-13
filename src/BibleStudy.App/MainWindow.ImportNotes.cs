using Avalonia.Platform.Storage;
using BibleStudy.Core.Import;

namespace BibleStudy.App;

/// <summary>
/// Notes from e-Sword (and MySword): the own notes and commentaries become notes on the verses of the current vault, topics and the notes on
/// whole books or chapters become documents. A note that is already there keeps its text; the imported one is added below it.
/// </summary>
public partial class MainWindow
{
    async Task ImportEswordNotesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("Notizen aus e-Sword oder MySword importieren"), AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(L("e-Sword-Notizen und -Kommentare")) { Patterns = ["*.notx", "*.cmtx", "*.topx", "*.mybible"] }],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return;
        int notes = 0, docs = 0;
        var failed = new List<string>();
        foreach (var path in paths)
        {
            ImportedNotes read;
            try { read = await Task.Run(() => EswordNotes.Read(path)); }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidCastException or FormatException) { failed.Add(Path.GetFileName(path)); continue; }
            foreach (var n in read.Notes)
            {
                var text = n.EndVerse > n.Ref.Verse ? $"({Label(n.Ref)}–{n.EndVerse}) {n.Text}" : n.Text;
                var old = _vault.GetNote(n.Ref)?.Text ?? "";
                if (old.Contains(text, StringComparison.Ordinal)) continue;   // imported before
                _vault.SetNote(n.Ref, old.Trim().Length > 0 ? old.TrimEnd() + "\n\n" + text : text);
                notes++;
            }
            foreach (var t in read.Texts)
            {
                if (_vault.GetDocuments().Any(d => d.Title == t.Title && d.Body.Trim() == t.Text.Trim())) continue;
                _vault.AddDocument(t.Title, t.Text, null, false, ["e-Sword"]);
                docs++;
            }
        }
        _notesStale = true;
        RefreshDocs();
        if (_curBook.Length > 0) _ = ShowChapterAsync(null, true);
        StatusText.Text = L("Importiert: {0} Notizen zu Versen, {1} Dokumente.", notes, docs)
            + (failed.Count > 0 ? " " + L("Nicht lesbar: {0}", string.Join(", ", failed)) : "")
            + (notes + docs == 0 && failed.Count == 0 ? " " + L("(Es war nichts Neues dabei.)") : "");
        if (BackupInfo is not null) BackupInfo.Text = StatusText.Text;
    }
}
