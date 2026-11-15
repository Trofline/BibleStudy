#if ANDROID
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// Updating the Android app: the new APK is loaded (checked against its checksum) and handed to the installer of Android, which asks once and
/// replaces the app; the data stays. The first time Android also asks to allow this app to install apps.
/// </summary>
public partial class MainWindow
{
    async Task InstallApkAsync(UpdateInfo info)
    {
        _updating = true;
        try
        {
            var progress = new Progress<double>(p => UpdateStatus.Text = L("Lade Update … {0:P0}", p));
            UpdateStatus.Text = L("Lade Update … {0:P0}", 0);
            var path = await _updater!.DownloadAsync(info, progress);
            var ctx = Android.App.Application.Context;
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(ctx, "de.trofline.biblestudy.files", new Java.IO.File(path));
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
            intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission | Android.Content.ActivityFlags.NewTask);
            UpdateStatus.Text = L("Fertig geladen. Bestätige die Installation von Android.");
            SaveForBackground();
            ctx.StartActivity(intent);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException or Java.Lang.Exception)
        {
            UpdateStatus.Text = L("Update fehlgeschlagen: ") + ex.Message;
        }
        finally { _updating = false; }
    }
}
#endif
