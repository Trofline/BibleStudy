namespace BibleStudy.App;

/// <summary>What kind of device the program runs on. A phone (Android, iPhone) has one screen view instead of windows, no keyboard shortcuts and no PC tools.</summary>
static class AppPlatform
{
    public static bool IsMobile { get; } = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || Environment.GetEnvironmentVariable("BIBLESTUDY_PHONE") == "1";   // the variable is for trying the phone layout on a PC
}
