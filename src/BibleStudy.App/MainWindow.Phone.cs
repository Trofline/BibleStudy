using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

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
        if (!OperatingSystem.IsAndroid()) return;
        // a phone has no window to minimize, maximize or close
        MinBtn.IsVisible = MaxBtn.IsVisible = CloseBtn.IsVisible = false;
        SizeChanged += (_, _) => ApplyPhoneLayout();
#if ANDROID
        BackHandler = OnPhoneBack;
#endif
        ApplyPhoneLayout();
    }

    void ApplyPhoneLayout()
    {
        if (!OperatingSystem.IsAndroid() || Bounds.Width <= 0) return;
        var phone = Bounds.Width < PhoneWidth;
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
            TopBar.Height = phone ? double.NaN : 60;
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
                TabBar.Child = new ScrollViewer { Content = TabStrip, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            }
            TabBar.Padding = phone ? new Thickness(6, 4, 6, 0) : new Thickness(16, 6, 16, 0);
            // the settings: the list of sections on the right takes half of a phone; the sections are folded and found by scrolling
            if (SettingsScroll.Parent is Grid settings)
            {
                foreach (var c in settings.Children.Where(c => Grid.GetColumn(c) == 1)) c.IsVisible = !phone;
                settings.Margin = phone ? new Thickness(8, 10, 0, 8) : new Thickness(16, 14, 4, 12);
            }
        }
        if (phone)
        {
            // the panel takes the whole width: beside it the text would be one word wide
            SidePanel.MaxWidth = double.PositiveInfinity;
            SidePanel.Width = Bounds.Width;
        }
        else SidePanel.Width = Math.Clamp(_settings.SideWidth, 300, 760);
        FitTopBar();
    }

    /// <summary>On a phone a passage or a tool opened from the side panel is shown at once: the panel lies over it and goes away.</summary>
    void ClosePanelOnPhone()
    {
        if (_phone && SidePanel.IsVisible) SetPanelOpen(false);
    }

    /// <summary>The back key of the phone: first the side panel, then an open tool goes back to the text; at the text the app goes to the background.</summary>
    bool OnPhoneBack()
    {
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
            if (!_selVerses.Contains(verse)) SelectVerse(verse);
            ShowVerseMenu(verse, box);
        };
        timer.Start();
    }
}
