using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using Xunit;

namespace BibleStudy.Tests;

public class ChiasmFinderTests
{
    static List<CorpusVerse> Passage(params string[] texts) =>
        texts.Select((t, i) => new CorpusVerse(new VerseRef("PSA", 1, i + 1), t)).ToList();

    [Fact]
    public void Finds_a_mirror_around_a_single_verse()
    {
        var p = Passage("Barmherzigkeit lebt", "Friede wohnt", "Gerechtigkeit wächst", "Mittelpunkt steht", "Gerechtigkeit blüht", "Friede bleibt", "Barmherzigkeit endet");
        var c = Assert.Single(ChiasmFinder.Find(p));
        Assert.Equal(1, c.From.Verse);
        Assert.Equal(7, c.To.Verse);
        Assert.True(c.SingleCenter);
        Assert.Equal(4, c.CenterFrom.Verse);
        Assert.Equal(["A", "B", "C"], c.Levels.Select(l => l.Label));
        Assert.Equal("barmherzigkeit", c.Levels[0].LeftWord);
        Assert.Equal(7, c.Levels[0].Right.Verse);
    }

    [Fact]
    public void Finds_a_mirror_whose_middle_is_a_pair()
    {
        var p = Passage("Barmherzigkeit lebt", "Friede wohnt", "Gerechtigkeit wächst", "Gerechtigkeit blüht", "Friede bleibt", "Barmherzigkeit endet");
        var c = Assert.Single(ChiasmFinder.Find(p));
        Assert.False(c.SingleCenter);
        Assert.Equal(3, c.CenterFrom.Verse);
        Assert.Equal(4, c.CenterTo.Verse);
    }

    [Fact]
    public void Two_levels_are_not_enough_by_default()
    {
        var p = Passage("Barmherzigkeit lebt", "Friede wohnt", "Mittelpunkt steht", "Friede bleibt", "Barmherzigkeit endet");
        Assert.Empty(ChiasmFinder.Find(p));
        Assert.Single(ChiasmFinder.Find(p, minLevels: 2));
    }

    [Fact]
    public void A_word_that_runs_through_the_passage_is_a_theme_and_no_mirror()
    {
        var p = Passage("Liebe wächst", "Liebe blüht", "Liebe trägt", "Liebe bleibt", "Liebe glaubt", "Liebe hofft", "Liebe endet nie");
        Assert.Empty(ChiasmFinder.Find(p));
    }

    [Fact]
    public void Forms_of_a_word_count_as_the_same_word()
    {
        var p = Passage("Herrn lobt", "Friede wohnt", "Gerechtigkeit wächst", "Mittelpunkt steht", "Gerechtigkeiten blühen", "Friedens bleibt", "Herr endet");
        var c = Assert.Single(ChiasmFinder.Find(p));
        Assert.Equal(3, c.Levels.Count);
    }

    [Fact]
    public void Rare_shared_words_score_higher_than_common_ones()
    {
        var corpus = new List<CorpusVerse>();
        for (var i = 1; i <= 100; i++) corpus.Add(new(new VerseRef("GEN", 1, i), i <= 4 ? "Gerechtigkeit Friede Barmherzigkeit steht hier" : "Etwas ganz anderes steht hier"));
        corpus.Add(new(new VerseRef("GEN", 2, 1), "Zebaoth steht"));
        var df = ChiasmFinder.DocumentFrequencies(corpus);
        var rare = ChiasmFinder.Find(Passage("Zebaoth lebt", "Friede wohnt", "Gerechtigkeit wächst", "Mittelpunkt steht", "Gerechtigkeit blüht", "Friede bleibt", "Zebaoth endet"), df, corpus.Count);
        var common = ChiasmFinder.Find(Passage("Gerechtigkeit lebt", "Friede wohnt", "Barmherzigkeit wächst", "Mittelpunkt steht", "Barmherzigkeit blüht", "Friede bleibt", "Gerechtigkeit endet"), df, corpus.Count);
        Assert.True(rare[0].Score > common[0].Score);
    }

    [Fact]
    public void Short_passages_give_nothing()
    {
        Assert.Empty(ChiasmFinder.Find(Passage("Alpha beta", "Beta alpha")));
    }
}
