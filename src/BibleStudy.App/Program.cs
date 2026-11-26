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
        // a click on a link biblestudy://Joh3,16 while the program already runs: the open program shows the passage, this start ends
        // (before the update check: a start without a window must not count as a failed try of a new version)
        if (LinkInbox.FromArgs(args) is { } link && LinkInbox.SendToRunning(link)) return;
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
