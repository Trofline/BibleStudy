using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Verses into a document: the list of saved verses in the "+ Vers" menu, and dragging a mark from the side panel into the text.</summary>
public partial class MainWindow
{
    bool _docVersesAllVaults;
    bool _marksHeld;

    sealed record SavedVerse(VerseRef Ref, string Label, string Text, string Vault);

    static string QuoteBlock(string text, string label) => L("> {0}\n> — {1}\n\n", text.ReplaceLineEndings(" ").Trim(), label);

    /// <summary>The verses saved as a mark or bookmark in this vault, or in all vaults, in the order of the Bible.</summary>
    List<SavedVerse> SavedVerses(bool allVaults)
    {
        var list = new List<SavedVerse>();
        var seen = new HashSet<string>();
        foreach (var name in allVaults ? VaultNames() : [_vault.Name])
        {
            var store = StoreOf(name);
            foreach (var m in store.GetMarks().Where(m => m.Text.Trim().Length > 0))
                if (seen.Add(MarkLabel(m) + "|" + m.Text)) list.Add(new(m.Ref, MarkLabel(m), m.Text.Trim(), name));
            foreach (var b in store.GetBookmarks().Where(b => b.Text.Trim().Length > 0))
                if (seen.Add(Label(b.Ref) + "|" + b.Text)) list.Add(new(b.Ref, Label(b.Ref), b.Text.Trim(), name));
        }
        return list.OrderBy(s => BookOrder(s.Ref.Book)).ThenBy(s => s.Ref.Chapter).ThenBy(s => s.Ref.Verse).ToList();
    }

    /// <summary>The saved-verses part of the "+ Vers" menu: a check box for all vaults and the list; a click inserts the verse as a quote.</summary>
    Control BuildSavedVersesPicker(Flyout fly, double width)
    {
        var all = new CheckBox { Content = L("Aus allen Vaults"), IsChecked = _docVersesAllVaults };
        var host = new StackPanel { Spacing = 2 };
        void Fill()
        {
            host.Children.Clear();
            var verses = SavedVerses(_docVersesAllVaults);
            if (verses.Count == 0) { host.Children.Add(new TextBlock { Text = L("Noch keine gespeicherten Verse."), Classes = { "caption" } }); return; }
            foreach (var s in verses.Take(300))
            {
                var title = new TextBlock { Text = _docVersesAllVaults ? $"{s.Label}  ·  {s.Vault}" : s.Label, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
                var text = new TextBlock { Text = Shorten(s.Text, 90), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
                var b = PickRow(new StackPanel { Children = { title, text } });
                var verse = s;
                b.Click += (_, _) => { fly.Hide(); InsertIntoDoc(QuoteBlock(verse.Text, verse.Label), null); };
                host.Children.Add(b);
            }
        }
        all.IsCheckedChanged += (_, _) => { _docVersesAllVaults = all.IsChecked == true; Fill(); };
        fly.Opening += (_, _) => { all.IsChecked = _docVersesAllVaults; Fill(); };
        return new StackPanel
        {
            Spacing = 4, Width = width,
            Children =
            {
                new TextBlock { Text = L("Gespeicherte Verse"), FontWeight = FontWeight.SemiBold, Margin = new(0, 6, 0, 0) }, all,
                new ScrollViewer { MaxHeight = 240, Content = host, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
            },
        };
    }

    // ---- dragging a mark from the side panel into the document -----------------------------------------------------------

    void InitDocDrag()
    {
        DragDrop.SetAllowDrop(DocBodyBox, true);
        DocBodyBox.AddHandler(DragDrop.DragOverEvent, (object? _, DragEventArgs e) =>
        {
            if (!e.DataTransfer.Contains(DataFormat.Text)) return;
            e.DragEffects = DragDropEffects.Copy; e.Handled = true;
        }, RoutingStrategies.Bubble);
        DocBodyBox.AddHandler(DragDrop.DropEvent, (object? _, DragEventArgs e) =>
        {
            if (e.DataTransfer.TryGetText() is not { Length: > 0 } text) return;
            e.Handled = true;
            // where it was let go
            if (DocBodyBox.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().FirstOrDefault() is { } presenter)
            {
                var at = presenter.TextLayout.HitTestPoint(e.GetPosition(presenter)).TextPosition;
                _docSel = (at, at);
            }
            InsertIntoDoc(text, null);
        }, RoutingStrategies.Bubble);

        Point? down = null; Entry? dragged = null; PointerPressedEventArgs? pressed = null;
        MarksList.AddHandler(PointerPressedEvent, (object? _, PointerPressedEventArgs e) =>
        {
            pressed = e;
            var left = e.GetCurrentPoint(MarksList).Properties.IsLeftButtonPressed;
            down = left ? e.GetPosition(MarksList) : null;
            dragged = left ? (e.Source as StyledElement)?.DataContext as Entry : null;
            _marksHeld = left;   // jumping to the verse waits for the release: it would take the document away from under the drag
        }, RoutingStrategies.Tunnel, true);
        MarksList.AddHandler(PointerReleasedEvent, async (object? _, PointerReleasedEventArgs e) =>
        {
            down = null; dragged = null;
            if (!_marksHeld) return;
            _marksHeld = false;
            // after the list has handled the release itself
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(OnMarksListPickedAsync, Avalonia.Threading.DispatcherPriority.Input);
        }, RoutingStrategies.Tunnel, true);
        MarksList.AddHandler(PointerMovedEvent, async (object? _, PointerEventArgs e) =>
        {
            if (down is not { } from || dragged is not { } entry || !e.GetCurrentPoint(MarksList).Properties.IsLeftButtonPressed) return;
            var now = e.GetPosition(MarksList);
            if (Math.Abs(now.X - from.X) + Math.Abs(now.Y - from.Y) < 10) return;
            down = null; dragged = null;
            if (pressed is null || entry.Id is not long rowId || MarkOfRow(rowId) is not { } mark) return;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(QuoteBlock(mark.Text.Length > 0 ? mark.Text : mark.Note, MarkLabel(mark))));
            try { await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Copy); }
            finally
            {
                // no jump after a drag, and the row is not left picked
                _marksHeld = false;
                _marksListLoading = true;
                MarksList.SelectedItem = null;
                _marksListLoading = false;
            }
        }, RoutingStrategies.Tunnel, true);
    }

    /// <summary>The mark behind a row of the marks list (also one of another vault); null for folder and document rows.</summary>
    Mark? MarkOfRow(long id)
    {
        if (id >= ForeignRowBase)
        {
            var rel = id - ForeignRowBase;
            var name = VaultNames().ElementAtOrDefault((int)(rel / ForeignSpan) - 1);
            return name is null ? null : StoreOf(name).GetMarks().FirstOrDefault(m => m.Id == rel % ForeignSpan);
        }
        if (id <= 0 || id >= DocRowBase) return null;
        return _vault.GetMarks().FirstOrDefault(m => m.Id == id);
    }
}
