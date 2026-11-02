using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.Tests;

/// <summary>Word distance in a search: NAH5 (either order) and DANN5 (second word follows the first).</summary>
public class SearchNearTests
{
    static List<CorpusVerse> Corpus() =>
    [
        new(new VerseRef("JHN", 1, 1), "Gnade und Wahrheit sind durch Jesus Christus geworden"),
        new(new VerseRef("JHN", 1, 2), "Die Wahrheit macht euch frei und die Gnade bleibt"),
        new(new VerseRef("JHN", 1, 3), "Gnade a b c d e f g h Wahrheit"),
        new(new VerseRef("JHN", 1, 4), "Dann kam die Gnade"),
    ];

    static int[] Verses(string q) => SearchEngine.Run(Corpus(), q, null, true).Results.Select(r => r.Ref.Verse).ToArray();

    [Fact] public void Near_finds_both_orders_within_the_distance() => Assert.Equal([1, 2], Verses("Gnade NAH6 Wahrheit"));
    [Fact] public void Three_words_apart() => Assert.Equal([1], Verses("Gnade NAH3 Wahrheit"));
    [Fact] public void Adjacent_words_are_one_apart() => Assert.Equal([1], Verses("Gnade DANN1 und"));
    [Fact] public void Ordered_search_needs_the_second_word_after_the_first() => Assert.Equal([1, 3], Verses("Gnade DANN9 Wahrheit"));
    [Fact] public void Too_far_apart_is_no_hit() => Assert.Equal([1], Verses("Gnade NAH5 Wahrheit"));
    [Fact] public void English_names_work() => Assert.Equal([1], Verses("Gnade NEAR3 Wahrheit"));
    [Fact] public void A_bare_dann_stays_an_ordinary_word() => Assert.Equal([4], Verses("dann"));
    [Fact] public void Phrases_and_groups_can_be_sides() => Assert.Equal([1], Verses("\"Gnade und\" NAH2 (Wahrheit / Frieden)"));

    [Fact]
    public void Both_words_are_marked()
    {
        var r = SearchEngine.Run(Corpus(), "Gnade NAH3 Wahrheit", null, true).Results[0];
        Assert.Equal("«Gnade» und «Wahrheit» sind durch Jesus Christus geworden", r.Marked());
    }
}
