using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The document list as folders you can fill by dragging: a document is dragged onto a folder (or onto "Ohne Ordner") to move it,
/// or onto a category chip to change its category. The same actions are in the right-click menu for anyone who does not drag.
/// </summary>
public partial class MainWindow
{
    sealed record DropTarget(Control Control, Action<Document> Drop, Action<bool> Highlight);

    readonly List<DropTarget> _dropTargets = [];
    readonly HashSet<long> _closedDocFolders = [];
    Document? _dragDoc;
    Point _dragFrom;
    bool _dragging;
    DropTarget? _dragOver;

    void InitDocTree()
    {
        var name = new TextBox { PlaceholderText = L("Name des Ordners"), Width = 220 };
        var create = new Button { Content = L("Anlegen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var fly = new Flyout { Content = new StackPanel { Spacing = 8, Children = { name, create } } };
        void Make()
        {
            var n = (name.Text ?? "").Trim();
            if (n.Length == 0) return;
            if (!_vault.GetFolders().Any(f => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase))) _vault.AddFolder(n);
            name.Text = ""; fly.Hide();
            RefreshDocs(); RefreshCollection();
        }
        create.Click += (_, _) => Make();
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Make(); };
        DocFolderBtn.Flyout = fly;
        DocTrashBtn.Click += (_, _) => OpenTrash();
        UpdateDocTrashBtn();
    }

