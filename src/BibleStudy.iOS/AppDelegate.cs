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

    /// <summary>The app goes to the background (iOS may end it any time after this): everything is saved as when the desktop window closes.</summary>
    [Export("applicationDidEnterBackground:")]
    public void DidEnterBackground(UIApplication application) => BibleStudy.App.App.SaveForBackground();

    [Export("applicationDidBecomeActive:")]
    public void DidBecomeActive(UIApplication application) => BibleStudy.App.App.CameToFront();

    /// <summary>The address of a pairing QR code (biblestudy-sync://pair?k=…), opened from the camera app.</summary>
    [Export("application:openURL:options:")]
    public bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        if (url.AbsoluteString is { } s && s.StartsWith("biblestudy-sync:", StringComparison.Ordinal)) { BibleStudy.App.App.OfferPairLink(s); return true; }
        return false;
    }
}
