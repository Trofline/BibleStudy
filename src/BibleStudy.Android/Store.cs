using Android.Content;
using Android.App;
using Android.Provider;

[assembly: UsesPermission(Name = "android.permission.INTERNET")]
[assembly: UsesPermission(Name = "android.permission.MANAGE_EXTERNAL_STORAGE")]

namespace BibleStudy.Android;

/// <summary>Where the data lives and the few app settings (kept in the private folder of the app, so they survive a change of the data folder).</summary>
static class Store
{
    static string Private => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static string RootFile => Path.Combine(Private, "root.txt");
    static string StateFile => Path.Combine(Private, "translation.txt");

    /// <summary>The data folder: the folder chosen in the settings, else the app's own folder on the shared storage.</summary>
    public static string Root
    {
        get
        {
            try { if (File.Exists(RootFile) && File.ReadAllText(RootFile).Trim() is { Length: > 0 } p) return p; } catch (IOException) { }
            return Application.Context.GetExternalFilesDir(null)?.AbsolutePath ?? Path.Combine(Private, "BibleStudy");
        }
        set
        {
            if (value.Length > 0) Directory.CreateDirectory(value);   // fails at once if the folder cannot be used
            File.WriteAllText(RootFile, value);
        }
    }

    public static string Translation
    {
        get { try { return File.Exists(StateFile) ? File.ReadAllText(StateFile).Trim() : ""; } catch (IOException) { return ""; } }
        set { try { File.WriteAllText(StateFile, value); } catch (IOException) { } }
    }

    /// <summary>Android 11 and later: a folder outside the app's own one (for Syncthing) needs the "all files" permission, which the user grants in the system settings.</summary>
    public static void AskForAllFilesAccess()
    {
        var ctx = Application.Context;
        var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission, global::Android.Net.Uri.Parse("package:" + ctx.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        ctx.StartActivity(intent);
    }
}
