using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using Xunit;

namespace BibleStudy.Tests;

public class SearchOptionsTests
{
    static readonly (int Chapter, int Verse, string Text)[] Texts =
    [
        (1, 1, "Die Liebe ist langmütig."),             // 0
        (1, 2, "Wir lieben, weil er uns zuerst liebte."),// 1
        (1, 3, "Der HERR ist mein Hirte."),              // 2
        (1, 4, "Der Herr sprach zu seinem Knecht."),     // 3
        (1, 5, "Gnade sei mit euch."),                   // 4
        (1, 6, "Nichts zu sagen hier."),                 // 5
        (1, 7, "Wahrheit ist Licht."),                   // 6
        (2, 1, "Die Wahrheit macht frei."),              // 7
        (2, 2, "Erbarmen und Barmherzigkeit."),          // 8
    ];

    static List<CorpusVerse> Corpus() => Texts.Select(t => new CorpusVerse(new VerseRef("JHN", t.Chapter, t.Verse), t.Text)).ToList();

    static string[] Hits(string q, SearchOptions o, StrongIndex? s = null) =>
        SearchEngine.Run(Corpus(), q, s, o).Results.Select(r => $"{r.Ref.Chapter},{r.Ref.Verse}").ToArray();

    [Fact]
    public void Prefix_whole_suffix_and_contains()
    {
        Assert.Equal(["1,1", "1,2"], Hits("lieb", new(WordMatch.Prefix)));
        Assert.Equal([], Hits("lieb", new(WordMatch.Whole)));
        Assert.Equal(["1,1"], Hits("liebe", new(WordMatch.Whole)));
        Assert.Equal(["1,3", "1,4"], Hits("herr", new(WordMatch.Whole)));
        Assert.Equal(["2,2"], Hits("herzigkeit", new(WordMatch.Suffix)));
        Assert.Equal(["2,2"], Hits("armherz", new(WordMatch.Contains)));
        Assert.Equal(["2,2"], Hits("*armherz*", new(WordMatch.Whole)));
    }

    [Fact]
    public void Case_sensitive_tells_HERR_from_Herr()
    {
        Assert.Equal(["1,3"], Hits("HERR", new(WordMatch.Whole, CaseSensitive: true)));
        Assert.Equal(["1,4"], Hits("Herr", new(WordMatch.Whole, CaseSensitive: true)));
        Assert.Equal(["1,3", "1,4"], Hits("Herr", new(WordMatch.Whole)));
    }

    [Fact]
    public void Words_may_stand_in_neighbouring_verses_or_one_chapter()
    {
        Assert.Equal([], Hits("Gnade Wahrheit", new()));
        Assert.Equal(["1,5", "1,7"], Hits("Gnade Wahrheit", new(Range: 2)));
        Assert.Equal([], Hits("Gnade Wahrheit", new(Range: 1)));
        Assert.Equal(["1,5", "1,7"], Hits("Gnade Wahrheit", new(Range: -1)));   // 2,1 has Wahrheit, but Gnade is in another chapter
    }

    [Fact]
    public void Original_word_finds_every_verse_with_the_same_Strong_number()
    {
        // the translation renders G5485 as "Gnade" in one verse and as "Barmherzigkeit" in another
        var index = new StrongIndex(
        [
            new StrongsOccurrence("G5485", new VerseRef("JHN", 1, 5), "Gnade"),
            new StrongsOccurrence("G5485", new VerseRef("JHN", 2, 2), "Barmherzigkeit"),
        ]);
        Assert.Equal(["1,5", "2,2"], Hits("Gnade", new(Original: true), index));
        Assert.Equal(["1,5", "2,2"], Hits("O=Gnade", new(), index));
        var outcome = SearchEngine.Run(Corpus(), "Gnade", index, new SearchOptions(Original: true));
        Assert.Equal(["G5485"], outcome.Strongs);
        Assert.True(SearchEngine.Run(Corpus(), "Gnade", null, new SearchOptions(Original: true)).NeedsStrongs);
    }
}
