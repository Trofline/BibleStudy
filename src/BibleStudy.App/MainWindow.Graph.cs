using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Cross references in the text, the thread graph beside the text, and the full graph views.</summary>
public partial class MainWindow
{
    CrossRefGraph? _graph;
    bool _graphBuilding, _xrefInText, _threadOn;
    Dictionary<int, List<CrossRef>>? _chapterRefs;

    WorldMap? _worldMap;

    void InitMap()
    {
        MapBtn.Click += async (_, _) => await ShowMapAsync();
        MapPane.CloseRequested += ShowReader;
        MapPane.MapProvider = async () => _worldMap ??= await WorldMap.LoadAsync(_http, Path.Combine(_root, "cache"));
        MapPane.PlacesProvider = (book, chapter) => _hello.GetChapterPlacesAsync(book, chapter);
        MapPane.OpenVerse += async (book, chapter, verse) => await GoToAsync(book, chapter, verse);
    }

    async Task ShowMapAsync()
    {
        Home.IsVisible = false; ReaderHost.IsVisible = false; GraphPane.IsVisible = false; KnowledgePane.IsVisible = false; TimelinePane.IsVisible = false; FamilyPane.IsVisible = false; MapPane.IsVisible = true;
        var chapters = _books.FirstOrDefault(b => b.Id == _curBook)?.NumberOfChapters ?? 1;
        await MapPane.OpenAsync(_curBook, BookName(_curBook), _curChapter, chapters);
    }

    void InitGraph()
    {
        InitMap();
        _xrefInText = _settings.XrefInText;
        _threadOn = _settings.ThreadGraph;
        ViewBtn.Click += (_, _) => ShowViewFlyout();
        GraphBtn.Click += async (_, _) => await ShowGraphAsync();
        GraphPane.BuildRequested += async () => await BuildGraphAsync();
        GraphPane.OpenChapter += async (book, chapter) => await GoToAsync(book, chapter);
        GraphPane.CloseRequested += ShowReader;
        ThreadPane.Picked += ShowRefsFlyout;
        ThreadPane.Jump += r => _ = GoToAsync(r.Target.Book, r.Target.Chapter, r.Target.Verse);
        ThreadPane.BuildRequested += async () => await BuildGraphAsync();
        ReaderScroll.ScrollChanged += (_, _) => UpdateThread();
        ReaderHost.SizeChanged += (_, _) => UpdateThread();
    }

    /// <summary>The reader was hidden (start page or graph) while it was measured as empty, so positions must be recomputed.</summary>
    void OnReaderShown()
    {
        FitReader();
        if (ThreadPane.IsVisible) Dispatcher.UIThread.Post(UpdateThread, DispatcherPriority.Loaded);
    }

    // ---- side panel: resizable and collapsible --------------------------------------------------------------------

