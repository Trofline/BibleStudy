using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;

namespace BibleStudy.App;

/// <summary>
/// The shell of the program on a phone. It is not the desktop made smaller: the reading screen has one slim bar on top (back, where I am,
/// translation, chapter back / forward) and a navigation bar at the bottom (read, search, tools, collection, more). Everything else that the
/// desktop shows as rows of buttons lives in two sheets that come up from the bottom: "Tools" (what I can do with this chapter) and "More"
/// (the areas of the program and its settings). The buttons of the desktop stay where they are, folded to nothing: they hold the logic and the
/// sheets press them, so every function is still there.
/// </summary>
public partial class MainWindow
{
    bool _shellOn;
    Grid? _sheetLayer;
    Border? _sheetBox;
    StackPanel? _sheetBody;
    TextBlock? _sheetTitle;
    Border? _pBar, _pNav;
    Button? _pBack, _pTitle, _pTrans, _pPrev, _pNext;
    TextBlock? _pTitleText, _pTransText;
    readonly Dictionary<string, Button> _navBtns = [];
    DispatcherTimer? _shellTimer;

    static Geometry Geo(string path) => Geometry.Parse(path);

    // ---- building ---------------------------------------------------------------------------------------------------------

    void EnsurePhoneShell()
    {
        if (_pBar is not null) return;
        if (TopBar.Parent is not DockPanel dock) return;

        // the layer for the sheets lies over the whole screen
        _sheetLayer = BuildSheetLayer();
        var parent = dock.Parent;
        var host = new Grid();
        if (parent is Panel p)
        {
            var at = p.Children.IndexOf(dock);
            p.Children.RemoveAt(at);
            host.Children.Add(dock);
            host.Children.Add(_sheetLayer);
            p.Children.Insert(at, host);
        }
        else if (parent is ContentControl cc)
        {
            cc.Content = null;
            host.Children.Add(dock);
            host.Children.Add(_sheetLayer);
            cc.Content = host;
        }
        else if (parent is Avalonia.Controls.Presenters.ContentPresenter)
        {
            // the window shows its content through a presenter: the window itself holds it
            Content = null;
            host.Children.Add(dock);
            host.Children.Add(_sheetLayer);
            Content = host;
        }
        else return;

        InstallPhoneStyles();
        HookShortHints();
        _pBar = BuildPhoneBar();
        _pNav = BuildPhoneNav();
        DockPanel.SetDock(_pNav, Dock.Bottom);
        DockPanel.SetDock(_pBar, Dock.Top);
        dock.Children.Insert(0, _pNav);
        dock.Children.Insert(0, _pBar);

        _shellTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _shellTimer.Tick += (_, _) => RefreshPhoneShell();
        _shellTimer.Start();
    }

