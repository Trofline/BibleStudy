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
        MapPane.AnimationsEnabled = () => _settings.Animations;
        MapPane.BookNames = BookName;
        MapPane.BookOrder = id => _books.ToList().FindIndex(b => b.Id == id);
        MapPane.RouteRequested += async name =>
        {
            var entry = await FindEntityAsync("people", name);
            if (entry is null) { MapPane.ShowMessage(L("Keine Person „{0}“ gefunden.", name)); return; }
            await MapPane.OpenHitsAsync(L("Reiseweg: {0}", entry.Name), entry.References, route: true);
        };
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
        InitNets();
        _xrefInText = _settings.XrefInText;
        _threadOn = _settings.ThreadGraph;
        ViewBtn.Click += (_, _) => ShowViewFlyout();
        GraphBtn.Click += async (_, _) => await ShowGraphAsync();
        GraphPane.BuildRequested += async () => await BuildGraphAsync();
        GraphPane.OpenChapter += async (book, chapter) => await GoToAsync(book, chapter);
        GraphPane.CloseRequested += ShowReader;
        GraphPane.OtNtBookPicked += async id =>
        {
            var book = _books.FirstOrDefault(b => b.Id == id);
            if (book is null) return;
            ShowReader();
            RevealSideTab(0);   // the search is a side panel tab, not a work area
            SearchBox.Text = $"Bezug:\"{book.CommonName} 1-{book.NumberOfChapters}\"";
            await SearchAsync();
        };
        // a narrow window must keep room for the text: the thread graph gives way (its labels shorten themselves)
        ReaderHost.SizeChanged += (_, e) => ThreadPane.MaxWidth = _phone ? Math.Clamp(e.NewSize.Width * 0.3, 96, 140) : Math.Clamp(e.NewSize.Width * 0.42, 290, ThreadGraph.GraphWidth);
        ThreadPane.Picked += ShowRefsFlyout;
        ThreadPane.Jump += r => FollowXref(r.Target, r.EndVerse);
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
        SidePanel.IsVisible = open; SideSplitter.IsVisible = open && !IsPhone;
        if (opening) AnimateIn(SidePanel, 48, 0);
        if (opening && _notesStale && SideTabs.SelectedIndex == 2) RefreshNotesTree();
        PanelBtn.IsChecked = open;
        _settings.SideOpen = open;
        SaveSettings();
    }

    // ---- word study without Strong's tags -------------------------------------------------------------------------

    /// <summary>Strong's numbers exist only in some translations; if the open one has none, the user picks one that has them and it is added as a column.</summary>
    async Task OnStudyToggledAsync()
    {
        if (StudyBtn.IsChecked == true && TranslationBox.SelectedItem is TranslationInfo t && !TranslationHasStrongs(t)
            && !_extras.Any(e => e.Translation is { } x && TranslationHasStrongs(x)))
        {
            if (_extras.Count >= MaxExtras) StatusText.Text = L("Wortstudie: {0} hat keine Strong-Nummern. Entferne eine Spalte und schalte die Wortstudie noch einmal ein.", t.ShortName);
            else if (await PickStrongsTranslationAsync(t) is { } chosen)
            {
                StatusText.Text = L("Wortstudie: {0} hat keine Strong-Nummern. Als Spalte wurde {1} hinzugefügt; dort sind die Wörter anklickbar.", t.ShortName, chosen.Name);
                AddColumn(chosen);
                return;
            }
            else { StudyBtn.IsChecked = false; return; }   // no choice: no word study (the switch handler redraws the text)
        }
        await RedrawChapterAsync();
    }

    // ---- graph beside the text: what was clicked ------------------------------------------------------------------

    void ShowRefsFlyout(string title, IReadOnlyList<CrossRef> refs, Point at)
    {
        var flyout = new Flyout { Placement = PlacementMode.Left };
        var room = _phone ? Math.Max(200, Bounds.Width - ThreadPane.Bounds.Width - 36) : 340;   // on a phone the list opens left of the narrow graph
        var list = new WrapPanel { MaxWidth = room };
        foreach (var r in refs.OrderByDescending(x => x.Score).Take(40))
            list.Children.Add(Chip(Label(r.Target) + (r.EndVerse > r.Target.Verse ? "–" + r.EndVerse : ""),
                () => { flyout.Hide(); FollowXref(r.Target, r.EndVerse); }, (TranslationBox.SelectedItem as TranslationInfo)?.Id ?? "", r));
        foreach (var (chip, r) in list.Children.OfType<Border>().Zip(refs.OrderByDescending(x => x.Score).Take(40)))
            chip.ContextMenu = new ContextMenu { ItemsSource = XrefOpenItems(r.Target, r.EndVerse).ToList() };
        var head = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = room, Margin = new(0, 0, 0, 8) };
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
            await RedrawChapterAsync();
        };
        thread.IsCheckedChanged += async (_, _) =>
        {
            _threadOn = thread.IsChecked == true; _settings.ThreadGraph = _threadOn; SaveSettings();
            await RedrawChapterAsync();
        };
        var votes = new NumericUpDown { Minimum = 0, Maximum = 1000, Increment = 10, FormatString = "0", Value = _settings.XrefMinVotes, Width = 130 };
        var before = (_settings.XrefMinVotes, _settings.XrefSource, _settings.XrefOwnWeight);
        votes.ValueChanged += (_, _) => { _settings.XrefMinVotes = (int)(votes.Value ?? 0); SaveSettings(); };
        // which ones: printed and your own, only your own, only the printed ones (for the program, not for one vault)
        var sources = new[] { ("both", L("Gedruckte und eigene")), ("own", L("Nur eigene")), ("printed", L("Nur gedruckte")) };
        var source = new ComboBox { MinWidth = 220, ItemsSource = sources.Select(s => s.Item2).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(sources, s => s.Item1 == _settings.XrefSource)) };
        var weight = new NumericUpDown { Minimum = 1, Maximum = 5000, Increment = 50, FormatString = "0", Value = _settings.XrefOwnWeight, Width = 130 };
        weight.ValueChanged += (_, _) => { _settings.XrefOwnWeight = (int)(weight.Value ?? 200); SaveSettings(); };
        var votesRow = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = L("Mindestens Stimmen:"), Margin = new(0, 6, 0, 0) }, votes } };
        var weightRow = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = L("Eigene zählen wie so viele Stimmen:"), Margin = new(0, 6, 0, 0) }, weight } };
        void Show() { votesRow.IsVisible = _settings.XrefSource != "own"; weightRow.IsVisible = _settings.XrefSource != "printed"; }
        source.SelectionChanged += (_, _) => { if (source.SelectedIndex >= 0) { _settings.XrefSource = sources[source.SelectedIndex].Item1; SaveSettings(); Show(); } };
        Show();
        flyout.Closed += async (_, _) => { if ((_settings.XrefMinVotes, _settings.XrefSource, _settings.XrefOwnWeight) != before) await RedrawChapterAsync(); };   // redraw once, not at every click
        var votesHelp = new TextBlock { Text = L("Nur Querverweise mit mindestens so vielen Stimmen im Text zeigen (0 = alle, 100 = nur die wichtigsten). Die Liste im Seitenfeld bleibt vollständig."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        var sourceHelp = new TextBlock { Text = L("Deine eigenen Querverweise aus allen Vaults zählen im Graph neben dem Text mit, so schwer wie die Zahl darunter. Gilt für das ganze Programm, nicht nur für diesen Vault."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        votesRow.Children.Add(votesHelp);
        flyout.Content = new StackPanel { Spacing = 8, Children = { inText, thread, new TextBlock { Text = L("Welche Querverweise:"), Margin = new(0, 6, 0, 0) }, source, sourceHelp, votesRow, weightRow } };
        flyout.ShowAt(Anchor(ViewBtn));
    }

    // ---- in the text ------------------------------------------------------------------------------------------------

    /// <summary>Loads the cross references of the chapter when a view needs them (cached on disk, so usually instant).</summary>
    async Task LoadChapterRefsAsync(string book, int chapter)
    {
        _chapterRefs = null; _ownChapterRefs = null;
        if (!_xrefInText && !_threadOn) return;
        _ownChapterRefs = _settings.XrefSource == "printed" ? [] : OwnChapterRefs(book, chapter);
        if (_settings.XrefSource == "own") { _chapterRefs = []; return; }
        try
        {
            var all = await _hello.GetChapterCrossRefsAsync(book, chapter);
            // the ones you hid are left out (a copy: the cached data stays as it came)
            // the best voted come first, and only the ones with enough votes stay (setting "important only")
            _chapterRefs = all.ToDictionary(p => p.Key, p => VisibleXrefs(new VerseRef(book, chapter, p.Key), p.Value)
                .Where(x => x.Score >= _settings.XrefMinVotes).OrderByDescending(x => x.Score).ToList());
        }
        // offline, too slow or a broken answer: the views simply stay empty (your own still show), the chapter itself is shown anyway
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException) { _chapterRefs = []; }
    }

    /// <summary>Your own cross references of this chapter from every vault, by verse, each counting as <see cref="AppSettings.XrefOwnWeight"/> votes
    /// (one that leads here shows the way back). For the graph beside the text; the golden chips under the verse stay those of the open vault.</summary>
    Dictionary<int, List<CrossRef>> OwnChapterRefs(string book, int chapter)
    {
        var weight = Math.Max(1, _settings.XrefOwnWeight);
        var result = new Dictionary<int, List<CrossRef>>();
        void Add(int verse, VerseRef target, int end)
        {
            var list = result.TryGetValue(verse, out var l) ? l : result[verse] = [];
            if (!list.Any(x => x.Target == target && x.EndVerse == end)) list.Add(new CrossRef(target, end, weight));
        }
        foreach (var name in VaultNames().Where(n => !IsGuestVault(n)))
            try
            {
                var store = name == _vault.Name ? _vault : StoreOf(name);
                foreach (var x in store.GetMyXrefs(book, chapter))
                {
                    if (x.From.Book == book && x.From.Chapter == chapter) Add(x.From.Verse, x.To, Math.Max(x.To.Verse, x.EndVerse));
                    if (x.To.Book == book && x.To.Chapter == chapter) Add(x.To.Verse, x.From, x.From.Verse);
                }
            }
            catch { /* a vault that cannot be read gives nothing */ }
        return result;
    }

    Dictionary<int, List<CrossRef>>? _ownChapterRefs;

    /// <summary>For the graph beside the text: the printed ones of the verse with your own, the heaviest first.</summary>
    List<CrossRef> GraphRefsAt(int verse) =>
        [.. (_chapterRefs?.GetValueOrDefault(verse) ?? []).Concat(_ownChapterRefs?.GetValueOrDefault(verse) ?? []).OrderByDescending(x => x.Score)];

    Control? XrefChips(int verse, string translationId)
    {
        var mine = MyXrefsAt(verse);
        IReadOnlyList<CrossRef> refs = _xrefInText && _chapterRefs is not null && _chapterRefs.TryGetValue(verse, out var found) ? found : [];
        var paths = PathChips(verse).ToList();   // the reading paths that have a step here
        if (mine.Count == 0 && refs.Count == 0 && paths.Count == 0) return null;
        var wrap = new WrapPanel { Margin = new(_phone ? 6 : 30, 0, 0, 6) };
        foreach (var p in paths) wrap.Children.Add(p);
        foreach (var x in mine) wrap.Children.Add(MyXrefChip(x, verse, translationId));   // your own come first and stand out
        foreach (var r in refs.Take(6))
        {
            var chip = Chip(Label(r.Target) + (r.EndVerse > r.Target.Verse ? "–" + r.EndVerse : ""), () => FollowXref(r.Target, r.EndVerse), translationId, r);
            AttachHideXrefMenu(chip, new VerseRef(_curBook, _curChapter, verse), r.Target, r.EndVerse);
            wrap.Children.Add(chip);
        }
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
        chip.OnPress((_, e) => { if (e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) click(); });
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
        if (_chapterRefs is not null || _ownChapterRefs is not null)
            foreach (var (verse, box) in _verseBoxes)
            {
                var refs = GraphRefsAt(verse);
                if (refs.Count == 0) continue;
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
