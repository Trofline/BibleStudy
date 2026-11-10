using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The marks and notes of the open vault as a small file explorer: a folder tree on the left, the content of the chosen folder on the right,
/// a path bar on top. Folders can lie in folders; marks, notes and folders are moved by dragging them onto a folder (in the tree, in the list
/// or in the path), or with the right-click menu. A double click opens a folder or jumps to the verse.
/// </summary>
public partial class MainWindow
{
    const int ExplorerTab = 30;

    enum ExKind { Folder, Mark, Note }

    sealed record ExItem(ExKind Kind, long Id, string Title, string Place, string? Color, long? FolderId, VerseRef? Ref = null, IReadOnlyList<(string Text, string Color)>? Cores = null);

    sealed record ExTarget(Control Control, long? Folder, Action<bool> Highlight);

    bool _exBuilt, _exQuiet;
    long? _exFolder;                       // the folder that is open (null = the top)
    readonly Stack<long?> _exBack = new();
    readonly HashSet<long> _exOpen = [];   // folders that are opened in the tree
    readonly List<ExItem> _exSel = [];
    readonly List<ExTarget> _exTargets = [];
    StackPanel _exTree = null!, _exList = null!, _exPath = null!;
    TextBlock _exInfo = null!, _exEmpty = null!;
    TextBox _exSearch = null!;
    Button _exBackBtn = null!, _exUpBtn = null!;
    Border _exGhost = null!;
    TextBlock _exGhostText = null!;
    Canvas _exLayer = null!;
    List<Folder> _exFolders = [];
    List<ExItem> _exRows = [];             // what the list shows, in its order (for the arrow keys)
    ExItem? _exAnchor;                     // where a Shift selection starts
    List<ExItem>? _exCut;                  // Ctrl+X: waits for Ctrl+V
    Grid _exHead = null!;
    Border _exToast = null!;
    TextBlock _exToastText = null!;
    Button _exToastUndo = null!;
    Avalonia.Threading.DispatcherTimer? _exToastTimer;

    enum ExSort { Name, Place, Kind }
    ExSort _exSort = ExSort.Place;
    bool _exSortDesc;

    /// <summary>One step that Ctrl+Z (or the "Rückgängig" button) takes back.</summary>
    sealed record ExUndo(string Text, Action Undo, bool TouchesText);
    readonly List<ExUndo> _exUndo = [];

    static bool SameItem(ExItem a, ExItem b) => a.Kind == b.Kind && a.Id == b.Id;

    // drag state
    List<ExItem>? _exDrag;
    Point _exFrom;
    bool _exDragging;
    ExTarget? _exOver;

    const string FolderIcon = "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z";
    const string NoteIcon = "M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z";
    const string MarkIcon = "M9 11l-6 6v3h9l3-3 M22 12l-4.6 4.6a2 2 0 0 1-2.8 0l-5.2-5.2a2 2 0 0 1 0-2.8L14 4";

    void BuildExplorer()
    {
        if (_exBuilt) return;
        _exBuilt = true;

        Button ToolBtn(string icon, string tip)
        {
            var b = new Button { Classes = { "tool" }, Width = 32, Height = 32, Content = new Icon { Data = Geometry.Parse(icon), IconSize = 16 } };
            ToolTip.SetTip(b, tip);
            return b;
        }
        _exBackBtn = ToolBtn("M19 12H5 M12 19l-7-7 7-7", L("Zurück"));
        _exUpBtn = ToolBtn("M12 19V5 M5 12l7-7 7 7", L("Eine Ebene höher"));
        _exBackBtn.Click += (_, _) => { if (_exBack.Count > 0) { _exFolder = _exBack.Pop(); _exInTrash = false; _exSel.Clear(); RefreshExplorer(); } };
        _exUpBtn.Click += (_, _) => { if (_exInTrash) ExNavigate(_exFolder); else if (_exFolder is long f) ExNavigate(FolderById(f)?.ParentId); };
        var newFolder = new Button { Content = L("＋ Neuer Ordner"), Classes = { "primary" }, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        newFolder.Click += (_, _) => AskFolderName(newFolder, L("Neuer Ordner"), "", n => { _vault.AddFolder(n); var made = _vault.GetFolders().Where(f => f.Name == n.Trim() && f.ParentId is null).MaxBy(f => f.Id); if (made is not null && _exFolder is long p) _vault.SetFolderParent(made.Id, p); AfterExplorerChange(); });
        _exSearch = new TextBox { PlaceholderText = L("In diesem Ordner suchen …"), Width = 220, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _exSearch.TextChanged += (_, _) => FillExplorerList();
        _exPath = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        var pathBox = new Border
        {
            CornerRadius = new(10), Padding = new(6, 2), Margin = new(8, 0, 0, 0), MinHeight = 34, BorderThickness = new(1),
            Child = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = _exPath },
        };
        pathBox.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        pathBox.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        var bar = new DockPanel { Margin = new(14, 12, 14, 8) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { _exSearch, newFolder } };
        DockPanel.SetDock(right, Dock.Right);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _exBackBtn, _exUpBtn } };
        DockPanel.SetDock(nav, Dock.Left);
        bar.Children.Add(nav); bar.Children.Add(right); bar.Children.Add(pathBox);

