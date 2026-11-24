using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

/// <summary>Putting marks of neighbouring verses together, and cutting them up again.</summary>
public class MarkMergeTests
{
    readonly string _dir;
    public MarkMergeTests() { _dir = Path.Combine(Path.GetTempPath(), "bs_merge_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }

    static long Whole(VaultStore v, int verse, string color = "#E57373", string note = "", int to = 0) =>
        v.AddMark(Mark.AnyTranslation, new VerseRef("MRK", 7, verse), -1, -1, "Vers " + verse, color, note, null, [], to);

    [Fact]
    public void Verses_that_follow_each_other_become_one_mark_and_can_be_cut_up_again()
    {
        var v = new VaultStore(Path.Combine(_dir, "a.db"), "a");
        Whole(v, 19, note: "A"); Whole(v, 20, note: "B"); Whole(v, 21, note: "A");
        var marks = v.GetMarks("MRK", 7);
        Assert.True(VaultStore.CanMergeMarks(marks));
        var one = v.MergeMarks(marks)!;
        Assert.Equal((19, 21), (one.Ref.Verse, one.LastVerse));
        Assert.Equal("A\n\nB", one.Note);
        Assert.Single(v.GetMarks("MRK", 7));

        var pieces = v.SplitMark(one, n => "Text " + n);
        Assert.Equal([19, 20, 21], pieces.Select(p => p.Ref.Verse).Order());
        Assert.All(pieces, p => Assert.False(p.MultiVerse));
        v.DeleteMark(pieces.Single(p => p.Ref.Verse == 20).Id);   // now only the middle verse can go
        Assert.Equal([19, 21], v.GetMarks("MRK", 7).Select(p => p.Ref.Verse).Order());
    }

    [Fact]
    public void A_gap_or_a_text_mark_blocks_merging()
    {
        var v = new VaultStore(Path.Combine(_dir, "b.db"), "b");
        Whole(v, 15); Whole(v, 17);
        Assert.False(VaultStore.CanMergeMarks(v.GetMarks("MRK", 7)));
        Assert.Null(v.MergeMarks(v.GetMarks("MRK", 7)));
        Assert.Empty(v.SplitMark(v.GetMarks("MRK", 7)[0], _ => ""));   // one verse: nothing to cut
        Assert.False(VaultStore.CanMergeMarks([v.GetMarks("MRK", 7)[0]]));
    }
}

public class MarkExtendTests
{
    // "Wer Ohren hat zu hören, der höre!" – the "!" is at offset 32
    const string Text = "Wer Ohren hat zu hören, der höre!";
    static Mark T(long id, int s, int e, string color = "#E57373") => new(id, "ELB", new VerseRef("MRK", 7, 16), s, e, "", color, "Notiz", null, []);
    static readonly Func<int, string> Raw = _ => Text;
    static MarkLogic.Pos P(int o) => new(16, o);

    [Fact]
    public void A_mark_right_behind_an_old_one_of_the_same_colour_extends_it()
    {
        var old = T(1, 0, 32);
        var g = MarkLogic.Extend([old], "ELB", "#E57373", P(32), P(33), Raw)!.Value;
        Assert.Equal((P(0), P(33)), (g.Start, g.End));
        Assert.Single(g.Absorbed);
        var before = MarkLogic.Extend([T(2, 4, 33)], "ELB", "#E57373", P(0), P(4), Raw)!.Value;   // in front of it
        Assert.Equal((P(0), P(33)), (before.Start, before.End));
        var over = MarkLogic.Extend([T(3, 0, 10)], "ELB", "#E57373", P(5), P(20), Raw)!.Value;   // overlapping
        Assert.Equal((P(0), P(20)), (over.Start, over.End));
    }

    [Fact]
    public void One_blank_between_still_goes_on_but_a_word_in_between_or_another_colour_does_not()
    {
        Assert.NotNull(MarkLogic.Extend([T(1, 0, 3)], "ELB", "#E57373", P(4), P(8), Raw));
        Assert.Null(MarkLogic.Extend([T(1, 0, 3)], "ELB", "#E57373", P(9), P(14), Raw));
        Assert.Null(MarkLogic.Extend([T(1, 0, 32, "#64B5F6")], "ELB", "#E57373", P(32), P(33), Raw));
        Assert.Null(MarkLogic.Extend([T(1, 0, 32)], "ESV", "#E57373", P(32), P(33), Raw));
    }

    [Fact]
    public void A_mark_that_closes_the_gap_between_two_takes_both_in()
    {
        var g = MarkLogic.Extend([T(1, 0, 3), T(2, 9, 14)], "ELB", "#E57373", P(3), P(9), Raw)!.Value;
        Assert.Equal((P(0), P(14)), (g.Start, g.End));
        Assert.Equal(2, g.Absorbed.Count);
    }
}

public class PassageExtractTests
{
    [Fact]
    public void A_pasted_list_with_bullets_and_verse_texts_gives_the_passages()
    {
        var found = BibleStudy.Core.Search.PassageParser.Extract("""
            - Johannes 3,16 – Denn so sehr hat Gott die Welt geliebt
            • Röm 5:8
            1) (Psalm 23)
            1. Mose 1,1-3; Mt 5,3-12
            nur ein Satz ohne Stelle
            """, out var unknown);
        Assert.Equal(["JHN 3:16", "ROM 5:8", "PSA 23", "GEN 1:1-3", "MAT 5:3-12"], found.Select(p => p.Book + " " + p.Chapter + (p.FromVerse is null ? "" : ":" + p.FromVerse + (p.ToVerse > p.FromVerse ? "-" + p.ToVerse : ""))));
        Assert.Equal(["nur ein Satz ohne Stelle"], unknown);
    }
}
