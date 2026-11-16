using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

public class SideStoreUpdateTests
{
    const string Json = """
    {"name":"BibleStudy","apps":[{"name":"BibleStudy","versions":[
      {"version":"1.9.10","localizedDescription":"new","downloadURL":"https://x/BibleStudy-iOS-1.9.10.ipa"},
      {"version":"1.9.9","localizedDescription":"old","downloadURL":"https://x/BibleStudy-iOS-1.9.9.ipa"}
    ]}]}
    """;

    [Fact]
    public void The_iPhone_is_told_of_the_newest_version_in_the_SideStore_source_only()
    {
        var info = Updater.ParseSideStore(Json, "1.9.9");
        Assert.NotNull(info);
        Assert.Equal("1.9.10", info!.Version);
        Assert.Equal("new", info.Notes);
        Assert.Null(Updater.ParseSideStore(Json, "1.9.10"));   // a release whose IPA is not in the source yet is no update for the iPhone
    }
}
