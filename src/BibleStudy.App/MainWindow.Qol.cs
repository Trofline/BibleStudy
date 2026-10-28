using Avalonia;
using Avalonia.Controls;
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
        TranslateMeaningsCheck.IsChecked = _settings.TranslateMeanings;
        TranslateMeaningsCheck.IsCheckedChanged += (_, _) => { _settings.TranslateMeanings = TranslateMeaningsCheck.IsChecked == true; SaveSettings(); };
        CommentariesCheck.IsChecked = _settings.ShowCommentaries;
        CommentariesCheck.IsCheckedChanged += (_, _) => { _settings.ShowCommentaries = CommentariesCheck.IsChecked == true; SaveSettings(); StatusText.Text = L("Die Einstellung gilt nach einem Neustart."); };
        JesusRedCheck.IsChecked = _settings.JesusRed; JustifyCheck.IsChecked = _settings.Justify;
        AnimationsCheck.IsChecked = _settings.Animations; AnimationsCheck.IsCheckedChanged += (_, _) => { _settings.Animations = AnimationsCheck.IsChecked == true; SaveSettings(); Classes.Set("noanim", !_settings.Animations); };
        Classes.Set("noanim", !_settings.Animations);
        HeadingCheck.IsChecked = _settings.ChapterHeading; ScrollReadCheck.IsChecked = _settings.ScrollMarksRead;
        LineLabel.Text = L("Zeilenabstand: {0:0.0}", _settings.LineSpacing); WidthLabel.Text = L("Textbreite: {0}", _settings.ReaderWidth);
        SpeakLabel.Text = L("Vorlesen: Geschwindigkeit {0:+0;-0;normal}", _settings.SpeakRate);
        BuildAccentPanel();
        ApplyAccent(_settings.Accent);

        async void Redraw() { if (_qolReady) { SaveSettings(); await ShowChapterAsync(_selected); } }
        FontBox.SelectionChanged += (_, _) => { if (FontBox.SelectedItem is string f) { _settings.ReaderFont = f; Redraw(); } };
        LineSlider.ValueChanged += (_, e) => { _settings.LineSpacing = Math.Round(e.NewValue, 1); LineLabel.Text = L("Zeilenabstand: {0:0.0}", _settings.LineSpacing); Redraw(); };
        WidthSlider.ValueChanged += (_, e) => { _settings.ReaderWidth = (int)e.NewValue; WidthLabel.Text = L("Textbreite: {0}", _settings.ReaderWidth); if (_qolReady) { FitReader(); SaveSettings(); } };
        VerseNumberBox.SelectionChanged += (_, _) => { _settings.VerseNumbers = VerseNumberModes[Math.Max(0, VerseNumberBox.SelectedIndex)]; Redraw(); };
        JesusRedCheck.IsCheckedChanged += (_, _) => { _settings.JesusRed = JesusRedCheck.IsChecked == true; Redraw(); };
        JustifyCheck.IsCheckedChanged += (_, _) => { _settings.Justify = JustifyCheck.IsChecked == true; Redraw(); };
        HeadingCheck.IsCheckedChanged += (_, _) => { _settings.ChapterHeading = HeadingCheck.IsChecked == true; Redraw(); };
        ScrollReadCheck.IsCheckedChanged += (_, _) => { _settings.ScrollMarksRead = ScrollReadCheck.IsChecked == true; SaveSettings(); };
        SpeakSlider.ValueChanged += (_, e) => { _settings.SpeakRate = (int)e.NewValue; SpeakLabel.Text = L("Vorlesen: Geschwindigkeit {0:+0;-0;normal}", _settings.SpeakRate); SaveSettings(); };
        ClearHistoryBtn.Click += (_, _) => { _settings.SearchHistory.Clear(); SaveSettings(); RefreshSearchHistory(); };
        ResetSettingsBtn.Click += (_, _) =>
        {
            FontSlider.Value = 22; LineSlider.Value = 1.7; WidthSlider.Value = 760; FontBox.SelectedItem = ReaderFonts[0]; VerseNumberBox.SelectedIndex = 0;
            JesusRedCheck.IsChecked = true; JustifyCheck.IsChecked = false; HeadingCheck.IsChecked = true; ApplyAccent("Messing"); BuildAccentPanel();
        };

        // reading: mark as read at the end of the chapter
        ReaderScroll.ScrollChanged += (_, _) =>
        {
            if (!_settings.ScrollMarksRead || _curBook == "" || _read.Contains((_curBook, _curChapter))) return;
            if (ReaderScroll.Extent.Height > ReaderScroll.Viewport.Height + 40 && ReaderScroll.Offset.Y + ReaderScroll.Viewport.Height >= ReaderScroll.Extent.Height - 24)
                SetRead(_curBook, [_curChapter], true);
        };

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

    void BuildAccentPanel()
    {
        AccentPanel.Children.Clear();
        foreach (var (name, light, dark) in Accents)
        {
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
            ToolTip.SetTip(dot, L(name));
            var captured = name;
            dot.PointerPressed += (_, _) => { ApplyAccent(captured); BuildAccentPanel(); };
            AccentPanel.Children.Add(dot);
        }
    }

    void ApplyAccent(string name)
    {
        var accent = Accents.FirstOrDefault(a => a.Name == name);
        if (accent.Name is null) accent = Accents[0];
        _settings.Accent = accent.Name;
        var res = Application.Current!.Resources;
        // Fluent derives sliders, check boxes and focus rings from the system accent
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var baseColor = Color.Parse(dark ? accent.Dark : accent.Light);
        res["SystemAccentColor"] = baseColor;
        res["SystemAccentColorLight1"] = Mix(baseColor, Colors.White, 0.15); res["SystemAccentColorLight2"] = Mix(baseColor, Colors.White, 0.3); res["SystemAccentColorLight3"] = Mix(baseColor, Colors.White, 0.45);
        res["SystemAccentColorDark1"] = Mix(baseColor, Colors.Black, 0.15); res["SystemAccentColorDark2"] = Mix(baseColor, Colors.Black, 0.3); res["SystemAccentColorDark3"] = Mix(baseColor, Colors.Black, 0.45);
        foreach (var (variant, hex, paper) in new[] { (ThemeVariant.Light, accent.Light, "#FFFCF6"), (ThemeVariant.Dark, accent.Dark, "#161A20") })
        {
            if (!res.ThemeDictionaries.TryGetValue(variant, out var provider) || provider is not ResourceDictionary d) continue;
            var c = Color.Parse(hex);
            var soft = Mix(Color.Parse(paper), c, variant == ThemeVariant.Dark ? 0.24 : 0.2);
            var solid = new SolidColorBrush(c); var softBrush = new SolidColorBrush(soft);
            d["BsAccent"] = solid; d["BsVerseNumber"] = solid; d["BsAccentSoft"] = softBrush;
            d["TabItemHeaderForegroundSelected"] = solid; d["TabItemHeaderForegroundSelectedPointerOver"] = solid;
            d["TabItemHeaderBackgroundSelected"] = softBrush; d["TabItemHeaderBackgroundSelectedPointerOver"] = softBrush;
        }
        SaveSettings();
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
        var tr = (TranslationBox.SelectedItem as TranslationInfo)?.ShortName ?? "";
        await clipboard.SetTextAsync($"„{VerseTextOf(v).Trim()}“ – {Label(r)} ({tr})");
        StatusText.Text = L("{0} kopiert.", Label(r));
    }

    void ToggleFocusMode()
    {
        _settings.FocusMode = !_settings.FocusMode;
        var focus = _settings.FocusMode;
        TopBar.IsVisible = !focus; TabBar.IsVisible = !focus; UpdateReadBar();
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
