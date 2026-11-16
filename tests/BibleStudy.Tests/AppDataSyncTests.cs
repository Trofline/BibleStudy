using BibleStudy.Core.Sync;
using Xunit;

namespace BibleStudy.Tests;

/// <summary>The program's own data of the person (verses to learn, vocabulary, categories …) in the device sync.</summary>
public class AppDataSyncTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_appdata_" + Guid.NewGuid().ToString("N"));
    public AppDataSyncTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Changes_additions_and_deletions_of_the_other_side_arrive_and_own_ones_stay()
    {
        Dictionary<string, string> bas = new() { ["joh3,16"] = "box1", ["ps23"] = "box2", ["röm8"] = "box1" };
        Dictionary<string, string> mine = new() { ["joh3,16"] = "box3", ["ps23"] = "box2", ["röm8"] = "box1", ["mt5"] = "box0" };   // practised here, one new
        Dictionary<string, string> theirs = new() { ["joh3,16"] = "box1", ["ps23"] = "box4", ["neu"] = "box0" };                  // practised there, one deleted, one new

        var merged = AppData.Merge(mine, theirs, bas);

        Assert.Equal("box3", merged["joh3,16"]);   // changed here only: stays
        Assert.Equal("box4", merged["ps23"]);      // changed there only: taken over
        Assert.False(merged.ContainsKey("röm8"));  // deleted there
        Assert.Equal("box0", merged["mt5"]);
        Assert.Equal("box0", merged["neu"]);
    }

    [Fact]
    public void A_device_with_nothing_or_without_a_base_deletes_nothing()
    {
        Dictionary<string, string> mine = new() { ["a"] = "1", ["b"] = "2" };
        Assert.Equal(2, AppData.Merge(mine, new Dictionary<string, string>(), mine).Count);
        Assert.Equal(3, AppData.Merge(mine, new Dictionary<string, string> { ["c"] = "3" }, null).Count);
    }

    [Fact]
    public void The_sets_travel_inside_a_vault_copy()
    {
        var path = Path.Combine(_dir, "copy.db");
        new BibleStudy.Core.Storage.VaultStore(path, "v").GetNotes();
        var sets = new Dictionary<string, Dictionary<string, string>> { ["memory"] = new() { ["joh3,16"] = "{\"Box\":2}" }, ["settings"] = new() { ["PlanId"] = "\"m-cheyne\"" } };
        AppData.WriteInto(path, sets);
        var back = AppData.ReadFrom(path)!;
        Assert.Equal("{\"Box\":2}", back["memory"]["joh3,16"]);
        Assert.Equal(AppData.Fingerprint(sets), AppData.Fingerprint(back));
        Assert.Null(AppData.ReadFrom(Path.Combine(_dir, "none.db")));
    }
}