    Button IconButton(string path, string tip, Action click, double size = 44)
    {
        var b = new Button
        {
            Classes = { "tool" }, Width = size, Height = size, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Content = new Icon { Data = Geo(path), IconSize = 22 },
        };
        ToolTip.SetTip(b, tip);
        Avalonia.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    Border BuildPhoneBar()
    {
        _pBack = IconButton("M19 12H5 M12 19l-7-7 7-7", L("Zurück"), () => { if (!OnPhoneBack()) _ = GoBackAsync(); });
        _pTitleText = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _pTitle = new Button
        {
            Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 44, Padding = new Thickness(8, 0),
            Content = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    Docked(new Icon { Data = Geo("M6 9l6 6 6-6"), IconSize = 16, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }, Avalonia.Controls.Dock.Right),
                    _pTitleText,
                },
            },
        };
        _pTitle.Click += (_, _) => { if (ReaderBrowsing()) ShowBookPicker(_pTitle, _pTitle); };
        _pTransText = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 90, TextTrimming = TextTrimming.CharacterEllipsis };
        _pTrans = new Button { Classes = { "ghost" }, Height = 36, MinHeight = 0, Padding = new Thickness(10, 0), Content = _pTransText };
        ToolTip.SetTip(_pTrans, L("Übersetzung wählen"));
        _pTrans.Click += (_, _) => ShowTranslationPicker(_pTrans);
        _pPrev = IconButton("M15 18l-6-6 6-6", L("Voriges Kapitel"), () => PrevBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), 40);
        _pNext = IconButton("M9 18l6-6-6-6", L("Nächstes Kapitel"), () => NextBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), 40);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        void Add(Control c, int col) { Grid.SetColumn(c, col); grid.Children.Add(c); }
        Add(_pBack, 0); Add(_pTitle, 1); Add(_pTrans, 2); Add(_pPrev, 3); Add(_pNext, 4);
        var bar = new Border { Child = grid, Height = 52, BorderThickness = new Thickness(0, 0, 0, 1) };
        bar.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        bar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        return bar;
    }

    static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }

    Border BuildPhoneNav()
    {
        var items = new (string Key, string Label, string Icon, Action Do)[]
        {
            ("read", L("Lesen"), "M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z", () => { ShowSheet(null); ShowReader(); }),
            ("search", L("Suche"), "M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0z M21 21l-4.3-4.3", () => PhoneGoto("Suchen")),
            ("tools", L("Werkzeuge"), "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94z", ShowToolsSheet),
            ("marks", L("Merken"), "M19 21l-7-4-7 4V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z", () => PhoneGoto("Merken")),
            ("more", L("Mehr"), "M4 6h16 M4 12h16 M4 18h16", ShowMoreSheet),
        };
        var grid = new UniformGrid { Rows = 1, Columns = items.Length };
        foreach (var it in items)
        {
            var label = new TextBlock { Text = it.Label, FontSize = 11, FontWeight = FontWeight.Medium, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            var b = new Button
            {
                Classes = { "rail" }, Height = 58, Margin = new Thickness(2, 3),
                Content = new StackPanel { Spacing = 3, Children = { new Icon { Data = Geo(it.Icon), IconSize = 22, HorizontalAlignment = HorizontalAlignment.Center }, label } },
            };
            Avalonia.Automation.AutomationProperties.SetName(b, it.Label);
            var act = it.Do;
            b.Click += (_, _) => act();
            _navBtns[it.Key] = b;
            grid.Children.Add(b);
        }
        var nav = new Border { Child = grid, BorderThickness = new Thickness(0, 1, 0, 0) };
        nav.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        nav.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        return nav;
    }

    // ---- the sheet that comes up from below ----------------------------------------------------------------------------------

    Border? _sheetScrim;
    static readonly IBrush ScrimBrush = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));

    Grid BuildSheetLayer()
    {
        var scrim = _sheetScrim = new Border { Background = ScrimBrush };
        scrim.PointerPressed += (_, e) => { ShowSheet(null); e.Handled = true; };
        _sheetTitle = new TextBlock { FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        var close = IconButton("M18 6L6 18 M6 6l12 12", L("Schließen"), () => ShowSheet(null), 44);
        var head = new DockPanel { Height = 48, LastChildFill = true };
        DockPanel.SetDock(close, Avalonia.Controls.Dock.Right);
        head.Children.Add(close);
        head.Children.Add(_sheetTitle);
        _sheetBody = new StackPanel { Spacing = 6, Margin = new Thickness(12, 0, 12, 16) };
        var scroll = new ScrollViewer { Content = _sheetBody, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var box = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Avalonia.Controls.Dock.Top);
        box.Children.Add(head);
        box.Children.Add(scroll);
        _sheetBox = new Border
        {
            Child = box, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(20, 20, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0),
        };
        _sheetBox.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        _sheetBox.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        var layer = new Grid { IsVisible = false, ZIndex = 400 };
        layer.Children.Add(scrim);
        layer.Children.Add(_sheetBox);
        return layer;
    }

    /// <summary>Shows the sheet with the content, or closes it (null).</summary>
    void ShowSheet(string? title, params Control[] content)
    {
        if (_sheetLayer is null || _sheetBody is null || _sheetBox is null) return;
        if (title is null) { _sheetLayer.IsVisible = false; _sheetBody.Children.Clear(); return; }
        _sheetTitle!.Text = title;
        if (_sheetScrim is { } scrim) scrim.Background = ScrimBrush;
        _sheetBody.Children.Clear();
        foreach (var c in content) _sheetBody.Children.Add(c);
        _sheetBox.MaxHeight = Math.Max(260, Bounds.Height * 0.82);
        _sheetLayer.IsVisible = true;
        AnimateIn(_sheetBox, 0, 40);
    }

    static string TipLabel(Control c)
    {
        var tip = ToolTip.GetTip(c) as string ?? "";
        var cut = tip.IndexOfAny([':', '(', ',', '\n']);
        return (cut > 0 ? tip[..cut] : tip).Trim();
    }

    /// <summary>The press of a button of the desktop bars, as if it had been clicked (a switch is turned over first).</summary>
    void PressButton(Control c)
    {
        if (c is ToggleButton t) t.IsChecked = t.IsChecked != true;
        c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>A tile of a sheet: a symbol above a short label.</summary>
    Button SheetTile(string label, Geometry? icon, Action press, bool on = false)
    {
        var text = new TextBlock { Text = label, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
        var stack = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null) stack.Children.Add(new Icon { Data = icon, IconSize = 24, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(text);
        var b = new Button { Classes = { "ghost" }, Height = 72, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(3), Padding = new Thickness(4), HorizontalContentAlignment = HorizontalAlignment.Center, Content = stack };
        if (on) { b.Background = Res("BsAccentSoft"); b.Foreground = Res("BsAccent"); }
        Avalonia.Automation.AutomationProperties.SetName(b, label);
        b.Click += (_, _) => press();
        return b;
    }

    /// <summary>A tile for a button of the desktop bars: its symbol, its label and, when it is a switch, whether it is on.</summary>
    Button TileFor(Control source, string? label = null)
    {
        var icon = source.GetLogicalDescendants().OfType<Icon>().FirstOrDefault()?.Data ?? FallbackIcon(source);
        var on = source is ToggleButton { IsChecked: true };
        label ??= source == QuickMarkBtn ? L("Schnellmarkieren") : source == ModeTopBtn ? L("Modus") + ": " + ModeTopText.Text : null;
        return SheetTile(label ?? SpillLabel(source), icon, () => { ShowSheet(null); Dispatcher.UIThread.Post(() => PressButton(source), DispatcherPriority.Background); }, on);
    }

    /// <summary>A symbol for the buttons of the desktop bars that show only a word or a colour dot.</summary>
    Geometry FallbackIcon(Control source) => Geo(
        source == QuickMarkBtn ? "M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"
        : source == ModeTopBtn ? "M4 21v-7 M4 10V3 M12 21v-9 M12 8V3 M20 21v-5 M20 12V3 M1 14h6 M9 8h6 M17 16h6"
        : "M12 12m-3 0a3 3 0 1 0 6 0a3 3 0 1 0-6 0");

    /// <summary>The font size as a small bar at the bottom: the text above stays in sight while it changes.</summary>
    void ShowFontBar()
    {
        var value = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        void Show() => value.Text = $"{FontSlider.Value:0}";
        Button Step(string text, double size, int by)
        {
            var b = new Button { Content = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold }, Width = 72, Height = 48, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            b.Click += (_, _) => { FontSlider.Value = Math.Clamp(FontSlider.Value + by, FontSlider.Minimum, FontSlider.Maximum); Show(); };
            return b;
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(4, 0, 4, 4) };
        var minus = Step("A−", 14, -1); var plus = Step("A+", 20, 1);
        Grid.SetColumn(value, 1); Grid.SetColumn(plus, 2);
        row.Children.Add(minus); row.Children.Add(value); row.Children.Add(plus);
        Show();
        ShowSheet(L("Schriftgröße"), row);
        if (_sheetScrim is { } s) s.Background = Brushes.Transparent;   // the reading text stays bright behind it
    }

    static Control Heading(string text) => new TextBlock { Text = text, Classes = { "eyebrow" }, Margin = new Thickness(6, 10, 0, 2) };

    static Control Grid3(IEnumerable<Control> tiles)
    {
        var g = new UniformGrid { Columns = 3 };
        foreach (var t in tiles) g.Children.Add(t);
        return g;
    }

    // ---- the two sheets ---------------------------------------------------------------------------------------------------

    /// <summary>What can be done with this chapter: the toolbar of the desktop, with labels.</summary>
    void ShowToolsSheet()
    {
        Control[] tools =
        [
            ReadBtn, QuickReportBtn, QuickPericopeBtn, QuickSentenceBtn, QuickStatsBtn, QuickMemoryBtn,
        ];
        Control[] views = [ViewBtn, ViewsBtn, ParallelBtn, StudyBtn];
        Control[] listen = [SpeakBtn, AudioBtn];
        Control[] mark = [MarkBtn, QuickMarkBtn, BookmarkBtn];
        var size = new Control[]
        {
            SheetTile(L("Schriftgröße"), Geo("M4 7V4h16v3 M9 20h6 M12 4v16"), () => Dispatcher.UIThread.Post(ShowFontBar, DispatcherPriority.Background)),
        };
        var parts = new List<Control>
        {
            Heading(L("Dieses Kapitel")), Grid3(tools.Select(c => TileFor(c))),
            Heading(L("Ansicht")), Grid3(views.Select(c => TileFor(c)).Concat(size)),
            Heading(L("Markieren")), Grid3(mark.Select(c => TileFor(c))),
            Heading(L("Anhören")), Grid3(listen.Select(c => TileFor(c))),
        };
        // the device sync, when it is set up: at hand instead of deep in the settings
        if (_syncCfg.Key is not null)
            parts.InsertRange(0, [Grid3([SheetTile(L("Synchronisieren"), Geo("M21 12a9 9 0 0 1-15.5 6.2 M3 12a9 9 0 0 1 15.5-6.2 M18 2v4h-4 M6 22v-4h4"), () => { ShowSheet(null); _ = SyncNowAsync(); })])]);
        ShowSheet(L("Werkzeuge"), [.. parts]);
    }

    /// <summary>The areas of the program and what the top bar of the desktop holds.</summary>
    void ShowMoreSheet()
    {
        var areas = Rail.Children.OfType<Button>()
            .Where(b => b.IsVisible && (b.Tag is NavGroup g ? g.Name != K("Suchen") && g.Name != K("Merken") : b.Tag is "adv"))
            .Select(b =>
            {
                var icon = b.GetLogicalDescendants().OfType<Icon>().FirstOrDefault()?.Data;
                var label = b.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";
                return (Control)SheetTile(label, icon, () => { ShowSheet(null); Dispatcher.UIThread.Post(() => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), DispatcherPriority.Background); });
            }).ToList();
        Control[] app = [HomeBtn, LangBtn, VaultTopBtn, ModeTopBtn, MarksTopBtn, ThemeBtn, TodoTopBtn, FeedbackTopBtn];
        var status = new TextBlock
        {
            Text = string.Join("  ·  ", new[] { StatusText.Text, VersionChipText.Text, UpdateChipText.Text }.Where(s => !string.IsNullOrWhiteSpace(s))),
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 8, 6, 0),
        };
        var update = SheetTile(L("Nach Updates suchen"), Geo("M21 12a9 9 0 1 1-3-6.7 M21 4v5h-5"), () => { ShowSheet(null); Dispatcher.UIThread.Post(() => PressButton(VersionChip), DispatcherPriority.Background); });
        ShowSheet(L("Mehr"),
            Heading(L("Bereiche")), Grid3(areas),
            Heading(L("Programm")), Grid3(app.Where(c => c.IsVisible || _topHidden.Any(h => h.Item == c)).Select(c => TileFor(c, c == VaultTopBtn ? L("Vault") + ": " + VaultTopText.Text : c == LangBtn ? L("Sprache") : null)).Append(update)),
            status);
    }

    /// <summary>Opens the side panel on a group (search, collection …); a second press on the open group closes it again.</summary>
    void PhoneGoto(string groupName)
    {
        ShowSheet(null);
        var btn = Rail.Children.OfType<Button>().FirstOrDefault(b => b.Tag is NavGroup g && g.Name == K(groupName));
        if (btn is null) return;
        if (SidePanel.IsVisible && btn.Classes.Contains("active")) { SetPanelOpen(false); return; }
        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    // ---- state ------------------------------------------------------------------------------------------------------------

    /// <summary>The reading screen is shown (not the side panel, not a tool, not the start page).</summary>
    bool ReaderBrowsing() => !SidePanel.IsVisible && _activeWork is null && ReaderHost.IsVisible;

    void ApplyPhoneShell()
    {
        var on = _phone;
        if (on) EnsurePhoneShell();
        _shellOn = on && _pBar is not null;
        if (_pBar is null || _pNav is null) return;
        _pBar.IsVisible = _pNav.IsVisible = on;
        // the bars of the desktop keep their buttons (they hold the logic and are the places menus open at) but take no room
        foreach (var b in new Border[] { TopBar, TabBar, ReadBar, StatusBar })
        {
            b.MaxHeight = on ? 0 : double.PositiveInfinity;
            b.ClipToBounds = on;
            b.IsHitTestVisible = !on;
        }
        // the rail of the side panel gives way to the bottom bar and the "More" sheet
        if (Rail.Parent?.Parent is Border railBorder) railBorder.IsVisible = !on;
        if (!on) ShowSheet(null);
        RefreshPhoneShell();
    }

    void RefreshPhoneShell()
    {
    WrapOpenAreas();
        if (!_shellOn || _pTitleText is null || _pBack is null || _pTrans is null || _pPrev is null || _pNext is null || _pTitle is null) return;
        // a phone held across is only about 360 high: the bars shrink (the labels of the bottom bar go)
        var low = Bounds.Height < 480;
        if (_pBar is { } pb) pb.Height = low ? 44 : 52;
        foreach (var nb in _navBtns.Values) { nb.Height = low ? 40 : 58; if (nb.Content is StackPanel { Children: [_, TextBlock lb] }) lb.IsVisible = !low; }
        var reader = ReaderBrowsing();
        string title;
        if (SidePanel.IsVisible) title = SideGroupTitle();
        else if (_activeWork is not null && _windows.TryGetValue(_activeWork, out var w)) title = L(w.Title);
        else if (Home.IsVisible) title = L("Bibel");
        else title = BookBtnText();
        if (_pTitleText.Text != title) _pTitleText.Text = title;
        _pTrans.IsVisible = _pPrev.IsVisible = _pNext.IsVisible = reader;
        var tr = TranslationBox.SelectedItem is BibleStudy.Core.Models.TranslationInfo t ? t.ShortName : "";
        if (_pTransText!.Text != tr) _pTransText.Text = tr;
        // the back arrow: only where there is something to go back to
        _pBack.IsVisible = SidePanel.IsVisible || _activeWork is not null || !reader || BackBtn.IsEnabled;
        _pTitle.IsHitTestVisible = reader;
        var panelGroup = SidePanel.IsVisible ? Rail.Children.OfType<Button>().FirstOrDefault(b => b.Classes.Contains("active") && b.Tag is NavGroup) : null;
        void Mark(string key, bool active) { if (_navBtns.TryGetValue(key, out var b) && b.Classes.Contains("active") != active) b.Classes.Set("active", active); }
        Mark("read", !SidePanel.IsVisible);
        Mark("search", panelGroup?.Tag is NavGroup g1 && g1.Name == K("Suchen"));
        Mark("marks", panelGroup?.Tag is NavGroup g2 && g2.Name == K("Merken"));
        Mark("tools", false); Mark("more", false);
    }

    string BookBtnText()
    {
        var book = BookBox.SelectedItem is BibleStudy.Core.Models.BookInfo b ? b.CommonName : L("Buch");
        return book + " " + (int)(ChapterBox.Value ?? 1);
    }

    string SideGroupTitle()
    {
        var group = NavGroups.FirstOrDefault(g => g.Tabs.Any(t => t.Index == SideTabs.SelectedIndex));
        return group is null ? L("Suche") : RailName(group.Name);
    }
}

public partial class MainWindow
{
    string? _wrappedWork;
    int _wrappedSide = -1;
    bool _rowsWrapped;

    /// <summary>
    /// A row made for a desktop (a label, then a box 200 wide, then a button) is wider than a phone: it becomes a wrapping row, so what does not fit
    /// goes to the next line instead of out of the screen. Only plain rows are touched (a row the program holds by name keeps its panel).
    /// </summary>
    void WrapRows(Control root)
    {
        StackLabelGrids(root);
        // a line of text made for a wide window wraps instead of being cut (labels inside buttons, boxes and tab heads keep their one line)
        foreach (var tb in root.GetLogicalDescendants().OfType<TextBlock>())
            if (tb.TextWrapping == TextWrapping.NoWrap && tb.TextTrimming == TextTrimming.None && tb.GetType() == typeof(TextBlock)
                && !tb.GetLogicalAncestors().Any(a => a is Button or ComboBox or TabItem or ToggleButton or MenuItem))
                tb.TextWrapping = TextWrapping.Wrap;
        foreach (var sp in root.GetLogicalDescendants().OfType<StackPanel>().Where(s => s.Orientation == Orientation.Horizontal && s.Name is null && s.GetType() == typeof(StackPanel)).ToList())
        {
            if (sp.Children.Count < 2 || sp.Children.Any(c => c is Icon || c is TextBox { AcceptsReturn: true })) continue;
            if (sp.Parent is not Panel and not Border) continue;
            var wrap = new WrapPanel { ItemSpacing = Math.Max(4, sp.Spacing), LineSpacing = 6, Margin = sp.Margin, HorizontalAlignment = sp.HorizontalAlignment, VerticalAlignment = sp.VerticalAlignment, IsVisible = sp.IsVisible, Tag = sp.Tag };
            DockPanel.SetDock(wrap, DockPanel.GetDock(sp));
            var kids = sp.Children.ToList();
            sp.Children.Clear();
            foreach (var k in kids) wrap.Children.Add(k);
            if (sp.Parent is Panel p) { var at = p.Children.IndexOf(sp); p.Children[at] = wrap; if (Grid.GetColumn(sp) is var col && col != 0) Grid.SetColumn(wrap, col); if (Grid.GetRow(sp) is var row && row != 0) Grid.SetRow(wrap, row); }
            else if (sp.Parent is Border b) b.Child = wrap;
        }
    }

    void WrapOpenAreas()
    {
        if (!_shellOn) return;
        if (SidePanel.IsVisible && SideTabs.SelectedIndex != _wrappedSide && SideTabs.SelectedItem is TabItem { Content: Control sideContent }) { _wrappedSide = SideTabs.SelectedIndex; WrapRows(sideContent); }
        if (_activeWork == _wrappedWork) return;
        _wrappedWork = _activeWork;
        if (_activeWork is not null) WrapRows(WorkHost);
    }
}

public partial class MainWindow
{
    /// <summary>A grid of a label column (170 wide) and a column of a box: on a phone the box goes below its label, the column is the width of the screen.</summary>
    void StackLabelGrids(Control root)
    {
        foreach (var g in root.GetLogicalDescendants().OfType<Grid>().ToList())
        {
            if (g.ColumnDefinitions.Count != 2 || g.ColumnDefinitions[0].Width.GridUnitType == GridUnitType.Star || g.ColumnDefinitions[1].Width.GridUnitType != GridUnitType.Star) continue;
            if (g.ColumnDefinitions[0].Width.Value < 60 || g.Children.Any(c => Grid.GetColumnSpan(c) > 1 || Grid.GetRowSpan(c) > 1)) continue;
            var rows = g.Children.Count == 0 ? 0 : g.Children.Max(c => Grid.GetRow(c)) + 1;
            g.ColumnDefinitions = new ColumnDefinitions("*");
            g.RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", rows * 2)));
            foreach (var c in g.Children)
            {
                Grid.SetRow(c, Grid.GetRow(c) * 2 + Grid.GetColumn(c));
                Grid.SetColumn(c, 0);
                if (c is Control ctl && ctl.HorizontalAlignment == HorizontalAlignment.Stretch) ctl.HorizontalAlignment = HorizontalAlignment.Left;
            }
        }
    }
}

public partial class MainWindow
{
    bool _phoneStyled;

    /// <summary>
    /// Rules that hold only on a phone: the text of a button wraps instead of running out of it, a box shows "…" when its text is longer than
    /// the box, the hint text of an empty field too. On a desktop the window is wide enough for one line.
    /// </summary>
    void InstallPhoneStyles()
    {
        if (_phoneStyled || Application.Current is not { } app) return;
        _phoneStyled = true;
        var styles = new Avalonia.Styling.Styles
        {
            new Avalonia.Styling.Style(x => x.Is<Button>().Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Name("PART_ContentPresenter"))
            { Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.TextWrappingProperty, TextWrapping.Wrap) } },
            new Avalonia.Styling.Style(x => x.Is<Button>().Child().OfType<StackPanel>().Child().OfType<TextBlock>())
            { Setters = { new Avalonia.Styling.Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) } },
            new Avalonia.Styling.Style(x => x.Is<CheckBox>().Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
            { Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.TextWrappingProperty, TextWrapping.Wrap) } },
            new Avalonia.Styling.Style(x => x.OfType<ComboBox>().Descendant().OfType<TextBlock>())
            { Setters = { new Avalonia.Styling.Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis) } },
            new Avalonia.Styling.Style(x => x.OfType<TextBox>().Template().OfType<TextBlock>())
            { Setters = { new Avalonia.Styling.Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis) } },
            new Avalonia.Styling.Style(x => x.OfType<Expander>().Template().Descendant().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
            { Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.TextWrappingProperty, TextWrapping.Wrap) } },
        };
        app.Styles.Add(styles);
        // the short descriptions under the tabs of the side panel: on a phone the room belongs to the content
        SubHint.IsVisible = false;
        // the title of the documents and its two buttons do not fit in one row: the buttons go below it
        if (DocNewBtn.Parent is DockPanel docHead)
        {
            DockPanel.SetDock(DocNewBtn, Avalonia.Controls.Dock.Bottom); DockPanel.SetDock(DocFolderBtn, Avalonia.Controls.Dock.Bottom);
            DocNewBtn.HorizontalAlignment = DocFolderBtn.HorizontalAlignment = HorizontalAlignment.Left;
            DocFolderBtn.Margin = new Thickness(0, 6, 0, 0); DocNewBtn.Margin = new Thickness(0, 6, 0, 0);
            docHead.Children.Remove(DocNewBtn); docHead.Children.Remove(DocFolderBtn); docHead.Children.Remove(DocTrashBtn);
            DocTrashBtn.Margin = new Thickness(0, 6, 0, 0);
            var row = new WrapPanel { ItemSpacing = 8, Children = { DocFolderBtn, DocNewBtn, DocTrashBtn } };
            DockPanel.SetDock(row, Avalonia.Controls.Dock.Bottom);
            docHead.Children.Insert(0, row);
        }
    }
}

