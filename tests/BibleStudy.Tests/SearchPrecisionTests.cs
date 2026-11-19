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
    public void Base_forms_in_an_English_Bible()
    {
        var en = new SearchOptions(Language: "eng");
        var c = Corpus("For God so loved the world.", "He that loveth not knoweth not God.", "Love is patient.", "Thou lovest righteousness.",
            "A lovely place.", "The glove fell.", "Loving one another.", "Beloved, let us love.");
        Assert.Equal([1, 2, 3, 4, 7, 8], Hits(c, "L=love", en));

        var go = Corpus("Jesus went up.", "Go ye therefore.", "He goeth before you.", "They were gone.", "A good man.", "Going forth.");
        Assert.Equal([1, 2, 3, 4, 6], Hits(go, "L=go", en));

        var man = Corpus("The man said.", "Men of Israel.", "The son of man's.", "Manna fell.", "Many came.");
        Assert.Equal([1, 2, 3], Hits(man, "L=man", en));

        var carry = Corpus("He carried it.", "She carries it.", "To carry the ark.", "A carriage.");
        Assert.Equal([1, 2, 3], Hits(carry, "L=carry", en));

        var say = Corpus("Thus saith the LORD.", "He said unto them.", "Saying, Repent.", "A saying.", "Sayest thou this?");
        Assert.Equal([1, 2, 3, 4, 5], Hits(say, "L=say", en));
    }

    [Fact]
    public void English_base_forms_do_not_take_other_words_that_start_alike()
    {
        var en = new SearchOptions(Language: "eng");
        var c = Corpus("Go ye.", "In the beginning God created.", "He goeth.", "Thou shalt surely die.", "He did so.", "He died.",
            "The man came.", "In this manner.", "They sinned.", "The sinners.", "He loved her.", "Her lover.");
        Assert.Equal([1, 3], Hits(c, "L=go", en));       // not God
        Assert.Equal([4, 6], Hits(c, "L=die", en));      // not did
        Assert.Equal([7], Hits(c, "L=man", en));         // not manner
        Assert.Equal([9], Hits(c, "L=sin", en));         // sinners are another word
        Assert.Equal([11], Hits(c, "L=love", en));
    }

    [Fact]
    public void A_word_behind_a_translated_word_counts_only_when_it_is_rendered_so_often_enough()
    {
        var occ = new List<BibleStudy.Core.Models.StrongsOccurrence>();
        for (var i = 1; i <= 120; i++) occ.Add(new("G5485", new VerseRef("ROM", 1, i), "grace"));
        for (var i = 1; i <= 30; i++) occ.Add(new("G5485", new VerseRef("ROM", 2, i), "favour"));
        for (var i = 1; i <= 400; i++) occ.Add(new("H853", new VerseRef("GEN", 1, i), "him"));
        occ.Add(new("H853", new VerseRef("GEN", 2, 1), "find grace in his sight"));   // once, inside a longer rendering
        occ.Add(new("H9999", new VerseRef("GEN", 3, 1), "grace"));                    // a rare word, rendered so its only time
        var idx = new StrongIndex(occ);
        Assert.Equal(["G5485", "H9999"], idx.IdsForWords(w => w == "grace").Order());
    }

    [Fact]
    public void Other_languages_take_the_ending_of_the_base_form_off()
    {
        var c = Corpus("Dios amó al mundo.", "Amamos porque él nos amó.", "Un amigo fiel.", "Amarás al Señor.", "Amaba a su hijo.");
        Assert.Equal([1, 2, 4, 5], Hits(c, "L=amar", new SearchOptions(Language: "spa")));
    }

    [Fact]
    public void A_search_limited_to_passages_and_whole_books()
    {
        var verses = new[] { new VerseRef("ROM", 1, 1), new VerseRef("ROM", 8, 39), new VerseRef("ROM", 9, 1), new VerseRef("GAL", 6, 18), new VerseRef("EPH", 1, 1) };
        var rest = ScopeFilter.Extract("Gnade in:\"Röm 1-8; Gal\"", out var passages, out var error);
        Assert.Null(error);
        Assert.Equal("Gnade", rest);
        Assert.Equal([true, true, false, true, false], verses.Select(v => ScopeFilter.Contains(passages!, v)));

        ScopeFilter.Extract("in:\"Joh 15,11; 16,24; Offb\"", out passages, out error);
        Assert.Null(error);
        Assert.True(ScopeFilter.Contains(passages!, new VerseRef("JHN", 16, 24)));
        Assert.False(ScopeFilter.Contains(passages!, new VerseRef("JHN", 16, 25)));
        Assert.True(ScopeFilter.Contains(passages!, new VerseRef("REV", 22, 21)));

        ScopeFilter.Extract("in:\"1. Mose; Mt 5-7\"", out passages, out error);
        Assert.Null(error);
        Assert.True(ScopeFilter.Contains(passages!, new VerseRef("GEN", 50, 26)));
        Assert.True(ScopeFilter.Contains(passages!, new VerseRef("MAT", 7, 29)));
        Assert.False(ScopeFilter.Contains(passages!, new VerseRef("MAT", 8, 1)));

        ScopeFilter.Extract("in:\"Röm 1; Quatsch\"", out _, out error);
        Assert.NotNull(error);
    }

    [Fact]
    public void A_similar_spelling_does_not_count_an_umlaut_as_a_mistake()
    {
        var c = Corpus("Die Könige kamen.", "Der König sprach.", "Die Kinder spielten.");
        Assert.Equal([1, 2], Hits(c, "~Konige"));
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
