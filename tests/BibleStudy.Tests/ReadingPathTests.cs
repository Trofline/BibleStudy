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
    public void A_path_belongs_to_a_folder_and_follows_it_when_renamed_or_moved()
    {
        var v = new VaultStore(Path.Combine(_dir, "f.db"), "f");
        var top = v.AddFolder("Engel"); var sub = v.AddFolder("Des Herrn"); v.SetFolderParent(sub, top);
        var id = v.AddReadingPath("Engel des Herrn", "", [S("GEN", 16, 11), S("EXO", 3, 2)]);
        Assert.Empty(v.GetReadingPathsIn(sub));
        v.SetReadingPathFolder(id, sub);
        Assert.Equal(id, Assert.Single(v.GetReadingPathsIn(sub)).Id);
        Assert.Empty(v.GetReadingPathsIn(top));

        v.RenameFolder(top, "Boten");             // a folder above is renamed: the path stays in its folder
        Assert.Single(v.GetReadingPathsIn(sub));
        v.SetFolderParent(sub, null);             // moved to the top
        Assert.Single(v.GetReadingPathsIn(sub));
        v.UpdateReadingPath(v.GetReadingPath(id)! with { Name = "Der Engel" });   // a change of the steps or name keeps the folder
        Assert.Single(v.GetReadingPathsIn(sub));
        Assert.Equal("Der Engel", v.GetLastReadingPath()!.Name);

        v.DeleteFolder(sub);                      // the folder goes: the path stays, in no folder
        Assert.Equal("", v.GetReadingPath(id)!.Folder);
        v.SetReadingPathFolder(id, top);
        v.SetReadingPathFolder(id, null);
        Assert.Empty(v.GetReadingPathsIn(top));
    }

    [Fact]
    public void The_folder_of_a_path_travels_by_its_name()
    {
        var pc = new VaultStore(Path.Combine(_dir, "pcf.db"), "pc");
        pc.AddFolder("Engel des Herrn");
        var basePath = Path.Combine(_dir, "basef.db");
        pc.AddReadingPath("Pfad", "", [S("GEN", 16, 11)]);
        pc.BackupTo(basePath);
        File.Copy(Path.Combine(_dir, "pcf.db"), Path.Combine(_dir, "phonef.db"));
        var phone = new VaultStore(Path.Combine(_dir, "phonef.db"), "phone");
        var folder = phone.GetFolders().Single(f => f.Name == "Engel des Herrn").Id;
        phone.SetReadingPathFolder(phone.GetReadingPaths().Single().Id, folder);
        var copy = Path.Combine(_dir, "phonef-copy.db"); File.Copy(Path.Combine(_dir, "phonef.db"), copy);
        pc.MergeFrom(copy, "Handy", basePath);
        var here = pc.GetFolders().Single(f => f.Name == "Engel des Herrn").Id;
        Assert.Single(pc.GetReadingPathsIn(here));
    }

    [Fact]
    public void A_path_goes_to_a_file_with_its_marks_and_comes_back_into_another_vault()
    {
        var mine = new VaultStore(Path.Combine(_dir, "mine.db"), "mine");
        var id = mine.AddReadingPath("Engel", "#E07A3A", [S("GEN", 16, 11), S("EXO", 3, 2, 3)]);
        mine.AddMark(Mark.AnyTranslation, new BibleStudy.Core.Models.VerseRef("GEN", 16, 11), -1, -1, "Weiter sprach der Engel", "#E57373", "Notiz", null, []);
        mine.AddMark("ELB", new BibleStudy.Core.Models.VerseRef("EXO", 3, 3), 4, 12, "ich will", "#FFD54F", "", null, []);    // a word in the second step
        mine.AddMark(Mark.AnyTranslation, new BibleStudy.Core.Models.VerseRef("EXO", 3, 7), -1, -1, "nicht im Pfad", "#64B5F6", "", null, []);

        var bare = ReadingPathFile.Read(ReadingPathFile.Write(mine, [mine.GetReadingPath(id)!], withMarks: false));
        Assert.Empty(Assert.Single(bare).Marks);

        var read = Assert.Single(ReadingPathFile.Read(ReadingPathFile.Write(mine, [mine.GetReadingPath(id)!], withMarks: true)));
        Assert.Equal("Engel", read.Path.Name);
        Assert.Equal(["GEN 16:11", "EXO 3:2-3"], read.Path.Steps.Select(s => s.ToString()));
        Assert.Equal(2, read.Marks.Count);   // the mark on Ex 3:7 lies outside the path
        var word = read.Marks.Single(m => m.TranslationId == "ELB");
        Assert.Equal((4, 12, "#FFD54F"), (word.Start, word.End, word.Color));

        var theirs = new VaultStore(Path.Combine(_dir, "theirs.db"), "theirs");
        theirs.AddReadingPath("Engel", "", [S("GEN", 1, 1)]);
        ReadingPathFile.KeepPaths(theirs, [read.Path]);
        Assert.Contains(theirs.GetReadingPaths(), p => p.Name == "Engel (2)" && p.Steps.Count == 2);   // a name that is taken gets a number
        Assert.Equal(2, ReadingPathFile.KeepMarks(theirs, read.Marks));
        Assert.Equal(0, ReadingPathFile.KeepMarks(theirs, read.Marks));   // read in twice: nothing twice
        Assert.Throws<InvalidDataException>(() => ReadingPathFile.Read("{\"something\":1}"));
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
