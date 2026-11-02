using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.Tests;

public class TextStatsTests
{
    static List<CorpusVerse> Corpus()
    {
        var list = new List<CorpusVerse>();
        for (var i = 1; i <= 8; i++) list.Add(new(new VerseRef("ROM", 1, i), "Die Gnade Gottes und die Rechtfertigung durch Glauben"));
        for (var i = 1; i <= 40; i++) list.Add(new(new VerseRef("PSA", 1, i), "Der Herr ist mein Hirte und mir wird nichts mangeln"));
        list.Add(new(new VerseRef("PSA", 2, 1), "Ein Einzelwort Zebaoth steht hier"));
        return list;
    }

    [Fact]
    public void Collocates_are_the_words_that_keep_company_with_the_hit()
    {
        var corpus = Corpus();
        var hits = SearchEngine.Run(corpus, "Gnade", null, true).Results;
        var words = TextStats.Collocates(corpus, hits, 5).Select(x => x.Word).ToList();
        Assert.Contains("rechtfertigung", words);
        Assert.DoesNotContain("gnade", words);
        Assert.DoesNotContain("hirte", words);
    }

    [Fact]
    public void Key_words_of_a_book_are_the_ones_the_others_lack()
    {
        var words = TextStats.KeyWords(Corpus(), ["ROM"]).Select(x => x.Word).ToList();
        Assert.Contains("rechtfertigung", words);
        Assert.DoesNotContain("hirte", words);
    }

    [Fact]
    public void Rare_words_occur_once_in_the_whole_text()
    {
        var rare = TextStats.Rare(Corpus(), ["PSA"]);
        Assert.Contains(rare, r => r.Word == "Zebaoth" && r.Ref.Chapter == 2);
        Assert.DoesNotContain(rare, r => r.Word == "Hirte");
    }

    [Fact]
    public void Log_likelihood_is_zero_when_shares_are_equal_and_grows_with_the_difference()
    {
        Assert.Equal(0, TextStats.LogLikelihood(10, 10, 100, 100), 6);
        Assert.True(TextStats.LogLikelihood(30, 10, 100, 100) > TextStats.LogLikelihood(15, 10, 100, 100));
    }
}
