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
}
