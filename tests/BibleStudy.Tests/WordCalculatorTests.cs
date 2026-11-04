using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using Xunit;

namespace BibleStudy.Tests;

public class WordCalculatorTests
{
    static List<CorpusVerse> Corpus() =>
    [
        new(new VerseRef("ROM", 1, 1), "Paulus, ein Knecht Jesu Christi, berufen."),            // 6 words, 1 Christ in "Jesu Christi"
        new(new VerseRef("ROM", 1, 2), "Christus ist unser Friede und Jesus Christus lebt."),   // 1 alone, 1 in "Jesus Christus"
        new(new VerseRef("PHM", 1, 1), "Paulus, Christus Jesus gehört zu uns."),                // 1 alone ("Christus Jesus" is a pair)
        new(new VerseRef("PHM", 1, 2), "Gnade sei mit euch."),
    ];

    static readonly SearchOptions Prefix = new(WordMatch.Prefix);

    [Fact]
    public void Counts_hits_per_book_and_words_per_book()
    {
        var r = WordCalculator.Run(Corpus(), "Christ", "", null, Prefix);
        var rom = Assert.Single(r.Rows, x => x.Book == "ROM");
        Assert.Equal(3, rom.Count);
        Assert.Equal(0, rom.Subtracted);
        Assert.Equal(14, rom.Words);
        Assert.Equal(3 * 1000.0 / 14, rom.Per1000, 6);
    }

    [Fact]
    public void Subtracts_only_hits_inside_a_hit_of_the_second_search()
    {
        var r = WordCalculator.Run(Corpus(), "Christus", "\"Jesus Christus\" OR \"Christus Jesus\"", null, new(WordMatch.Whole));
        var rom = Assert.Single(r.Rows, x => x.Book == "ROM");
        Assert.Equal(2, rom.Count);   // "Christus ist unser Friede" stays, "Jesus Christus" goes
        Assert.Equal(1, rom.Rest);
        var phm = Assert.Single(r.Rows, x => x.Book == "PHM");
        Assert.Equal(0, phm.Rest);
    }

    [Fact]
    public void Reports_a_broken_query_instead_of_throwing()
    {
        var r = WordCalculator.Run(Corpus(), "-Christus", "", null, Prefix);
        Assert.NotNull(r.Error);
        Assert.Empty(r.Rows);
    }

    [Fact]
    public void Counts_a_second_word_per_book_for_a_ratio()
    {
        var r = WordCalculator.Run(Corpus(), "Christus", "", null, new(WordMatch.Whole), "Jesus");
        var rom = Assert.Single(r.Rows, x => x.Book == "ROM");
        Assert.Equal(2, rom.Count);
        Assert.Equal(1, rom.Other);
        Assert.Equal(2.0, rom.Ratio, 6);
        var phm = Assert.Single(r.Rows, x => x.Book == "PHM");
        Assert.Equal(1, phm.Other);
    }

    [Fact]
    public void A_book_with_only_the_comparison_word_still_gets_a_row()
    {
        var corpus = new List<CorpusVerse> { new(new VerseRef("MAT", 1, 1), "Jesus kam"), new(new VerseRef("ROM", 1, 1), "Christus lebt") };
        var r = WordCalculator.Run(corpus, "Christus", "", null, new(WordMatch.Whole), "Jesus");
        var mat = Assert.Single(r.Rows, x => x.Book == "MAT");
        Assert.Equal(0, mat.Count);
        Assert.Equal(1, mat.Other);
    }

    [Fact]
    public void Chapters_hold_the_remaining_hits_per_chapter()
    {
        var corpus = new List<CorpusVerse> { new(new VerseRef("ROM", 1, 1), "Christus lebt"), new(new VerseRef("ROM", 8, 1), "Christus Christus"), new(new VerseRef("ROM", 8, 2), "Jesus Christus") };
        var r = WordCalculator.Run(corpus, "Christus", "\"Jesus Christus\"", null, new(WordMatch.Whole));
        Assert.Equal(1, r.Chapters![("ROM", 1)]);
        Assert.Equal(2, r.Chapters[("ROM", 8)]);
    }

    [Fact]
    public void Books_can_be_put_together_by_writer()
    {
        var r = WordCalculator.Run(Corpus(), "Christus", "", null, new(WordMatch.Whole));
        var order = new[] { "ROM", "PHM" };
        var grouped = WordCalculator.Group(r.Rows, _ => "Paulus", order);
        var one = Assert.Single(grouped);
        Assert.Equal("Paulus", one.Book);
        Assert.Equal(r.Rows.Sum(x => x.Count), one.Count);
        Assert.Equal(r.Rows.Sum(x => x.Words), one.Words);
        Assert.Equal("ROM", one.First!.Value.Book);
    }

    [Fact]
    public void Strong_marks_inside_the_text_are_not_counted_as_words()
    {
        var corpus = new List<CorpusVerse> { new(new VerseRef("LUK", 23, 42), "Herr G2962 TR gedenke meiner H430") };
        var r = WordCalculator.Run(corpus, "Herr", "", null, new(WordMatch.Whole));
        Assert.Equal(3, Assert.Single(r.Rows).Words);   // Herr gedenke meiner; G2962, TR and H430 are marks
    }
}
