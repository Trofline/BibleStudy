using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

[Collection("Updater")]
public class ApkUpdateTests
{
    const string Json = """
    [
      {"tag_name":"ios-beta-1.9.5","draft":false,"prerelease":true,"html_url":"u","body":"","assets":[{"name":"BibleStudy-iOS-1.9.5.ipa","browser_download_url":"x","size":1}]},
      {"tag_name":"android-beta-1.9.4","draft":false,"prerelease":true,"html_url":"u","body":"n","assets":[{"name":"BibleStudy-Android-beta-1.9.4.apk","browser_download_url":"https://x/a.apk","size":5,"digest":"sha256:ab"},{"name":"BibleStudy-Android-beta-1.9.4.apk.sha256","browser_download_url":"s","size":1}]},
      {"tag_name":"v1.9.1","draft":false,"prerelease":false,"html_url":"u","body":"","assets":[]}
    ]
    """;

    [Fact]
    public void Android_follows_its_own_apk_releases_and_the_pc_ignores_them()
    {
        try
        {
            Updater.ApkMode = true;
            var info = Updater.ParseUpdate(Json, "1.9.2", false, "android-arm64");
            Assert.NotNull(info);
            Assert.Equal("1.9.4", info!.Version);
            Assert.Equal("https://x/a.apk", info.DownloadUrl);
            Assert.Null(Updater.ParseUpdate(Json, "1.9.4", false, "android-arm64"));
            Updater.ApkMode = false;
            Assert.Null(Updater.ParseUpdate(Json, "1.9.2", false, "win-x64"));
        }
        finally { Updater.ApkMode = false; }
    }

    // from 1.9.7 one release holds every system: the phone finds its APK there, the PC its installer, and a quick release without an APK is no update for the phone
    const string Shared = """
    [
      {"tag_name":"v1.9.8","draft":false,"prerelease":true,"html_url":"u","body":"","assets":[{"name":"BibleStudy-Update-win-x64-1.9.8.json","browser_download_url":"m","size":1}]},
      {"tag_name":"v1.9.7","draft":false,"prerelease":false,"html_url":"u","body":"n","assets":[
        {"name":"BibleStudy-Setup-1.9.7.exe","browser_download_url":"https://x/setup.exe","size":9},
        {"name":"BibleStudy-Android-1.9.7.apk","browser_download_url":"https://x/b.apk","size":5},
        {"name":"BibleStudy-iOS-1.9.7.ipa","browser_download_url":"https://x/c.ipa","size":5}]},
      {"tag_name":"android-beta-1.9.6","draft":false,"prerelease":true,"html_url":"u","body":"","assets":[{"name":"BibleStudy-Android-beta-1.9.6.apk","browser_download_url":"https://x/old.apk","size":5}]}
    ]
    """;

    [Fact]
    public void One_release_for_every_system_gives_each_its_own_file()
    {
        try
        {
            Updater.ApkMode = true;
            Assert.Equal("https://x/b.apk", Updater.ParseUpdate(Shared, "1.9.6", false, "android-arm64")!.DownloadUrl);
            Assert.Null(Updater.ParseUpdate(Shared, "1.9.7", false, "android-arm64"));
            Updater.ApkMode = false;
            var pc = Updater.ParseUpdate(Shared, "1.9.7", false, "win-x64");   // the quick release after it: its list of files, never an APK
            Assert.Equal("1.9.8", pc!.Version);
            Assert.Equal("m", pc.ManifestUrl);
            Assert.DoesNotContain(".apk", pc.DownloadUrl);
            var full = Shared.Replace("\"tag_name\":\"v1.9.8\",\"draft\":false", "\"tag_name\":\"v1.9.8\",\"draft\":true");   // without the quick release: the installer of the shared one
            Assert.Equal("https://x/setup.exe", Updater.ParseUpdate(full, "1.9.6", false, "win-x64")!.DownloadUrl);
        }
        finally { Updater.ApkMode = false; }
    }
}
