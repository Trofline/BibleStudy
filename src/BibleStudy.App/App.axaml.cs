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
        ListenForLinks();
    }

    bool _listening;

    /// <summary>
    /// iPhone: a pairing QR code opened from the camera app reaches the app as an activation of its scene, not through the app delegate
    /// (Avalonia 12 runs the app in a scene): the link is taken from there. As early as possible, so the link that started the app is not missed.
    /// </summary>
    void ListenForLinks()
    {
#if !ANDROID
        // iPhone and Mac: links opened while the program runs (or that started it) come as an activation; Windows and Linux start a new
        // process with the link as its argument instead (see Program), Android through its activity
        if (_listening || this.TryGetFeature<Avalonia.Controls.ApplicationLifetimes.IActivatableLifetime>() is not { } life) return;
        _listening = true;
        life.Activated += (_, e) =>
        {
            if (e is not Avalonia.Controls.ApplicationLifetimes.ProtocolActivatedEventArgs { Uri: { } uri }) return;
#if IOS
            if (uri.Scheme == "biblestudy-sync") { OfferPairLink(uri.OriginalString); return; }
#endif
            if (uri.Scheme == BibleStudy.Core.Search.BibleLinks.Scheme) OfferLink(uri.OriginalString);
        };
#endif
    }

    static string? _pendingLink;
    static MainWindow? _linkTarget;

    /// <summary>
    /// A link to a passage (biblestudy://open/JHN.3.16) or a text shared from another app: opened at once when the program is ready,
    /// otherwise as soon as it is (the main window asks for it with <see cref="TakePendingLink"/>).
    /// </summary>
    public static void OfferLink(string text)
    {
        _pendingLink = text;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_linkTarget is { } w && TakePendingLink() is { } t) _ = w.OpenLinkTextAsync(t); });
    }

    /// <summary>Keeps the link for later without trying to open it now.</summary>
    public static void KeepLink(string text) => _pendingLink = text;

    public static bool HasPendingLink => _pendingLink is not null;

    public static string? TakePendingLink() { var t = _pendingLink; _pendingLink = null; return t; }

    /// <summary>The main window is ready for links (from now on they are opened at once).</summary>
    internal static void LinksGoTo(MainWindow w) => _linkTarget = w;

    public override void OnFrameworkInitializationCompleted()
    {
        ErrorLog.Install();
        ListenForLinks();
#if !MOBILE
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => KeepOnScreen(w));
        // every submenu shows the same "›" (the theme's own arrow is hidden in App.axaml, it was cut off at the edge)
        Control.LoadedEvent.AddClassHandler<MenuItem>((m, _) => { if (m.ItemCount > 0 && m.Header is string s) m.Header = MainWindow.SubHeader(s); });
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Skip(1).FirstOrDefault(BibleStudy.Core.Search.BibleLinks.IsLink) is { } link) _pendingLink = link;   // started by a link (Windows, Linux)
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

#if MOBILE
        if (ApplicationLifetime is ISingleViewApplicationLifetime single) StartMobile(single);
#endif
        base.OnFrameworkInitializationCompleted();
    }

#if MOBILE
    static MainWindow? _main;

    /// <summary>
    /// Phone (Android, iPhone): one view, no windows. The terms come first and alone, as on a desktop; then the program. The view of the activity is
    /// set once (Android does not take a second one well); what it shows is changed inside it.
    /// </summary>
    static void StartMobile(ISingleViewApplicationLifetime single)
    {
        var host = new ContentControl();
        host.Bind(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, host.GetResourceObservable("BsBackground"));
        host.AttachedToVisualTree += (_, _) => KeepInsideSafeArea(host);
        single.MainView = host;
        var path = Path.Combine(AppPaths.Root, "settings.json");
        AppSettings st;
        try { st = AppSettings.Load(path); } catch { st = new(); }
        try { Loc.Use(st.Language); } catch { Loc.Use(null); }
        if (st.TermsAccepted.StartsWith(TermsWindow.Version + "|", StringComparison.Ordinal)) { StartMobileMain(host); return; }
        var terms = new TermsWindow { IsRootView = true };
        terms.Closed += (_, _) =>
        {
            if (!terms.Accepted)
            {
#if ANDROID
                Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
#else
                Environment.Exit(0);   // (an iPhone app that does not agree has to be left by the person)
#endif
                return;
            }
            st.TermsAccepted = $"{TermsWindow.Version}|{DateTime.Now:yyyy-MM-dd}";
            try { st.Save(path); } catch { /* asked again next time */ }
            StartMobileMain(host);
        };
        host.Content = terms;
    }

    /// <summary>
    /// Newer Android versions draw an app under the status bar, the camera cut-out and the navigation bar. Everything is kept inside the free area:
    /// otherwise the top bar hides below the status bar and the lowest buttons (the terms!) cannot be reached. The strip left over takes the color of the app.
    /// </summary>
    static void KeepInsideSafeArea(ContentControl host)
    {
        if (TopLevel.GetTopLevel(host)?.InsetsManager is not { } insets) return;
        try { insets.DisplayEdgeToEdgePreference = true; } catch { /* the system decides */ }
        void Apply()
        {
            // (the first report comes before the view has a size and is in screen pixels: wait for the real size, the value is then in device-independent pixels)
            if (host.Bounds.Width < 10) return;
            host.Padding = insets.SafeAreaPadding;
            try { if (host.Background is Avalonia.Media.ISolidColorBrush b) insets.SystemBarColor = b.Color; } catch { /* old versions keep their bar */ }
        }
        insets.SafeAreaChanged += (_, _) => Apply();
        host.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty) Apply(); };
        host.SizeChanged += (_, _) => Apply();   // after a turn too: the free area is another one
        Apply();
    }

    /// <summary>"Starting …" at once, the program right after: built inside a tap or before the first picture, Android would call the app frozen.</summary>
    static void StartMobileMain(ContentControl host)
    {
        host.Content = new TextBlock { Text = L("Starte …"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Avalonia.Threading.DispatcherTimer.RunOnce(() => host.Content = _main = new MainWindow(), TimeSpan.FromMilliseconds(50));
    }

    static string? _pendingPair;

    /// <summary>A pairing address (biblestudy-sync://pair?k=…) that the camera app or another app opened: taken over at once, or when the program is ready.</summary>
    public static void OfferPairLink(string url)
    {
        _pendingPair = url;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_main is not null && TakePendingPair() is { } u) _main.JoinWithCode(u); });
    }

    public static string? TakePendingPair() { var u = _pendingPair; _pendingPair = null; return u; }

    /// <summary>The activity stops (the app goes to the background): save what would be saved when the window closes.</summary>
    public static Task SaveForBackground() => _main?.SaveForBackground() ?? Task.CompletedTask;

    /// <summary>The app is in front again (after the home screen or another app).</summary>
    public static void CameToFront() => Avalonia.Threading.Dispatcher.UIThread.Post(() => _main?.SyncSoon());
#endif

#if !MOBILE
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
