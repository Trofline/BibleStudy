using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BibleStudy.Core;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ErrorLog.Install();
#if !ANDROID
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => KeepOnScreen(w));
        // every submenu shows the same "›" (the theme's own arrow is hidden in App.axaml, it was cut off at the edge)
        Control.LoadedEvent.AddClassHandler<MenuItem>((m, _) => { if (m.ItemCount > 0 && m.Header is string s) m.Header = MainWindow.SubHeader(s); });
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = Environment.GetCommandLineArgs();
            var path = Path.Combine(AppPaths.Root, "settings.json");
            try { Loc.Use(AppSettings.Load(path).Language); } catch { Loc.Use(null); }
            if (args.Contains("--uninstall")) desktop.MainWindow = new UninstallWindow(args);
            else
            {
                AppSettings st;
                try { st = AppSettings.Load(path); }
                catch (IOException)
                {
                    // held by another program (sync, virus scanner) for longer: starting with empty settings would save them over the real ones
                    desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var close = new Button { Content = L("Beenden"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
                    var w = new Window
                    {
                        Title = "BibleStudy", Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, CanResize = false,
                        Content = new StackPanel
                        {
                            Margin = new Thickness(20), Spacing = 14,
                            Children = { new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Text = L("Die Einstellungen sind gerade von einem anderen Programm gesperrt (etwa Sync oder Virenscanner). Bitte gleich neu starten; deine Einstellungen bleiben erhalten.") }, close },
                        },
                    };
                    close.Click += (_, _) => w.Close();
                    w.Closed += (_, _) => desktop.Shutdown();
                    desktop.MainWindow = w;
                    w.Show();
                    base.OnFrameworkInitializationCompleted();
                    return;
                }
                catch { st = new(); }

                if (st.TermsAccepted.StartsWith(TermsWindow.Version + "|", StringComparison.Ordinal)) StartMain(desktop, st);
                else
                {
                    // The terms come first and alone: the program itself is not loaded (not even its start window) until they are accepted;
                    // declining or closing the window ends the program.
                    desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var terms = new TermsWindow();
                    terms.Closed += (_, _) =>
                    {
                        if (!terms.Accepted) { desktop.Shutdown(); return; }
                        st.TermsAccepted = $"{TermsWindow.Version}|{DateTime.Now:yyyy-MM-dd}";
                        try { st.Save(path); } catch { /* asked again next time */ }
                        desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
                        StartMain(desktop, st);
                    };
                    desktop.MainWindow = terms;
                    terms.Show();
                    DeltaFinish.Confirm();   // a new version asks about its terms first: it did start
                }
            }
        }
#endif

#if ANDROID
        if (ApplicationLifetime is ISingleViewApplicationLifetime single) StartAndroid(single);
#endif
        base.OnFrameworkInitializationCompleted();
    }

#if ANDROID
    static MainWindow? _main;

    /// <summary>
    /// Android: one view, no windows. The terms come first and alone, as on a desktop; then the program. The view of the activity is
    /// set once (Android does not take a second one well); what it shows is changed inside it.
    /// </summary>
    static void StartAndroid(ISingleViewApplicationLifetime single)
    {
        var host = new ContentControl();
        single.MainView = host;
        var path = Path.Combine(AppPaths.Root, "settings.json");
        AppSettings st;
        try { st = AppSettings.Load(path); } catch { st = new(); }
        try { Loc.Use(st.Language); } catch { Loc.Use(null); }
        if (st.TermsAccepted.StartsWith(TermsWindow.Version + "|", StringComparison.Ordinal)) { StartMainAndroid(host); return; }
        var terms = new TermsWindow { IsRootView = true };
        terms.Closed += (_, _) =>
        {
            if (!terms.Accepted) { Android.OS.Process.KillProcess(Android.OS.Process.MyPid()); return; }
            st.TermsAccepted = $"{TermsWindow.Version}|{DateTime.Now:yyyy-MM-dd}";
            try { st.Save(path); } catch { /* asked again next time */ }
            StartMainAndroid(host);
        };
        host.Content = terms;
    }

    /// <summary>"Starting …" at once, the program right after: built inside a tap or before the first picture, Android would call the app frozen.</summary>
    static void StartMainAndroid(ContentControl host)
    {
        host.Content = new TextBlock { Text = L("Starte …"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Avalonia.Threading.DispatcherTimer.RunOnce(() => host.Content = _main = new MainWindow(), TimeSpan.FromMilliseconds(50));
    }

    /// <summary>The activity stops (the app goes to the background): save what would be saved when the window closes.</summary>
    public static void SaveForBackground() => _main?.SaveForBackground();
#endif

#if !ANDROID
    /// <summary>A window made for a big screen must not reach beyond a small one: the other windows (not the main window, which has its own rules) never grow past the usable area and are moved back inside it.</summary>
    static void KeepOnScreen(Window w)
    {
        if (w is MainWindow || w is SplashWindow || w.WindowState != WindowState.Normal || w.Screens.ScreenFromWindow(w) is not { } screen) return;
        var k = Math.Max(0.5, w.RenderScaling); var area = screen.WorkingArea;
        w.MaxWidth = Math.Min(w.MaxWidth, area.Width / k); w.MaxHeight = Math.Min(w.MaxHeight, area.Height / k);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var size = new PixelSize((int)(w.Bounds.Width * k), (int)(w.Bounds.Height * k));
            var x = Math.Clamp(w.Position.X, area.X, Math.Max(area.X, area.Right - size.Width));
            var y = Math.Clamp(w.Position.Y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
            if (x != w.Position.X || y != w.Position.Y) w.Position = new PixelPoint(x, y);
            // a form taller than the space it got (a small screen) scrolls instead of cutting off its buttons; one with its own scrolling parts reports a small height and stays as it is
            if (w.Content is Control body and not ScrollViewer && w.Bounds.Width > 0)
            {
                body.Measure(new Size(w.Bounds.Width, double.PositiveInfinity));
                if (body.DesiredSize.Height > w.Bounds.Height + 1)
                {
                    w.Content = null;
                    w.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
                }
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    static void StartMain(IClassicDesktopStyleApplicationLifetime desktop, AppSettings st)
    {
        // A start window shows the loading progress (not when the program starts minimized into the tray).
        if (st.StartMode != "Minimized" && st.Resume is null)   // right after an update the window just comes back
        {
            try { new SplashWindow(st.Logo).Show(); SplashWindow.Report(0.1, L("Starte …")); } catch { /* the splash is optional */ }
        }
        // build the main window after the splash had a chance to paint
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var main = new MainWindow();
            desktop.MainWindow = main;
            main.Show();
            DeltaFinish.Confirm();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }
#endif
}
