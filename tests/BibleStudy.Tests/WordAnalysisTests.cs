using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.Tests;

public class WordAnalysisTests
{
    static StrongsOccurrence O(string id, string book, int ch, int v, string word = "w") => new(id, new VerseRef(book, ch, v), word);

    static WordAnalyzer Sample() => new(
    [
        O("G0025", "JHN", 3, 16, "loved"), O("G2316", "JHN", 3, 16, "God"), O("G2889", "JHN", 3, 16, "world"), O("G3588", "JHN", 3, 16, "the"),
        O("G0025", "JHN", 3, 35, "loveth"), O("G2316", "JHN", 3, 34, "God"), O("G3588", "JHN", 3, 35, "the"), O("G3588", "JHN", 3, 34, "the"),
        O("G0025", "1JN", 4, 8, "love"), O("G2316", "1JN", 4, 8, "God"), O("G3588", "1JN", 4, 8, "the"), O("G9999", "JHN", 3, 1, "rare"),
        O("G0025", "1JN", 4, 9, "love"), O("G2316", "1JN", 4, 9, "God"), O("G3588", "1JN", 4, 9, "the"),
    ]);

    [Theory]
    [InlineData("g25", "G25")]
    [InlineData("G0025a", "G25")]
    [InlineData("H0157", "H157")]
    [InlineData("hello", null)]
    public void Ids_are_normalized(string input, string? expected) => Assert.Equal(expected, WordAnalyzer.Normalize(input));

    [Fact]
    public void Counts_and_related_numbers()
    {
        var a = Sample();
        Assert.Equal(4, a.Count("G25"));
        Assert.Equal(["G25"], WordAnalyzer.Related("from G25 (ἀγαπάω)", "G26"));
        Assert.Empty(WordAnalyzer.Related("from G26", "G26"));
    }

    [Fact]
    public void Cooccurring_words_skip_the_word_itself()
    {
        var a = Sample();
        var partners = a.CoOccurring("G25").Select(p => p.Id).ToList();
        Assert.DoesNotContain("G25", partners);
        Assert.Contains("G2316", partners);
    }

    [Fact]
    public void Keywords_prefer_characteristic_words_over_filler()
    {
        var a = Sample();
        var keys = a.Keywords("1JN", 4).Select(k => k.Id).ToList();
        Assert.Contains("G25", keys);
        Assert.Contains(a.Hapax("JHN", 3), h => h.Id == "G9999");
    }
}
