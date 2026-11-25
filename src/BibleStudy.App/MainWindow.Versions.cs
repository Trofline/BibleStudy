using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Earlier versions of documents and notes: a list with the time and what changed, the whole text to look at, and a button that puts
/// the text back. Going back keeps the current text as a version too, so it can be undone the same way.
/// </summary>
public partial class MainWindow
{
    readonly MenuItem _miVersions = new();
    Button? _noteVersionsBtn;

    void InitVersions()
    {
        _miVersions.Header = L("Frühere Fassungen …");
        _miVersions.Click += (_, _) =>
        {
            if (_doc is not { Id: > 0 } d) return;
            AutoSaveDocument(leaving: false);   // what is in the editor counts as the text of now
            ShowVersions(DocMoreBtn, L("Frühere Fassungen von „{0}“", Shorten(d.Title, 40)), DocBodyBox.Text ?? "", _vault.GetDocumentVersions(d.Id), v =>
            {
                if (!_vault.RestoreDocumentVersion(d.Id, v.Id)) return;
                _doc = _vault.GetDocuments().First(x => x.Id == d.Id);
                OpenDocument(_doc);
                RefreshDocs(); RefreshCollection();
                DocPathText.Text = L("Fassung vom {0} wiederhergestellt. Der Text davor ist unter „Frühere Fassungen“ zu finden.", v.Saved.ToString("g", Loc.Culture));
            });
        };
        if (DocMoreBtn.Flyout is MenuFlyout more) more.Items.Insert(Math.Max(0, more.Items.Count - 2), _miVersions);   // above "Delete"

        _noteVersionsBtn = new Button { Content = L("Frühere Fassungen") };
        ToolTip.SetTip(_noteVersionsBtn, L("Frühere Fassungen dieser Notiz ansehen und zurückholen"));
        _noteVersionsBtn.Click += (_, _) =>
        {
            var at = _noteRef ?? (_selected is int v ? new VerseRef(_curBook, _curChapter, v) : null);
            if (at is not { } r) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
            SaveNoteDraft();
            ShowVersions(_noteVersionsBtn, L("Frühere Fassungen der Notiz zu {0}", Label(r)), _vault.GetNote(r)?.Text ?? "", _vault.GetNoteVersions(r), v =>
            {
                if (!_vault.RestoreNoteVersion(r, v.Id)) return;
                if (_noteRef == r) NoteEditor.Text = _noteLoaded = _vault.GetNote(r)?.Text ?? "";
                RefreshLists(); RefreshCells();
                StatusText.Text = L("Fassung vom {0} wiederhergestellt. Der Text davor ist unter „Frühere Fassungen“ zu finden.", v.Saved.ToString("g", Loc.Culture));
            });
        };
        if (NoteDeleteBtn.Parent is Panel row) row.Children.Insert(row.Children.IndexOf(NoteDeleteBtn) + 1, _noteVersionsBtn);
    }

    /// <summary>The list of versions, newest first; each with what changed against the text of now, the whole text on request, and "restore".</summary>
    void ShowVersions(Control anchor, string title, string current, List<TextVersion> versions, Action<TextVersion> restore)
    {
        var list = new StackPanel { Spacing = 8 };
        var fly = new Flyout();
        if (versions.Count == 0)
            list.Children.Add(new TextBlock
            {
                Text = L("Noch keine früheren Fassungen. Sobald du den Text änderst, merkt sich BibleStudy, wie er vorher war (höchstens alle {0} Minuten eine Fassung).", (int)VaultStore.VersionGap.TotalMinutes),
                Classes = { "caption" }, TextWrapping = TextWrapping.Wrap,
            });
        foreach (var v in versions)
        {
            var version = v;
            var (added, removed) = VaultStore.LineChanges(v.Body, current);
            var change = added == 0 && removed == 0 ? L("gleicher Text") : L("seitdem +{0} / −{1} Zeilen", added, removed);
            var head = new TextBlock { Text = $"{v.Saved.ToString("g", Loc.Culture)}  ·  {change}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
            var firstLine = v.Body.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            var preview = new TextBlock { Text = (v.Title.Length > 0 ? v.Title + " · " : "") + Shorten(firstLine, 90), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
            var full = new SelectableTextBlock { Text = v.Body, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0) };
            var fullHost = new ScrollViewer { Content = full, MaxHeight = 260, IsVisible = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            var look = new Button { Content = L("Ansehen") };
            look.Click += (_, _) =>
            {
                fullHost.IsVisible = !fullHost.IsVisible;
                look.Content = fullHost.IsVisible ? L("Ausblenden") : L("Ansehen");
            };
            var back = new Button { Content = L("Wiederherstellen"), Classes = { "primary" } };
            back.Click += (_, _) => { fly.Hide(); restore(version); };
            var card = new Border
            {
                Padding = new(10, 8), CornerRadius = new(8), BorderThickness = new(1),
                Child = new StackPanel { Spacing = 4, Children = { head, preview, fullHost, new WrapPanel { ItemSpacing = 6, Margin = new(0, 4, 0, 0), Children = { look, back } } } },
            };
            card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
            list.Children.Add(card);
        }
        fly.Content = new StackPanel
        {
            Spacing = 8, Width = PW(440),
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = L("Die Fassungen bleiben auf diesem Gerät. Wiederherstellen ersetzt den Text, der jetzige bleibt als Fassung erhalten."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                new ScrollViewer { Content = list, MaxHeight = 460, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
            },
        };
        fly.ShowAt(anchor);
    }
}
