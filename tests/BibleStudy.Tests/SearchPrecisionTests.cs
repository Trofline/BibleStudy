using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.Tests;

/// <summary>The search finds what was asked for and nothing else: umlauts count, base forms take only real German endings, texts stored oddly are found too.</summary>
public class SearchPrecisionTests
{
    static List<CorpusVerse> Corpus(params string[] texts) => texts.Select((t, i) => new CorpusVerse(new VerseRef("GEN", 1, i + 1), t)).ToList();

    static int[] Hits(List<CorpusVerse> corpus, string q, SearchOptions? o = null) =>
        SearchEngine.Run(corpus, q, null, o ?? new SearchOptions()).Results.Select(r => r.Ref.Verse).ToArray();

    [Fact]
    public void Umlauts_and_sharp_s_are_letters_of_their_own()
    {
        var c = Corpus("Das ist schön.", "Er war schon da.", "Er sagte, daß er kommt.", "Die Mütter weinten.", "Die Mutter stand dort.");
        Assert.Equal([1], Hits(c, "schön"));
        Assert.Equal([2], Hits(c, "schon"));
        Assert.Equal([1], Hits(c, "das"));            // not "daß"
        Assert.Equal([3], Hits(c, "daß"));
        Assert.Equal([5], Hits(c, "Mutter"));
        Assert.Equal([4], Hits(c, "Mütter"));
        Assert.Equal([4, 5], Hits(c, "M?tter"));
        // when they should not count, it can be said so
        var loose = new SearchOptions(Accents: false);
        Assert.Equal([1, 2], Hits(c, "schon", loose));
        Assert.Equal([4, 5], Hits(c, "Mutter", loose));
    }

    [Fact]
    public void A_text_that_stores_umlauts_as_two_characters_is_found_and_marked()
    {
        var c = Corpus("Er ging über den Jordan.", "Der König sprach.");
        Assert.Equal([1], Hits(c, "über"));
        Assert.Equal([2], Hits(c, "König", new SearchOptions(WordMatch.Whole)));
        var hit = SearchEngine.Run(c, "über", null, new SearchOptions()).Results.Single();
        Assert.Equal("Er ging «über» den Jordan.", hit.Marked());
        Assert.Equal([1], Hits(c, "\"ging über den\""));
        Assert.Equal([1], Hits(c, "über"));   // typed the odd way too
    }

    [Fact]
    public void A_base_form_finds_its_forms_and_not_other_words_that_start_alike()
    {
        var c = Corpus(
            "Er geht hinaus.",                 // 1
            "Das ist ein Geheimnis und geheim.",// 2
            "Sie sind gegangen.",              // 3
            "Die Gehenden und die Gehorsamen.",// 4
            "Er ging fort.");                  // 5
        Assert.Equal([1, 3, 5], Hits(c, "L=gehen"));

        var love = Corpus("Ich liebe dich.", "Er liebte sie.", "Sie ist geliebt.", "Ein liebliches Lied.", "Der Liebende wartet.", "Die Geliebten kamen.");
        Assert.Equal([1, 2, 3], Hits(love, "L=lieben"));
        Assert.Equal([1, 2, 3, 5, 6], Hits(love, "L~lieben"));   // without capitals, also the nouns made of it

        var house = Corpus("Das Haus stand.", "Die Häuser der Stadt.", "In den Häusern.", "Der Haushalter kam.", "Des Hauses Tür.");
        Assert.Equal([1, 2, 3, 5], Hits(house, "L=Haus"));

        var father = Corpus("Der Vater sprach.", "Unsere Väter aßen.", "Den Vätern gab er.", "Des Vaters Haus.", "Vaterland.");
        Assert.Equal([1, 2, 3, 4], Hits(father, "L=Vater"));

        var be = Corpus("Er ist da.", "Seine Jünger kamen.", "Sie waren dort.", "Das soll sein.");
        Assert.Equal([1, 3, 4], Hits(be, "L=sein"));   // seine is not a form of sein

        var walk = Corpus("Sie wanderten lange.", "Er ist gewandert.", "Wir wandern.", "Der Wanderstab.");
        Assert.Equal([1, 2, 3], Hits(walk, "L=wandern"));
    }

    [Fact]
    public void The_counts_are_exact()
    {
        var c = Corpus("Gott sprach zu Gott, und Gottes Geist.", "Die Gottlosen und der Gott.");
        var whole = SearchEngine.Run(c, "Gott", null, new SearchOptions(WordMatch.Whole)).Results;
        Assert.Equal(3, whole.Sum(r => r.Spans.Count));
        var start = SearchEngine.Run(c, "Gott", null, new SearchOptions(WordMatch.Prefix)).Results;
        Assert.Equal(5, start.Sum(r => r.Spans.Count));
        Assert.Equal([("gott", 3), ("gottes", 1), ("gottlosen", 1)], HitViews.WordForms(start));
    }
}
