using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The trash in the explorer: what was deleted (marks, notes, folders, documents) waits there for 30 days and comes back with a double click,
/// Enter or the right-click menu; also after the program was closed in between.
/// </summary>
public partial class MainWindow
{
    bool _exInTrash;                  // the explorer shows the trash
    readonly List<long> _trSel = [];  // chosen entries of the trash
    long? _trAnchor;
    List<TrashItem> _trRows = [];

    const string TrashIcon = "M3 6h18 M8 6V4h8v2 M19 6l-1 14H6L5 6 M10 11v6 M14 11v6";

    /// <summary>"GEN 1:1–3" (how the trash keeps a place) as "1. Mose 1:1–3" in the language of the program.</summary>
    string TrashPlace(string place)
    {
        var m = Regex.Match(place, @"^(\S+) (\d+):(\d+)(–\d+)?$");
        return m.Success ? Label(new VerseRef(m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))) + m.Groups[4].Value : place;
    }

    string TrashKind(string kind) => kind switch { "mark" => L("Markierung"), "note" => L("Notiz"), "folder" => L("Ordner"), "doc" => L("Dokument"), _ => kind };

    /// <summary>Brings entries of the trash back and shows them everywhere (text, lists, the note that is open).</summary>
    async Task RestoreTrashAsync(IEnumerable<long> ids, bool quiet = false)
    {
        var kinds = _vault.GetTrash().Where(t => ids.Contains(t.Id)).ToDictionary(t => t.Id, t => t);
        var back = 0;
        foreach (var id in ids) if (_vault.RestoreFromTrash(id)) back++;
        if (back == 0) return;
        if (kinds.Values.Any(t => t.Kind == "note") && _noteRef is { } r && (NoteEditor.Text ?? "") == (_noteLoaded ?? ""))
            NoteEditor.Text = _noteLoaded = _vault.GetNotes().FirstOrDefault(n => n.Ref == r)?.Text ?? "";
        _trSel.Clear();
        AfterExplorerChange();
        RefreshDocs();
        if (!quiet && _exBuilt)
            ExNote(back == 1 && kinds.Values.FirstOrDefault() is { } one ? L("„{0}“ ist wieder da.", Shorten(one.Title.Length > 0 ? one.Title : TrashPlace(one.Place), 40)) : L("{0} Einträge sind wieder da.", back), false);
        if (kinds.Values.Any(t => t.Kind is "mark" or "folder")) { LoadChapterMarks(); await RedrawChapterAsync(); }
        RefreshCells();
    }

    /// <summary>A document goes into the trash; the bar under the text can take it back at once.</summary>
    void TrashDocumentWithUndo(Document d)
    {
        if (_doc?.Id == d.Id) CloseDocument();
        var t = _vault.TrashDocument(d.Id);
        RefreshDocs(); RefreshCollection();
        if (t == 0) return;
        ShowUndoToast(L("„{0}“ in den Papierkorb gelegt.", Shorten(d.Title, 40)), async () => { await RestoreTrashAsync([t], quiet: true); RefreshDocs(); });
    }

    /// <summary>A note goes into the trash (it used to be gone at once); the bar under the text can take it back.</summary>
    void TrashNoteWithUndo(VerseRef r)
    {
        var t = _vault.TrashNote(r);
        if (_noteRef == r) NoteEditor.Text = _noteLoaded = "";
        RefreshLists(); RefreshCells();
        if (t == 0) return;
        ShowUndoToast(L("Notiz zu {0} in den Papierkorb gelegt.", Label(r)), () => RestoreTrashAsync([t], quiet: true));
    }

    /// <summary>A folder goes into the trash with what lay in it (the content itself stays, without the folder); the bar can put it all back.</summary>
    void TrashFolderWithUndo(long id, string name)
    {
        var t = _vault.TrashFolder(id);
        RefreshCollection(); RefreshDocs(); RefreshNotesTree();
        if (t == 0) return;
        ShowUndoToast(L("Ordner „{0}“ in den Papierkorb gelegt.", Shorten(name, 40)), async () => { await RestoreTrashAsync([t], quiet: true); RefreshCollection(); RefreshNotesTree(); });
    }

    // ---- the trash in the explorer ----------------------------------------------------------------------------------

    void ExOpenTrash()
    {
        if (!_exInTrash) _exBack.Push(_exFolder);
        _exInTrash = true;
        _exSel.Clear(); _trSel.Clear();
        _exSearch.Text = "";
        RefreshExplorer();
    }

    Control TrashTreeRow()
    {
        var count = _vault.GetTrash().Count;
        var label = new TextBlock { Text = L("Papierkorb"), VerticalAlignment = VerticalAlignment.Center, FontWeight = _exInTrash ? FontWeight.SemiBold : FontWeight.Normal };
        var num = new TextBlock { Text = count > 0 ? count.ToString() : "", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        var line = new DockPanel();
        DockPanel.SetDock(num, Dock.Right);
        line.Children.Add(num);
        line.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Width = 14 }, new Icon { Data = Geometry.Parse(TrashIcon), IconSize = 15, VerticalAlignment = VerticalAlignment.Center }, label } });
        var box = new Border { Padding = new(6, 6, 8, 6), CornerRadius = new(8), Child = line, Cursor = new Cursor(StandardCursorType.Hand), Margin = new(0, 10, 0, 1) };
        ToolTip.SetTip(box, L("Gelöschtes wartet hier {0} Tage und lässt sich zurückholen.", VaultStore.TrashDays));
        void Paint(bool hover) => box.Background = _exInTrash ? Res("BsAccentSoft") ?? Brushes.Transparent : hover ? Res("BsHover") ?? Brushes.Transparent : Brushes.Transparent;
        Paint(false);
        box.PointerEntered += (_, _) => Paint(true);
        box.PointerExited += (_, _) => Paint(false);
        box.PointerPressed += (_, e) => { if (e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) ExOpenTrash(); };
        box.ContextMenu = new ContextMenu { Items = { MenuAction(L("Papierkorb leeren …"), () => _ = EmptyTrashAsync(), count > 0) } };
        return box;
    }

    void FillTrashHead()
    {
        _exHead.Children.Clear();
        void Head(string text, int column)
        {
            var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new(4, 2), Opacity = 0.75 };
            Grid.SetColumn(t, column);
            _exHead.Children.Add(t);
        }
        Head(L("Name"), 1); Head(L("Stelle"), 2); Head(L("Gelöscht am"), 3);
    }

    void FillTrashList()
    {
        _exTargets.RemoveAll(t => t.Control.Parent == _exList);
        _exList.Children.Clear();
        FillTrashHead();
        var words = (_exSearch.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _trRows = _vault.GetTrash().Where(t => words.All(w => (t.Title + " " + TrashPlace(t.Place) + " " + TrashKind(t.Kind)).Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
        _trSel.RemoveAll(id => !_trRows.Any(r => r.Id == id));
        foreach (var t in _trRows) _exList.Children.Add(TrashRow(t));
        _exEmpty.IsVisible = _trRows.Count == 0;
        _exEmpty.Text = words.Length > 0 ? L("Nichts gefunden.") : L("Der Papierkorb ist leer.");
        _exInfo.Text = L("{0} Einträge im Papierkorb", _trRows.Count) + (_trSel.Count > 0 ? "  ·  " + L("{0} ausgewählt", _trSel.Count) : "")
            + "  ·  " + L("Doppelklick oder Enter holt zurück · Nach {0} Tagen wird endgültig gelöscht", VaultStore.TrashDays);
    }

    Control TrashRow(TrashItem t)
    {
        var selected = _trSel.Contains(t.Id);
        var grid = new Grid { ColumnDefinitions = new("28,*,140,110") };
        var icon = new Icon
        {
            Data = Geometry.Parse(t.Kind switch { "folder" => FolderIcon, "note" => NoteIcon, "doc" => "M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z M14 3v6h6", _ => MarkIcon }),
            IconSize = 15, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var name = new TextBlock { Text = (t.Title.Length > 0 ? t.Title : L("(ohne Text)")) + "   · " + TrashKind(t.Kind), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var place = new TextBlock { Text = TrashPlace(t.Place), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var when = new TextBlock { Text = t.Deleted.ToString("g", Loc.Culture), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(when, L("Wird am {0} endgültig gelöscht.", t.Deleted.AddDays(VaultStore.TrashDays).ToString("d", Loc.Culture)));
        Grid.SetColumn(name, 1); Grid.SetColumn(place, 2); Grid.SetColumn(when, 3);
        grid.Children.Add(icon); grid.Children.Add(name); grid.Children.Add(place); grid.Children.Add(when);
        var row = new Border { Padding = new(8, 7), CornerRadius = new(8), Child = grid, Margin = new(0, 1), Cursor = new Cursor(StandardCursorType.Hand) };
        void Paint(bool hover) => row.Background = selected ? Res("BsAccentSoft") ?? Brushes.Transparent : hover ? Res("BsHover") ?? Brushes.Transparent : Brushes.Transparent;
        Paint(false);
        row.PointerEntered += (_, _) => Paint(true);
        row.PointerExited += (_, _) => Paint(false);
        row.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(row).Properties;
            ExplorerHost.Focus();
            if (props.IsRightButtonPressed) { if (!_trSel.Contains(t.Id)) { _trSel.Clear(); _trSel.Add(t.Id); FillTrashList(); } return; }
            if (!props.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { e.Handled = true; _ = RestoreTrashAsync([t.Id]); return; }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _trAnchor is long a && _trRows.FindIndex(r => r.Id == a) is var i and >= 0 && _trRows.FindIndex(r => r.Id == t.Id) is var j and >= 0)
            { _trSel.Clear(); _trSel.AddRange(_trRows.Skip(Math.Min(i, j)).Take(Math.Abs(i - j) + 1).Select(r => r.Id)); }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { if (!_trSel.Remove(t.Id)) _trSel.Add(t.Id); _trAnchor = t.Id; }
            else { _trSel.Clear(); _trSel.Add(t.Id); _trAnchor = t.Id; }
            FillTrashList();
        };
        row.ContextMenu = new ContextMenu();
        row.ContextMenu.Opening += (_, _) =>
        {
            var ids = _trSel.Contains(t.Id) ? _trSel.ToList() : [t.Id];
            row.ContextMenu.Items.Clear();
            row.ContextMenu.Items.Add(MenuAction(ids.Count > 1 ? L("{0} Einträge wiederherstellen", ids.Count) : L("Wiederherstellen"), () => _ = RestoreTrashAsync(ids)));
            row.ContextMenu.Items.Add(new Separator());
            row.ContextMenu.Items.Add(MenuAction(L("Endgültig löschen …"), () => _ = DeleteForGoodAsync(ids)));
        };
        row.ContextMenu.Items.Add(new MenuItem());   // a menu without items does not open
        return row;
    }

    /// <summary>The keys in the trash: arrows choose, Enter brings back, Del throws away for good (after a question).</summary>
    bool TrashKeyDown(KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Up or Key.Down or Key.Home or Key.End when _trRows.Count > 0:
            {
                var cur = _trSel.Count > 0 ? _trRows.FindIndex(r => r.Id == _trSel[^1]) : -1;
                var next = e.Key switch
                {
                    Key.Home => 0, Key.End => _trRows.Count - 1,
                    Key.Up => cur < 0 ? _trRows.Count - 1 : Math.Max(0, cur - 1),
                    _ => cur < 0 ? 0 : Math.Min(_trRows.Count - 1, cur + 1),
                };
                _trSel.Clear(); _trSel.Add(_trRows[next].Id); _trAnchor = _trRows[next].Id;
                FillTrashList();
                if (next < _exList.Children.Count) _exList.Children[next].BringIntoView();
                return true;
            }
            case Key.A when ctrl: _trSel.Clear(); _trSel.AddRange(_trRows.Select(r => r.Id)); FillTrashList(); return true;
            case Key.Enter when _trSel.Count > 0: _ = RestoreTrashAsync(_trSel.ToList()); return true;
            case Key.Delete when _trSel.Count > 0: _ = DeleteForGoodAsync(_trSel.ToList()); return true;
            case Key.Back or Key.Escape: ExNavigate(_exFolder); return true;
            case Key.Z when ctrl: ExUndoLast(); return true;
        }
        return false;
    }

    async Task DeleteForGoodAsync(List<long> ids)
    {
        var what = ids.Count == 1 && _trRows.FirstOrDefault(r => r.Id == ids[0]) is { } one ? "„" + Shorten(one.Title.Length > 0 ? one.Title : TrashPlace(one.Place), 40) + "“" : L("{0} Einträge", ids.Count);
        if (await AskAsync(L("Endgültig löschen?"), L("{0} lässt sich danach nicht mehr zurückholen.", what), L("Abbrechen"), L("Endgültig löschen")) != 1) return;
        foreach (var id in ids) _vault.DeleteFromTrash(id);
        _trSel.Clear();
        RefreshExplorer();
        ExNote(L("{0} endgültig gelöscht.", what), false);
    }

    async Task EmptyTrashAsync()
    {
        var n = _vault.GetTrash().Count;
        if (n == 0) return;
        if (await AskAsync(L("Papierkorb leeren?"), L("{0} Einträge werden endgültig gelöscht und lassen sich nicht mehr zurückholen.", n), L("Abbrechen"), L("Papierkorb leeren")) != 1) return;
        _vault.EmptyTrash();
        _trSel.Clear();
        RefreshExplorer();
        ExNote(L("Der Papierkorb ist leer."), false);
    }
}
