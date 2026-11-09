using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class MarkLogicTests
{
    [Fact]
    public void Trim_cuts_the_blanks_at_both_ends_but_keeps_those_between_words()
    {
        var text = "Am Anfang schuf Gott";
        // " schuf Gott" selected from the blank before "schuf"
        var t = MarkLogic.Trim(text, 9, text.Length)!.Value;
        Assert.Equal("schuf Gott", text[t.Start..t.End]);
        // "Am Anfang " with the blank at the end
        t = MarkLogic.Trim(text, 0, 10)!.Value;
        Assert.Equal("Am Anfang", text[t.Start..t.End]);
        Assert.Null(MarkLogic.Trim(text, 2, 3));   // only a blank
    }

    [Fact]
    public void Runs_join_neighbouring_verses()
    {
        Assert.Equal([(3, 3), (5, 7)], MarkLogic.Runs([7, 3, 5, 6, 6]));
        Assert.Empty(MarkLogic.Runs([]));
    }

    [Fact]
    public void A_mark_without_its_translation_is_found_by_its_words_nearest_to_its_old_place()
    {
        var stored = new Mark(9, "gone", new VerseRef("GEN", 1, 4), 30, 33, "und", "#FFD54F", "", null, []);
        Assert.Equal((33, 36), MarkLogic.ProjectByStoredText(stored, "Licht und Finsternis schied Gott und Tag"));
        Assert.Null(MarkLogic.ProjectByStoredText(stored with { Text = "Gnade" }, "Licht und Finsternis"));
        Assert.Null(MarkLogic.ProjectByStoredText(stored with { EndVerse = 6 }, "Licht und Finsternis"));
        Assert.Null(MarkLogic.ProjectByStoredText(stored with { Start = -1 }, "Licht und Finsternis"));
    }

    [Fact]
    public void A_mark_over_several_verses_covers_the_ends_partly_and_the_middle_wholly()
    {
        var m = new Mark(1, "*", new VerseRef("GEN", 1, 4), 10, 7, "x", "#FFD54F", "", null, [], 6);
        Assert.True(m.MultiVerse); Assert.Equal(6, m.LastVerse);
        Assert.Equal((10, 40), MarkLogic.PartIn(m, 4, 40));
        Assert.Equal((0, 25), MarkLogic.PartIn(m, 5, 25));
        Assert.Equal((0, 7), MarkLogic.PartIn(m, 6, 30));
        Assert.Null(MarkLogic.PartIn(m, 7, 30));
    }

    static Segment[] Seg(params (string Text, string? Strong)[] parts) => parts.Select(p => new Segment(p.Text, false, p.Strong is null ? null : [p.Strong])).ToArray();

    [Fact]
    public void Project_finds_the_same_words_in_a_translation_that_reads_alike()
    {
        var a = Seg(("Am Anfang schuf Gott Himmel und Erde.", null));
        var b = Seg(("Im Anfang schuf Gott Himmel und Erde.", null));
        var r = MarkLogic.Project(a, 10, 20, b)!.Value;   // "schuf Gott"
        Assert.Equal("schuf Gott", MarkLogic.RawText(b)[r.Start..r.End]);
    }

    [Fact]
    public void Project_follows_Strong_numbers_when_the_words_differ()
    {
        var a = Seg(("In the beginning ", null), ("God", "H430"), (" created ", "H1254"), ("the heaven", "H8064"));
        var b = Seg(("Am Anfang schuf ", null), ("Gott", "H430"), (" ", null), ("Himmel", "H8064"), (" und Erde", null), (" erschaffen", "H1254"));
        // "God created": H430 + H1254 (the first test text is "In the beginning God created the heaven")
        var r = MarkLogic.Project(a, 17, 29, b);
        // the hits lie too far apart (Gott … erschaffen) for a two-word mark, so no guess is made
        Assert.Null(r);
        var c = Seg(("Am Anfang ", null), ("schuf", "H1254"), (" ", null), ("Gott", "H430"), (" Himmel", null));
        var r2 = MarkLogic.Project(a, 17, 29, c)!.Value;
        Assert.Equal("schuf Gott", MarkLogic.RawText(c)[r2.Start..r2.End]);
    }

    [Fact]
    public void Project_gives_nothing_when_it_cannot_be_sure()
    {
        var a = Seg(("alpha beta gamma", null));
        var b = Seg(("one two three four five six seven eight", null));   // far longer: a guess by position would be wrong
        Assert.Null(MarkLogic.Project(a, 0, 5, b));
    }

    [Fact]
    public void Project_guesses_by_position_when_neither_text_has_Strong_numbers()
    {
        var a = Seg(("Da sprach Gott: Es werde Licht, und es ward Licht.", null));
        var b = Seg(("Und Gott sagte: Es werde Licht! Und es wurde Licht.", null));
        var start = MarkLogic.RawText(a).IndexOf("Gott", StringComparison.Ordinal);
        var r = MarkLogic.Project(a, start, start + 4, b)!.Value;
        Assert.Equal("Gott", MarkLogic.RawText(b)[r.Start..r.End]);
    }

    [Fact]
    public void Position_guess_refuses_long_marks_and_verses_of_very_different_length()
    {
        var text = "eins zwei drei vier fünf sechs sieben acht neun zehn";
        Assert.Null(MarkLogic.ProjectByPosition(text, (0, 30), text));            // six words marked
        Assert.Null(MarkLogic.ProjectByPosition(text, (0, 4), "nur drei Worte")); // far shorter
    }

    [Fact]
    public void Sub_marks_are_those_of_the_sub_color_inside_a_marked_verse()
    {
        Mark M(long id, int verse, int start, string color) => new(id, "x", new VerseRef("GEN", 1, verse), start, start < 0 ? -1 : start + 4, "", color, "", null, []);
        var marks = new List<Mark> { M(1, 3, -1, "#F26D6D"), M(2, 3, 5, "#112233"), M(3, 4, 5, "#112233"), M(4, 3, 5, "#FFD54F") };
        Assert.Equal([2L], MarkLogic.SubMarkIds(marks, "#112233"));   // verse 4 has no marked verse around it; mark 4 has another color
    }

    [Fact]
    public void Vault_name_is_read_from_the_export()
    {
        Assert.Equal("Predigt", MarkLogic.VaultNameFromExport("Predigt\n", null));
        Assert.Equal("Meine Studie", MarkLogic.VaultNameFromExport(null, "BibleStudy vault \"Meine Studie\", exported 2026-11-08 10:00"));
        Assert.Null(MarkLogic.VaultNameFromExport("", "no quotes here"));
        Assert.True(MarkLogic.IsValidVaultName("Predigt 2026"));
        Assert.False(MarkLogic.IsValidVaultName("a/b"));
        Assert.False(MarkLogic.IsValidVaultName("CON"));
        Assert.False(MarkLogic.IsValidVaultName(".."));
    }
}

