using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using Xunit;

namespace BibleStudy.Tests;

public class SearchEngineTests
{
    static readonly string[] Texts =
    [
        "Und Abraham ging nach Ägypten.",          // 1
        "Mose und Elia redeten mit ihm.",          // 2
        "Gnade und Wahrheit sind durch Jesus.",    // 3
        "Die Gnade des Herrn ist groß.",           // 4
        "Der Engel des HERRN sprach: Ich bin.",    // 5
        "Die Häuser der Stadt und das Haus Gottes.",// 6
        "Sie gingen und waren gegangen.",          // 7
        "Er hütete die Schafe und weidete die Herde.", // 8
    ];

    static List<CorpusVerse> Corpus() => Texts.Select((t, i) => new CorpusVerse(new VerseRef("GEN", 1, i + 1), t)).ToList();
    static int[] Hits(string q, StrongIndex? s = null, bool exact = false) =>
        SearchEngine.Run(Corpus(), q, s, exact).Results.Select(r => r.Ref.Verse).ToArray();

    [Fact]
    public void Words_phrases_or_and_not()
    {
        Assert.Equal([2], Hits("Mose Elia"));
        Assert.Equal([5], Hits("\"Ich bin\""));
        Assert.Equal([5], Hits("\"Engel des HERRN\""));
        Assert.Equal([3, 4], Hits("Gnade"));
        Assert.Equal([2, 3, 4], Hits("Gnade / Mose / Wahrheit"));
        Assert.Equal([4], Hits("Gnade -Wahrheit"));
        Assert.Equal([3, 4], Hits("gnade", exact: true));
    }

    [Fact]
    public void Groups_and_wildcards()
    {
        Assert.Equal([8], Hits("(L=hüten / L=weiden) (Schaf / Herde / Vieh)"));
        Assert.Equal([6], Hits("Haus*"));
        Assert.Equal([6], Hits("*äuser"));
    }

    [Fact]
    public void Lemma_finds_inflected_forms()
    {
        Assert.Equal([6], Hits("L=Haus"));
        Assert.Equal([1, 7], Hits("L=gehen"));
        Assert.Empty(Hits("L=haus"));      // case matters for lemmata
        Assert.Equal([6], Hits("L~haus")); // ~ ignores case
    }

    [Fact]
    public void Strong_numbers()
    {
        var idx = new StrongIndex([
            new("G3056", new VerseRef("GEN", 1, 3), "Wahrheit"), new("G3056", new VerseRef("GEN", 1, 4), "Gnade"),
            new("H241", new VerseRef("GEN", 1, 4), "Herrn"), new("G26", new VerseRef("GEN", 1, 4), "groß")]);
        Assert.Equal([3, 4], Hits("G3056", idx));
        Assert.Equal([4], Hits("G3056 H241", idx));
        Assert.Equal([3], Hits("G3056=Wahrheit", idx));
        Assert.True(SearchEngine.Run(Corpus(), "G3056", null).NeedsStrongs);
    }

    [Fact]
    public void Highlights_hits()
    {
        var r = SearchEngine.Run(Corpus(), "Gnade", null).Results[0];
        Assert.Equal("«Gnade» und Wahrheit sind durch Jesus.", r.Marked());
    }

    [Fact]
    public void Passages()
    {
        var p = PassageParser.Parse("Joh 3,16", out var single)!;
        Assert.True(single); Assert.Equal(new PassageRef("JHN", 3, 16, 16), p[0]);
        Assert.Equal(new PassageRef("PSA", 119, null, null), PassageParser.Parse("Psalm 119", out _)![0]);
        var list = PassageParser.Parse("Joh 15,11; 16,24; 17,13", out single)!;
        Assert.False(single); Assert.Equal(3, list.Count); Assert.Equal(new PassageRef("JHN", 16, 24, 24), list[1]);
        var gospels = PassageParser.Parse("Mt 14,13-21; Mk 6,30-44", out _)!;
        Assert.Equal(new PassageRef("MRK", 6, 30, 44), gospels[1]);
        Assert.False(PassageParser.Parse("Joh 3,16;", out single)!.Count != 1 || single);
        Assert.Null(PassageParser.Parse("Abraham", out _));
        Assert.Null(PassageParser.Parse("Gnade 5", out _));
    }
}
