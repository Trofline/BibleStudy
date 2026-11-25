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
        // a link to a passage (biblestudy://open/JHN.3.16) while the program already runs: that one opens it, no second program starts
        if (args.FirstOrDefault(BibleStudy.Core.Search.BibleLinks.IsLink) is { } link && LinkForwarder.SendToRunning(link)) return;
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
