using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

/// <summary>Reading paths: their steps in order, the arrows of a chapter and their lanes, and that they travel with a vault.</summary>
public class ReadingPathTests
{
    readonly string _dir;
    public ReadingPathTests() { _dir = Path.Combine(Path.GetTempPath(), "bs_paths_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }

    static StudyPassage S(string book, int chapter, int from, int to = 0) => new(book, chapter, from, to == 0 ? from : to);

    [Fact]
    public void Steps_keep_their_order_and_the_same_verse_may_come_twice()
    {
        var v = new VaultStore(Path.Combine(_dir, "v.db"), "v");
        var id = v.AddReadingPath("Rechtfertigung", "#3A8FE0", [S("ROM", 3, 23), S("ROM", 6, 23), S("ROM", 3, 23), S("ROM", 5, 1, 2)]);
        var p = v.GetReadingPath(id)!;
        Assert.Equal(["ROM 3:23", "ROM 6:23", "ROM 3:23", "ROM 5:1-2"], p.Steps.Select(s => s.ToString()));
        v.UpdateReadingPath(p with { Name = "Gnade", Steps = [.. p.Steps.Reverse()] });
        Assert.Equal("Gnade", v.GetReadingPath(id)!.Name);
        Assert.Equal("ROM 5:1-2", v.GetReadingPath(id)!.Steps[0].ToString());
        v.DeleteReadingPath(id);
        Assert.Empty(v.GetReadingPaths());
    }

    [Fact]
    public void Arrows_join_neighbouring_steps_of_one_chapter_only()
    {
        var p = new ReadingPath(1, "p", "", [S("JHN", 3, 16), S("JHN", 3, 3), S("ROM", 5, 8), S("JHN", 3, 36), S("JHN", 3, 18)]);
        Assert.Equal([(1, 16, 3), (4, 36, 18)], p.ArrowsIn("JHN", 3).ToList());   // 3 → Rom 5 and Rom 5 → 36 leave the chapter: no arrow
        Assert.Equal([1, 2, 4, 5], p.StepsIn("JHN", 3).Select(x => x.Number));
    }

    [Fact]
    public void Overlapping_arrows_get_their_own_lanes_and_short_ones_lie_next_to_the_text()
    {
        // 1→10 spans 2→4 and 5→8; the two short ones do not touch each other and share the inner lane
        var lanes = PathArrows.Lanes([(1, 10), (2, 4), (5, 8)]);
        Assert.Equal(1, lanes[0]);
        Assert.Equal(0, lanes[1]);
        Assert.Equal(0, lanes[2]);
        // arrows that meet in one verse (a step that is reached and left) do not share a line
        var chain = PathArrows.Lanes([(1, 3), (3, 6)]);
        Assert.NotEqual(chain[0], chain[1]);
        Assert.Empty(PathArrows.Lanes([]));
    }

    [Fact]
    public void A_path_travels_to_the_other_device_once_and_a_change_follows()
    {
        var pc = new VaultStore(Path.Combine(_dir, "pc.db"), "pc");
        var phonePath = Path.Combine(_dir, "phone.db");
        var phone = new VaultStore(phonePath, "phone");
        phone.AddReadingPath("Vom Handy", "#9B5DE5", [S("GEN", 1, 1), S("JHN", 1, 1)]);
        pc.MergeFrom(phonePath, "Handy");
        pc.MergeFrom(phonePath, "Handy");   // a second time brings nothing twice
        var p = Assert.Single(pc.GetReadingPaths());
        Assert.Equal(["GEN 1:1", "JHN 1:1"], p.Steps.Select(s => s.ToString()));

        // after a comparison: a step added on the phone comes over with the three-way merge
        var basePath = Path.Combine(_dir, "base.db"); pc.BackupTo(basePath);
        File.Copy(Path.Combine(_dir, "pc.db"), Path.Combine(_dir, "phone2.db"));
        var phone2 = new VaultStore(Path.Combine(_dir, "phone2.db"), "phone2");
        var q = phone2.GetReadingPaths().Single();
        phone2.UpdateReadingPath(q with { Steps = [.. q.Steps, S("REV", 22, 21)] });
        var copy = Path.Combine(_dir, "phone2-copy.db"); File.Copy(Path.Combine(_dir, "phone2.db"), copy);
        pc.MergeFrom(copy, "Handy", basePath);
        Assert.Equal(3, pc.GetReadingPaths().Single().Steps.Count);
    }

    [Fact]
    public void A_wiped_vault_has_no_paths()
    {
        var v = new VaultStore(Path.Combine(_dir, "w.db"), "w");
        v.AddReadingPath("x", "", [S("GEN", 1, 1)]);
        v.Wipe();
        Assert.Empty(v.GetReadingPaths());
    }
}
