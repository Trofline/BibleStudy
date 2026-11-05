using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace BibleStudy.Android;

[Application]
public class AndroidApp(IntPtr handle, JniHandleOwnership transfer) : AvaloniaAndroidApplication<App>(handle, transfer)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont();
}

[Activity(Label = "BibleStudy", Theme = "@style/MyTheme.NoActionBar", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public class MainActivity : AvaloniaMainActivity
{
}
