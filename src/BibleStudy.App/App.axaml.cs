using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace BibleStudy.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--uninstall")) desktop.MainWindow = new UninstallWindow(args);
            else
            {
                // A start window shows the loading progress (not when the program starts minimized into the tray).
                var minimized = false; var logo = "Book";
                try { var st = BibleStudy.Core.Storage.AppSettings.Load(Path.Combine(BibleStudy.Core.AppPaths.Root, "settings.json")); minimized = st.StartMode == "Minimized"; logo = st.Logo; } catch { /* no settings yet */ }
                if (!minimized)
                {
                    try { new SplashWindow(logo).Show(); SplashWindow.Report(0.1, "Starte …"); } catch { /* the splash is optional */ }
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

        base.OnFrameworkInitializationCompleted();
    }
}