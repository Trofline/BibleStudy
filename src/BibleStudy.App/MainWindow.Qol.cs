using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>Reader appearance settings, accent colors, quick jump, keyboard shortcuts and other comfort features.</summary>
public partial class MainWindow
{
    static readonly string[] ReaderFonts = ["Crimson Text", "Georgia", "Cambria", "Palatino Linotype", "Segoe UI", "Verdana"];
    static readonly string[] HeadingModes = ["All", "Smart", "Mine", "Translation", "None"];
    static readonly string[] VerseNumberModes = ["Small", "Normal", "Hidden"];

    // name, accent in light theme, accent in dark theme
    static readonly (string Name, string Light, string Dark)[] Accents =
    [
        (K("Messing"), "#8A4F0F", "#E3B65F"), (K("Saphir"), "#2457A6", "#7DB2F2"), (K("Smaragd"), "#1B7A57", "#5BCB9C"),
        (K("Granat"), "#A02F44", "#F0889A"), (K("Amethyst"), "#6A47B8", "#B69CF6"), (K("Graphit"), "#3E4A59", "#B3BDCB"),
    ];

    bool _qolReady;

    void InitQol()
    {
        // appearance
        FontBox.ItemsSource = ReaderFonts;
        FontBox.SelectedItem = ReaderFonts.Contains(_settings.ReaderFont) ? _settings.ReaderFont : ReaderFonts[0];
        LineSlider.Value = _settings.LineSpacing; WidthSlider.Value = _settings.ReaderWidth; SpeakSlider.Value = _settings.SpeakRate;
        VerseNumberBox.SelectedIndex = Math.Max(0, Array.IndexOf(VerseNumberModes, _settings.VerseNumbers));
        HeadingModeBox.SelectedIndex = Math.Max(0, Array.IndexOf(HeadingModes, _settings.HeadingMode));
        TranslateMeaningsCheck.IsChecked = _settings.TranslateMeanings;
        TranslateMeaningsCheck.IsCheckedChanged += (_, _) => { _settings.TranslateMeanings = TranslateMeaningsCheck.IsChecked == true; SaveSettings(); };
        CommentariesCheck.IsChecked = _settings.ShowCommentaries;
        CommentariesCheck.IsCheckedChanged += (_, _) => { _settings.ShowCommentaries = CommentariesCheck.IsChecked == true; SaveSettings(); ApplyCommentariesSetting(_settings.ShowCommentaries); };
        CompactLookCheck.IsChecked = _settings.CompactLook;
        CompactLookCheck.IsCheckedChanged += (_, _) => { _settings.CompactLook = CompactLookCheck.IsChecked == true; SaveSettings(); StatusText.Text = L("Die Einstellung gilt nach einem Neustart."); };
        JesusRedCheck.IsChecked = _settings.JesusRed; JustifyCheck.IsChecked = _settings.Justify;
        AnimationsCheck.IsChecked = _settings.Animations; AnimationsCheck.IsCheckedChanged += (_, _) => { _settings.Animations = AnimationsCheck.IsChecked == true; SaveSettings(); Classes.Set("noanim", !_settings.Animations); };
        Classes.Set("noanim", !_settings.Animations);
        AnimLevelBox.SelectedIndex = Math.Clamp(_settings.AnimationLevel, 0, 1);
        AnimLevelBox.SelectionChanged += (_, _) => { if (AnimLevelBox.SelectedIndex >= 0) { _settings.AnimationLevel = AnimLevelBox.SelectedIndex; SaveSettings(); } };
        CloseAnimBox.ItemsSource = new[] { L("Weich (wegzoomen)"), L("Unscharf (verschwimmen)"), L("Dezent (nur ausblenden)"), L("Röhrenfernseher"), L("Keine") };
        CloseAnimBox.SelectedIndex = Math.Max(0, Array.IndexOf(CloseStyles, CloseStyle));
        CloseAnimBox.SelectionChanged += (_, _) => { if (CloseAnimBox.SelectedIndex >= 0) { _settings.CloseAnimation = CloseStyles[CloseAnimBox.SelectedIndex]; SaveSettings(); } };
        HeadingCheck.IsChecked = _settings.ChapterHeading; ScrollReadCheck.IsChecked = _settings.ScrollMarksRead;
        LineLabel.Text = L("Zeilenabstand: {0:0.0}", _settings.LineSpacing); WidthLabel.Text = L("Textbreite: {0}", _settings.ReaderWidth);
        SpeakLabel.Text = L("Vorlesen: Geschwindigkeit {0:+0;-0;normal}", _settings.SpeakRate);
        BuildAccentPanel();
        ApplyAccent(_settings.Accent);

        async void Redraw() { if (_qolReady) { SaveSettings(); await RedrawChapterAsync(); } }
        FontBox.SelectionChanged += (_, _) => { if (FontBox.SelectedItem is string f) { _settings.ReaderFont = f; Redraw(); } };
        LineSlider.ValueChanged += (_, e) => { _settings.LineSpacing = Math.Round(e.NewValue, 1); LineLabel.Text = L("Zeilenabstand: {0:0.0}", _settings.LineSpacing); Redraw(); };
        WidthSlider.ValueChanged += (_, e) => { _settings.ReaderWidth = (int)e.NewValue; WidthLabel.Text = L("Textbreite: {0}", _settings.ReaderWidth); if (_qolReady) { FitReader(); SaveSettings(); } };
        HeadingModeBox.SelectionChanged += (_, _) => { _settings.HeadingMode = HeadingModes[Math.Max(0, HeadingModeBox.SelectedIndex)]; Redraw(); };
        VerseNumberBox.SelectionChanged += (_, _) => { _settings.VerseNumbers = VerseNumberModes[Math.Max(0, VerseNumberBox.SelectedIndex)]; Redraw(); };
        JesusRedCheck.IsCheckedChanged += (_, _) => { _settings.JesusRed = JesusRedCheck.IsChecked == true; Redraw(); };
        // own color for the words of Jesus: fixed choices, the first one ("" ) is the program default that follows the theme
        void BuildJesusColors()
        {
            JesusColorPanel.Children.Clear();
            var fixedColors = new[] { "", "#C62828", "#8B1A1A", "#D9480F", "#B8860B", "#2E7D32", "#1565C0", "#6A1B9A", "#6D4C41" };
            foreach (var hex in fixedColors.Concat(_settings.CustomJesusColors))
            {
                var chosen = hex == _settings.JesusColor;
                var dot = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new(13), Margin = new(0, 4, 8, 4), Cursor = new Cursor(StandardCursorType.Hand),
                    BorderThickness = new(chosen ? 3 : 1), BorderBrush = chosen ? Res("BsText") : Res("BsBorder"),
                };
                if (hex.Length == 0) dot.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsJesus")); else dot.Background = new SolidColorBrush(Color.Parse(hex));
                var captured = hex;
                dot.PointerPressed += (_, _) => { _settings.JesusColor = captured; BuildJesusColors(); Redraw(); };
                JesusColorPanel.Children.Add(dot);
            }
            var add = new Border
            {
                Width = 26, Height = 26, CornerRadius = new(13), Margin = new(0, 4, 8, 4), Cursor = new Cursor(StandardCursorType.Hand), BorderThickness = new(1), BorderBrush = Res("BsBorder"),
                Child = new TextBlock { Text = "+", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 16 },
            };
            ToolTip.SetTip(add, L("Eigene Farbe wählen"));
            add.PointerPressed += (_, _) => ShowCustomColor(Color.TryParse(_settings.JesusColor, out var cur) ? cur : Color.Parse("#C62828"), hex =>
            {
                if (!fixedColors.Contains(hex, StringComparer.OrdinalIgnoreCase) && !_settings.CustomJesusColors.Contains(hex, StringComparer.OrdinalIgnoreCase))
                {
                    _settings.CustomJesusColors.Add(hex);
                    if (_settings.CustomJesusColors.Count > 6) _settings.CustomJesusColors.RemoveAt(0);
                }
                _settings.JesusColor = hex; BuildJesusColors(); Redraw();
            });
            JesusColorPanel.Children.Add(add);
        }
        BuildJesusColors();
        JustifyCheck.IsCheckedChanged += (_, _) => { _settings.Justify = JustifyCheck.IsChecked == true; Redraw(); };
        HeadingCheck.IsCheckedChanged += (_, _) => { _settings.ChapterHeading = HeadingCheck.IsChecked == true; Redraw(); };
        ScrollReadCheck.IsCheckedChanged += (_, _) => { _settings.ScrollMarksRead = ScrollReadCheck.IsChecked == true; SaveSettings(); };
        SpeakSlider.ValueChanged += (_, e) => { _settings.SpeakRate = (int)e.NewValue; SpeakLabel.Text = L("Vorlesen: Geschwindigkeit {0:+0;-0;normal}", _settings.SpeakRate); SaveSettings(); };
        ClearHistoryBtn.Click += (_, _) => { _settings.SearchHistory.Clear(); SaveSettings(); RefreshSearchHistory(); };
        ResetSettingsBtn.Click += (_, _) =>
        {
            FontSlider.Value = 22; LineSlider.Value = 1.7; WidthSlider.Value = 760; FontBox.SelectedItem = ReaderFonts[0]; VerseNumberBox.SelectedIndex = 0; HeadingModeBox.SelectedIndex = 0;
            JesusRedCheck.IsChecked = true; _settings.JesusColor = ""; BuildJesusColors(); JustifyCheck.IsChecked = false; HeadingCheck.IsChecked = true; ApplyAccent("Messing"); BuildAccentPanel();
        };

