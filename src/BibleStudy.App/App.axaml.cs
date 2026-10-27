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
            desktop.MainWindow = Environment.GetCommandLineArgs().Contains("--uninstall") ? new UninstallWindow(Environment.GetCommandLineArgs()) : new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}