        _exTree = new StackPanel { Margin = new(6, 4, 6, 12) };
        var treeBox = new Border { CornerRadius = new(12), BorderThickness = new(1), Child = new ScrollViewer { Content = _exTree } };
        treeBox.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        treeBox.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));

        var head = _exHead = new Grid { ColumnDefinitions = new("28,*,140,110"), Margin = new(8, 6, 12, 4) };
        _exList = new StackPanel { Margin = new(6, 0, 6, 12) };
        _exEmpty = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(14, 10), IsVisible = false };
        var listScroll = new ScrollViewer { Content = new StackPanel { Children = { _exList, _exEmpty } }, Background = Brushes.Transparent };
        var listDock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        listDock.Children.Add(head); listDock.Children.Add(listScroll);
        var listBox = new Border { CornerRadius = new(12), BorderThickness = new(1), Child = listDock, Margin = new(0) };
        listBox.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        listBox.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        // a right click on the free space of the list: a new folder here, paste, undo
        var free = new ContextMenu();
        free.Opening += (_, _) =>
        {
            free.Items.Clear();
            if (_exInTrash)
            {
                free.Items.Add(MenuAction(L("Alle wiederherstellen"), () => _ = RestoreTrashAsync(_trRows.Select(r => r.Id).Reverse().ToList()), _trRows.Count > 0));
                free.Items.Add(MenuAction(L("Papierkorb leeren …"), () => _ = EmptyTrashAsync(), _trRows.Count > 0));
                return;
            }
            free.Items.Add(MenuAction(L("Neuer Ordner hier …"), () => newFolder.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent))));
            free.Items.Add(MenuAction(L("Hier einfügen (Strg+V)"), () => ExPaste(_exFolder), _exCut is not null));
            free.Items.Add(ExportMenu(_exFolder));
            free.Items.Add(MenuAction(L("Ganzen Vault als Markdown-Ordner exportieren …"), () => _ = ExportObsidianAsync()));
            free.Items.Add(MenuAction(_exUndo.Count > 0 ? L("Rückgängig: {0} (Strg+Z)", _exUndo[^1].Text) : L("Rückgängig (Strg+Z)"), ExUndoLast, _exUndo.Count > 0));
        };
        free.Items.Add(new MenuItem());   // a menu without items does not open
        listScroll.ContextMenu = free;
        // a click on the free space takes the selection away
        listScroll.PointerPressed += (_, e) => { if (e.Source == listScroll || e.Source is StackPanel) { _exSel.Clear(); var trash = _trSel.Count > 0; _trSel.Clear(); if (trash) FillExplorerList(); else ExShowSelection(); } };

        var split = new GridSplitter { Width = 8, Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Columns };
        var body = new Grid { ColumnDefinitions = new("240,8,*"), Margin = new(14, 0, 14, 0) };
        Grid.SetColumn(split, 1); Grid.SetColumn(listBox, 2);
        body.Children.Add(treeBox); body.Children.Add(split); body.Children.Add(listBox);

        _exInfo = new TextBlock { Classes = { "caption" }, Margin = new(16, 6, 16, 10), TextWrapping = TextWrapping.Wrap };
        // the note after a change: what happened, and a button that takes it back
        _exToastText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
        _exToastUndo = new Button { Content = L("Rückgängig"), Classes = { "primary" }, Margin = new(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _exToastUndo.Click += (_, _) => ExUndoLast();
        _exToast = new Border
        {
            CornerRadius = new(10), Padding = new(14, 6, 6, 6), Margin = new(14, 8, 14, 0), BorderThickness = new(1), IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _exToastText, _exToastUndo } },
        };
        _exToast.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
        _exToast.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsAccent"));
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top); DockPanel.SetDock(_exInfo, Dock.Bottom); DockPanel.SetDock(_exToast, Dock.Bottom);
        root.Children.Add(bar); root.Children.Add(_exInfo); root.Children.Add(_exToast); root.Children.Add(body);

        _exGhostText = new TextBlock { FontWeight = FontWeight.SemiBold };
        _exGhost = new Border { CornerRadius = new(8), Padding = new(10, 6), Child = _exGhostText, IsVisible = false, IsHitTestVisible = false, BorderThickness = new(1), Opacity = 0.95 };
        _exGhost.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
        _exGhost.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsAccent"));
        _exLayer = new Canvas { IsHitTestVisible = false, Children = { _exGhost } };
        ExplorerHost.Children.Add(root);
        ExplorerHost.Children.Add(_exLayer);
        ExplorerHost.Focusable = true;
        ExplorerHost.KeyDown += ExKeyDown;
    }

    /// <summary>The explorer without a mouse: arrows choose, Enter opens, Ctrl+X / Ctrl+V move, Ctrl+Z takes back, Del deletes, F2 renames.</summary>
    void ExKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || FocusManager?.GetFocusedElement() is TextBox) return;
        // a letter or digit: jump to the row that starts with it (also in the trash)
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.KeySymbol is { Length: 1 } sym && char.IsLetterOrDigit(sym[0]))
        { ExTypeAhead(sym); e.Handled = true; return; }
        if (_exInTrash) { if (TrashKeyDown(e)) e.Handled = true; return; }
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl: ExUndoLast(); break;
            case Key.X when ctrl: ExCut(); break;
            case Key.V when ctrl: ExPaste(_exFolder); break;
            case Key.A when ctrl: _exSel.Clear(); _exSel.AddRange(_exRows); if (_exInTrash) FillExplorerList(); else ExShowSelection(); break;
            case Key.Up when alt: if (_exFolder is long f) ExNavigate(FolderById(f)?.ParentId); break;
            case Key.Up: ExStep(-1, shift); break;
            case Key.Down: ExStep(1, shift); break;
            case Key.Home: ExStep(-100_000, shift); break;
            case Key.End: ExStep(100_000, shift); break;
            case Key.PageUp: ExStep(-10, shift); break;
            case Key.PageDown: ExStep(10, shift); break;
            case Key.Enter when _exSel.Count == 1: ExOpen(_exSel[0]); break;
            case Key.Delete when _exSel.Count > 0: ExDelete(_exSel.ToList()); break;
            case Key.F2 when _exSel is [{ Kind: ExKind.Folder } one]: ExRename(one, ExRowControl(one) ?? ExplorerHost); break;
            case Key.Back when _exFolder is long g: ExNavigate(FolderById(g)?.ParentId); break;
            case Key.Escape when _exCut is not null || _exSel.Count > 0: _exCut = null; _exSel.Clear(); FillExplorerList(); break;
            default: handled = false; break;
        }
        if (handled) e.Handled = true;
    }

    string _exTyped = "";
    DateTime _exTypedAt;

    /// <summary>Typing jumps to the first row that starts with what was typed ("Rö" → Römer …), as in the Windows Explorer; a pause starts anew.</summary>
    void ExTypeAhead(string text)
    {

        var now = DateTime.Now;
        if ((now - _exTypedAt).TotalSeconds > 1) _exTyped = "";
        _exTypedAt = now;
        var again = _exTyped.Length == 1 && text.Equals(_exTyped, StringComparison.CurrentCultureIgnoreCase);   // the same letter again: the next one with it
        if (!again) _exTyped += text;
        var typed = _exTyped;
        bool Starts(string s) => s.TrimStart('„', '"', '(').StartsWith(typed, StringComparison.CurrentCultureIgnoreCase);
        if (_exInTrash)
        {
            var cur = _trSel.Count > 0 ? _trRows.FindIndex(r => r.Id == _trSel[^1]) : -1;
            var order = _trRows.Select((r, i) => (r, i)).OrderBy(x => x.i <= cur && again ? x.i + _trRows.Count : x.i);
            if (order.FirstOrDefault(x => (!again || x.i != cur) && (Starts(x.r.Title) || Starts(TrashPlace(x.r.Place)))) is { r: not null } hit)
            {
                _trSel.Clear(); _trSel.Add(hit.r.Id); _trAnchor = hit.r.Id; FillTrashList();
                if (hit.i < _exList.Children.Count) _exList.Children[hit.i].BringIntoView();
            }
            return;
        }
        var at = _exSel.Count > 0 ? _exRows.FindIndex(r => SameItem(r, _exSel[^1])) : -1;
        var rows = _exRows.Select((r, i) => (r, i)).OrderBy(x => x.i <= at && again ? x.i + _exRows.Count : x.i);
        if (rows.FirstOrDefault(x => (!again || x.i != at) && (Starts(x.r.Title) || Starts(x.r.Place))) is { r: not null } found)
        {
            _exSel.Clear(); _exSel.Add(found.r); _exAnchor = found.r;
            ExShowSelection();
            ExRowControl(found.r)?.BringIntoView();
        }
    }

    Control? ExRowControl(ExItem item)
    {
        var i = _exRows.FindIndex(r => SameItem(r, item));
        return i >= 0 && i < _exList.Children.Count ? _exList.Children[i] : null;
    }

    /// <summary>Moves the chosen row up or down; with Shift the choice grows from where it started.</summary>
    void ExStep(int delta, bool extend)
    {
        if (_exRows.Count == 0) return;
        var cur = _exSel.Count > 0 ? _exRows.FindIndex(r => SameItem(r, _exSel[^1])) : -1;
        var next = cur < 0 ? (delta > 0 ? 0 : _exRows.Count - 1) : Math.Clamp(cur + delta, 0, _exRows.Count - 1);
        _exSel.Clear();
        var anchor = _exAnchor is { } a ? _exRows.FindIndex(r => SameItem(r, a)) : -1;
        if (extend && anchor >= 0)
        {
            var step = next >= anchor ? 1 : -1;
            for (var i = anchor; i != next + step; i += step) _exSel.Add(_exRows[i]);   // the last one is where the cursor is
        }
        else { _exSel.Add(_exRows[next]); _exAnchor = _exRows[next]; }
        ExShowSelection();
        ExRowControl(_exRows[next])?.BringIntoView();
    }

    void ExCut()
    {
        if (_exSel.Count == 0) return;
        _exCut = _exSel.ToList();
        FillExplorerList();
        ExNote(L("{0} ausgeschnitten. Öffne den Zielordner und drücke Strg+V.", _exCut.Count == 1 ? "„" + Shorten(_exCut[0].Title, 40) + "“" : L("{0} Einträge", _exCut.Count)), false);
    }

    void ExPaste(long? target)
    {
        if (_exCut is not { } items) return;
        _exCut = null;
        ExMove(items, target);
    }

    // ---- undo -----------------------------------------------------------------------------------------------------------

    void ExRemember(string text, Action undo, bool touchesText)
    {
        _exUndo.Add(new ExUndo(text, undo, touchesText));
        if (_exUndo.Count > 50) _exUndo.RemoveAt(0);
    }

    /// <summary>Shows what just happened under the list for a few seconds, with "Rückgängig" when it can be taken back.</summary>
    void ExNote(string text, bool canUndo)
    {
        _exToastText.Text = text;
        _exToastUndo.IsVisible = canUndo;
        _exToast.IsVisible = true;
        _exToastTimer?.Stop();
        _exToastTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(canUndo ? 10 : 6) };
        _exToastTimer.Tick += (_, _) => { _exToastTimer?.Stop(); _exToast.IsVisible = false; };
        _exToastTimer.Start();
    }

    async void ExUndoLast()
    {
        if (_exUndo.Count == 0) { ExNote(L("Es gibt nichts mehr rückgängig zu machen."), false); return; }
        var last = _exUndo[^1];
        _exUndo.RemoveAt(_exUndo.Count - 1);
        try { last.Undo(); }
        catch (Exception ex) { ExNote(L("Konnte nicht rückgängig gemacht werden: {0}", ex.Message), false); return; }
        _exSel.Clear();
        AfterExplorerChange();
        ExNote(L("Rückgängig gemacht: {0}", last.Text), false);
        if (last.TouchesText) { LoadChapterMarks(); await RedrawChapterAsync(); }
        RefreshCells();
    }

    MenuItem MenuAction(string text, Action act, bool enabled = true)
    {
        var i = new MenuItem { Header = text, IsEnabled = enabled };
        i.Click += (_, _) => act();
        return i;
    }

    Folder? FolderById(long id) => _exFolders.FirstOrDefault(f => f.Id == id);

    /// <summary>The folder a thing lies in; a folder id that no longer exists counts as the top.</summary>
    long? Known(long? folder) => folder is long f && _exFolders.Any(x => x.Id == f) ? f : null;

    IEnumerable<Folder> ChildFolders(long? parent) =>
        _exFolders.Where(f => Known(f.ParentId) == parent && f.Id != parent).OrderBy(f => f.Name, Comparer<string>.Create(NaturalCompare));

    /// <summary>The folder and every folder below it.</summary>
    HashSet<long?> Subtree(long? folder)
    {
        var set = new HashSet<long?> { folder };
        var queue = new Queue<long?>([folder]);
        while (queue.Count > 0)
            foreach (var c in ChildFolders(queue.Dequeue())) if (set.Add(c.Id)) queue.Enqueue(c.Id);
        return set;
    }

    void ExNavigate(long? folder)
    {
        if (folder == _exFolder && !_exInTrash) return;
        _exInTrash = false; _trSel.Clear();
        _exBack.Push(_exFolder);
        _exFolder = folder;
        _exSel.Clear();
        for (long? at = folder; at is long a; at = FolderById(a)?.ParentId) _exOpen.Add(a);   // the tree shows where you are
        _exSearch.Text = "";
        RefreshExplorer();
    }

    /// <summary>Everything the explorer shows: the marks and notes of the open vault.</summary>
    List<ExItem> ExAllItems()
    {
        var items = new List<ExItem>();
        var marks = _vault.GetMarks();
        // a mark inside a marked verse is the most important part of it: it shows highlighted in the verse, not as an entry of its own
        var wholeVerses = new HashSet<(string, int, int)>();
        foreach (var m in marks.Where(m => m.WholeVerse)) for (var v = m.Ref.Verse; v <= m.LastVerse; v++) wholeVerses.Add((m.Ref.Book, m.Ref.Chapter, v));
        bool IsCore(Mark m) => !m.WholeVerse && Enumerable.Range(m.Ref.Verse, m.LastVerse - m.Ref.Verse + 1).All(v => wholeVerses.Contains((m.Ref.Book, m.Ref.Chapter, v)));
        var cores = marks.Where(IsCore).ToLookup(m => (m.Ref.Book, m.Ref.Chapter));
        foreach (var m in marks.Where(m => !IsCore(m)))
        {
            var place = Label(m.Ref) + (m.MultiVerse ? "–" + m.LastVerse : "");
            var hasNote = m.Note.Length > 0;
            var title = (hasNote ? m.Note : m.Text).ReplaceLineEndings(" ").Trim();   // the whole text: a long verse wraps instead of being cut off
            var parts = hasNote || !m.WholeVerse ? null
                : cores[(m.Ref.Book, m.Ref.Chapter)].Where(c => c.Ref.Verse >= m.Ref.Verse && c.LastVerse <= m.LastVerse && c.Text.Trim().Length > 0).Select(c => (c.Text.Trim(), c.Color)).ToList();
            items.Add(new ExItem(ExKind.Mark, m.Id, title.Length > 0 ? title : place, place, m.Color, Known(m.FolderId), m.Ref, parts));
        }
        foreach (var n in _vault.GetNotes())
            items.Add(new ExItem(ExKind.Note, NoteKey(n.Ref), n.Text.ReplaceLineEndings(" ").Trim(), Label(n.Ref), null, Known(n.FolderId), n.Ref));
        return items;
    }

    /// <summary>Notes have no id of their own (one note per verse): the verse is the key.</summary>
    static long NoteKey(VerseRef r) => (long)r.Book.Aggregate(0, (h, c) => (h * 31 + c) % 100_000) * 1_000_000 + r.Chapter * 1000 + r.Verse;   // the same book always gives the same number, two books never the same one

    void RefreshExplorer()
    {
        if (!_exBuilt || _vault is null) return;
        _exFolders = _vault.GetFolders();
        if (_exFolder is long f && FolderById(f) is null) _exFolder = null;
        FillExplorerTree();
        FillExplorerPath();
        FillExplorerList();
        _exBackBtn.IsEnabled = _exBack.Count > 0;
        _exUpBtn.IsEnabled = _exFolder is not null || _exInTrash;
        _exSearch.PlaceholderText = _exInTrash ? L("Im Papierkorb suchen …") : L("In diesem Ordner suchen …");
    }

    /// <summary>After a change here the other lists (Markierungen, Notizen, the text) show it too.</summary>
    void AfterExplorerChange()
    {
        RefreshExplorer();
        _exQuiet = true;
        try { RefreshCollection(); RefreshNotesTree(); } finally { _exQuiet = false; }
    }

    // ---- tree -----------------------------------------------------------------------------------------------------------

    void FillExplorerTree()
    {
        _exTree.Children.Clear();
        _exTargets.RemoveAll(t => t.Control.Parent == _exTree);
        var items = ExAllItems();
        int Count(long? folder) => items.Count(i => i.FolderId == folder);
        _exTree.Children.Add(TreeRow(null, L("Alle Markierungen"), 0, Count(null), ChildFolders(null).Any()));
        void Add(long? parent, int depth)
        {
            foreach (var f in ChildFolders(parent))
            {
                var kids = ChildFolders(f.Id).Any();
                _exTree.Children.Add(TreeRow(f, f.Name, depth, Count(f.Id), kids));
                if (kids && _exOpen.Contains(f.Id)) Add(f.Id, depth + 1);
            }
        }
        Add(null, 1);
        _exTree.Children.Add(TrashTreeRow());
    }

    Control TreeRow(Folder? folder, string name, int depth, int count, bool hasKids)
    {
        long? id = folder?.Id;
        var open = id is null || _exOpen.Contains(id.Value);
        var chevron = new TextBlock { Text = hasKids && id is not null ? (open ? "▾" : "▸") : "", Width = 14, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        var icon = new Icon { Data = Geometry.Parse(id is null ? "M3 12l9-8 9 8 M5 10v10h14V10" : FolderIcon), IconSize = 15, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var num = new TextBlock { Text = count > 0 ? count.ToString() : "", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        var line = new DockPanel();
        DockPanel.SetDock(num, Dock.Right);
        line.Children.Add(num);
        line.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { chevron, icon, label } });
        var current = id == _exFolder && !_exInTrash;
        var box = new Border { Padding = new(6 + depth * 14, 6, 8, 6), CornerRadius = new(8), Child = line, Cursor = new Cursor(StandardCursorType.Hand), Margin = new(0, 1) };
        void Paint(bool hover) => box.Background = current ? Res("BsAccentSoft") ?? Brushes.Transparent : hover ? Res("BsHover") ?? Brushes.Transparent : Brushes.Transparent;
        Paint(false);
        if (current) label.FontWeight = FontWeight.SemiBold;
        box.PointerEntered += (_, _) => { if (!_exDragging) Paint(true); };
        box.PointerExited += (_, _) => { if (!_exDragging) Paint(false); };
        box.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
            // the arrow opens and closes, the rest opens the folder
            if (hasKids && id is long f && e.GetPosition(box).X < 6 + depth * 14 + 18) { if (!_exOpen.Add(f)) _exOpen.Remove(f); FillExplorerTree(); return; }
            if (id is long g && e.ClickCount == 2 && hasKids) { if (!_exOpen.Add(g)) _exOpen.Remove(g); }
            ExNavigate(id);
            if (e.ClickCount == 2) FillExplorerTree();
        };
        _exTargets.Add(new ExTarget(box, id, on => { if (on) box.Background = Res("BsAccentSoft") ?? Brushes.Transparent; else Paint(false); }));
        if (folder is not null) box.ContextMenu = FolderContextMenu(new ExItem(ExKind.Folder, folder.Id, folder.Name, "", null, Known(folder.ParentId)), box);
        else box.ContextMenu = new ContextMenu { Items = { MenuAction(L("Neuer Ordner …"), () => AskFolderName(box, L("Neuer Ordner"), "", n => { _vault.AddFolder(n); AfterExplorerChange(); })), ExportMenu(null) } };
        return box;
    }

    // ---- path bar -------------------------------------------------------------------------------------------------------

    void FillExplorerPath()
    {
        _exTargets.RemoveAll(t => t.Control.Parent == _exPath);
        _exPath.Children.Clear();
        var chain = new List<Folder>();
        for (long? at = _exFolder; at is long a && FolderById(a) is { } f && chain.Count < 50; at = f.ParentId) chain.Insert(0, f);
        void Crumb(string text, long? folder, bool last)
        {
            var b = new Button { Content = text, Classes = { "tool" }, Width = double.NaN, Height = 28, Padding = new(8, 0), FontWeight = last ? FontWeight.SemiBold : FontWeight.Normal };
            b.Click += (_, _) => ExNavigate(folder);
            _exTargets.Add(new ExTarget(b, folder, on => b.Background = on ? Res("BsAccentSoft") ?? Brushes.Transparent : Brushes.Transparent));
            _exPath.Children.Add(b);
            if (!last) _exPath.Children.Add(new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 });
        }
        if (_exInTrash) { Crumb(L("Papierkorb"), null, true); return; }
        Crumb(L("Alle Markierungen"), null, chain.Count == 0);
        for (var i = 0; i < chain.Count; i++) Crumb(chain[i].Name, chain[i].Id, i == chain.Count - 1);
    }

    // ---- list -----------------------------------------------------------------------------------------------------------

    void FillExplorerList()
    {
        if (!_exBuilt) return;
        if (_exInTrash) { FillTrashList(); return; }
        _exTargets.RemoveAll(t => t.Control.Parent == _exList);
        _exList.Children.Clear();
        var query = (_exSearch.Text ?? "").Trim();
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var all = ExAllItems();
        IEnumerable<ExItem> folderRows, itemRows;
        if (words.Length == 0)
        {
            folderRows = ChildFolders(_exFolder).Select(f => new ExItem(ExKind.Folder, f.Id, f.Name, "", null, Known(f.ParentId)));
            itemRows = all.Where(i => i.FolderId == _exFolder);
        }
        else
        {
            // a search looks into the open folder and every folder below it
            var inside = Subtree(_exFolder);
            bool Hit(string text) => words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
            folderRows = _exFolders.Where(f => inside.Contains(f.Id) && f.Id != _exFolder && Hit(f.Name)).Select(f => new ExItem(ExKind.Folder, f.Id, f.Name, "", null, Known(f.ParentId)));
            itemRows = all.Where(i => inside.Contains(i.FolderId) && Hit(i.Title + " " + i.Place));
        }
        // folders stay on top (as in the Explorer); the column that was clicked decides the order
        var byName = Comparer<string>.Create(NaturalCompare);
        var folderList = folderRows.OrderBy(f => f.Title, byName).ToList();
        if (_exSort == ExSort.Name && _exSortDesc) folderList.Reverse();
        IOrderedEnumerable<ExItem> ordered = _exSort switch
        {
            ExSort.Name => itemRows.OrderBy(i => i.Title, byName),
            ExSort.Kind => itemRows.OrderBy(i => i.Kind),
            _ => itemRows.OrderBy(i => i.Ref is { } r ? BookOrder(r.Book) : int.MaxValue),
        };
        var itemList = ordered.ThenBy(i => i.Ref is { } r ? BookOrder(r.Book) : int.MaxValue).ThenBy(i => i.Ref?.Chapter).ThenBy(i => i.Ref?.Verse).ToList();
        if (_exSortDesc) itemList.Reverse();
        List<ExItem> rows = [.. folderList, .. itemList];
        _exRows = rows;
        FillExplorerHead();
        _exSel.RemoveAll(s => !rows.Any(r => SameItem(r, s)));
        foreach (var r in rows) _exList.Children.Add(ListRow(r, all));
        _exEmpty.IsVisible = rows.Count == 0;
        _exEmpty.Text = words.Length > 0 ? L("Nichts gefunden.") : L("Dieser Ordner ist leer. Ziehe Markierungen, Notizen oder Ordner hierher, oder lege mit „＋ Neuer Ordner“ einen Unterordner an.");
        ExUpdateInfo();
    }

    /// <summary>The column heads: a click sorts by that column, a second click turns the order round.</summary>
    void FillExplorerHead()
    {
        _exHead.Children.Clear();
        void Head(string text, ExSort sort, int column)
        {
            var on = _exSort == sort;
            var b = new Button
            {
                Classes = { "ghost" }, Padding = new(4, 2), HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12, FontWeight = FontWeight.SemiBold,
                Content = text + (on ? (_exSortDesc ? "  ▼" : "  ▲") : ""), Opacity = on ? 1 : 0.75,
            };
            ToolTip.SetTip(b, L("Nach dieser Spalte sortieren"));
            b.Click += (_, _) => { if (_exSort == sort) _exSortDesc = !_exSortDesc; else { _exSort = sort; _exSortDesc = false; } FillExplorerList(); };
            Grid.SetColumn(b, column);
            _exHead.Children.Add(b);
        }
        Head(L("Name"), ExSort.Name, 1);
        Head(L("Stelle"), ExSort.Place, 2);
        Head(L("Art"), ExSort.Kind, 3);
    }


    Control ListRow(ExItem item, List<ExItem> all)
    {

        var grid = new Grid { ColumnDefinitions = new("28,*,140,110") };
        Control lead = item.Kind switch
        {
            ExKind.Folder => new Icon { Data = Geometry.Parse(FolderIcon), IconSize = 16 },
            ExKind.Note => new Icon { Data = Geometry.Parse(NoteIcon), IconSize = 15 },
            _ => new Border { Width = 12, Height = 12, CornerRadius = new(6), Background = new SolidColorBrush(MarkColor(item.Color ?? "")) },
        };
        lead.VerticalAlignment = VerticalAlignment.Center; lead.HorizontalAlignment = HorizontalAlignment.Left;
        if (item.Kind == ExKind.Folder) lead.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        var title = item.Kind == ExKind.Folder
            ? item.Title + "   " + L("({0})", all.Count(i => i.FolderId == item.Id) + ChildFolders(item.Id).Count())
            : item.Title.Length > 0 ? item.Title : L("(ohne Text)");
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = item.Kind == ExKind.Folder ? FontWeight.SemiBold : FontWeight.Normal };
        if (item.Kind == ExKind.Folder) { name.Text = title; name.TextTrimming = TextTrimming.CharacterEllipsis; }
        else FillWrapped(name, title, item.Cores);
        var place = new TextBlock { Text = item.Place, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var kind = new TextBlock { Text = item.Kind switch { ExKind.Folder => L("Ordner"), ExKind.Note => L("Notiz"), _ => L("Markierung") }, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1); Grid.SetColumn(place, 2); Grid.SetColumn(kind, 3);
        grid.Children.Add(lead); grid.Children.Add(name); grid.Children.Add(place); grid.Children.Add(kind);
        var row = new Border { Padding = new(8, 7), CornerRadius = new(8), Child = grid, Margin = new(0, 1), Cursor = new Cursor(StandardCursorType.Hand), Opacity = _exCut?.Any(c => SameItem(c, item)) == true ? 0.45 : 1 };
        void Paint(bool hover) => row.Background = _exSel.Any(s => s.Kind == item.Kind && s.Id == item.Id) ? Res("BsAccentSoft") ?? Brushes.Transparent : hover ? Res("BsHover") ?? Brushes.Transparent : Brushes.Transparent;
        row.Tag = (Action)(() => Paint(row.IsPointerOver && !_exDragging));   // a new choice only paints the rows again (see ExShowSelection)
        Paint(false);
        row.PointerEntered += (_, _) => { if (!_exDragging) Paint(true); };
        row.PointerExited += (_, _) => { if (!_exDragging) Paint(false); };
        if (item.Kind == ExKind.Folder) _exTargets.Add(new ExTarget(row, item.Id, on => { if (on) row.Background = Res("BsAccentSoft") ?? Brushes.Transparent; else Paint(false); }));
        ExDragSource(row, item);
        row.ContextMenu = item.Kind == ExKind.Folder ? FolderContextMenu(item, row) : ItemContextMenu(item, row);
        return row;
    }

    /// <summary>The whole text of an entry (it wraps, nothing is cut off); the most important parts of a verse stand highlighted in their color.</summary>
    static void FillWrapped(TextBlock block, string text, IReadOnlyList<(string Text, string Color)>? cores)
    {
        block.TextWrapping = TextWrapping.Wrap;
        var spans = new List<(int At, int Len, string Color)>();
        foreach (var (core, color) in (cores ?? []).OrderByDescending(c => c.Text.Length))
        {
            var from = 0;
            while (text.IndexOf(core, from, StringComparison.OrdinalIgnoreCase) is var at and >= 0)
            {
                if (!spans.Any(s => at < s.At + s.Len && s.At < at + core.Length)) { spans.Add((at, core.Length, color)); break; }
                from = at + 1;
            }
        }
        if (spans.Count == 0) { block.Text = text; return; }
        var pos = 0;
        foreach (var (at, len, color) in spans.OrderBy(s => s.At))
        {
            if (at > pos) block.Inlines!.Add(new Avalonia.Controls.Documents.Run(text[pos..at]));
            var c = MarkColor(color);
            block.Inlines!.Add(new Avalonia.Controls.Documents.Run(text.Substring(at, len)) { Background = new SolidColorBrush(Color.FromArgb(120, c.R, c.G, c.B)) });
            pos = at + len;
        }
        if (pos < text.Length) block.Inlines!.Add(new Avalonia.Controls.Documents.Run(text[pos..]));
    }

    void ExOpen(ExItem item)
    {
        if (item.Kind == ExKind.Folder) { ExNavigate(item.Id); return; }
        if (item.Ref is not { } r) return;
        _ = GoToAsync(r.Book, r.Chapter, r.Verse);
    }

    // ---- menus ----------------------------------------------------------------------------------------------------------

    ContextMenu FolderContextMenu(ExItem folder, Control anchor) => new()
    {
        Items =
        {
            MenuAction(L("Öffnen"), () => ExNavigate(folder.Id)),
            MenuAction(L("Neuer Unterordner …"), () => AskFolderName(anchor, L("Neuer Ordner"), "", n =>
            {
                var id = _vault.AddFolder(n); _vault.SetFolderParent(id, folder.Id); _exOpen.Add(folder.Id); AfterExplorerChange();
            })),
            MenuAction(L("Umbenennen …"), () => ExRename(folder, anchor)),
            MoveMenu([folder]),
            new Separator(),
            ExportMenu(folder.Id),
            MenuAction(L("Ausschneiden (Strg+X)"), () => { ExSelectFor(folder); ExCut(); }),
            MenuAction(L("Hier einfügen (Strg+V)"), () => ExPaste(folder.Id), _exCut is not null && !_exCut.Any(c => c.Kind == ExKind.Folder && Subtree(c.Id).Contains(folder.Id))),
            new Separator(),
            MenuAction(L("Ordner löschen (der Inhalt bleibt erhalten)"), () => ExDelete([folder])),
        },
    };

    ContextMenu ItemContextMenu(ExItem item, Control anchor)
    {
        var targets = _exSel.Any(s => s.Kind == item.Kind && s.Id == item.Id) && _exSel.Count > 1 ? _exSel.ToList() : [item];
        return new ContextMenu
        {
            Items =
            {
                MenuAction(L("Zur Stelle springen"), () => ExOpen(item)),
                MenuAction(L("Ausschneiden (Strg+X)"), () => { ExSelectFor(item); ExCut(); }),
                MoveMenu(targets),
                ColorMenu(targets, anchor),
                TagMenu(targets, anchor),
                new Separator(),
                MenuAction(targets.Count > 1 ? L("{0} Einträge löschen", targets.Count) : item.Kind == ExKind.Note ? L("Notiz löschen") : L("Markierung löschen"), () => ExDelete(targets)),
            },
        };
    }

    /// <summary>"Verschieben nach" with every folder, as a tree (indented), for anyone who does not drag.</summary>
    MenuItem MoveMenu(List<ExItem> items)
    {
        var menu = new MenuItem { Header = L("Verschieben nach") };
        menu.Items.Add(MenuAction(L("Alle Markierungen (oberste Ebene)"), () => ExMove(items, null)));
        void Add(long? parent, int depth)
        {
            foreach (var f in ChildFolders(parent))
            {
                var id = f.Id;
                var blocked = items.Any(i => i.Kind == ExKind.Folder && Subtree(i.Id).Contains(id));
                menu.Items.Add(MenuAction(new string(' ', depth * 4) + f.Name, () => ExMove(items, id), !blocked));
                Add(f.Id, depth + 1);
            }
        }
        Add(null, 0);
        return menu;
    }

    void AskFolderName(Control anchor, string title, string start, Action<string> done)
    {
        var box = new TextBox { Text = start, Width = 240, PlaceholderText = L("Name des Ordners") };
        var ok = new Button { Content = L("OK"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var fly = new Flyout { Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, box, ok } } };
        void Apply()
        {
            var n = (box.Text ?? "").Trim();
            if (n.Length == 0) return;
            fly.Hide();
            done(n);
        }
        ok.Click += (_, _) => Apply();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(); };
        fly.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        fly.ShowAt(anchor);
    }

    // ---- actions --------------------------------------------------------------------------------------------------------

    /// <summary>A menu on a row that is not chosen works on that row alone; on a chosen row on everything chosen.</summary>
    void ExSelectFor(ExItem item)
    {
        if (_exSel.Any(s => SameItem(s, item))) return;
        _exSel.Clear(); _exSel.Add(item);
    }

    void ExRename(ExItem folder, Control anchor) =>
        AskFolderName(anchor, L("Umbenennen"), folder.Title, n =>
        {
            var old = FolderById(folder.Id)?.Name ?? folder.Title;
            _vault.RenameFolder(folder.Id, n);
            ExRemember(L("„{0}“ umbenannt", old), () => _vault.RenameFolder(folder.Id, old), false);
            AfterExplorerChange();
            ExNote(L("„{0}“ heißt jetzt „{1}“.", old, n.Trim()), true);
        });

    void ExMove(List<ExItem> items, long? target)
    {
        var moved = 0; var refused = false; ExItem? firstMoved = null;
        var back = new List<Action>();
        foreach (var i in items)
        {
            switch (i.Kind)
            {
                case ExKind.Folder:
                    var oldParent = Known(FolderById(i.Id)?.ParentId);
                    if (i.Id == target || oldParent == target) continue;
                    if (_vault.SetFolderParent(i.Id, target)) { moved++; firstMoved ??= i; back.Add(() => _vault.SetFolderParent(i.Id, oldParent)); } else refused = true;
                    break;
                case ExKind.Mark:
                    if (i.FolderId == target) continue;
                    _vault.SetMarkFolder(i.Id, target); moved++; firstMoved ??= i;
                    var oldMark = i.FolderId; back.Add(() => _vault.SetMarkFolder(i.Id, oldMark));
                    break;
                case ExKind.Note:
                    if (i.FolderId == target || i.Ref is not { } r) continue;
                    _vault.SetNoteFolder(r, target); moved++; firstMoved ??= i;
                    var oldNote = i.FolderId; back.Add(() => _vault.SetNoteFolder(r, oldNote));
                    break;
            }
        }
        if (target is long t) _exOpen.Add(t);
        _exSel.Clear();
        AfterExplorerChange();
        var where = target is long tf && FolderById(tf) is { } f ? f.Name : L("Alle Markierungen");
        var what = moved == 1 && firstMoved is not null ? "„" + Shorten(firstMoved.Title, 40) + "“" : L("{0} Einträge", moved);
        if (moved > 0)
        {
            back.Reverse();
            ExRemember(L("{0} verschoben", what), () => { foreach (var b in back) b(); }, false);
            ExNote(L("{0} nach „{1}“ verschoben.", what, where), true);
        }
        if (refused) ExNote(L("Ein Ordner kann nicht in sich selbst oder in einen seiner Unterordner."), moved > 0);
    }

    /// <summary>
    /// Deletes at once (no question): everything goes into the trash, where it waits 30 days. Ctrl+Z or the "Rückgängig" button
    /// brings it back from there as it was.
    /// </summary>
    async void ExDelete(List<ExItem> items)
    {
        if (items.Count == 0) return;
        var trash = new List<long>();
        var touchedText = false;
        foreach (var i in items)
        {
            long t = 0;
            switch (i.Kind)
            {
                case ExKind.Folder: t = _vault.TrashFolder(i.Id); _exOpen.Remove(i.Id); break;
                case ExKind.Mark: t = _vault.TrashMark(i.Id); touchedText |= t > 0; break;
                case ExKind.Note when i.Ref is { } r:
                    t = _vault.TrashNote(r);
                    if (t > 0 && _noteRef == r) NoteEditor.Text = _noteLoaded = "";
                    break;
            }
            if (t > 0) trash.Add(t);
        }
        if (trash.Count == 0) return;
        trash.Reverse();   // back in the opposite order (a folder before what was taken out of it)
        var what = trash.Count == 1 ? "„" + Shorten(items[0].Title, 40) + "“" : L("{0} Einträge", trash.Count);
        ExRemember(L("{0} gelöscht", what), () => _ = RestoreTrashAsync(trash, quiet: true), touchedText);
        _exSel.Clear();
        AfterExplorerChange();
        ExNote(L("{0} in den Papierkorb gelegt.", what), true);
        if (touchedText) { LoadChapterMarks(); await RedrawChapterAsync(); }
        RefreshCells();
    }

    // ---- dragging -------------------------------------------------------------------------------------------------------

    ExTarget? ExTargetAt(Point p) =>
        _exTargets.FirstOrDefault(t => t.Control.IsEffectivelyVisible && t.Control.TranslatePoint(default, ExplorerHost) is { } tl && new Rect(tl, t.Control.Bounds.Size).Contains(p));

    void ExDragSource(Control row, ExItem item)
    {
        void Reset()
        {
            _exDrag = null; _exDragging = false; _exGhost.IsVisible = false;
            _exOver?.Highlight(false); _exOver = null;
        }
        row.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(row).Properties;
            if (props.IsRightButtonPressed)
            {
                // a right click on a row that is not chosen chooses it alone (as in the Explorer)
                if (!_exSel.Any(s => s.Kind == item.Kind && s.Id == item.Id)) { _exSel.Clear(); _exSel.Add(item); }
                return;
            }
            if (!props.IsLeftButtonPressed) return;
            ExplorerHost.Focusable = true; ExplorerHost.Focus();
            if (e.ClickCount == 2) { e.Handled = true; ExOpen(item); return; }
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            var chosen = _exSel.Any(s => s.Kind == item.Kind && s.Id == item.Id);
            var shiftKey = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (shiftKey && _exAnchor is { } from && _exRows.FindIndex(r => SameItem(r, from)) is var a and >= 0 && _exRows.FindIndex(r => SameItem(r, item)) is var b and >= 0)
            { _exSel.Clear(); _exSel.AddRange(_exRows.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1)); }   // Shift+click: everything in between
            else if (ctrl) { if (chosen) _exSel.RemoveAll(s => s.Kind == item.Kind && s.Id == item.Id); else _exSel.Add(item); _exAnchor = item; }
            else { if (!chosen) { _exSel.Clear(); _exSel.Add(item); } _exAnchor = item; }
            _exDrag = _exSel.Count > 0 && _exSel.Any(s => s.Kind == item.Kind && s.Id == item.Id) ? _exSel.ToList() : [item];
            _exFrom = e.GetPosition(ExplorerHost); _exDragging = false;
            e.Pointer.Capture(row);
            ExShowSelection();   // the rows stay as they are (the list used to be built anew on every click), so the pointer keeps its capture
        };
        row.PointerMoved += (_, e) =>
        {
            if (_exDrag is null) return;
            var p = e.GetPosition(ExplorerHost);
            if (!_exDragging)
            {
                if (Math.Abs(p.X - _exFrom.X) + Math.Abs(p.Y - _exFrom.Y) < 8) return;   // a click must not become a drag
                _exDragging = true;
                _exGhostText.Text = _exDrag.Count == 1 ? "↳ " + Shorten(_exDrag[0].Title, 40) : "↳ " + L("{0} Einträge", _exDrag.Count);
                _exGhost.IsVisible = true;
            }
            Canvas.SetLeft(_exGhost, Math.Min(p.X + 14, Math.Max(0, ExplorerHost.Bounds.Width - 260)));
            Canvas.SetTop(_exGhost, p.Y + 12);
            var over = ExTargetAt(p);
            if (over is not null && _exDrag.Any(d => d.Kind == ExKind.Folder && Subtree(d.Id).Contains(over.Folder))) over = null;   // not into itself
            if (!ReferenceEquals(over, _exOver)) { _exOver?.Highlight(false); over?.Highlight(true); _exOver = over; }
        };
        row.PointerReleased += (_, e) =>
        {
            if (_exDrag is null) return;
            var items = _exDrag; var wasDragging = _exDragging; var target = _exOver;
            e.Pointer.Capture(null);
            Reset();
            if (wasDragging && target is not null) ExMove(items, target.Folder);
            else if (!wasDragging && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _exSel.Count > 1) { _exSel.Clear(); _exSel.Add(item); ExShowSelection(); }
        };
        row.PointerCaptureLost += (_, _) => { if (_exDrag is not null && !_exKeepCapture) Reset(); };
    }

    bool _exKeepCapture;

    /// <summary>Shows a new choice of rows: only their color and the line below change, nothing is read from the vault again.</summary>
    void ExShowSelection()
    {
        if (_exInTrash) { FillExplorerList(); return; }
        foreach (var c in _exList.Children) if (c.Tag is Action paint) paint();
        ExUpdateInfo();
    }

    void ExUpdateInfo()
    {
        var folders = _exRows.Count(r => r.Kind == ExKind.Folder);
        _exInfo.Text = L("{0} Ordner, {1} Einträge", folders, _exRows.Count - folders) + (_exSel.Count > 0 ? "  ·  " + L("{0} ausgewählt", _exSel.Count) : "");
        // the keys stood here as one long line all the time; they are one hover away now
        ToolTip.SetTip(_exInfo, L("Doppelklick oder Enter öffnet · Ziehen oder Strg+X / Strg+V ordnet ein · Pfeiltasten wählen, mit Umschalt mehrere · Entf löscht · Strg+Z macht rückgängig · F2 benennt um"));
    }
}