        // reading: mark as read at the end of the chapter
        ReaderScroll.ScrollChanged += (_, _) =>
        {
            if (!_settings.ScrollMarksRead || _curBook == "" || _read.Contains((_curBook, _curChapter))) return;
            if (ReaderScroll.Extent.Height > ReaderScroll.Viewport.Height + 40 && ReaderScroll.Offset.Y + ReaderScroll.Viewport.Height >= ReaderScroll.Extent.Height - 24)
                SetRead(_curBook, [_curChapter], true);
        };

        // focus mode belongs to one session: a program closed in it starts normal, and the reading bar must not stay hidden then
        _settings.FocusMode = false;

        // side panel remembers its tab
        if (_settings.SideTab >= 0 && _settings.SideTab < SideTabs.ItemCount) SideTabs.SelectedIndex = _settings.SideTab;
        SideTabs.SelectionChanged += (_, e) => { if (e.Source == SideTabs) { _settings.SideTab = SideTabs.SelectedIndex; } };

        // quick jump and shortcuts
        InitPalette();
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        ReaderHost.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) UpdateReadBar(); };
        ActualThemeVariantChanged += (_, _) => ApplyAccent(_settings.Accent);
        _qolReady = true;
    }

    // ---- accent color -------------------------------------------------------------------------------------------------

    /// <summary>The colours of a ready-made accent, or of an own one ("#RRGGBB": made readable on paper and on the dark background).</summary>
    static (string Name, string Light, string Dark) AccentOf(string name)
    {
        var known = Accents.FirstOrDefault(a => a.Name == name);
        if (known.Name is not null) return known;
        if (name.StartsWith('#') && Color.TryParse(name, out var c))
            return (name, HexOf(Readable(c, Color.Parse("#FFFCF6"), Colors.Black)), HexOf(Readable(c, Color.Parse("#161A20"), Colors.White)));
        return Accents[0];
    }

    static double Luminance(Color c)
    {
        static double F(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * F(c.R) + 0.7152 * F(c.G) + 0.0722 * F(c.B);
    }

    /// <summary>The colour, moved towards <paramref name="toward"/> just far enough that text in it can be read on <paramref name="background"/> (contrast 4.5).</summary>
    static Color Readable(Color c, Color background, Color toward)
    {
        for (var t = 0.0; t <= 1.0001; t += 0.04)
        {
            var m = Mix(c, toward, t);
            var l1 = Luminance(m) + 0.05; var l2 = Luminance(background) + 0.05;
            if ((Math.Max(l1, l2) / Math.Min(l1, l2)) >= 4.5) return m;
        }
        return toward;
    }

    void BuildAccentPanel()
    {
        AccentPanel.Children.Clear();
        foreach (var (name, light, dark) in Accents.Concat(_settings.CustomAccents.Where(h => Color.TryParse(h, out _)).Select(AccentOf)))
        {
            var custom = name.StartsWith('#');
            var dot = new Border
            {
                Width = 26, Height = 26, CornerRadius = new(13), Margin = new(0, 0, 8, 0), Cursor = new Cursor(StandardCursorType.Hand),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse(light), 0.5), new GradientStop(Color.Parse(dark), 0.5) },
                },
                BorderThickness = new(name == _settings.Accent ? 3 : 1), BorderBrush = name == _settings.Accent ? Res("BsText") : Res("BsBorder"),
            };
            ToolTip.SetTip(dot, custom ? name : L(name));
            var captured = name;
            dot.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(dot).Properties.IsRightButtonPressed) return;
                ApplyAccent(captured); BuildAccentPanel();
            };
            if (custom)
            {
                var remove = new MenuItem { Header = L("Entfernen") };
                remove.Click += (_, _) =>
                {
                    _settings.CustomAccents.Remove(captured);
                    if (_settings.Accent == captured) ApplyAccent("Messing");
                    SaveSettings(); BuildAccentPanel();
                };
                dot.ContextMenu = new ContextMenu { ItemsSource = new[] { remove } };
            }
            AccentPanel.Children.Add(dot);
        }
        var add = new Border
        {
            Width = 26, Height = 26, CornerRadius = new(13), Margin = new(0, 0, 8, 0), Cursor = new Cursor(StandardCursorType.Hand),
            BorderThickness = new(1), BorderBrush = Res("BsBorder"),
            Child = new TextBlock { Text = "+", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 16 },
        };
        ToolTip.SetTip(add, L("Eigene Akzentfarbe wählen"));
        add.PointerPressed += (_, _) => ShowCustomAccent();
        AccentPanel.Children.Add(add);
    }

    /// <summary>A window with a colour picker; the program shows the colour at once and keeps the old one when the user cancels.</summary>
    void ShowCustomAccent()
    {
        var before = _settings.Accent;
        var start = Color.TryParse(before, out var cur) ? cur : Color.Parse(AccentOf(before).Light);
        var picker = new ColorView { Color = start, IsAlphaVisible = false };
        var save = new Button { Content = L("Speichern"), Classes = { "primary" }, Margin = new(8, 0, 0, 0) };
        var cancel = new Button { Content = L("Abbrechen") };
        var hint = new TextBlock { Text = L("Die Farbe wird für Hell und Dunkel so angepasst, dass Schrift darauf lesbar bleibt."), Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var stack = new StackPanel
        {
            Spacing = 10, Margin = new(20),
            Children =
            {
                new TextBlock { Text = L("Eigene Akzentfarbe wählen"), Classes = { "h2" } }, picker, hint,
                new WrapPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { cancel, save } },
            },
        };
        var win = new Window { Title = L("Eigene Akzentfarbe wählen"), Width = 420, SizeToContent = SizeToContent.Height, CanResize = false, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = stack };
        var saved = false;
        var chosen = start;
        picker.ColorChanged += (_, e) => { chosen = e.NewColor; ApplyAccent(HexOf(chosen), persist: false); };
        save.Click += (_, _) =>
        {
            saved = true;
            var hex = HexOf(chosen);
            if (!_settings.CustomAccents.Contains(hex)) _settings.CustomAccents.Add(hex);
            if (_settings.CustomAccents.Count > 8) _settings.CustomAccents.RemoveAt(0);
            ApplyAccent(hex); BuildAccentPanel(); win.Close();
        };
        cancel.Click += (_, _) => win.Close();
        win.Closed += (_, _) => { if (!saved) { ApplyAccent(before); BuildAccentPanel(); } };
        win.Show(this);
    }

    static string HexOf(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Sets the accent colour; with <paramref name="persist"/> false it is only shown (while the picker is open).</summary>
    void ApplyAccent(string name, bool persist = true)
    {
        var accent = AccentOf(name);
        if (persist) _settings.Accent = accent.Name;
        var res = Application.Current!.Resources;
        // Fluent derives sliders, check boxes and focus rings from the system accent
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var baseColor = Color.Parse(dark ? accent.Dark : accent.Light);
        res["SystemAccentColor"] = baseColor;
        res["SystemAccentColorLight1"] = Mix(baseColor, Colors.White, 0.15); res["SystemAccentColorLight2"] = Mix(baseColor, Colors.White, 0.3); res["SystemAccentColorLight3"] = Mix(baseColor, Colors.White, 0.45);
        res["SystemAccentColorDark1"] = Mix(baseColor, Colors.Black, 0.15); res["SystemAccentColorDark2"] = Mix(baseColor, Colors.Black, 0.3); res["SystemAccentColorDark3"] = Mix(baseColor, Colors.Black, 0.45);
        foreach (var (variant, hex, paper) in new[] { (ThemeVariant.Light, accent.Light, "#FFFCF6"), (ThemeVariant.Dark, accent.Dark, CurrentDark.Surface) })
        {
            if (!res.ThemeDictionaries.TryGetValue(variant, out var provider) || provider is not ResourceDictionary d) continue;
            var c = Color.Parse(hex);
            var soft = Mix(Color.Parse(paper), c, variant == ThemeVariant.Dark ? 0.24 : 0.2);
            var solid = new SolidColorBrush(c); var softBrush = new SolidColorBrush(soft);
            d["BsAccent"] = solid; d["BsVerseNumber"] = solid; d["BsAccentSoft"] = softBrush;
            d["TabItemHeaderForegroundSelected"] = solid; d["TabItemHeaderForegroundSelectedPointerOver"] = solid;
            d["TabItemHeaderBackgroundSelected"] = softBrush; d["TabItemHeaderBackgroundSelectedPointerOver"] = softBrush;
        }
        if (persist) SaveSettings();
    }

    static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    // ---- quick jump ---------------------------------------------------------------------------------------------------

    async Task QuickJumpAsync(string text)
    {
        if (text.Length == 0) return;
        QuickJumpBox.Text = "";
        if (PassageParser.Parse(text, out var single) is { } p && single) { await GoToAsync(p[0].Book, p[0].Chapter, p[0].FromVerse); Focus(); return; }
        if (DocumentLinks.BookId(text) is { } book && _books.Any(b => b.Id == book)) { await GoToAsync(book, 1); Focus(); return; }
        SetPanelOpen(true);
        SideTabs.SelectedIndex = 0;
        SearchBox.Text = text;
        await SearchAsync();
    }

    // ---- shortcuts ----------------------------------------------------------------------------------------------------

    async Task CopyVerseAsync()
    {
        if (_selected is not int v || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
        var r = new VerseRef(_curBook, _curChapter, v);
        var info = TranslationBox.SelectedItem as TranslationInfo;
        var tr = info?.ShortName ?? "";
        // source with the license: the address of the license text unless the translation is public domain
        var license = info is { License.PublicDomain: false, LicenseUrl: { Length: > 0 } url } ? "\n" + url : "";
        await clipboard.SetTextAsync($"{Loc.Quote(VerseTextOf(v).Trim())} – {Label(r)} ({tr}){license}");
        StatusText.Text = L("{0} kopiert.", Label(r));
    }

    void ToggleFocusMode()
    {
        _settings.FocusMode = !_settings.FocusMode;
        var focus = _settings.FocusMode;
        TopBar.IsVisible = !focus; TabBar.IsVisible = !focus; StatusBar.IsVisible = !focus; FocusBar.IsVisible = focus; UpdateReadBar();
        if (focus)
        {
            _preFocus = (WindowState, SidePanel.IsVisible);
            SidePanel.IsVisible = SideSplitter.IsVisible = false; WindowState = WindowState.FullScreen;
        }
        else
        {
            var (state, sideOpen) = _preFocus ?? (WindowState.Normal, _settings.SideOpen);
            _preFocus = null;
            WindowState = state;
            SetPanelOpen(sideOpen);
        }
    }

    void UpdateTitle(string book, int chapter, string translation) => Title = L("{0} {1} · {2} – BibleStudy", book, chapter, translation);
}
