using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.Tests;

public class WordChartDataTests
{
    static WordAnalyzer Analyzer() => new([
        new StrongsOccurrence("H1", new VerseRef("GEN", 1, 1), "Wagen"), new StrongsOccurrence("H1", new VerseRef("GEN", 1, 2), "wagen,"),
        new StrongsOccurrence("H1", new VerseRef("EXO", 3, 1), "Reiter"), new StrongsOccurrence("H2", new VerseRef("EXO", 3, 1), "Pferd"),
    ]);

    [Fact]
    public void Book_counts_count_every_occurrence()
    {
        var c = Analyzer().BookCounts("H1");
        Assert.Equal(2, c["GEN"]);
        Assert.Equal(1, c["EXO"]);
        Assert.False(c.ContainsKey("LEV"));
    }

    [Fact]
    public void Renderings_group_the_words_without_case_and_punctuation()
    {
        var r = Analyzer().Renderings("H1");
        Assert.Equal([("wagen", 2), ("reiter", 1)], r);
    }
}