public partial class MainWindow
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, string> FullHints = new();
    static bool _hintsHooked;

    /// <summary>
    /// The hint in an empty field ("Notizen durchsuchen (Text oder Stelle)") is cut in the middle of a letter when the field is narrow: on a phone it is
    /// shortened to its first part (before a bracket, a comma or "z. B."), or ends in "…".
    /// </summary>
    static void HookShortHints()
    {
        if (_hintsHooked) return;
        _hintsHooked = true;
        Visual.BoundsProperty.Changed.AddClassHandler<TextBox>((box, _) => FitHint(box));
    }

    static void FitHint(TextBox box)
    {
        if (box.Bounds.Width <= 0) return;
        if (!FullHints.TryGetValue(box, out var full)) { full = box.PlaceholderText ?? ""; if (full.Length == 0) return; FullHints.AddOrUpdate(box, full); }
        var room = box.Bounds.Width - box.Padding.Left - box.Padding.Right - 14;
        var size = box.FontSize > 0 ? box.FontSize : 14;
        double Width(string s) => new Avalonia.Media.TextFormatting.TextLayout(s, new Typeface(box.FontFamily), size, null).WidthIncludingTrailingWhitespace;
        var text = full;
        if (Width(text) > room)
        {
            foreach (var cut in new[] { " (", ", ", " z. B.", " e.g.", " – ", ": " })
                if (text.IndexOf(cut, StringComparison.Ordinal) is > 3 and var i) text = text[..i] + " …";
            while (text.Length > 4 && Width(text) > room) text = text[..(text.Length - 3)].TrimEnd(' ', '…', ',') + " …";
        }
        if (box.PlaceholderText != text) box.PlaceholderText = text;
    }
}
