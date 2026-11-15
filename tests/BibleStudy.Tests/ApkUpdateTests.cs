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
}
