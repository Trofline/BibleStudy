using Avalonia;
using System;

namespace BibleStudy.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains(DeltaFinish.CheckArgument)) { Environment.Exit(DeltaFinish.CheckAndExit()); return; }   // a new version tried out after installing, no window
        DeltaFinish.AtStart();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        if (DeltaFinish.Run()) Environment.Exit(0);   // a quick update waited for the program to end
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
