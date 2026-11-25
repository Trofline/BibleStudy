using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace BibleStudy.Droid;

[Application]
public class AndroidApp(IntPtr handle, JniHandleOwnership transfer) : AvaloniaAndroidApplication<BibleStudy.App.App>(handle, transfer)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont().LogToTrace();
}

[IntentFilter(new[] { global::Android.Content.Intent.ActionView }, Categories = new[] { global::Android.Content.Intent.CategoryDefault, global::Android.Content.Intent.CategoryBrowsable }, DataScheme = "biblestudy-sync")]
[Activity(Label = "BibleStudy", Theme = "@style/MyTheme.NoActionBar", MainLauncher = true, Icon = "@mipmap/appicon",
    LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = global::Android.Views.SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity
{
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);
        Pair(Intent);
    }

    protected override void OnNewIntent(global::Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        Pair(intent);
    }

    /// <summary>A QR code of the comparison, scanned with the camera app, opens the app with its pairing address.</summary>
    static void Pair(global::Android.Content.Intent? intent)
    {
        if (intent?.DataString is { } url && url.StartsWith("biblestudy-sync:", StringComparison.Ordinal)) BibleStudy.App.App.OfferPairLink(url);
    }

    /// <summary>The split view and parallel translations need a wide screen: a phone is turned across while they are open and may turn back afterwards.</summary>
    public static void Landscape(bool on)
    {
        var a = Current;
        if (a is null) return;
        var want = on ? ScreenOrientation.SensorLandscape : ScreenOrientation.Unspecified;
        a.RunOnUiThread(() => { if (a.RequestedOrientation != want) a.RequestedOrientation = want; });
    }

    static TaskCompletionSource<bool>? _permission;
    const int PermissionRequest = 7;

    /// <summary>Asks for a permission (the microphone for a spoken note) and waits for the answer; true when it is given.</summary>
    public static Task<bool> AskPermissionAsync(string permission)
    {
        var a = Current;
        if (a is null) return Task.FromResult(false);
        if (a.CheckSelfPermission(permission) == Permission.Granted) return Task.FromResult(true);
        _permission?.TrySetResult(false);
        var ask = _permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        a.RunOnUiThread(() => a.RequestPermissions([permission], PermissionRequest));
        return ask.Task;
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == PermissionRequest) _permission?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
    }

    /// <summary>The app goes to the background: everything is saved as when the desktop window closes (Android may end it any time after this).</summary>
    protected override void OnStop()
    {
        _ = BibleStudy.App.App.SaveForBackground();   // the process lives on for a while: the sync runs to its end
        base.OnStop();
    }

    /// <summary>The app is in front again: the paired devices are asked at once for what changed meanwhile.</summary>
    protected override void OnResume()
    {
        base.OnResume();
        BibleStudy.App.App.CameToFront();
    }
}

/// <summary>
/// "Restart": an app cannot start itself anew while it is still running (a second window of the same process would meet the views of the first).
/// This small invisible activity runs in a process of its own: the app ends, it waits a moment and then opens the app again.
/// </summary>
[Activity(Process = ":restart", Exported = false, NoHistory = true, ExcludeFromRecents = true, Theme = "@android:style/Theme.Translucent.NoTitleBar")]
public class RestartActivity : Activity
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName ?? "");
        launch?.AddFlags(global::Android.Content.ActivityFlags.NewTask | global::Android.Content.ActivityFlags.ClearTask);
        new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper!).PostDelayed(() =>
        {
            if (launch is not null) StartActivity(launch);
            Finish();
            global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
        }, 800);
    }
}