    void InitPanel()
    {
        SidePanel.Width = Math.Clamp(_settings.SideWidth, 300, 760);
        SetPanelOpen(_settings.SideOpen);
        PanelBtn.Click += (_, _) => SetPanelOpen(PanelBtn.IsChecked == true);
        // Drag the left edge of the panel: wider to the left, narrower to the right.
        double? dragFrom = null; var startWidth = 0.0;
        SideSplitter.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            dragFrom = e.GetPosition(this).X; startWidth = SidePanel.Width;
            e.Pointer.Capture(SideSplitter);
        };
        SideSplitter.PointerMoved += (_, e) =>
        {
            if (dragFrom is not double from) return;
            SidePanel.Width = Math.Clamp(startWidth - (e.GetPosition(this).X - from), 300, Math.Max(300, Bounds.Width - 420));
            _settings.SideWidth = SidePanel.Width;
        };
        SideSplitter.PointerReleased += (_, e) => { if (dragFrom is null) return; dragFrom = null; e.Pointer.Capture(null); SaveSettings(); };
        SideSplitter.PointerEntered += (_, _) => SideSplitter.Background = Res("BsBorder") ?? Brushes.Gray;
        SideSplitter.PointerExited += (_, _) => { if (dragFrom is null) SideSplitter.Background = Brushes.Transparent; };
    }

    void SetPanelOpen(bool open)
    {
        var opening = open && !SidePanel.IsVisible;
        SidePanel.IsVisible = SideSplitter.IsVisible = open;
        if (opening) AnimateIn(SidePanel, 48, 0);
        PanelBtn.IsChecked = open;
        _settings.SideOpen = open;
        SaveSettings();
    }

    // ---- word study without Strong's tags -------------------------------------------------------------------------

    /// <summary>Strong's numbers exist only in some translations; otherwise the KJV is added as a column so the study still works.</summary>
    async Task OnStudyToggledAsync()
    {
        var hasStrongs = TranslationBox.SelectedItem is TranslationInfo cur && (LocalBibleStore.Owns(cur.Id)
            ? _curChapterData?.Verses.Any(v => v.Segments.Any(s => s.Strongs is { Count: > 0 })) ?? false
            : _hello.MayHaveStrongs(cur.Id));
        if (StudyBtn.IsChecked == true && TranslationBox.SelectedItem is TranslationInfo t && !hasStrongs)
        {
            var kjv = _all.FirstOrDefault(x => x.Id == "eng_kjv");
            if (kjv is not null && !_extras.Any(e => e.Translation?.Id == kjv.Id) && _extras.Count < MaxExtras)
            {
                StatusText.Text = L("Wortstudie: {0} hat keine Strong-Nummern. Die KJV wurde als Spalte hinzugefügt; dort sind die Wörter anklickbar.", t.ShortName);
                AddColumn(kjv);
                return;
            }
            StatusText.Text = L("Wortstudie: {0} hat keine Strong-Nummern. Nutze eine Spalte mit KJV (Parallel +).", t.ShortName);
        }
        await ShowChapterAsync(_selected);
    }

    // ---- graph beside the text: what was clicked ------------------------------------------------------------------

    void ShowRefsFlyout(string title, IReadOnlyList<CrossRef> refs, Point at)
    {
        var flyout = new Flyout { Placement = PlacementMode.Left };
        var list = new WrapPanel { MaxWidth = 340 };
        foreach (var r in refs.OrderByDescending(x => x.Score).Take(40))
            list.Children.Add(Chip(Label(r.Target) + (r.EndVerse > r.Target.Verse ? "–" + r.EndVerse : ""),
                () => { flyout.Hide(); _ = GoToAsync(r.Target.Book, r.Target.Chapter, r.Target.Verse); }, (TranslationBox.SelectedItem as TranslationInfo)?.Id ?? "", r));
        var head = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, Margin = new(0, 0, 0, 8) };
        flyout.Content = new StackPanel { Children = { head, new ScrollViewer { MaxHeight = 320, Content = list } } };
        flyout.ShowAt(ThreadPane);
    }

    void ShowViewFlyout()
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var inText = new CheckBox { Content = L("Querverweise im Text"), IsChecked = _xrefInText };
        var thread = new CheckBox { Content = L("Graph neben dem Text (eine Spalte)"), IsChecked = _threadOn };
        inText.IsCheckedChanged += async (_, _) =>
        {
            _xrefInText = inText.IsChecked == true; _settings.XrefInText = _xrefInText; SaveSettings();
            await ShowChapterAsync(_selected);
        };
        thread.IsCheckedChanged += async (_, _) =>
        {
            _threadOn = thread.IsChecked == true; _settings.ThreadGraph = _threadOn; SaveSettings();
            await ShowChapterAsync(_selected);
        };
        flyout.Content = new StackPanel { Spacing = 8, Children = { inText, thread } };
        flyout.ShowAt(ViewBtn);
    }

    // ---- in the text ------------------------------------------------------------------------------------------------

    /// <summary>Loads the cross references of the chapter when a view needs them (cached on disk, so usually instant).</summary>
    async Task LoadChapterRefsAsync(string book, int chapter)
    {
        _chapterRefs = null;
        if (!_xrefInText && !_threadOn) return;
        try { _chapterRefs = await _hello.GetChapterCrossRefsAsync(book, chapter); }
        catch (HttpRequestException) { /* offline and not cached: the views simply stay empty */ }
    }

    Control? XrefChips(int verse, string translationId)
    {
        if (!_xrefInText || _chapterRefs is null || !_chapterRefs.TryGetValue(verse, out var refs) || refs.Count == 0) return null;
        var wrap = new WrapPanel { Margin = new(30, 0, 0, 6) };
        foreach (var r in refs.Take(6))
            wrap.Children.Add(Chip(Label(r.Target) + (r.EndVerse > r.Target.Verse ? "–" + r.EndVerse : ""), () => _ = GoToAsync(r.Target.Book, r.Target.Chapter, r.Target.Verse), translationId, r));
        if (refs.Count > 6) wrap.Children.Add(Chip($"+{refs.Count - 6}", () => SideTabs.SelectedIndex = 4, translationId, null));
        return wrap;
    }

    Border Chip(string text, Action click, string translationId, CrossRef? target)
    {
        var label = new TextBlock { Text = text, FontSize = 12 };
        label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        var chip = new Border { Child = label, Padding = new(9, 2), Margin = new(0, 0, 6, 4), CornerRadius = new(10), BorderThickness = new(1), Cursor = new Cursor(StandardCursorType.Hand) };
        chip.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
        chip.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        chip.PointerPressed += (_, e) => { if (e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) click(); };
        if (target is not null)
        {
            var tip = new TextBlock { Text = "…", TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
            ToolTip.SetTip(chip, tip);
            var loaded = false;
            chip.PointerEntered += async (_, _) =>
            {
                if (loaded) return;
                loaded = true;
                tip.Text = await VerseTextAsync(translationId, target);
            };
        }
        return chip;
    }

    // ---- graph beside the text --------------------------------------------------------------------------------------

    /// <summary>Threads of the verses that are visible right now (the graph is fixed beside the scrolling text).</summary>
    void UpdateThread()
    {
        if (!ThreadPane.IsVisible) return;
        var anchors = new List<ThreadGraph.Anchor>();
        if (_chapterRefs is not null)
            foreach (var (verse, box) in _verseBoxes)
            {
                if (!_chapterRefs.TryGetValue(verse, out var refs) || refs.Count == 0) continue;
                if (box.TranslatePoint(new Point(0, box.Bounds.Height / 2), ThreadPane) is not { } p) continue;
                if (p.Y < -4 || p.Y > ThreadPane.Bounds.Height + 4) continue;
                anchors.Add(new ThreadGraph.Anchor(p.Y, verse, refs));
            }
        ThreadPane.SetData(_graph, _graph?.Index(_curBook, _curChapter) ?? -1, anchors, BookName);
    }

    void ShowThread(bool visible)
    {
        ThreadPane.IsVisible = visible;
        if (!visible) return;
        if (_graph is null && CrossRefDataCached()) _ = BuildGraphAsync();
        Dispatcher.UIThread.Post(UpdateThread, DispatcherPriority.Loaded);
    }

    bool CrossRefDataCached()
    {
        var dir = Path.Combine(_root, "cache", "api", "d", "open-cross-ref");
        return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Count() >= 1150;
    }

    // ---- full graph -------------------------------------------------------------------------------------------------

    async Task ShowGraphAsync()
    {
        Home.IsVisible = false; ReaderHost.IsVisible = false; MapPane.IsVisible = false; TimelinePane.IsVisible = false; FamilyPane.IsVisible = false; KnowledgePane.IsVisible = false; GraphPane.IsVisible = true;
        if (_graph is not null) { GraphPane.FocusChapter(_curBook, _curChapter); return; }
        var dir = Path.Combine(_root, "cache", "api", "d", "open-cross-ref");
        var cached = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Count() : 0;
        if (cached >= 1150) await BuildGraphAsync(); else GraphPane.ShowBuildPrompt();
    }

    async Task BuildGraphAsync()
    {
        if (_graphBuilding) return;
        _graphBuilding = true;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => { GraphPane.ReportProgress(p.Done, p.Total); StatusText.Text = L("Querverweise werden geladen … {0}/{1}", p.Done, p.Total); });
            _graph = await Task.Run(() => CrossRefGraphBuilder.BuildAsync(_hello, progress));
            GraphPane.SetGraph(_graph, BookName);
            StatusText.Text = "";
            UpdateThread();
            GraphPane.FocusChapter(_curBook, _curChapter);
        }
        catch (Exception ex) { GraphPane.ShowBuildPrompt(L("Das Laden ist fehlgeschlagen: ") + ex.Message); }
        finally { _graphBuilding = false; }
    }
}
