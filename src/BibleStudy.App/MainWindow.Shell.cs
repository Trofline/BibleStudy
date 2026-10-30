using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>How the program starts and ends: icon, system tray, autostart, window position, focus mode.</summary>
public partial class MainWindow
{
    TrayIcon? _tray;
    (WindowState State, bool SideOpen)? _preFocus;

    /// <summary>The book logo as a bitmap, used for the window, the taskbar and the tray.</summary>
    static string LogoAsset(string logo) => logo == "Circle" ? "app-circle.png" : "app.png";

    static WindowIcon MakeIcon(string logo)
    {
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://BibleStudy.App/Assets/" + LogoAsset(logo)));
            return new WindowIcon(new Bitmap(s));
        }
        catch { /* fall back to the drawn icon below */ }
        var rtb = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#E3B65F")), null, new RoundedRect(new Rect(0, 0, 64, 64), 16));
            var pen = new Pen(new SolidColorBrush(Color.Parse("#161A20")), 4.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var scale = Matrix.CreateScale(1.6, 1.6) * Matrix.CreateTranslation(13, 13);
            using var _ = ctx.PushTransform(scale);
            ctx.DrawGeometry(null, pen, Geometry.Parse("M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z"));
        }
        return new WindowIcon(rtb);
    }

    void InitShell()
    {
        Icon = MakeIcon(_settings.Logo);
        // settings controls
        StartWithWindowsCheck.IsChecked = AutostartEnabled();
        TrayCheck.IsChecked = _settings.Tray; CloseToTrayCheck.IsChecked = _settings.CloseToTray;
        RememberWindowCheck.IsChecked = _settings.RememberWindow; RestoreWindowsCheck.IsChecked = _settings.RestoreWindows;
        StartModeBox.SelectedIndex = _settings.StartMode switch { "Maximized" => 1, "Minimized" => 2, _ => 0 };
        StartWithWindowsCheck.IsCheckedChanged += (_, _) => SetAutostart(StartWithWindowsCheck.IsChecked == true);
        TrayCheck.IsCheckedChanged += (_, _) => { _settings.Tray = TrayCheck.IsChecked == true; SaveSettings(); UpdateTray(); };
        CloseToTrayCheck.IsCheckedChanged += (_, _) => { _settings.CloseToTray = CloseToTrayCheck.IsChecked == true; SaveSettings(); if (_settings.CloseToTray && !_settings.Tray) TrayCheck.IsChecked = true; };
        RememberWindowCheck.IsCheckedChanged += (_, _) => { _settings.RememberWindow = RememberWindowCheck.IsChecked == true; SaveSettings(); };
        RestoreWindowsCheck.IsCheckedChanged += (_, _) => { _settings.RestoreWindows = RestoreWindowsCheck.IsChecked == true; SaveSettings(); };
        StartModeBox.SelectionChanged += (_, _) => { _settings.StartMode = StartModeBox.SelectedIndex switch { 1 => "Maximized", 2 => "Minimized", _ => "Normal" }; SaveSettings(); };
        UpdateTray();

        // custom title bar: drag, double click = maximize, own caption buttons
        TopBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(TopBar).Properties.IsLeftButtonPressed || e.Source is not (Border or StackPanel or DockPanel or TextBlock or Avalonia.Controls.Shapes.Path)) return;
            if (e.ClickCount == 2) ToggleMaximize(); else BeginMoveDrag(e);
        };
        MinBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        MaxBtn.Click += (_, _) => ToggleMaximize();
        CloseBtn.Click += (_, _) => Close();

        // window position and start mode
        if (_settings.RememberWindow && _settings.WindowBounds is { } b && Screens.All.Any(s => s.WorkingArea.Contains(new PixelPoint(b.X + 40, b.Y + 20))))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(b.X, b.Y); Width = Math.Max(MinWidth, b.W); Height = Math.Max(MinHeight, b.H);
            if (b.Maximized) WindowState = WindowState.Maximized;
        }
        if (_settings.StartMode == "Maximized") WindowState = WindowState.Maximized;
        var startupDone = false;
        Opened += (_, _) =>
        {
            if (startupDone) return;   // Opened fires on every Show(), e.g. when coming back from the tray
            startupDone = true;
            if (_settings.StartMode == "Minimized")
            {
                // hiding inside Opened leaves the window unable to show again; wait until it is fully up
                if (_settings.Tray && _tray is not null) DispatcherTimer.RunOnce(Hide, TimeSpan.FromMilliseconds(400));
                else WindowState = WindowState.Minimized;
            }
            if (_settings.RestoreWindows && _settings.LastLayout is { Windows.Count: > 0 } last)
                Dispatcher.UIThread.Post(() => OpenLayoutWindows(last), DispatcherPriority.Background);
        };
        Closing += OnWindowClosing;
    }

    void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        var toTray = !_exiting && _settings.CloseToTray && _tray is not null && _settings.Tray;
        if (_closeAnimating) { e.Cancel = true; return; }
        if (!_closeAnimated && _settings.Animations && IsVisible && WindowState != WindowState.Minimized && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true; _closeAnimating = true;
            Dispatcher.UIThread.Post(async () =>
            {
                await PlayCloseAnimationAsync();
                _closeAnimating = false;
                if (toTray) { Hide(); ResetAfterCloseAnimation(); }
                else { _closeAnimated = true; Close(); }
            });
            return;
        }
        if (!_exiting && _settings.CloseToTray && _tray is not null && _settings.Tray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        // remember where and how the program was left (not the focus-mode state)
        var maximized = _preFocus is { } p ? p.State == WindowState.Maximized : WindowState == WindowState.Maximized;
        if (WindowState != WindowState.Minimized && !_settings.FocusMode && WindowState == WindowState.Normal)
            _settings.WindowBounds = new WindowBounds(Position.X, Position.Y, (int)Width, (int)Height, false);
        else if (_settings.WindowBounds is { } old) _settings.WindowBounds = old with { Maximized = maximized };
        if (maximized && _settings.WindowBounds is { } wb) _settings.WindowBounds = wb with { Maximized = true };
        // what was typed in the last seconds (the autosave runs every few seconds) is kept
        try { AutoSaveDocument(); SaveNoteDraft(); } catch (Exception ex) { ErrorLog.Write(ex); }
        _settings.LastLayout = CaptureLayout();
        if (_settings.FocusMode && _preFocus is { } pre) _settings.LastLayout.SideOpen = pre.SideOpen;
        CloseAllDetached();
        SaveSettings();
        _tray?.Dispose();
    }

    // ---- tray ---------------------------------------------------------------------------------------------------------

    void RestoreFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void UpdateTray()
    {
        if (!_settings.Tray)
        {
            if (_tray is not null) { _tray.IsVisible = false; _tray.Dispose(); _tray = null; TrayIcon.SetIcons(Application.Current!, []); }
            return;
        }
        if (_tray is not null) return;

        var menu = new NativeMenu();
        var open = new NativeMenuItem(L("BibleStudy öffnen")); open.Click += (_, _) => RestoreFromTray();
        var quit = new NativeMenuItem(L("Beenden")); quit.Click += (_, _) => { _exiting = true; Close(); };
        menu.Items.Add(open); menu.Items.Add(new NativeMenuItemSeparator()); menu.Items.Add(quit);
        _tray = new TrayIcon { Icon = Icon, ToolTipText = "BibleStudy", Menu = menu, IsVisible = true };
        _tray.Clicked += (_, _) => RestoreFromTray();
        TrayIcon.SetIcons(Application.Current!, [_tray]);
    }

    // ---- autostart (Windows) --------------------------------------------------------------------------------------------

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static bool AutostartEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("BibleStudy") is string; } catch { return false; }
    }

    void SetAutostart(bool on)
    {
        if (!OperatingSystem.IsWindows()) { StatusText.Text = L("Autostart wird derzeit nur unter Windows unterstützt."); return; }
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (on && Environment.ProcessPath is { } exe && !exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)) k?.SetValue("BibleStudy", $"\"{exe}\"");
            else if (on) { StatusText.Text = L("Autostart geht nur mit der fertig gebauten Programmdatei (BibleStudy.exe), nicht beim Start über „dotnet run“."); StartWithWindowsCheck.IsChecked = false; }
            else k?.DeleteValue("BibleStudy", throwOnMissingValue: false);
        }
        catch (Exception ex) { StatusText.Text = L("Autostart nicht änderbar: ") + ex.Message; }
    }
}
