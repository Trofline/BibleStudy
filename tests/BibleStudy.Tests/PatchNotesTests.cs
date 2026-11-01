using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

public class PatchNotesTests
{
    const string Body = "## Neu in 1.0.20\n- **Rundgang:** deutsch\n\n## New in 1.0.20\n- **Tour:** english\n\nDateien: `x.exe`";

    [Fact]
    public void NotesFollowTheLanguage()
    {
        Assert.Contains("deutsch", Updater.NotesFor(Body, "de"));
        Assert.DoesNotContain("english", Updater.NotesFor(Body, "de"));
        Assert.Contains("english", Updater.NotesFor(Body, "fr"));
        Assert.DoesNotContain("x.exe", Updater.NotesFor(Body, "en"));
    }

    [Fact]
    public void PlainNotesStayWhole() => Assert.Equal("nur Text", Updater.NotesFor("nur Text", "de"));

    [Fact]
    public void ReleasesAreSortedAndDraftsSkipped()
    {
        var json = """
        [{"tag_name":"v1.0.9","body":"a","html_url":"u9","published_at":"2026-01-01T10:00:00Z"},
         {"tag_name":"v1.0.20","body":"b","html_url":"u20","published_at":"2026-02-01T10:00:00Z"},
         {"tag_name":"v1.0.21","body":"c","html_url":"u21","draft":true},
         {"tag_name":"foo","body":"d","html_url":"x"}]
        """;
        var list = Updater.ParseReleases(json);
        Assert.Equal(["1.0.20", "1.0.9"], list.Select(r => r.Version));
    }
}
