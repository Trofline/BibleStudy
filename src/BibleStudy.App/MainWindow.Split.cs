using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// The split view (switch in the top bar, off at first), as in Logos: up to four areas side by side, each with the Bible text or a tab
/// (documents, materials, notes …), each as wide as the line between them is dragged. A tab dragged from the tab strip onto the middle of
/// an area takes that area; onto its left or right edge it gets a new area there; out of the window it becomes a window of its own.
/// The Bible text can be moved like any tab. Switch, areas and widths are kept for the next start.
/// </summary>
public partial class MainWindow
{
    const string ReaderPane = "reader";
    const int MaxPanes = 4;

    List<string> Panes => _settings.SplitPanes;
    string? _focusPane;
    bool _panesReady;
    readonly List<(string Key, Border Header)> _paneHeaders = [];

    /// <summary>True when the tab is one of the areas of the split view.</summary>
    bool InPane(string? key) => _settings.SplitView && key is not null && Panes.Contains(key);

    void InitSplit()
    {
        SplitTopBtn.IsChecked = _settings.SplitView;
        SplitTopBtn.IsCheckedChanged += (_, _) => SetSplitView(SplitTopBtn.IsChecked == true);
        if (_settings.SplitView) { if (Panes.Count == 0) Panes.Add(ReaderPane); LayoutPanes(); }
        else _panesReady = true;
        // after the start the tabs of the areas come back (the tabs of the last session are opened first)
        var restored = false;
        Opened += (_, _) =>
        {
            if (restored) return;
            restored = true;
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var key in Panes.ToList())
                    if (key != ReaderPane && !_windows.ContainsKey(key) && NavGroups.FirstOrDefault(g => "group:" + g.Name == key) is { } g) OpenWork(g);
                _panesReady = true;
                if (_settings.SplitView) LayoutPanes();
            }, DispatcherPriority.Background);
        };
    }

    void SetSplitView(bool on)
    {
        if (_settings.SplitView == on) return;
        if (on)
        {
            // the text and the tab that is open now stand side by side at once
            var open = _activeWork;
            if (open is not null) { _activeWork = null; WorkHost.IsVisible = false; }
            _settings.SplitView = true;
            // the areas of the last time come back (tabs closed since then are opened again); without them: the text and the open tab
            var saved = Panes.Where(k => k == ReaderPane || _windows.ContainsKey(k) || NavGroups.Any(g => "group:" + g.Name == k)).Distinct().Take(MaxPanes).ToList();
            if (saved.Count > 1)
            {
                Panes.Clear(); Panes.AddRange(saved);
                foreach (var k in saved.Where(k => k != ReaderPane && !_windows.ContainsKey(k)))
                    if (NavGroups.FirstOrDefault(g => "group:" + g.Name == k) is { } g) OpenWork(g);
                if (open is not null && !Panes.Contains(open) && Panes.Count < MaxPanes) { Panes.Add(open); _settings.SplitWidths.Clear(); }
            }
            else
            {
                Panes.Clear(); _settings.SplitWidths.Clear();
                Panes.Add(ReaderPane);
                if ((open ?? _lastWorkTab) is { } key && _windows.TryGetValue(key, out var d) && d.Win is null) Panes.Add(key);
            }
            _focusPane = Panes[^1];
            StatusText.Text = L("Geteilte Ansicht an: bis zu vier Bereiche nebeneinander. Einen Reiter auf die Mitte eines Bereichs ziehen ersetzt ihn, auf seinen Rand legt ihn daneben.");
            // the areas need the width: the side panel folds away (Ctrl+B brings it back)
            if (SidePanel.IsVisible) { SetPanelOpen(false); StatusText.Text += " " + L("Die Seitenleiste ist eingeklappt (Strg+B holt sie zurück)."); }
        }
        else
        {
            // one area again: the area used last is the one shown
            var show = _focusPane is { } f && f != ReaderPane && Panes.Contains(f) ? f : Panes.Contains(ReaderPane) ? null : Panes.FirstOrDefault(p => p != ReaderPane);
            _settings.SplitView = false;
            LayoutPanes();
            ShowWork(show);
            StatusText.Text = L("Geteilte Ansicht aus.");
        }
        SaveSettings();
        LayoutPanes();
        RebuildTabStrip();
    }

    /// <summary>Puts the text and the tabs where they belong: in the areas of the split view, or (without it) the text with the open tab over it.</summary>
    void LayoutPanes()
    {
        // first everything out of its old place: a control may have only one parent
        (LeftArea.Parent as Panel)?.Children.Remove(LeftArea);
        foreach (var w in _windows.Values.Where(w => w.Win is null)) (w.Panel.Parent as Panel)?.Children.Remove(w.Panel);
        MainArea.Children.Clear();
        MainArea.ColumnDefinitions.Clear();
        _paneHeaders.Clear();

        if (!_settings.SplitView)
        {
            MainArea.Children.Add(LeftArea);
            foreach (var w in _windows.Values.Where(w => w.Win is null)) { w.Panel.IsVisible = w.Key == _activeWork; WorkHost.Children.Add(w.Panel); }
            WorkHost.IsVisible = _activeWork is not null;
            MainArea.Children.Add(SplitDropZone);
            UpdateReadBar();
            return;
        }

        bool Shown(string k) => k == ReaderPane || _windows.TryGetValue(k, out var d) && d.Win is null;
        if (_panesReady) Panes.RemoveAll(k => !Shown(k));   // a tab that was closed or became a window leaves its area
        if (Panes.Count == 0) Panes.Add(ReaderPane);
        var shown = Panes.Where(Shown).ToList();
        var widths = _settings.SplitWidths.Count == Panes.Count ? _settings.SplitWidths : Enumerable.Repeat(1.0, Panes.Count).ToList();
        _activeWork = null;
        WorkHost.IsVisible = false;

        var col = 0;
        foreach (var key in shown)
        {
            if (col > 0)
            {
                MainArea.ColumnDefinitions.Add(new ColumnDefinition(6, GridUnitType.Pixel));
                var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
                splitter.Bind(BackgroundProperty, this.GetResourceObservable("BsBorder"));
                splitter.DragCompleted += (_, _) => SavePaneWidths();
                Grid.SetColumn(splitter, MainArea.ColumnDefinitions.Count - 1);
                MainArea.Children.Add(splitter);
            }
            var share = Math.Max(0.05, widths[Panes.IndexOf(key)]);
            MainArea.ColumnDefinitions.Add(new ColumnDefinition(share, GridUnitType.Star) { MinWidth = 240 });
            var host = PaneHost(key);
            Grid.SetColumn(host, MainArea.ColumnDefinitions.Count - 1);
            MainArea.Children.Add(host);
            col++;
        }
        // the tabs that are not in an area wait (hidden) in the work area of the text
        foreach (var w in _windows.Values.Where(w => w.Win is null && w.Panel.Parent is null)) { w.Panel.IsVisible = false; WorkHost.Children.Add(w.Panel); }
        Grid.SetColumnSpan(SplitDropZone, Math.Max(1, MainArea.ColumnDefinitions.Count));
        MainArea.Children.Add(SplitDropZone);
        if (_focusPane is null || !Panes.Contains(_focusPane)) _focusPane = Panes[^1];
        PaintPaneHeaders();
        UpdateReadBar();
        SaveSettings();
    }

    /// <summary>One area: a slim head with its name and a ✕, below it the text or the tab.</summary>
    Control PaneHost(string key)
    {
        var isReader = key == ReaderPane;
        _windows.TryGetValue(key, out var d);
        var title = new TextBlock { Text = isReader ? L("Lesen") : L(d!.Title), FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var close = new Button { Classes = { "tool" }, Width = 22, Height = 22, Padding = new(0), Content = new Icon { Data = Geometry.Parse("M18 6L6 18 M6 6l12 12"), IconSize = 11 } };
        ToolTip.SetTip(close, isReader ? L("Bereich schließen (der Bibeltext kommt mit „Lesen“ zurück)") : L("Bereich schließen (der Reiter bleibt oben offen)"));
        close.Click += (_, _) => { if (Panes.Count > 1) { Panes.Remove(key); _settings.SplitWidths.Clear(); LayoutPanes(); RebuildTabStrip(); } };
        close.IsVisible = Panes.Count > 1;
        var row = new DockPanel { Margin = new(10, 0, 4, 0) };
        DockPanel.SetDock(close, Dock.Right); row.Children.Add(close); row.Children.Add(title);
        var header = new Border { Height = 28, BorderThickness = new(0, 0, 0, 2), Child = row };
        header.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        ToolTip.SetTip(header, L("Reiter von oben hierher ziehen: Mitte ersetzt diesen Bereich, Rand legt ihn daneben."));
        _paneHeaders.Add((key, header));

        Control content = isReader ? LeftArea : d!.Panel;
        content.IsVisible = true;
        var host = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        host.Children.Add(header);
        host.Children.Add(content);
        // a click anywhere in the area makes it the one new tabs open in
        host.AddHandler(PointerPressedEvent, (object? _, PointerPressedEventArgs _) => { if (_focusPane != key) { _focusPane = key; PaintPaneHeaders(); } }, RoutingStrategies.Tunnel, true);
        return host;
    }

    void PaintPaneHeaders()
    {
        foreach (var (key, header) in _paneHeaders)
            header.Bind(Border.BorderBrushProperty, this.GetResourceObservable(key == _focusPane && _paneHeaders.Count > 1 ? "BsAccent" : "BsBorder"));
    }

    void SavePaneWidths()
    {
        var cols = MainArea.ColumnDefinitions.Where(c => c.Width.IsStar).Select(c => c.ActualWidth).ToList();
        var total = cols.Sum();
        if (total <= 0 || cols.Count != Panes.Count) return;
        _settings.SplitWidths = cols.Select(c => c / total).ToList();
        SaveSettings();
    }

    /// <summary>Shows a tab in the split view: an area it has already is used; otherwise it goes into the area used last (not the text), or a new area beside them.</summary>
    void ShowInPane(string key)
    {
        if (!_windows.TryGetValue(key, out var d) || d.Win is not null) return;
        if (!Panes.Contains(key))
        {
            var target = _focusPane is { } f && f != ReaderPane && Panes.Contains(f) ? Panes.IndexOf(f) : Panes.FindLastIndex(p => p != ReaderPane);
            if (Panes.Count < 2 || target < 0) { if (Panes.Count < MaxPanes) { Panes.Add(key); _settings.SplitWidths.Clear(); } else Panes[^1] = key; }
            else Panes[target] = key;
            LayoutPanes();
            AnimateIn(d.Panel);
        }
        _focusPane = key;
        _lastWorkTab = key;
        PaintPaneHeaders();
        RebuildTabStrip();
        TabShown(d.Current);
        if (_contextTabs.TryGetValue(key, out var context)) _ = DrawContextTabAsync(context);
    }

    /// <summary>The Bible text is wanted (a passage was opened, "Lesen" was clicked): it gets an area again if it had none.</summary>
    void EnsureReaderPane()
    {
        if (!_settings.SplitView) return;
        if (!Panes.Contains(ReaderPane))
        {
            if (Panes.Count < MaxPanes) Panes.Insert(0, ReaderPane); else Panes[0] = ReaderPane;
            _settings.SplitWidths.Clear();
            LayoutPanes();
            RebuildTabStrip();
        }
        _focusPane = ReaderPane;
        PaintPaneHeaders();
    }

    /// <summary>Where a dragged tab would go: the area under the pointer, replaced (middle) or with a new area before or after it (edges).</summary>
    (int Index, bool Insert)? PaneDropTarget(Point p)
    {
        if (!_settings.SplitView || p.Y < 0 || p.Y > MainArea.Bounds.Height || p.X < 0 || p.X > MainArea.Bounds.Width) return null;
        var hosts = MainArea.Children.OfType<DockPanel>().ToList();
        for (var i = 0; i < hosts.Count; i++)
        {
            var b = hosts[i].Bounds;
            if (p.X < b.Left || p.X > b.Right) continue;
            var rel = (p.X - b.Left) / Math.Max(1, b.Width);
            return rel < 0.22 ? (i, true) : rel > 0.78 ? (i + 1, true) : (i, false);
        }
        return null;
    }

    void ShowSplitDropZone((int Index, bool Insert)? target)
    {
        SplitDropZone.IsVisible = target is not null;
        if (target is not { } t) return;
        var hosts = MainArea.Children.OfType<DockPanel>().ToList();
        if (hosts.Count == 0) { SplitDropZone.IsVisible = false; return; }
        var accent = (Res("BsAccent") as ISolidColorBrush)?.Color ?? Colors.Goldenrod;
        SplitDropZone.Background = new SolidColorBrush(accent, t.Insert ? 0.35 : 0.12);
        SplitDropZone.BorderBrush = new SolidColorBrush(accent, 0.9);
        Rect r;
        if (t.Insert)
        {
            // a narrow strip on the edge where the new area would come
            var x = t.Index < hosts.Count ? hosts[t.Index].Bounds.Left : hosts[^1].Bounds.Right;
            r = new Rect(Math.Max(0, x - 14), 0, 28, MainArea.Bounds.Height);
        }
        else r = hosts[Math.Min(t.Index, hosts.Count - 1)].Bounds;
        SplitDropZone.Margin = new Thickness(r.X, 0, 0, 0);
        SplitDropZone.Width = r.Width;
        SplitDropZone.Height = MainArea.Bounds.Height;
        SplitDropZone.VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>Puts a tab (null: the Bible text) into an area of the split view.</summary>
    void PlaceTabAt(string? key, int index, bool insert)
    {
        var k = key ?? ReaderPane;
        if (key is not null && !_windows.ContainsKey(key)) return;
        // a tab in a window of its own comes back into the main window first
        if (key is not null && _windows[key] is { Win: { } win } loose) { loose.Redock = true; win.Close(); }
        if (key is not null && _windows.TryGetValue(key, out var d) && d.Win is not null) return;
        var from = Panes.IndexOf(k);
        if (insert && from < 0 && Panes.Count >= MaxPanes) { insert = false; index = Math.Min(index, Panes.Count - 1); StatusText.Text = L("Mehr als {0} Bereiche gehen nicht; der Reiter ersetzt den Bereich.", MaxPanes); }
        if (insert)
        {
            if (from >= 0) { Panes.RemoveAt(from); if (from < index) index--; }
            Panes.Insert(Math.Clamp(index, 0, Panes.Count), k);
        }
        else
        {
            index = Math.Clamp(index, 0, Panes.Count - 1);
            if (from >= 0) (Panes[from], Panes[index]) = (Panes[index], Panes[from]);   // two areas swap
            else Panes[index] = k;
        }
        _settings.SplitWidths.Clear();
        _focusPane = k;
        LayoutPanes();
        RebuildTabStrip();
        if (key is not null && _windows.TryGetValue(key, out var shown)) TabShown(shown.Current);
    }

    /// <summary>Takes a tab out of the areas (it stays open in the tab strip).</summary>
    void RemoveFromPanes(string key)
    {
        if (!Panes.Remove(key)) return;
        _settings.SplitWidths.Clear();
        LayoutPanes();
        RebuildTabStrip();
    }
}