    /// <summary>The trash button above the documents, with how much lies in the trash.</summary>
    void UpdateDocTrashBtn()
    {
        if (_vault is null) return;
        var n = _vault.GetTrash().Count;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new Icon { Data = Geometry.Parse(TrashIcon), IconSize = 14, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = n > 0 ? $"{L("Papierkorb")} {n}" : L("Papierkorb"), VerticalAlignment = VerticalAlignment.Center });
        DocTrashBtn.Content = row;
    }

    // ---- the tree ------------------------------------------------------------------------------------------------------

    /// <summary>With "all vaults" (or shared vaults): the documents of the other vaults under their own heading; a click opens the vault and the document.</summary>
    int AddForeignDocs(string? query)
    {
        var shown = 0;
        foreach (var name in ShownVaults().Skip(1))
        {
            List<Document> docs;
            try { docs = StoreOf(name).GetDocuments(string.IsNullOrEmpty(query) ? null : query).Where(d => !d.IsTemplate).ToList(); }
            catch { continue; }
            if (docs.Count == 0) continue;
            shown += docs.Count;
            DocTree.Children.Add(new TextBlock { Text = "◆ " + name + "   ·   " + docs.Count, FontWeight = FontWeight.SemiBold, Margin = new(4, 14, 0, 4) });
            foreach (var d in docs)
            {
                var vault = name; var doc = d;
                var row = new Border
                {
                    Padding = new(10, 7, 8, 7), CornerRadius = new(8), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new StackPanel { Spacing = 2, Children = {
                        new TextBlock { Text = d.Title.Length > 0 ? d.Title : L("(ohne Titel)"), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = Shorten(PlainPreview(d.Body), 100), Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis } } },
                };
                ToolTip.SetTip(row, L("Wechselt in den Vault „{0}“ und öffnet das Dokument.", vault));
                row.PointerEntered += (_, _) => row.Background = Res("BsSurface2") ?? Brushes.Transparent;
                row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
                // a tap that lifts where it went down (a finger: the list scrolls and takes the finger, so the row itself never heard it lift)
                row.OnPress(async (_, e) =>
                {
                    if (e.Pointer.Type != PointerType.Touch && !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                    await OpenForeignDocumentAsync(vault, doc.Id);
                });
                DocTree.Children.Add(row);
            }
        }
        return shown;
    }

    /// <summary>A document of another vault: that vault becomes the active one, then the document opens.</summary>
    async Task OpenForeignDocumentAsync(string vault, long id)
    {
        await SwitchVaultAsync(vault);
        if (_vault.Name == vault && _vault.GetDocuments().FirstOrDefault(x => x.Id == id) is { } fresh) OpenDocument(fresh);
    }

    /// <summary>The list; returns how many documents of other vaults are shown below the own ones.</summary>
    int BuildDocTree(List<Document> docs, bool flat)
    {
        BuildOwnDocTree(docs, flat);
        return ShownVaults().Count > 1 ? AddForeignDocs(DocSearchBox.Text?.Trim()) : 0;
    }

    void BuildOwnDocTree(List<Document> docs, bool flat)
    {
        DocTree.Children.Clear();
        var folders = FolderTree(_vault.GetFolders());
        if (flat || folders.Count == 0)
        {
            foreach (var d in docs) DocTree.Children.Add(DocRow(d, 0));
            if (folders.Count == 0 && docs.Count > 0)
                DocTree.Children.Add(new TextBlock { Text = L("Tipp: „＋ Ordner“ legt Ordner an. Danach kannst du Dokumente per Ziehen hineinlegen."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(4, 10, 0, 0) });
            return;
        }
        var known = folders.Select(f => f.Id).ToHashSet();
        var groups = folders.Select(f => (f.Id, f.Name, f.Depth, Docs: docs.Where(d => d.FolderId == f.Id).ToList())).ToList();
        groups.Add((0, L("Ohne Ordner"), 0, docs.Where(d => d.FolderId is not long fid || !known.Contains(fid)).ToList()));
        foreach (var (id, name, depth, inFolder) in groups)
        {
            var open = !_closedDocFolders.Contains(id);
            DocTree.Children.Add(FolderHeader(id, name, inFolder.Count, open, depth));
            if (!open) continue;
            if (inFolder.Count == 0) DocTree.Children.Add(new TextBlock { Text = L("leer – hierher ziehen"), Classes = { "caption" }, Margin = new(30, 0, 0, 4), Opacity = 0.7 });
            foreach (var d in inFolder) DocTree.Children.Add(DocRow(d, 16));
        }
    }

    Control FolderHeader(long id, string name, int count, bool open, int depth = 0)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children =
            {
                new TextBlock { Text = open ? "▾" : "▸", Width = 12, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 },
                FolderGlyph(),
                new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = count.ToString(), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var box = new Border { Padding = new(8, 8), CornerRadius = new(8), Background = Brushes.Transparent, Child = line, Cursor = new Cursor(StandardCursorType.Hand), Margin = new(depth * 16, 4, 0, 0) };
        // on the lift of a tap (a finger: the list scrolls and takes the finger, so the release never came to this row)
        box.OnPress((_, e) =>
        {
            if (_dragging || (e.Pointer.Type != PointerType.Touch && !e.GetCurrentPoint(box).Properties.IsLeftButtonPressed)) return;
            if (!_closedDocFolders.Add(id)) _closedDocFolders.Remove(id);
            RefreshDocs();
        });
        void Light(bool on) => box.Background = on ? Res("BsAccentSoft") ?? Brushes.Transparent : Brushes.Transparent;
        box.PointerEntered += (_, _) => { if (!_dragging) box.Background = Res("BsSurface2") ?? Brushes.Transparent; };
        box.PointerExited += (_, _) => { if (!_dragging) box.Background = Brushes.Transparent; };
        _dropTargets.Add(new DropTarget(box, d => MoveDocument(d, id == 0 ? null : id, name), Light));
        if (id > 0) box.ContextMenu = FolderMenu(id, name, box);
        return box;
    }

    ContextMenu FolderMenu(long id, string name, Control anchor)
    {
        var rename = new MenuItem { Header = L("Umbenennen …") };
        rename.Click += (_, _) =>
        {
            var box = new TextBox { Text = name, Width = 220 };
            var ok = new Button { Content = L("Umbenennen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
            var fly = new Flyout { Content = new StackPanel { Spacing = 8, Children = { box, ok } } };
            void Apply()
            {
                var n = (box.Text ?? "").Trim();
                if (n.Length > 0) _vault.RenameFolder(id, n);
                fly.Hide(); RefreshDocs(); RefreshCollection();
            }
            ok.Click += (_, _) => Apply();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(); };
            fly.ShowAt(anchor);
        };
        var delete = new MenuItem { Header = L("Ordner löschen (die Dokumente bleiben erhalten)") };
        delete.Click += (_, _) => { _closedDocFolders.Remove(id); TrashFolderWithUndo(id, name); };
        return new ContextMenu { Items = { rename, delete } };
    }

    Control DocRow(Document d, double indent)
    {
        var cat = CategoryOf(d);
        var head = new DockPanel();
        var date = new TextBlock { Text = d.Updated.ToLocalTime().ToString("M", Loc.Culture), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
        DockPanel.SetDock(date, Dock.Right);
        head.Children.Add(date);
        // a dock, not a row: in a row the title is as wide as its text and ran over the date on a narrow screen; here it ends with "…"
        var left = new DockPanel();
        if (cat is not null)
            left.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new(5), Margin = new(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.Parse(CategoryColors[Math.Clamp(cat.Color, 0, CategoryColors.Length - 1)].Hex)) });
        left.Children.Add(new TextBlock { Text = (d.IsTemplate ? "◇ " : "") + (d.Title.Length > 0 ? d.Title : L("(ohne Titel)")), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        head.Children.Add(left);

        var preview = d.Path is not null ? L("Datei: ") + System.IO.Path.GetFileName(d.Path)
            : Shorten(PlainPreview(d.Body), 100);
        var stack = new StackPanel { Spacing = 2, Children = { head } };
        if (preview.Length > 0) stack.Children.Add(new TextBlock { Text = preview, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
        var shown = d.Tags.Where(t => !IsCategoryTag(t)).ToList();
        if (shown.Count > 0) stack.Children.Add(new TextBlock { Text = string.Join("  ", shown.Select(t => "#" + t)), Classes = { "caption" }, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis });

        var row = new Border { Padding = new(10, 7, 8, 7), CornerRadius = new(8), Background = Brushes.Transparent, Margin = new(indent, 0, 0, 0), Child = stack, Cursor = new Cursor(StandardCursorType.Hand) };
        row.PointerEntered += (_, _) => { if (!_dragging) row.Background = Res("BsSurface2") ?? Brushes.Transparent; };
        row.PointerExited += (_, _) => { if (!_dragging) row.Background = Brushes.Transparent; };
        ToolTip.SetTip(row, L("Klicken zum Öffnen, ziehen zum Einordnen"));
        MakeDragSource(row, d, () => OpenDocument(d));
        row.ContextMenu = DocRowMenu(d, row);
        return row;
    }

    ContextMenu DocRowMenu(Document d, Control anchor)
    {
        var open = new MenuItem { Header = L("Öffnen") };
        open.Click += (_, _) => OpenDocument(d);

        var move = new MenuItem { Header = L("Verschieben nach") };
        var none = new MenuItem { Header = L("Ohne Ordner") };
        none.Click += (_, _) => MoveDocument(d, null, L("Ohne Ordner"));
        move.Items.Add(none);
        foreach (var f in FolderTree(_vault.GetFolders()))
        {
            var folder = f;
            var item = new MenuItem { Header = new string(' ', folder.Depth * 4) + folder.Name, Icon = FolderGlyph() };
            item.Click += (_, _) => MoveDocument(d, folder.Id, folder.Name);
            move.Items.Add(item);
        }

        var category = new MenuItem { Header = L("Kategorie") };
        var noCat = new MenuItem { Header = L("(keine)") };
        noCat.Click += (_, _) => SetCategory(d, null);
        category.Items.Add(noCat);
        foreach (var c in Categories)
        {
            var cat = c;
            var item = new MenuItem { Header = CategoryChip(cat.Color, CategoryLabel(cat.Name)) };
            item.Click += (_, _) => SetCategory(d, cat);
            category.Items.Add(item);
        }

        var copy = new MenuItem { Header = L("Duplizieren") };
        copy.Click += (_, _) =>
        {
            _vault.AddDocument(d.Title + L(" (Kopie)"), d.Body, null, d.IsTemplate, d.Tags.ToArray(), d.FolderId);
            RefreshDocs(); RefreshCollection();
        };

        var delete = new MenuItem { Header = L("Löschen") };
        delete.Click += (_, _) => TrashDocumentWithUndo(d);
        return new ContextMenu { Items = { open, move, category, copy, new Separator(), delete } };
    }

    // ---- actions -------------------------------------------------------------------------------------------------------

    void MoveDocument(Document d, long? folderId, string folderName)
    {
        if (d.FolderId == folderId) return;
        _vault.SetDocumentFolder(d.Id, folderId);
        DocInfo.Text = L("„{0}“ liegt jetzt in „{1}“.", d.Title, folderName);
        if (folderId is long f) _closedDocFolders.Remove(f);
        RefreshDocs(); RefreshCollection();
    }

    /// <summary>Sets the category of a document (null = none; a plain tag that only happens to carry a category's name goes too).</summary>
    void SetCategory(Document d, DocCategory? category)
    {
        var tags = d.Tags.Where(t => !IsCategoryTag(t) && (category is not null || !Categories.Any(c => string.Equals(c.Name, t, StringComparison.OrdinalIgnoreCase)))).ToList();
        if (category is not null) tags.Add(CategoryPrefix + category.Name);
        _vault.UpdateDocument(d.Id, d.Title, d.Body, d.Path, d.IsTemplate, tags, d.FolderId);
        DocInfo.Text = category is null ? L("„{0}“ hat jetzt keine Kategorie.", d.Title) : L("„{0}“ gehört jetzt zu „{1}“.", d.Title, CategoryLabel(category.Name));
        RefreshDocs();
    }

    // ---- dragging ------------------------------------------------------------------------------------------------------

    DropTarget? TargetAt(Point p) =>
        _dropTargets.FirstOrDefault(t => t.Control.IsEffectivelyVisible && t.Control.TranslatePoint(default, DocHost) is { } tl && new Rect(tl, t.Control.Bounds.Size).Contains(p));

    void MakeDragSource(Control row, Document d, Action click)
    {
        void Reset()
        {
            _dragDoc = null; _dragging = false; DocDragGhost.IsVisible = false;
            _dragOver?.Highlight(false); _dragOver = null;
        }
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            _dragDoc = d; _dragFrom = e.GetPosition(DocHost); _dragging = false;
            e.Pointer.Capture(row);
        };
        row.PointerMoved += (_, e) =>
        {
            if (_dragDoc != d) return;
            var p = e.GetPosition(DocHost);
            if (!_dragging)
            {
                if (Math.Abs(p.X - _dragFrom.X) + Math.Abs(p.Y - _dragFrom.Y) < 8) return;   // a click must not become a drag
                _dragging = true;
                DocDragText.Text = "↳ " + d.Title;
                DocDragGhost.IsVisible = true;
            }
            Canvas.SetLeft(DocDragGhost, Math.Min(p.X + 14, Math.Max(0, DocHost.Bounds.Width - 200)));
            Canvas.SetTop(DocDragGhost, p.Y + 10);
            var over = TargetAt(p);
            if (!ReferenceEquals(over, _dragOver)) { _dragOver?.Highlight(false); over?.Highlight(true); _dragOver = over; }
        };
        row.PointerReleased += (_, e) =>
        {
            if (_dragDoc != d) return;
            var wasDragging = _dragging; var target = _dragOver;
            e.Pointer.Capture(null);
            Reset();
            if (wasDragging) target?.Drop(d); else if (e.InitialPressMouseButton == MouseButton.Left) click();
        };
        row.PointerCaptureLost += (_, _) => { if (_dragDoc == d) Reset(); };
    }
}