public class MarkCarveTests
{
    static Mark Text(int first, int start, int last, int end) =>
        new(1, "t", new VerseRef("GEN", 1, first), start, end, "x", "#FFD54F", "", null, [], last);
    static int Len(int verse) => 30;

    [Fact]
    public void Cutting_the_middle_of_a_mark_leaves_two_pieces()
    {
        var left = MarkLogic.CarveText(Text(2, 5, 2, 25), [new(2, 10, 15)], Len);
        Assert.Equal([new MarkLogic.Spec(2, 5, 2, 10), new MarkLogic.Spec(2, 15, 2, 25)], left);
    }

    [Fact]
    public void Cutting_all_of_a_mark_leaves_nothing_and_cutting_elsewhere_leaves_it_whole()
    {
        Assert.Empty(MarkLogic.CarveText(Text(2, 5, 2, 25), [new(2, 0, 30)], Len));
        Assert.Equal([new MarkLogic.Spec(2, 5, 2, 25)], MarkLogic.CarveText(Text(2, 5, 2, 25), [new(3, 0, 30)], Len));
    }

    [Fact]
    public void A_mark_over_three_verses_loses_its_middle_verse_and_falls_into_two()
    {
        var left = MarkLogic.CarveText(Text(4, 10, 6, 7), [new(5, 0, 30)], Len);
        Assert.Equal([new MarkLogic.Spec(4, 10, 4, 30), new MarkLogic.Spec(6, 0, 6, 7)], left);
    }

    [Fact]
    public void Cutting_the_start_of_a_multi_verse_mark_keeps_the_rest_as_one_mark()
    {
        var left = MarkLogic.CarveText(Text(4, 10, 6, 7), [new(4, 0, 30)], Len);
        Assert.Equal([new MarkLogic.Spec(5, 0, 6, 7)], left);
    }

    [Fact]
    public void Cutting_verses_out_of_a_mark_of_whole_verses_leaves_the_runs_that_remain()
    {
        var m = new Mark(1, "*", new VerseRef("GEN", 1, 3), -1, -1, "x", "#FFD54F", "", null, [], 8);
        var left = MarkLogic.CarveWhole(m, new HashSet<int> { 5, 6 });
        Assert.Equal([new MarkLogic.Spec(3, -1, 4, -1), new MarkLogic.Spec(7, -1, 8, -1)], left);
    }
}
