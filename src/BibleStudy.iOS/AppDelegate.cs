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
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont();

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

    /// <summary>The address of a pairing QR code (biblestudy-sync://pair?k=…), opened from the camera app.</summary>
    [Export("application:openURL:options:")]
    public bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        if (url.AbsoluteString is { } s && s.StartsWith("biblestudy-sync:", StringComparison.Ordinal)) { BibleStudy.App.App.OfferPairLink(s); return true; }
        return false;
    }
}
