using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BibleStudy.Core;
using BibleStudy.Core.Materials;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// The "Materials" tab: the documents of a Bible study (what someone sent you) read inside the program, so you need not switch to Word and
/// back with Alt+Tab. A folder is linked once (with or without its sub-folders); its files show as a tree like in the Explorer, a click opens
/// a file here, and Tab / Shift+Tab switch between the open files. Bible references in a file are listed above it and open the passage.
/// </summary>
public partial class MainWindow
{
    const int MaterialsTab = 31;

    bool _matBuilt;
    TreeView _matTree = null!;
    TextBlock _matFolderText = null!;
    CheckBox _matSubCheck = null!;
    WrapPanel _matOpenStrip = null!;
    ScrollViewer _matScroll = null!;
    StackPanel _matView = null!;
    WrapPanel _matRefs = null!;
    TextBlock _matInfo = null!;
    Border _matViewer = null!;
    WrapPanel _matActions = null!;
    string? _matCurrent;
    readonly Dictionary<string, (DateTime Stamp, MaterialDoc Doc)> _matCache = [];

    void InitMaterials()
    {
        AddHandler(KeyDownEvent, OnMaterialsKey, RoutingStrategies.Tunnel);
    }

    void BuildMaterials()
    {
        if (_matBuilt) { RefreshMaterialsTree(); return; }
        _matBuilt = true;

        // left: the folder and its files
        var pick = new Button { Classes = { "primary" }, Content = L("Ordner wählen …"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        pick.Click += async (_, _) => await PickMaterialsFolderAsync();
        var refresh = new Button { Classes = { "tool" }, Width = 32, Height = 32, Content = new Icon { Data = Geometry.Parse("M21 12a9 9 0 1 1-3-6.7L21 8 M21 3v5h-5"), IconSize = 16 } };
        ToolTip.SetTip(refresh, L("Ordner neu einlesen"));
        refresh.Click += (_, _) => RefreshMaterialsTree();
        // all folders open or closed at once; which folders are open is kept for the next start
        var expandAll = new Button { Classes = { "tool" }, Width = 32, Height = 32, Content = new Icon { Data = Geometry.Parse("M7 15l5 5 5-5 M7 9l5-5 5 5"), IconSize = 16 } };
        ToolTip.SetTip(expandAll, L("Alle Ordner aufklappen"));
        expandAll.Click += (_, _) => SetAllMaterialFolders(true);
        var collapseAll = new Button { Classes = { "tool" }, Width = 32, Height = 32, Content = new Icon { Data = Geometry.Parse("M7 20l5-5 5 5 M7 4l5 5 5-5"), IconSize = 16 } };
        ToolTip.SetTip(collapseAll, L("Alle Ordner zuklappen"));
        collapseAll.Click += (_, _) => SetAllMaterialFolders(false);
        _matFolderText = new TextBlock { Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _matSubCheck = new CheckBox { Content = L("Unterordner mit einlesen"), IsChecked = _settings.MaterialsSubfolders };
        _matSubCheck.IsCheckedChanged += (_, _) => { _settings.MaterialsSubfolders = _matSubCheck.IsChecked == true; SaveSettings(); RefreshMaterialsTree(); };
        _matTree = new TreeView { Margin = new(0, 6, 0, 0) };
        _matTree.SelectionChanged += (_, _) => { if (_matTree.SelectedItem is TreeViewItem { Tag: string path } && File.Exists(path)) OpenMaterial(path); };
        var head = new DockPanel { Margin = new(0, 6, 0, 0) };
        DockPanel.SetDock(collapseAll, Dock.Right); DockPanel.SetDock(expandAll, Dock.Right);
        DockPanel.SetDock(refresh, Dock.Right); head.Children.Add(refresh); head.Children.Add(collapseAll); head.Children.Add(expandAll); head.Children.Add(_matFolderText);
        var top = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = L("MATERIALIEN"), Classes = { "eyebrow" } }, pick, head, _matSubCheck } };
        var left = new DockPanel { Margin = new(14, 12, 8, 12) };
        DockPanel.SetDock(top, Dock.Top); left.Children.Add(top); left.Children.Add(_matTree);

        // right: the open files as small tabs, and the file
        // the open files wrap into rows: in a scroll area its bar lay over the tabs and covered them
        _matOpenStrip = new WrapPanel { Margin = new(0, 0, 0, 4) };
        var stripScroll = _matOpenStrip;
        _matActions = new WrapPanel { Margin = new(0, 0, 0, 6) };
        _matRefs = new WrapPanel { Margin = new(0, 0, 0, 4) };
        _matInfo = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) };
        _matView = new StackPanel { Spacing = 8, MaxWidth = 860, Margin = new(28, 22, 28, 60), HorizontalAlignment = HorizontalAlignment.Center };
        _matScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _matView };
        _matViewer = new Border { CornerRadius = new(10), BorderThickness = new(1), Child = _matScroll, Focusable = true };
        _matViewer.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsBackground"));
        _matViewer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        _matViewer.PointerPressed += (_, _) => _matViewer.Focus();
        // the folder list can be hidden, so the file gets the whole width (in the split view above all); the button stays beside the tabs
        var treeBtn = new Button { Classes = { "tool" }, Width = 32, Height = 32, Margin = new(0, 0, 6, 4), VerticalAlignment = VerticalAlignment.Top, Content = new Icon { Data = Geometry.Parse("M4 4h16v16H4z M10 4v16"), IconSize = 16 } };
        var stripRow = new DockPanel { Children = { treeBtn, stripScroll } };
        DockPanel.SetDock(treeBtn, Dock.Left);
        var rightTop = new StackPanel { Children = { stripRow, _matActions, _matRefs, _matInfo } };
        var right = new DockPanel { Margin = new(8, 12, 14, 12) };
        DockPanel.SetDock(rightTop, Dock.Top); right.Children.Add(rightTop); right.Children.Add(_matViewer);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,6,5*") };
        grid.ColumnDefinitions[0].MinWidth = 170; grid.ColumnDefinitions[0].MaxWidth = 380; grid.ColumnDefinitions[2].MinWidth = 220;
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1); Grid.SetColumn(right, 2);
        grid.Children.Add(left); grid.Children.Add(splitter); grid.Children.Add(right);
        MaterialsHost.Children.Add(grid);
        var treeWidth = grid.ColumnDefinitions[0].Width;
        void ApplyTree()
        {
            var hide = _settings.MaterialsTreeHidden;
            var col = grid.ColumnDefinitions[0];
            if (hide && col.Width.Value > 0) treeWidth = col.Width;   // the width the splitter left, for showing it again
            col.MinWidth = hide ? 0 : 170;
            col.Width = hide ? new GridLength(0) : treeWidth;
            grid.ColumnDefinitions[1].Width = new GridLength(hide ? 0 : 6);
            left.IsVisible = splitter.IsVisible = !hide;
            right.Margin = new(hide ? 14 : 8, 12, 14, 12);
            ToolTip.SetTip(treeBtn, hide ? L("Ordnerliste einblenden") : L("Ordnerliste ausblenden"));
        }
        treeBtn.Click += (_, _) => { _settings.MaterialsTreeHidden = !_settings.MaterialsTreeHidden; SaveSettings(); ApplyTree(); };
        ApplyTree();

        RefreshMaterialsTree();
        _settings.MaterialsOpen.RemoveAll(p => !File.Exists(p));
        RebuildMaterialStrip();
        if (_settings.MaterialsOpen.Count > 0) OpenMaterial(_settings.MaterialsOpen[0]);
        else ShowMaterialEmpty();
    }

    async Task PickMaterialsFolderAsync()
    {
        var start = Directory.Exists(_settings.MaterialsFolder) ? await StorageProvider.TryGetFolderFromPathAsync(_settings.MaterialsFolder) : null;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L("Ordner mit den Materialien wählen"), SuggestedStartLocation = start });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } p) return;
        _settings.MaterialsFolder = p; SaveSettings();
        RefreshMaterialsTree();
    }

    /// <summary>Reads the folder again and shows its files as a tree (folders first, then files, each sorted by name).</summary>
    void RefreshMaterialsTree()
    {
        if (!_matBuilt) return;
        var root = _settings.MaterialsFolder;
        _matTree.Items.Clear();
        if (root.Length == 0 || !Directory.Exists(root))
        {
            _matFolderText.Text = root.Length == 0 ? L("Noch kein Ordner gewählt.") : L("Der Ordner ist nicht mehr da: {0}", root);
            return;
        }
        _matFolderText.Text = root;
        WatchMaterialsFolder();
        ToolTip.SetTip(_matFolderText, root);
        var files = MaterialReader.Scan(root, _settings.MaterialsSubfolders);
        var folders = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);
        ItemsControl ParentOf(string dir)
        {
            if (string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return _matTree;
            if (folders.TryGetValue(dir, out var f)) return f;
            var parent = ParentOf(Path.GetDirectoryName(dir)!);
            var rel = Path.GetRelativePath(root, dir);
            var item = new TreeViewItem { Header = TreeRow(FolderGlyph(), Path.GetFileName(dir), false), IsExpanded = !_settings.MaterialsCollapsed.Contains(rel, StringComparer.OrdinalIgnoreCase), Tag = dir };
            item.PropertyChanged += (_, e) => { if (e.Property == TreeViewItem.IsExpandedProperty && !_matTreeFilling) RememberMaterialFolder(rel, item.IsExpanded); };
            folders[dir] = item;
            parent.Items.Add(item);
            return item;
        }
        foreach (var f in files.OrderBy(f => Path.GetDirectoryName(f), StringComparer.CurrentCultureIgnoreCase).ThenBy(f => Path.GetFileName(f), Comparer<string>.Create(NaturalCompare)))
        {
            var parent = ParentOf(Path.GetDirectoryName(f)!);
            parent.Items.Add(new TreeViewItem { Header = TreeRow(new TextBlock { Text = FileGlyph(f) }, Path.GetFileName(f), !MaterialReader.CanShow(f)), Tag = f });
        }
        SortTree(_matTree);
        if (files.Count == 0) _matTree.Items.Add(new TreeViewItem { Header = new TextBlock { Text = L("In diesem Ordner liegen keine Dokumente."), Classes = { "caption" } }, IsEnabled = false });
    }

    bool _matTreeFilling;

    /// <summary>A folder was opened or closed by hand: kept, so it looks the same at the next start.</summary>
    void RememberMaterialFolder(string rel, bool open)
    {
        var list = _settings.MaterialsCollapsed;
        var had = list.FindIndex(x => string.Equals(x, rel, StringComparison.OrdinalIgnoreCase));
        if (open && had >= 0) list.RemoveAt(had);
        else if (!open && had < 0) list.Add(rel);
        else return;
        SaveSettings();
    }

    /// <summary>The two buttons above the tree: every folder open, or every folder closed.</summary>
    void SetAllMaterialFolders(bool open)
    {
        var items = new List<TreeViewItem>();
        void Collect(ItemsControl parent) { foreach (var i in parent.Items.OfType<TreeViewItem>()) { if (i.Tag is string t && Directory.Exists(t)) { items.Add(i); Collect(i); } } }
        Collect(_matTree);
        _matTreeFilling = true;
        foreach (var i in items) i.IsExpanded = open;
        _matTreeFilling = false;
        var root = _settings.MaterialsFolder;
        _settings.MaterialsCollapsed = open ? [] : items.Select(i => Path.GetRelativePath(root, (string)i.Tag!)).ToList();
        SaveSettings();
    }

    /// <summary>Folders before files at every level.</summary>
    static void SortTree(ItemsControl parent)
    {
        var items = parent.Items.OfType<TreeViewItem>().ToList();
        var ordered = items.Where(i => i.Items.Count > 0 || i.Tag is string t && Directory.Exists(t)).Concat(items.Where(i => !(i.Items.Count > 0 || i.Tag is string t && Directory.Exists(t)))).ToList();
        parent.Items.Clear();
        foreach (var i in ordered) { parent.Items.Add(i); SortTree(i); }
    }

    Control TreeRow(Control glyph, string name, bool external)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        glyph.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(new Border { Width = 18, Child = glyph });
        row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = external ? 0.6 : 1 });
        if (external) ToolTip.SetTip(row, L("Öffnet sich mit dem eigenen Programm"));
        return row;
    }

    static string FileGlyph(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "▤",
        ".doc" or ".docx" or ".odt" or ".rtf" => "▦",
        ".ppt" or ".pptx" or ".odp" => "▭",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "▣",
        ".mp3" or ".m4a" or ".wav" or ".mp4" => "♪",
        _ => "▢",
    };

    // ---- open files ------------------------------------------------------------------------------------------------------

    void OpenMaterial(string path)
    {
        if (!MaterialReader.CanShow(path)) { OpenExternally(path); return; }
        if (!_settings.MaterialsOpen.Contains(path, StringComparer.OrdinalIgnoreCase)) { _settings.MaterialsOpen.Add(path); SaveSettings(); }
        _matCurrent = path;
        RebuildMaterialStrip();
        _ = ShowMaterialAsync(path);
    }

    void CloseMaterial(string path)
    {
        var i = _settings.MaterialsOpen.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        _settings.MaterialsOpen.RemoveAt(i); SaveSettings();
        if (string.Equals(_matCurrent, path, StringComparison.OrdinalIgnoreCase))
        {
            if (_settings.MaterialsOpen.Count > 0) { OpenMaterial(_settings.MaterialsOpen[Math.Min(i, _settings.MaterialsOpen.Count - 1)]); return; }
            _matCurrent = null; ShowMaterialEmpty();
        }
        RebuildMaterialStrip();
    }

    /// <summary>Tab / Shift+Tab: the next or previous open file.</summary>
    void StepMaterial(int dir)
    {
        var open = _settings.MaterialsOpen;
        if (open.Count == 0) return;
        var i = _matCurrent is null ? -1 : open.FindIndex(p => string.Equals(p, _matCurrent, StringComparison.OrdinalIgnoreCase));
        OpenMaterial(open[((i + dir) % open.Count + open.Count) % open.Count]);
        _matViewer.Focus();
    }

    void RebuildMaterialStrip()
    {
        if (!_matBuilt) return;
        _matOpenStrip.Children.Clear();
        foreach (var path in _settings.MaterialsOpen)
        {
            var p = path;
            var active = string.Equals(p, _matCurrent, StringComparison.OrdinalIgnoreCase);
            var x = new Button { Classes = { "tool" }, Width = 20, Height = 20, Padding = new(0), Content = new Icon { Data = Geometry.Parse("M18 6L6 18 M6 6l12 12"), IconSize = 11 }, Opacity = active ? 1 : 0.5 };
            ToolTip.SetTip(x, L("Schließen"));
            x.Click += (_, e) => { e.Handled = true; CloseMaterial(p); };
            var tab = new Button { Classes = { "seg" }, Margin = new(0, 0, 4, 4), Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = FileGlyph(p) }, new TextBlock { Text = Path.GetFileNameWithoutExtension(p), MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, x } } };
            tab.Classes.Set("active", active);
            ToolTip.SetTip(tab, p + "\n" + L("Tab / Umschalt+Tab: nächste oder vorige Datei"));
            tab.Click += (_, _) => OpenMaterial(p);
            _matOpenStrip.Children.Add(tab);
        }
    }

    void ShowMaterialEmpty()
    {
        if (!_matBuilt) return;
        _matActions.Children.Clear(); _matRefs.Children.Clear(); _matInfo.Text = "";
        _matView.Children.Clear();
        _matView.Children.Add(new TextBlock { Text = L("Materialien"), Classes = { "h2" }, Margin = new(0, 20, 0, 4) });
        _matView.Children.Add(new TextBlock
        {
            Text = _settings.MaterialsFolder.Length == 0
                ? L("Wähle links den Ordner, in dem die Unterlagen der Bibelstunde liegen. Seine Dateien erscheinen als Baum; ein Klick öffnet eine Datei hier im Programm. Mit Tab wechselst du zwischen den geöffneten Dateien.")
                : L("Klicke links eine Datei an. Word, PDF, PowerPoint, OpenDocument, RTF, Text, Markdown, Webseiten und Bilder öffnen sich hier; andere Dateien mit ihrem eigenen Programm."),
            TextWrapping = TextWrapping.Wrap, Classes = { "caption" },
        });
    }

    async Task ShowMaterialAsync(string path)
    {
        var version = ++_matShowVersion;   // a newer call (another file, the folder changed) wins; this one stops at its next step
        _matActions.Children.Clear(); _matRefs.Children.Clear();
        _matInfo.Text = L("Wird geladen …");
        MaterialDoc doc;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_matCache.TryGetValue(path, out var cached) && cached.Stamp == stamp) doc = cached.Doc;
            else { var media = MaterialCacheDir(path, stamp); doc = await Task.Run(() => MaterialReader.Read(path, media)); _matCache[path] = (stamp, doc); }
        }
        catch (Exception ex) { doc = new MaterialDoc(path, [], ex.Message); }
        if (version != _matShowVersion || !string.Equals(_matCurrent, path, StringComparison.OrdinalIgnoreCase)) return;   // another file was chosen meanwhile

        var external = new Button { Content = L("Mit eigenem Programm öffnen"), Classes = { "ghost" } };
        external.Click += (_, _) => OpenExternally(path); external.Margin = new(0, 0, 6, 4);
        var folder = new Button { Content = L("Im Ordner zeigen"), Classes = { "ghost" } };
        folder.Click += (_, _) => OpenExternally(Path.GetDirectoryName(path)!); folder.Margin = new(0, 0, 6, 4);
        _matActions.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new(2, 0, 8, 4), MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis });
        _matActions.Children.Add(external); _matActions.Children.Add(folder);
        var isPdf = string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
        if (isPdf)
        {
            // a PDF shows as its pages (pictures, so also scanned papers can be read) or as text that can be selected and searched
            var mode = new Button { Content = _matPdfText ? L("Seitenbilder zeigen") : L("Nur Text zeigen"), Classes = { "ghost" }, Margin = new(0, 0, 6, 4) };
            mode.Click += (_, _) => { _matPdfText = !_matPdfText; _ = ShowMaterialAsync(path); };
            _matActions.Children.Add(mode);
        }
        _matInfo.Text = doc.Problem is { } problem && !(isPdf && !_matPdfText) ? problem
            : isPdf && !_matPdfText ? ""
            : MaterialReader.Pictures.Contains(Path.GetExtension(path)) ? "" : L("Nur Text und Bilder werden gezeigt; das genaue Aussehen zeigt das eigene Programm.");

        // the Bible references of the file: a few stand above the text, all of them behind a button (a big file has hundreds;
        // as chips they pushed the text out of sight); a click opens the passage (in the split view beside the file)
        var refs = doc.Blocks.SelectMany(b => DocumentLinks.VerseSpans(b.Text)).Select(s => s.Ref).Distinct().ToList();
        Button Chip(VerseRef r)
        {
            var chip = new Button { Classes = { "seg" }, Content = Label(r), Margin = new(0, 0, 4, 4), FontSize = 12 };
            ToolTip.SetTip(chip, L("Bibelstelle öffnen"));
            chip.Click += async (_, _) => await OpenMaterialRefAsync(r);
            return chip;
        }
        const int FewRefs = 6;
        foreach (var r in refs.Take(refs.Count <= FewRefs + 1 ? FewRefs + 1 : FewRefs)) _matRefs.Children.Add(Chip(r));
        if (refs.Count > FewRefs + 1)
        {
            var all = new Button { Classes = { "seg" }, Content = L("Alle {0} Bibelstellen …", refs.Count), Margin = new(0, 0, 4, 4), FontSize = 12 };
            all.Click += (_, _) =>
            {
                var panel = new WrapPanel();
                foreach (var r in refs) panel.Children.Add(Chip(r));
                new Flyout { Content = new ScrollViewer { MaxHeight = 480, Width = 460, Content = panel } }.ShowAt(all);
            };
            _matRefs.Children.Add(all);
        }

        _matView.Children.Clear();
        _matScroll.Offset = default;
        _matBlocks.Clear(); _pdfPageMarks.Clear(); _pdfNextPage = 0; _pdfMore = null;
        // the table of contents: the lines of the document's own, otherwise its headings; for a PDF its outline
        var outline = isPdf ? await Task.Run(() => MaterialReader.PdfOutline(path)) : [];
        if (version != _matShowVersion) return;
        var contents = isPdf ? outline : doc.Blocks.Any(b => b.Kind == BlockKind.TocEntry) ? doc.Blocks.Where(b => b.Kind == BlockKind.TocEntry).ToList() : doc.Blocks.Where(b => b.IsHeading).ToList();
        if (contents.Count > 0)
        {
            var toc = new Button { Content = L("Inhalt"), Classes = { "ghost" }, Margin = new(0, 0, 6, 4) };
            ToolTip.SetTip(toc, L("Inhaltsverzeichnis: zu einem Abschnitt springen"));
            toc.Click += (_, _) => ShowContentsFlyout(toc, contents, path, version);
            _matActions.Children.Insert(1, toc);
        }
        if (isPdf && !_matPdfText) { await ShowPdfPagesAsync(path, 0, version); return; }
        foreach (var b in doc.Blocks) { var c = BlockControl(b, path, version); _matBlocks.Add((b, c)); _matView.Children.Add(c); }
    }

    readonly List<(Block Block, Control Control)> _matBlocks = [];
    readonly Dictionary<int, Control> _pdfPageMarks = [];
    int _pdfNextPage, _pdfCount;
    Button? _pdfMore;

    /// <summary>The table of contents as a list to jump from, with a field that narrows it down (a long folder of Bible studies has hundreds of entries).</summary>
    void ShowContentsFlyout(Control anchor, List<Block> contents, string path, int version)
    {
        var flyout = new Flyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft };
        var list = new StackPanel { Spacing = 1 };
        var filter = new TextBox { PlaceholderText = L("Im Inhalt suchen …"), Margin = new(0, 0, 0, 6) };
        void Fill()
        {
            list.Children.Clear();
            var words = (filter.Text ?? "").Trim();
            foreach (var b in contents.Where(b => words.Length == 0 || TocTitle(b).Contains(words, StringComparison.CurrentCultureIgnoreCase)).Take(500))
            {
                var level = b.Kind == BlockKind.TocEntry ? b.Level : b.HeadingLevel;
                var row = new DockPanel();
                var page = b.Kind == BlockKind.TocEntry && b.Spans.Count > 1 ? b.Spans[1].Text : "";
                if (page.Length > 0) row.Children.Add(new TextBlock { Text = page, Opacity = 0.55, Margin = new(10, 0, 0, 0), [DockPanel.DockProperty] = Dock.Right });
                row.Children.Add(new TextBlock { Text = TocTitle(b), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = level <= 1 ? FontWeight.SemiBold : FontWeight.Normal });
                var item = new Button { Classes = { "ghost" }, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(6, 3), Margin = new(Math.Max(0, level - 1) * 16, 0, 0, 0) };
                item.Click += async (_, _) => { flyout.Hide(); await JumpToAsync(b, path, version); };
                list.Children.Add(item);
            }
        }
        filter.TextChanged += (_, _) => Fill();
        Fill();
        flyout.Content = new DockPanel
        {
            Width = 420,
            Children = { new StackPanel { [DockPanel.DockProperty] = Dock.Top, Children = { new TextBlock { Text = L("INHALT"), Classes = { "eyebrow" }, Margin = new(0, 0, 0, 6) }, filter } }, new ScrollViewer { MaxHeight = 560, Content = list } },
        };
        flyout.ShowAt(anchor);
        filter.Focus();
    }

    static string TocTitle(Block b) => b.Kind == BlockKind.TocEntry ? b.Spans[0].Text : b.Text;

    /// <summary>
    /// Jumps to what a line of the table of contents (or a heading) points to: its bookmark, else the heading with the same words;
    /// in a PDF shown as pages, its page (pages that are not drawn yet are drawn first).
    /// </summary>
    async Task JumpToAsync(Block target, string path, int version)
    {
        if (target.Page > 0 && !_matPdfText)
        {
            if (!_pdfPageMarks.ContainsKey(target.Page) && target.Page > _pdfNextPage)
            {
                if (_pdfMore is not null) { _matView.Children.Remove(_pdfMore); _pdfMore = null; }
                await ShowPdfPagesAsync(path, _pdfNextPage, version, target.Page + 2);
            }
            if (_pdfPageMarks.TryGetValue(target.Page, out var mark)) ScrollToControl(mark);
            return;
        }
        Control? hit = null;
        if (target.Kind != BlockKind.TocEntry) hit = _matBlocks.FirstOrDefault(x => ReferenceEquals(x.Block, target)).Control;
        if (hit is null && target.LinkTo is { } link) hit = _matBlocks.FirstOrDefault(x => x.Block.Kind != BlockKind.TocEntry && x.Block.Anchors?.Contains(link) == true).Control;
        if (hit is null)
        {
            // no bookmark: the first heading (after the table of contents) whose words are those of the line, without its numbering
            static string Norm(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"^[\d.\s)a-z]{0,6}\s|[^\p{L}\p{N}]+", " ").Trim().ToLowerInvariant();
            var want = Norm(TocTitle(target));
            hit = _matBlocks.FirstOrDefault(x => x.Block.IsHeading && Norm(x.Block.Text) == want).Control
                  ?? _matBlocks.FirstOrDefault(x => x.Block.IsHeading && want.Length > 3 && Norm(x.Block.Text).Contains(want)).Control;
        }
        if (hit is null) { StatusText.Text = L("Dieser Abschnitt wurde im Text nicht gefunden."); return; }
        ScrollToControl(hit);
    }

    void ScrollToControl(Control c)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (c.TranslatePoint(new Point(0, 0), _matView) is not { } p) return;
            _matScroll.Offset = new Vector(0, Math.Max(0, p.Y + _matView.Margin.Top - 12));
            // a short mark on the place jumped to, so the eye finds it
            var old = c.Opacity; c.Opacity = 0.55; DispatcherTimer.RunOnce(() => c.Opacity = old, TimeSpan.FromMilliseconds(350));
        }, DispatcherPriority.Background);
    }

    bool _matPdfText;
    int _matShowVersion;
    readonly SemaphoreSlim _pdfLock = new(1, 1);
    const int PdfPagesAtOnce = 12;

    /// <summary>The folder for what is made from a file (pictures taken out of it, rendered PDF pages); it changes with the file, so nothing old is shown.</summary>
    static string MaterialCacheDir(string path, DateTime stamp)
    {
        var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant() + "|" + stamp.Ticks)))[..20];
        return Path.Combine(Path.GetTempPath(), "BibleStudy-materials", id);
    }

    /// <summary>The pages of a PDF as pictures, a dozen at a time ("more pages" brings the next ones), rendered in the background.</summary>
    async Task ShowPdfPagesAsync(string path, int from, int version, int to = 0)
    {
        var dir = MaterialCacheDir(path, File.GetLastWriteTimeUtc(path));
        var loading = new TextBlock { Text = L("Seiten werden vorbereitet …"), Classes = { "caption" } };
        _matView.Children.Add(loading);
        (int Count, List<string> Files, string? Error) result;
        try
        {
            await _pdfLock.WaitAsync();   // one rendering at a time: PDFium is not made for several at once
            try
            {
            result = await Task.Run(() =>
            {
                var bytes = File.ReadAllBytes(path);
                var count = PDFtoImage.Conversion.GetPageCount(bytes);
                Directory.CreateDirectory(dir);
                var files = new List<string>();
                for (var i = from; i < Math.Min(count, Math.Max(from + PdfPagesAtOnce, to)); i++)
                {
                    var file = Path.Combine(dir, $"page{i + 1}.png");
                    if (!File.Exists(file)) PDFtoImage.Conversion.SavePng(file, bytes, i, options: new PDFtoImage.RenderOptions(Dpi: 110, WithAnnotations: true, WithFormFill: true));
                    files.Add(file);
                }
                return (count, files, (string?)null);
            });
            }
            finally { _pdfLock.Release(); }
        }
        catch (Exception ex) { result = (0, [], ex.Message); }
        if (version != _matShowVersion || !string.Equals(_matCurrent, path, StringComparison.OrdinalIgnoreCase) || _matPdfText) return;
        _matView.Children.Remove(loading);
        if (result.Error is { } error)
        {
            // the pages cannot be drawn (a protected or broken file): the text is the next best thing
            _matInfo.Text = L("Die Seiten lassen sich nicht als Bild zeigen ({0}); hier ist der Text.", error);
            _matPdfText = true;
            await ShowMaterialAsync(path);
            _matPdfText = false;
            return;
        }
        for (var i = 0; i < result.Files.Count; i++)
        {
            var mark = new TextBlock { Text = L("Seite {0}", from + i + 1), Classes = { "eyebrow" }, Margin = new(0, i == 0 && from == 0 ? 0 : 14, 0, 2) };
            _pdfPageMarks[from + i + 1] = mark;
            _matView.Children.Add(mark);
            try
            {
                var page = new Border { BorderThickness = new(1), BoxShadow = BoxShadows.Parse("0 2 10 0 #22000000"), Child = new Image { Source = new Bitmap(result.Files[i]), Stretch = Stretch.Uniform } };
                page.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
                _matView.Children.Add(page);
            }
            catch (Exception ex) { _matView.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap }); }
        }
        var next = from + result.Files.Count;
        _pdfNextPage = next; _pdfCount = result.Count;
        if (next < result.Count)
        {
            var more = new Button { Content = L("Weitere Seiten zeigen ({0} von {1})", next, result.Count), HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 16, 0, 0) };
            more.Click += async (_, _) => { _matView.Children.Remove(more); _pdfMore = null; await ShowPdfPagesAsync(path, next, _matShowVersion); };
            _matView.Children.Add(more);
            _pdfMore = more;
        }
    }

    FileSystemWatcher? _matWatcher;
    DispatcherTimer? _matWatchTimer;

    /// <summary>Files that are added, renamed or deleted in the materials folder show in the tree at once; a changed open file is shown anew.</summary>
    void WatchMaterialsFolder()
    {
        _matWatcher?.Dispose(); _matWatcher = null;
        var root = _settings.MaterialsFolder;
        if (root.Length == 0 || !Directory.Exists(root)) return;
        try
        {
            _matWatcher = new FileSystemWatcher(root) { IncludeSubdirectories = _settings.MaterialsSubfolders, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
            void Changed(object? s, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() =>
            {
                // many events come at once (Word saves in several steps): act once when they are over
                _matWatchTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                _matWatchTimer.Stop();
                _matWatchTimer.Tick -= MaterialsChanged; _matWatchTimer.Tick += MaterialsChanged;
                _matWatchTimer.Start();
            });
            _matWatcher.Created += Changed; _matWatcher.Deleted += Changed; _matWatcher.Renamed += (s, e) => Changed(s, e); _matWatcher.Changed += Changed;
            _matWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    void MaterialsChanged(object? sender, EventArgs e)
    {
        _matWatchTimer?.Stop();
        if (!_matBuilt) return;
        RefreshMaterialsTree();
        if (_matCurrent is { } cur)
        {
            if (!File.Exists(cur)) CloseMaterial(cur);
            else if (!_matCache.TryGetValue(cur, out var c) || c.Stamp != File.GetLastWriteTimeUtc(cur)) _ = ShowMaterialAsync(cur);
        }
    }

    async Task OpenMaterialRefAsync(VerseRef r, int end = 0)
    {
        if (!_settings.SplitView) StatusText.Text = L("Tipp: Mit der geteilten Ansicht (oben) steht die Bibelstelle neben der Datei.");
        await GoToRangeAsync(r, end);
    }

    Control BlockControl(Block b, string path, int version)
    {
        if (b.Kind == BlockKind.TocEntry)
        {
            // a line of the table of contents: a click jumps to its section
            var row = new DockPanel();
            var page = b.Spans.Count > 1 ? b.Spans[1].Text : "";
            if (page.Length > 0) row.Children.Add(new TextBlock { Text = page, Opacity = 0.6, Margin = new(12, 0, 0, 0), [DockPanel.DockProperty] = Dock.Right });
            row.Children.Add(new TextBlock { Text = b.Spans[0].Text, TextWrapping = TextWrapping.Wrap, FontWeight = b.Level <= 1 ? FontWeight.SemiBold : FontWeight.Normal });
            var entry = new Button { Classes = { "ghost" }, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(8, 3), Margin = new(Math.Max(0, b.Level - 1) * 18, 0, 0, 0), FontSize = 15 };
            ToolTip.SetTip(entry, L("Zu diesem Abschnitt springen"));
            entry.Click += async (_, _) => await JumpToAsync(b, path, version);
            return entry;
        }
        if (b.Kind == BlockKind.Image && b.ImagePath is { } img)
        {
            try { return new Image { Source = new Bitmap(img), Stretch = Stretch.Uniform, MaxHeight = 1400, HorizontalAlignment = HorizontalAlignment.Center }; }
            catch (Exception ex) { return new TextBlock { Text = L("Das Bild lässt sich nicht zeigen: {0}", ex.Message), TextWrapping = TextWrapping.Wrap }; }
        }
        if (b.Kind == BlockKind.Page)
            return new Border { BorderThickness = new(0, 1, 0, 0), Margin = new(0, 18, 0, 2), Padding = new(0, 6, 0, 0), BorderBrush = Res("BsBorder"), Child = new TextBlock { Text = b.Text, Classes = { "eyebrow" } } };
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = double.NaN };
        // Bible references in the text are colored and react to a click: the passage opens in the reader
        var whole = string.Concat(b.Spans.Select(s => s.Text));
        var links = DocumentLinks.VerseSpans(whole).Select(v =>
        {
            var end = DocumentLinks.VerseRanges(whole.Substring(v.Index, v.Length)).FirstOrDefault().EndVerse;
            return (Start: v.Index, End: v.Index + v.Length, v.Ref, To: end);
        }).ToList();
        var at = 0;
        foreach (var s in b.Spans)
        {
            var weight = s.Bold ? FontWeight.SemiBold : FontWeight.Normal;
            var style = s.Italic ? FontStyle.Italic : FontStyle.Normal;
            var pos = 0;
            while (pos < s.Text.Length)
            {
                var abs = at + pos;
                var link = links.FirstOrDefault(l => abs >= l.Start && abs < l.End);
                var stop = link.End > 0 ? Math.Min(s.Text.Length, link.End - at)
                    : Math.Min(s.Text.Length, links.Where(l => l.Start > abs).Select(l => l.Start - at).DefaultIfEmpty(s.Text.Length).Min());
                var run = new Run(s.Text[pos..stop]) { FontWeight = weight, FontStyle = style };
                if (link.End > 0) { run.Foreground = Res("BsAccent"); run.TextDecorations = TextDecorations.Underline; }
                tb.Inlines!.Add(run);
                pos = stop;
            }
            at += s.Text.Length;
        }
        if (links.Count > 0)
        {
            (int Start, int End, VerseRef Ref, int To)? LinkAt(PointerEventArgs e)
            {
                var pt = e.GetPosition(tb);
                if (pt.X < 0 || pt.Y < 0 || pt.Y > tb.TextLayout.Height) return null;
                var hit = tb.TextLayout.HitTestPoint(pt);
                foreach (var l in links) if (hit.TextPosition >= l.Start && hit.TextPosition < l.End) return l;
                return null;
            }
            tb.PointerMoved += (_, e) => tb.Cursor = LinkAt(e) is null ? null : new Cursor(StandardCursorType.Hand);
            tb.AddHandler(PointerReleasedEvent, (object? _, PointerReleasedEventArgs e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left || !string.IsNullOrEmpty(tb.SelectedText)) return;   // dragging to select text is not a click
                if (LinkAt(e) is { } l) _ = OpenMaterialRefAsync(l.Ref, l.To);
            }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        }
        switch (b.Kind)
        {
            case BlockKind.Heading1: tb.FontSize = 24; tb.FontWeight = FontWeight.SemiBold; tb.Margin = new(0, 14, 0, 2); break;
            case BlockKind.Heading2: tb.FontSize = 20; tb.FontWeight = FontWeight.SemiBold; tb.Margin = new(0, 10, 0, 0); break;
            case BlockKind.Heading3: tb.FontSize = 17; tb.FontWeight = FontWeight.SemiBold; tb.Margin = new(0, 6, 0, 0); break;
            case BlockKind.ListItem:
                tb.FontSize = 16;
                return new DockPanel { Margin = new(10, 0, 0, 0), Children = { new TextBlock { Text = "•", FontSize = 16, Width = 18, [DockPanel.DockProperty] = Dock.Left }, tb } };
            case BlockKind.TableRow: tb.FontSize = 15; tb.Opacity = 0.95; break;
            default: tb.FontSize = 16; tb.LineHeight = 26; break;
        }
        return tb;
    }

    void OpenExternally(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = L("Konnte nicht geöffnet werden: {0}", ex.Message); }
    }

    /// <summary>Tab and Shift+Tab switch between the open files while the materials are seen and no text field has the focus.</summary>
    void OnMaterialsKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Tab || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift) || !_matBuilt) return;
        if (!MaterialsHost.IsEffectivelyVisible || !TabActive(MaterialsTab) || _settings.MaterialsOpen.Count < 1) return;
        var focused = FocusedControl();
        if (focused is TextBox) return;
        if (focused is Visual v && !(v == MaterialsHost || MaterialsHost.IsVisualAncestorOf(v)) && (v.GetVisualAncestors().Contains(SidePanel) || v.GetVisualAncestors().Contains(TopBar))) return;
        e.Handled = true;
        StepMaterial(e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
    }
}
