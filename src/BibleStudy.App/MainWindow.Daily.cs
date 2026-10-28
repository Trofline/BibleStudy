using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>Small everyday helpers: verse of the day and automatic saving of documents while writing.</summary>
public partial class MainWindow
{
    static readonly (string Book, int Chapter, int Verse)[] DailyVerses =
    [
        ("JHN", 3, 16), ("PSA", 23, 1), ("ROM", 8, 28), ("PHP", 4, 13), ("PRO", 3, 5), ("ISA", 41, 10), ("JER", 29, 11), ("MAT", 11, 28), ("PSA", 46, 1), ("JOS", 1, 9),
        ("ROM", 12, 2), ("GAL", 2, 20), ("2TI", 3, 16), ("HEB", 11, 1), ("PSA", 119, 105), ("MAT", 6, 33), ("1CO", 13, 4), ("EPH", 2, 8), ("JHN", 14, 6), ("ROM", 5, 8),
        ("PSA", 27, 1), ("ISA", 40, 31), ("MAT", 28, 19), ("1JN", 1, 9), ("PRO", 22, 6), ("PSA", 91, 1), ("JAS", 1, 5), ("1PE", 5, 7), ("REV", 3, 20), ("DEU", 31, 6),
        ("MIC", 6, 8), ("LAM", 3, 22), ("JHN", 1, 1), ("GEN", 1, 1), ("PSA", 1, 1), ("ECC", 3, 1), ("ROM", 6, 23), ("COL", 3, 23), ("1TH", 5, 16), ("JHN", 15, 5), ("REV", 21, 4),
    ];

    static VerseRef VerseOfTheDay()
    {
        var (b, c, v) = DailyVerses[DateTime.Today.DayOfYear % DailyVerses.Length];
        return new VerseRef(b, c, v);
    }

    // ---- autosave -----------------------------------------------------------------------------------------------------

    string? _docSnapshot;

    string DocState() => string.Join("\u0001", DocTitleBox.Text, DocBodyBox.Text, DocTagsBox.Text, (DocCategoryBox.SelectedItem as DocCategory)?.Name);

    void InitAutosave()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) => AutoSaveDocument();
        timer.Start();
    }

    /// <summary>Saves quietly while the document is open and changed (needs a title); the editor keeps its cursor and selection.</summary>
    void AutoSaveDocument()
    {
        if (_doc is null || !DocEditorPanel.IsVisible) return;
        var state = DocState();
        if (state == _docSnapshot) return;
        var title = DocTitleBox.Text?.Trim() ?? "";
        if (title.Length == 0) return;
        try
        {
            var tags = (DocTagsBox.Text ?? "").Split(',', ';').Where(t => !IsCategoryTag(t.Trim())).Concat(SelectedCategoryTag()).ToArray();
            var body = DocBodyBox.Text ?? "";
            var template = DocTemplateCheck.IsChecked == true;
            var folderId = DocFolderBox.SelectedItem is Folder fo ? fo.Id : (long?)null;
            if (_doc.Id == 0) { var id = _vault.AddDocument(title, body, _doc.Path, template, tags, folderId); _doc = _vault.GetDocuments().First(d => d.Id == id); }
            else _vault.UpdateDocument(_doc.Id, title, body, _doc.Path, template, tags, folderId);
            _docSnapshot = state;
            DocPathText.Text = L("Automatisch gespeichert um {0:HH:mm:ss}", DateTime.Now);
        }
        catch (Exception ex) { DocPathText.Text = L("Automatisches Speichern fehlgeschlagen: ") + ex.Message; }
    }
}
