using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Cross references in the text, the thread graph beside the text, and the full graph views.</summary>
public partial class MainWindow
{
    CrossRefGraph? _graph;
    bool _graphBuilding, _xrefInText, _threadOn;
    Dictionary<int, List<CrossRef>>? _chapterRefs;

    void InitGraph()
    {
        _xrefInText = _settings.XrefInText;
        _threadOn = _settings.ThreadGraph;
        ViewBtn.Click += (_, _) => ShowViewFlyout();
        GraphBtn.Click += async (_, _) => await ShowGraphAsync();
        GraphPane.BuildRequested += async () => await BuildGraphAsync();
        GraphPane.OpenChapter += async (book, chapter) => await GoToAsync(book, chapter);
        ReaderHost.SizeChanged += (_, _) => UpdateThread();
    }

    void ShowViewFlyout()
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var inText = new CheckBox { Content = "Querverweise im Text", IsChecked = _xrefInText };
        var thread = new CheckBox { Content = "Graph neben dem Text (eine Spalte)", IsChecked = _threadOn };
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

    void UpdateThread()
    {
        if (!ThreadPane.IsVisible || _chapterRefs is null) return;
        var rows = new List<ThreadGraph.Row>();
        foreach (var (verse, box) in _verseBoxes)
        {
            if (!_chapterRefs.TryGetValue(verse, out var refs) || refs.Count == 0) continue;
            if (box.TranslatePoint(new Point(0, Math.Min(box.Bounds.Height / 2, 18)), ThreadPane) is not { } p) continue;
            rows.Add(new ThreadGraph.Row(p.Y, refs.GroupBy(r => r.Target.Book).Select(g => (g.Key, g.Count())).ToList()));
        }
        ThreadPane.SetData(rows, id => _books.FirstOrDefault(b => b.Id == id)?.Order ?? 999, BookName);
    }

    void ShowThread(bool visible)
    {
        ThreadPane.IsVisible = visible && _chapterRefs is not null;
        if (ThreadPane.IsVisible) Dispatcher.UIThread.Post(UpdateThread, DispatcherPriority.Loaded);
    }

    // ---- full graph -------------------------------------------------------------------------------------------------

    async Task ShowGraphAsync()
    {
        Home.IsVisible = false; ReaderScroll.IsVisible = false; GraphPane.IsVisible = true;
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
            var progress = new Progress<(int Done, int Total)>(p => GraphPane.ReportProgress(p.Done, p.Total));
            _graph = await Task.Run(() => CrossRefGraphBuilder.BuildAsync(_hello, progress));
            GraphPane.SetGraph(_graph, BookName);
            GraphPane.FocusChapter(_curBook, _curChapter);
        }
        catch (Exception ex) { GraphPane.ShowBuildPrompt("Das Laden ist fehlgeschlagen: " + ex.Message); }
        finally { _graphBuilding = false; }
    }
}
