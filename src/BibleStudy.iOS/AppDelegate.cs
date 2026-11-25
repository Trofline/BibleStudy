using Avalonia;
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace BibleStudy.iOS;

/// <summary>
/// BibleStudy for the iPhone: the whole program, built from the same sources as the desktop and Android versions (src/BibleStudy.App).
/// The main window becomes the one view of the app (see AppWindow); what only a PC has is left out with #if MOBILE.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<BibleStudy.App.App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont()
        .AfterSetup(_ => UIApplication.SharedApplication.InvokeOnMainThread(AskForFullFrameRate));

    /// <summary>
    /// ProMotion: an iPhone draws an app at only 60 frames a second unless the app says it wants more (Info.plist CADisableMinimumFrameDurationOnPhone)
    /// and its display link asks for it. Avalonia's display link does not ask, so its range is set here (best effort: if Avalonia changes, it just stays at the default).
    /// </summary>
    static void AskForFullFrameRate()
    {
        try
        {
            var max = (float)UIScreen.MainScreen.MaximumFramesPerSecond;
            if (max <= 60 || !UIDevice.CurrentDevice.CheckSystemVersion(15, 0)) return;
            var timerType = Type.GetType("Avalonia.Rendering.IRenderTimer, Avalonia.Base");
            var locator = Type.GetType("Avalonia.AvaloniaLocator, Avalonia.Base")?.GetProperty("Current", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(null) as IServiceProvider;
            var timer = timerType is null ? null : locator?.GetService(timerType);
            if (timer is null) return;
            foreach (var field in timer.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                if (field.GetValue(timer) is CoreAnimation.CADisplayLink link)
                    link.PreferredFrameRateRange = new CoreAnimation.CAFrameRateRange { Minimum = 60, Maximum = max, Preferred = max };
        }
        catch (Exception) { /* stays at the default rate */ }
    }

    // The app runs in a scene (Avalonia 12): iOS then does not call the app delegate's own background and foreground methods.
    // The notifications of the application come in any case.
    readonly NSObject _toBackground, _toFront;

    public AppDelegate()
    {
        _toBackground = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidEnterBackgroundNotification, _ => WentToBackground());
        _toFront = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidBecomeActiveNotification, _ => BibleStudy.App.App.CameToFront());
    }

    /// <summary>
    /// The app goes to the background (iOS may end it any time after this): everything is saved as when the desktop window closes, and iOS is asked
    /// for some more seconds, so what was just marked still goes to the other device before the app is frozen.
    /// </summary>
    static void WentToBackground()
    {
        var app = UIApplication.SharedApplication;
        nint task = 0;
        task = app.BeginBackgroundTask("BibleStudy sync", () => { if (task != 0) app.EndBackgroundTask(task); task = 0; });
        BibleStudy.App.App.SaveForBackground().ContinueWith(_ => app.InvokeOnMainThread(() => { if (task != 0) app.EndBackgroundTask(task); task = 0; }));
    }

    /// <summary>The address of a pairing QR code (biblestudy-sync://pair?k=…), opened from the camera app, or a link to a Bible passage (biblestudy://Joh3,16).</summary>
    [Export("application:openURL:options:")]
    public bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        if (url.AbsoluteString is not { } s || !s.StartsWith("biblestudy", StringComparison.OrdinalIgnoreCase)) return false;
        BibleStudy.App.App.OfferLink(s);
        return true;
    }
}
