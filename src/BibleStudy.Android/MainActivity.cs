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

[Activity(Label = "BibleStudy", Theme = "@style/MyTheme.NoActionBar", MainLauncher = true, Icon = "@mipmap/appicon",
    WindowSoftInputMode = global::Android.Views.SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>The app goes to the background: everything is saved as when the desktop window closes (Android may end it any time after this).</summary>
    protected override void OnStop()
    {
        BibleStudy.App.App.SaveForBackground();
        base.OnStop();
    }
}
