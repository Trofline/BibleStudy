using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class UserStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));
    UserStore NewStore() => new(Path.Combine(_dir, "t.db"));

    [Fact]
    public void Search_FindsPrefixAndAllWords_PerTranslation()
    {
        var s = NewStore();
        s.Index("kjv", [new("JHN", 3, 16, "For God so loved the world"), new("GEN", 1, 1, "In the beginning God created")]);
        Assert.True(s.IsIndexed("kjv"));
        Assert.Equal(new VerseRef("JHN", 3, 16), Assert.Single(s.Search("kjv", "god lov")).Ref);
        Assert.Equal(2, s.Search("kjv", "God").Count);
        Assert.Empty(s.Search("other", "God"));
        Assert.Empty(s.Search("kjv", "\"; DROP"));
    }

    [Fact]
    public void BookmarksAndNotes_RoundTrip()
    {
        var s = NewStore(); var r = new VerseRef("JHN", 3, 16);
        Assert.True(s.ToggleBookmark(r)); Assert.Single(s.GetBookmarks());
        Assert.False(s.ToggleBookmark(r)); Assert.Empty(s.GetBookmarks());
        s.SetNote(r, "Kernvers"); s.SetNote(r, "Kernvers 2");
        Assert.Equal("Kernvers 2", Assert.Single(s.GetNotes()).Text);
        s.SetNote(r, " "); Assert.Empty(s.GetNotes());
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }
}

public class ConcordanceStoreTests
{
    [Fact]
    public void Occurrences_AreStoredPerTranslationInOrder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));
        var s = new UserStore(Path.Combine(dir, "t.db"));
        Assert.False(s.IsStrongsIndexed("kjv"));
        s.IndexStrongs("kjv", [new("G3056", new("JHN", 1, 1), "Word"), new("G2316", new("JHN", 1, 1), "God"), new("G3056", new("JHN", 1, 14), "Word")]);
        Assert.True(s.IsStrongsIndexed("kjv"));
        Assert.Equal([1, 14], s.GetOccurrences("kjv", "G3056").Select(o => o.Ref.Verse));
        Assert.Empty(s.GetOccurrences("other", "G3056"));
        s.IndexStrongs("kjv", [new("G3056", new("JHN", 3, 3), "x")]); // re-index replaces
        Assert.Single(s.GetOccurrences("kjv", "G3056"));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch { }
    }
}

public class ReadingAndSettingsTests
{
    [Fact]
    public void ReadChapters_MarkAndClearInBulk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));
        var s = new UserStore(Path.Combine(dir, "t.db"));
        s.SetRead("GEN", [1, 2, 3], true);
        s.SetRead("GEN", [2], false);
        Assert.Equal([("GEN", 1), ("GEN", 3)], s.GetReadChapters().OrderBy(x => x.Chapter));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void Settings_RoundTrip_AndBrokenFileFallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"), "settings.json");
        new AppSettings { Theme = "Dark", FontSize = 26 }.Save(path);
        Assert.Equal(26, AppSettings.Load(path).FontSize);
        File.WriteAllText(path, "{ not json");
        Assert.Equal(22, AppSettings.Load(path).FontSize);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}
