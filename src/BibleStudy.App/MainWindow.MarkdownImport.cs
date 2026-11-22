using Avalonia.Platform.Storage;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>A folder of Markdown files (an Obsidian vault, or the export of this program) read into the open vault: see <see cref="MarkdownImport"/>.</summary>
public partial class MainWindow
{
    async Task ImportMarkdownFolderAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L("Ordner mit Markdown-Dateien wählen"), AllowMultiple = false });
        if (picked.Count == 0) return;
        if (picked[0].LocalPath() is not { } dir) { StatusText.Text = StorageFiles.NoFolderHere; return; }
        var backup = L("vor dem Einlesen von Markdown");
        try { await Task.Run(() => BackupData(backup)); } catch { /* a failed backup must not stop the import: nothing is replaced, only added or merged */ }
        StatusText.Text = L("Markdown-Dateien werden eingelesen …");
        try
        {
            var lists = new HashSet<string> { L("Markierungen"), L("Leseliste"), L("Lesezeichen"), L("To-dos"), L("Überschrift"), "Start" };
            var report = await Task.Run(() => MarkdownImport.ImportFolder(_vault, dir, lists, p => Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText.Text = L("Markdown-Dateien werden eingelesen … {0}", p))));
            _notesStale = true;
            RefreshDocs();
            if (_curBook.Length > 0) _ = ShowChapterAsync(null, true);
            var text = report.Added + report.Merged + report.Notes == 0
                ? L("Es war nichts Neues dabei ({0} Dateien waren schon im Vault).", report.Unchanged)
                : L("Eingelesen: {0} neue Dokumente, {1} Dokumente und Notizen mit dem Vault zusammengeführt, {2} neue Notizen zu Versen, {3} neue Ordner.", report.Added, report.Merged, report.Notes, report.Folders)
                  + (report.Skipped > 0 ? " " + L("{0} Dateien übersprungen (leer, Listen aus dem Export oder nicht lesbar).", report.Skipped) : "");
            BackupInfo.Text = StatusText.Text = text + (report.Problems.Count > 0 ? " " + L("Nicht lesbar: {0}", string.Join("; ", report.Problems.Take(3))) : "");
            if (_exBuilt) ExNote(text, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            BackupInfo.Text = StatusText.Text = L("Einlesen fehlgeschlagen: ") + ex.Message;
        }
    }
}
