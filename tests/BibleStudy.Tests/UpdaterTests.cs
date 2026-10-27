using BibleStudy.Core;

namespace BibleStudy.Tests;

public class UpdaterTests
{
    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.10.3-beta", "1.10.3")]
    [InlineData("V2.0.0+abc", "2.0.0")]
    public void Parses_tags(string tag, string expected) => Assert.Equal(Version.Parse(expected), Updater.Parse(tag));

    [Fact]
    public void Newer_versions_compare_numerically() => Assert.True(Updater.Parse("v1.10.0")! > Updater.Parse("1.9.9")!);

    [Fact]
    public void Garbage_is_rejected() => Assert.Null(Updater.Parse("nightly"));
}
