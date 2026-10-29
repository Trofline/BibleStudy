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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = Environment.GetCommandLineArgs();
            var path = Path.Combine(AppPaths.Root, "settings.json");
            try { Loc.Use(AppSettings.Load(path).Language); } catch { Loc.Use(null); }
            if (args.Contains("--uninstall")) desktop.MainWindow = new UninstallWindow(args);
            else
            {
                AppSettings st;
                try { st = AppSettings.Load(path); } catch { st = new(); }

                // a downloaded update is installed before anything else opens (the installer starts the program again)
                if (UpdateApply.TryApply(st, launch: true)) { Environment.Exit(0); return; }

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
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    static void StartMain(IClassicDesktopStyleApplicationLifetime desktop, AppSettings st)
    {
        // A start window shows the loading progress (not when the program starts minimized into the tray).
        if (st.StartMode != "Minimized")
        {
            try { new SplashWindow(st.Logo).Show(); SplashWindow.Report(0.1, L("Starte …")); } catch { /* the splash is optional */ }
        }
        // build the main window after the splash had a chance to paint
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var main = new MainWindow();
            desktop.MainWindow = main;
            main.Show();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }
}
