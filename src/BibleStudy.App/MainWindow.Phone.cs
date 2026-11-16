using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>
/// The program on a phone (Android, a screen narrower than <see cref="PhoneWidth"/>): the same program, arranged for a narrow screen.
/// The top bar has two rows (above: back, home, "go to", the "…" menu and the side panel; below: translation, book and chapter); the side
/// panel (search, marks, notes, the tools) lies over the whole screen instead of beside the text, and goes away when a passage or a tool is
/// opened from it, or with the back key. A tablet held across keeps the desktop arrangement.
/// </summary>
public partial class MainWindow
{
    const double PhoneWidth = 700;
    bool _phone;

    /// <summary>True while the program is shown in the arrangement for a phone.</summary>
    bool IsPhone => _phone;

    void InitPhone()
    {
        if (!AppPlatform.IsMobile) return;
        // a phone has no window to minimize, maximize or close
        MinBtn.IsVisible = MaxBtn.IsVisible = CloseBtn.IsVisible = false;
        WrapDocumentBar();
        KeysTopBtn.IsVisible = LayoutTopBtn.IsVisible = false;   // no keyboard, no windows to arrange
        // what needs a computer: the Windows start menu, a Syncthing folder, opening a folder in the file manager
        StartMenuBtn.IsVisible = StartMenuInfo.IsVisible = BackupSyncBtn.IsVisible = SourcesCacheBtn.IsVisible = false;
        if (OperatingSystem.IsIOS()) RestartBtn.IsVisible = false;   // an iPhone app cannot start itself again
        // the list of sections beside the settings is longer than a low screen: it scrolls on its own
        if (SettingsScroll.Parent is Grid sg && sg.Children.FirstOrDefault(c => Grid.GetColumn(c) == 1) is Control toc and not ScrollViewer)
        {
            sg.Children.Remove(toc);
            var sv = new ScrollViewer { Content = toc, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            Grid.SetColumn(sv, 1);
            sg.Children.Add(sv);
            // also in a narrow area of the split view the list would only take the room of the settings
            sg.SizeChanged += (_, _) => sv.IsVisible = sg.Bounds.Width >= 520 && !_phone;
        }
        SizeChanged += (_, _) => ApplyPhoneLayout();
#if MOBILE
        BackHandler = OnPhoneBack;
#endif
        ApplyPhoneLayout();
    }


    /// <summary>Rows of buttons that are made for a wide window (the editor of a document) wrap onto a second line instead of being cut off.</summary>
    void WrapDocumentBar()
    {
        if (DocSaveBtn.Parent is not DockPanel dock || dock.Parent is not Grid grid) return;
        var kids = dock.Children.ToList();
        dock.Children.Clear();
        var wrap = new WrapPanel { Margin = dock.Margin };
        foreach (var k in Enumerable.Reverse(kids)) { k.Margin = new Thickness(0, 0, 6, 6); wrap.Children.Add(k); }
        Grid.SetRow(wrap, Grid.GetRow(dock));
        grid.Children.Remove(dock);
        grid.Children.Add(wrap);
    }


    void ApplyPhoneLayout()
    {
        if (!AppPlatform.IsMobile || Bounds.Width <= 0) return;
        var phone = Bounds.Width < PhoneWidth || Bounds.Height < 500;   // a phone held across is as low as a phone is narrow
        var pickers = (Control)BookBtn.Parent!.Parent!.Parent!;   // the row with the language, translation, book and chapter
        if (phone != _phone)
        {
            _phone = phone;
            if (TopGrid.RowDefinitions.Count == 0) TopGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetRow(pickers, phone ? 1 : 0);
            Grid.SetColumn(pickers, phone ? 0 : 2);
            Grid.SetColumnSpan(pickers, phone ? 6 : 1);
            pickers.Margin = phone ? new Thickness(0, 6, 0, 2) : default;
            pickers.HorizontalAlignment = phone ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            TopBar.Height = phone ? double.NaN : 60;   // (ApplyCompact sets it for every size)
            TopBar.Padding = phone ? new Thickness(6, 4, 6, 4) : new Thickness(16, 0, 0, 0);
            // the pickers as wide as their text: on a phone the fixed widths of the desktop do not fit side by side
            LangBtn.MinWidth = phone ? 0 : 84; TransBtn.MinWidth = phone ? 0 : 110; BookBtn.MinWidth = phone ? 0 : 100; ChapterBtn.MinWidth = phone ? 0 : 64;
            ((Control)LogoImage.Parent!).IsVisible = !phone;
            ForwardBtn.IsVisible = !phone;
            SideSplitter.IsVisible = !phone && SidePanel.IsVisible;
            // the panel lies over the text and the tools: always on top, and what is below it never draws over it
            SidePanel.ZIndex = phone ? 10 : 0;
            FitExplorerToScreen();
            // the work tabs: on a phone they do not fit side by side, the row scrolls sideways with the finger
            if (phone && TabBar.Child == TabStrip)
            {
                TabBar.Child = null;
                TabBar.Child = MakeTabScroller();
            }
            TabBar.Padding = phone ? new Thickness(6, 4, 6, 0) : new Thickness(16, 6, 16, 0);
            // the settings: the list of sections on the right takes half of a phone; the sections are folded and found by scrolling
            if (SettingsScroll.Parent is Grid settings)
            {
                foreach (var c in settings.Children.Where(c => Grid.GetColumn(c) == 1)) c.IsVisible = !phone;
                settings.Margin = phone ? new Thickness(8, 10, 0, 8) : new Thickness(16, 14, 4, 12);
            }
            ApplyPhoneShell();
            if (phone && !_rowsWrapped) { _rowsWrapped = true; WrapRows(SettingsStack); }
        }
        if (phone)
        {
            // the panel takes the whole width: beside it the text would be one word wide
            SidePanel.MaxWidth = double.PositiveInfinity;
            SidePanel.Width = Bounds.Width;
        }
        else SidePanel.Width = Math.Clamp(_settings.SideWidth, 300, 760);
        ApplyCompact();
        _matApplyTree?.Invoke();
        FitTopBar();
        CapFixedWidths();
    }


    readonly HashSet<Control> _capped = [];

    /// <summary>
    /// Controls with a fixed width of a desktop (a slider of 340, a box of 230) reach out of a phone screen: they are made no wider than the screen.
    /// Only the ones that are too wide are touched, and they get their width back when the screen is wide again.
    /// </summary>
    void CapFixedWidths()
    {
        if (!AppPlatform.IsMobile || Bounds.Width <= 0) return;
        var cap = _phone ? Math.Max(120, Bounds.Width - 64) : double.PositiveInfinity;
        foreach (var c in _capped.ToList()) { c.MaxWidth = cap; if (double.IsInfinity(cap)) _capped.Remove(c); }
        if (!_phone) return;
        foreach (var c in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(this).OfType<Control>())
            if (c != SidePanel && c != ReaderPanel && !double.IsNaN(c.Width) && c.Width > cap && !_capped.Contains(c) && double.IsInfinity(c.MaxWidth)) { c.MaxWidth = cap; _capped.Add(c); }
    }


    /// <summary>On a phone a passage or a tool opened from the side panel is shown at once: the panel lies over it and goes away.</summary>
    void ClosePanelOnPhone()
    {
        if (_phone && SidePanel.IsVisible) SetPanelOpen(false);
        if (_phone) Avalonia.Threading.Dispatcher.UIThread.Post(CapFixedWidths, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>The back key of the phone: first the side panel, then an open tool goes back to the text; at the text the app goes to the background.</summary>
    bool OnPhoneBack()
    {
        if (_sheetLayer is { IsVisible: true }) { ShowSheet(null); return true; }
        if (SidePanel.IsVisible && _phone) { SetPanelOpen(false); return true; }
        if (_activeWork is not null) { ShowReader(); return true; }
        if (!ReaderHost.IsVisible) { ShowReader(); return true; }
        return false;
    }
}

public partial class MainWindow
{
    Avalonia.Threading.DispatcherTimer? _longPress;

    /// <summary>
    /// On a touch screen a long press is the right click: the verse menu. The finger may move a little; when it lifts or scrolls the page first,
    /// it was a tap or a scroll. (The scrolling of the page takes the finger, so the Holding gesture of Avalonia never comes here.)
    /// </summary>
    void StartLongPress(int verse, Border box, Avalonia.Input.PointerPressedEventArgs e)
    {
        _longPress?.Stop();
        if (e.Pointer.Type != Avalonia.Input.PointerType.Touch) return;
        var start = e.GetPosition(this);
        var offset = ReaderScroll.Offset;
        var pointer = e.Pointer;
        var timer = _longPress = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        void Cancel() { timer.Stop(); RemoveHandler(PointerReleasedEvent, OnUp); RemoveHandler(PointerMovedEvent, OnMove); }
        void OnUp(object? s, Avalonia.Input.PointerReleasedEventArgs a) { if (a.Pointer == pointer) Cancel(); }
        void OnMove(object? s, Avalonia.Input.PointerEventArgs a)
        {
            if (a.Pointer != pointer) return;
            var p = a.GetPosition(this);
            if (Math.Abs(p.X - start.X) > 12 || Math.Abs(p.Y - start.Y) > 12) Cancel();
        }
        AddHandler(PointerReleasedEvent, OnUp, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnMove, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        timer.Tick += (_, _) =>
        {
            Cancel();
            if (Math.Abs(ReaderScroll.Offset.Y - offset.Y) > 8) return;   // the page was scrolled
            if (TryStartWordDrag(start, pointer, verse, box)) return;
            if (!_selVerses.Contains(verse)) SelectVerse(verse);
            ShowVerseMenu(verse, box);
        };
        timer.Start();
    }

    /// <summary>
    /// A long press on a word selects it, as phones do; moving the finger on (without lifting) stretches the selection, lifting shows the colors.
    /// Lifting at once opens the verse menu for the word. Scrolling never selects: this starts only after the finger rested.
    /// </summary>
    bool TryStartWordDrag(Point at, Avalonia.Input.IPointer pointer, int verse, Border box)
    {
        foreach (var cref in _cells)
        {
            if (cref.Data.Foreign || cref.Tb.Tag is not CellInfo info || !cref.Tb.IsEffectivelyVisible || this.TranslatePoint(at, cref.Tb) is not { } p || !new Rect(cref.Tb.Bounds.Size).Contains(p)) continue;
            var t = info.RawText;
            if (t.Length == 0) return false;
            var i = Math.Clamp(RawAt(info, cref.Tb.TextLayout.HitTestPoint(p).TextPosition), 0, t.Length - 1);
            if (!IsWordChar(t[i]) && i > 0 && IsWordChar(t[i - 1])) i--;
            if (!IsWordChar(t[i])) return false;
            int s = i, en = i + 1;
            while (s > 0 && IsWordChar(t[s - 1])) s--;
            while (en < t.Length && IsWordChar(t[en])) en++;
            ClearTextSelection(true);
            SetSelectionFrom(cref, s, cref, en, ColCells(cref));
            _drag = new DragState { Anchor = cref, AnchorRaw = s, Start = at, Last = at, Cells = ColCells(cref), Scroller = ReaderScroll, TouchWord = true, Verse = verse, Box = box };
            pointer.Capture(ReaderScroll);   // the finger now selects; the page does not scroll under it
            return true;
        }
        return false;
    }
}

public partial class MainWindow
{
    /// <summary>A phone held across: little height, so the bars take less room (see <see cref="ApplyCompact"/>).</summary>
    bool _compact;
    /// <summary>Tabs and the reading toolbar are folded away (a small handle above unfolds them): the text gets the room.</summary>
    bool _barsFolded;
    Button? _foldHandle;
    Icon? _foldIcon;

    void EnsureFoldHandle()
    {
        if (_foldHandle is not null || TabBar.Parent is not DockPanel dock) return;
        _foldIcon = new Icon { Data = Geometry.Parse("M6 9l6 6 6-6"), IconSize = 14 };
        _foldHandle = new Button { Height = 22, MinHeight = 0, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, BorderThickness = new Thickness(0), Content = _foldIcon, IsVisible = false };
        _foldHandle.Bind(Button.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        _foldHandle.Click += (_, _) => { _barsFolded = !_barsFolded; ApplyFold(); };
        DockPanel.SetDock(_foldHandle, Dock.Top);
        dock.Children.Insert(dock.Children.IndexOf(TabBar), _foldHandle);
    }

    /// <summary>On a phone (also across) the tab row and the reading toolbar fold away; across, they start folded, the screen is only about 360 high.</summary>
    void ApplyCompact()
    {
        var compact = Bounds.Height > 0 && Bounds.Height < 480;
        EnsureFoldHandle();
        if (compact != _compact) { _compact = compact; _barsFolded = compact; }
        TopBar.Height = _phone ? double.NaN : compact ? 46 : 60;
        ApplyFold();
        UpdateOrientationLock();
    }

    void ApplyFold()
    {
        if (_foldHandle is null || _foldIcon is null) return;
        _foldHandle.IsVisible = AppPlatform.IsMobile && !_settings.FocusMode && (_phone || _compact) && !_shellOn;
        if (!_foldHandle.IsVisible) _barsFolded = false;
        TabBar.IsVisible = !_barsFolded && !_settings.FocusMode;
        ParallelBar.ClipToBounds = true;
        ParallelBar.MaxHeight = _barsFolded ? 0 : double.PositiveInfinity;   // the row of the extra translations folds with the others
        _foldIcon.Data = Geometry.Parse(_barsFolded ? "M6 9l6 6 6-6" : "M18 15l-6-6-6 6");
        UpdateReadBar();
    }

    /// <summary>The tab row scrolls sideways; a small arrow at the right edge shows that there is more (it is not to be seen otherwise).</summary>
    Control MakeTabScroller()
    {
        var scroll = new ScrollViewer { Content = TabStrip, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var more = new Border { Width = 26, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false, IsVisible = false, Child = new Icon { Data = Geometry.Parse("M9 6l6 6-6 6"), IconSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        more.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        void Update() => more.IsVisible = scroll.Extent.Width > scroll.Viewport.Width + 2 && scroll.Offset.X < scroll.Extent.Width - scroll.Viewport.Width - 2;
        scroll.ScrollChanged += (_, _) => Update();
        scroll.PropertyChanged += (_, e) => { if (e.Property == Visual.BoundsProperty) Update(); };
        return new Grid { Children = { scroll, more } };
    }

    /// <summary>Two areas side by side need a wide screen: on a phone the split view and the parallel translations turn it across (the phone may go back by itself when they end).</summary>
    void UpdateOrientationLock()
    {
#if ANDROID
        var wide = Math.Min(Bounds.Width, Bounds.Height) >= 600;   // a tablet is wide enough in both positions
        BibleStudy.Droid.MainActivity.Landscape(!wide && (_settings.SplitView || _colIds.Count > 1));
#endif
    }
}
