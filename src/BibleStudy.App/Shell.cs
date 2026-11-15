using System.Diagnostics;

namespace BibleStudy.App;

/// <summary>
/// Opens a web address, a file or a folder with what the system has for it. On a desktop the default program (browser, PDF viewer,
/// file manager); on Android the browser for an address, and for a file the choice of apps that can show or take it (to view an export
/// or send it on). A folder cannot be opened there: the files of the app lie where only the app can see them.
/// </summary>
static class Shell
{
    /// <summary>False when nothing could open it (no browser, no app for this kind of file).</summary>
    public static bool Open(string target)
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            Android.Content.Intent intent;
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                intent = new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse(target));
            else if (File.Exists(target))
            {
                var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, context.PackageName + ".files", new Java.IO.File(target));
                var type = Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(Path.GetExtension(target).TrimStart('.').ToLowerInvariant()) ?? "*/*";
                var view = new Android.Content.Intent(Android.Content.Intent.ActionView);
                view.SetDataAndType(uri, type);
                view.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission);
                intent = Android.Content.Intent.CreateChooser(view, Path.GetFileName(target))!;
            }
            else if (Directory.Exists(target))
            {
                // a folder (the program shows where an export went): nothing on a phone shows the inside of the app, so the file just
                // written there is offered instead
                var newest = new DirectoryInfo(target).GetFiles("*", SearchOption.AllDirectories).Where(f => f.LastWriteTimeUtc > DateTime.UtcNow.AddMinutes(-2)).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                return newest is not null && Open(newest.FullName);
            }
            else return false;
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
            return true;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or ArgumentException or InvalidOperationException)
        {
            ErrorLog.Write(ex);
            return false;
        }
#elif IOS
        try
        {
            var app = UIKit.UIApplication.SharedApplication;
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                return app.CanOpenUrl(new Foundation.NSUrl(target)) && app.OpenUrl(new Foundation.NSUrl(target));
            if (Directory.Exists(target))
            {
                // a folder (the program shows where an export went): the file just written there is offered instead
                var newest = new DirectoryInfo(target).GetFiles("*", SearchOption.AllDirectories).Where(f => f.LastWriteTimeUtc > DateTime.UtcNow.AddMinutes(-2)).OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                return newest is not null && Open(newest.FullName);
            }
            if (!File.Exists(target)) return false;
            // a file: the share sheet (view in another app, save to Files, send on)
            var share = new UIKit.UIActivityViewController([Foundation.NSUrl.FromFilename(target)], null);
            var top = TopViewController();
            if (top is null) return false;
            if (share.PopoverPresentationController is { } pop) { pop.SourceView = top.View; pop.SourceRect = new CoreGraphics.CGRect(top.View!.Bounds.Width / 2, top.View.Bounds.Height / 2, 1, 1); }   // on an iPad it is a popover
            top.PresentViewController(share, true, null);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorLog.Write(ex);
            return false;
        }
#else
        try { using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); return true; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException or FileNotFoundException) { return false; }
#endif
    }

#if IOS
    static UIKit.UIViewController? TopViewController()
    {
        var top = UIKit.UIApplication.SharedApplication.ConnectedScenes.OfType<UIKit.UIWindowScene>().SelectMany(s => s.Windows).FirstOrDefault(w => w.IsKeyWindow)?.RootViewController;
        while (top?.PresentedViewController is { } next) top = next;
        return top;
    }
#endif

    /// <summary>On a phone there is no window to show a folder in.</summary>
    public static bool CanOpenFolders => !AppPlatform.IsMobile;
}
