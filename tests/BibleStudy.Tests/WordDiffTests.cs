using BibleStudy.Core;

namespace BibleStudy.Tests;

public class WordDiffTests
{
    static string[] Marked(string other, string text) => WordDiff.Differing(other, text).Select(r => text[r.Start..r.End]).ToArray();

    [Fact]
    public void Same_text_has_no_differences_and_case_and_punctuation_do_not_count()
    {
        Assert.Empty(WordDiff.Differing("Im Anfang schuf Gott Himmel und Erde.", "im Anfang, schuf Gott Himmel und Erde"));
    }

    [Fact]
    public void Changed_words_are_found_in_the_second_text()
    {
        Assert.Equal(["erschuf"], Marked("Im Anfang schuf Gott die Himmel", "Im Anfang erschuf Gott die Himmel"));
        Assert.Equal(["Weltall"], Marked("Gott schuf das All", "Gott schuf das Weltall"));
    }

    [Fact]
    public void Words_that_are_only_in_the_second_text_and_neighbours_are_one_stretch()
    {
        var text = "Denn Gott hat so sehr die Welt geliebt";
        var r = WordDiff.Differing("Denn Gott hat die Welt geliebt", text);
        Assert.Equal(["so sehr"], r.Select(x => text[x.Start..x.End]).ToArray());
    }

    [Fact]
    public void A_moved_word_is_found_and_empty_texts_are_fine()
    {
        Assert.NotEmpty(Marked("Gott sprach: Es werde Licht", "Es werde Licht, sprach: Gott"));
        Assert.Empty(WordDiff.Differing("", ""));
        Assert.Equal(["Alles"], Marked("", "Alles"));
    }
}
