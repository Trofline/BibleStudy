using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.Tests;

/// <summary>Which verses a commentary note is about: whole sections (Matthew Henry), a range named at its start (Calvin, Tyndale), or one verse (Gill).</summary>
public class CommentaryRangesTests
{
    static List<CommentaryNote> Notes(params (int Verse, string Text)[] n) => n.Select(x => new CommentaryNote(x.Verse, x.Text)).ToList();

    [Fact]
    public void Sectional_works_run_to_the_next_note_and_the_last_to_the_end_of_the_chapter()
    {
        var r = CommentaryRanges.Of("matthew-henry", Notes((1, "We found …"), (22, "After this …")), 3, 36);
        Assert.Equal([(1, 21), (22, 36)], r);
    }

    [Fact]
    public void A_range_named_at_the_start_of_the_note_counts()
    {
        var r = CommentaryRanges.Of("john-calvin", Notes((1, "John 3:1-6\n\n1. Now there was …"), (7, "John 3:7-12 …")), 3, 36);
        Assert.Equal([(1, 6), (7, 12)], r);
        var t = CommentaryRanges.Of("tyndale", Notes((1, "3:1 John links …"), (16, "3:16-21 God loved …"), (22, "3:22 …")), 3, 36);
        Assert.Equal([(1, 1), (16, 21), (22, 22)], t);
    }

    [Fact]
    public void Verse_by_verse_works_keep_single_verses_so_a_verse_without_note_gets_none()
    {
        var r = CommentaryRanges.Of("john-gill", Notes((5, "a"), (6, "b"), (8, "c")), 3, 36);
        Assert.Equal([(5, 5), (6, 6), (8, 8)], r);
        Assert.DoesNotContain(r, x => x.From <= 7 && 7 <= x.To);
    }

    [Theory]
    [InlineData("This is the sum of the gospel, ver. 16.", 16, true)]
    [InlineData("God so loved the world (v. 16)", 16, true)]
    [InlineData("as in Joh 3:16 and elsewhere", 16, true)]
    [InlineData("16. For God so loved the world", 16, false)]   // "16." is an outline point unless the work numbers verses so
    [InlineData("see Gal 2:16 for this", 16, false)]
    [InlineData("in the year 1616 it was printed", 16, false)]
    public void A_paragraph_that_speaks_of_the_verse_is_found(string text, int verse, bool expected) =>
        Assert.Equal(expected, CommentaryRanges.Mentions(text, 3, verse));

    [Fact]
    public void Numbered_verses_count_only_where_the_work_numbers_them() =>
        Assert.True(CommentaryRanges.Mentions("16. For God so loved the world", 3, 16, numbered: true));
}
