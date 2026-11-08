using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.Tests;

/// <summary>The search syntax known from Logos: ? wildcard, wildcards in phrases, BEFORE / AFTER / NEAR / WITHIN, NOT / ANDNOT.</summary>
public class SearchLogosSyntaxTests
{
    static List<CorpusVerse> Corpus() =>
    [
        new(new VerseRef("JHN", 1, 1), "Gnade und Wahrheit sind durch Jesus Christus geworden"),
        new(new VerseRef("JHN", 1, 2), "Die Wahrheit macht euch frei und die Gnade bleibt"),
        new(new VerseRef("JHN", 1, 3), "Gnade a b c d e f g h Wahrheit"),
        new(new VerseRef("JHN", 1, 4), "Der Engel des Herrn sprach und der Engel Gottes kam"),
        new(new VerseRef("JHN", 1, 5), "Mann Maus Mus Mensch"),
    ];

    static int[] Verses(string q) => SearchEngine.Run(Corpus(), q, null, true).Results.Select(r => r.Ref.Verse).ToArray();

    [Fact] public void Question_mark_is_exactly_one_letter() => Assert.Equal([5], Verses("M?s"));
    [Fact] public void Question_mark_inside_a_word() => Assert.Equal([5], Verses("Ma?n"));
    [Fact] public void Question_mark_at_the_end_is_only_punctuation() => Assert.Equal([1, 2, 3], Verses("Gnade?"));
    [Fact] public void Star_in_a_phrase_stands_for_one_word() => Assert.Equal([4], Verses("\"Engel * Herrn\""));
    [Fact] public void Phrase_wildcards_match_by_pattern() => Assert.Equal([4], Verses("\"Engel G*\""));
    [Fact] public void Before_means_in_that_order() => Assert.Equal([1, 3], Verses("Gnade BEFORE Wahrheit"));
    [Fact] public void After_is_the_other_way_round() => Assert.Equal([2], Verses("Gnade AFTER Wahrheit"));
    [Fact] public void Before_takes_a_distance() => Assert.Equal([1], Verses("Gnade BEFORE 2 WORDS Wahrheit"));
    [Fact] public void Within_ignores_the_order() => Assert.Equal([1, 2], Verses("Gnade WITHIN 6 WORDS Wahrheit"));
    [Fact] public void Near_has_a_built_in_distance() => Assert.Equal([1, 2], Verses("Gnade NEAR Wahrheit"));
    [Fact] public void Not_excludes_a_word() => Assert.Equal([1, 3], Verses("Gnade NOT bleibt"));
    [Fact] public void Andnot_excludes_a_word() => Assert.Equal([1, 3], Verses("Gnade ANDNOT bleibt"));
    [Fact] public void And_is_the_same_as_a_space() => Assert.Equal([1], Verses("Gnade AND Jesus"));
    [Fact] public void Lowercase_operators_stay_ordinary_words() => Assert.Empty(Verses("Gnade before Wahrheit"));
}

/// <summary>Second round: distances in verses, NOT WITHIN, fuzzy words, passage ranges and grammar descriptions.</summary>
public class SearchLogosSyntax2Tests
{
    static List<CorpusVerse> Corpus() =>
    [
        new(new VerseRef("MAT", 1, 1), "Gnade und Friede"),
        new(new VerseRef("MAT", 1, 2), "Nichts davon hier"),
        new(new VerseRef("MAT", 1, 3), "Wahrheit und Licht"),
        new(new VerseRef("MAT", 1, 9), "Wahrheit allein"),
        new(new VerseRef("MAT", 2, 1), "Gnade Wahrheit bleiben"),
        new(new VerseRef("MAT", 2, 2), "Wahrheit weit weg von Gnade und nun etwas mehr Worte dazwischen bis zum Ende hin"),
    ];

    static int[] Hits(string q) => SearchEngine.Run(Corpus(), q, null, true).Results.Select(r => r.Ref.Chapter * 10 + r.Ref.Verse).ToArray();

    [Fact] public void Within_verses_looks_across_verses() => Assert.Equal([11, 13], Hits("Gnade WITHIN 2 VERSES Wahrheit").Where(x => x < 20).ToArray());
    [Fact] public void Too_many_verses_apart_is_no_hit() => Assert.DoesNotContain(19, Hits("Gnade WITHIN 2 VERSES Wahrheit"));
    [Fact] public void Within_the_chapter() => Assert.Contains(19, Hits("Gnade WITHIN 1 CHAPTER Wahrheit"));
    [Fact] public void Within_zero_verses_means_the_same_verse() => Assert.Equal([21, 22], Hits("Gnade WITHIN 0 VERSES Wahrheit").Where(x => x > 20).ToArray());
    [Fact] public void Not_within_words_keeps_the_far_ones() => Assert.Equal([22], Hits("Gnade NOT WITHIN 3 WORDS Wahrheit").Where(x => x > 20).ToArray());
    [Fact] public void Fuzzy_allows_a_spelling_difference() => Assert.Equal([11, 21], Hits("~Gnaden").Where(x => x != 22).ToArray());
    [Fact] public void Fuzzy_is_strict_for_short_words() => Assert.Empty(Hits("~Wasser"));

    [Fact]
    public void Passage_range_is_taken_out_of_the_query()
    {
        var rest = ScopeFilter.Extract("Gnade in:\"Mt 1-2\"", out var p, out var err);
        Assert.Null(err);
        Assert.Equal("Gnade", rest);
        Assert.True(ScopeFilter.Contains(p!, new VerseRef("MAT", 2, 7)));
        Assert.False(ScopeFilter.Contains(p!, new VerseRef("MAT", 3, 1)));
        Assert.False(ScopeFilter.Contains(p!, new VerseRef("JHN", 1, 1)));
    }

    [Fact]
    public void Passage_range_can_be_verses_or_a_whole_book()
    {
        ScopeFilter.Extract("in:\"Mt 1,2-3\"", out var verses, out _);
        Assert.True(ScopeFilter.Contains(verses!, new VerseRef("MAT", 1, 3)));
        Assert.False(ScopeFilter.Contains(verses!, new VerseRef("MAT", 1, 4)));
        ScopeFilter.Extract("Gnade in:Joh", out var book, out var err);
        Assert.Null(err);
        Assert.True(ScopeFilter.Contains(book!, new VerseRef("JHN", 21, 25)));
    }

    [Fact] public void Unknown_range_is_reported() { ScopeFilter.Extract("in:xyz", out _, out var err); Assert.NotNull(err); }
    [Fact] public void A_query_without_range_stays_as_it_is() => Assert.Equal("Gnade im Haus", ScopeFilter.Extract("Gnade im Haus", out var p, out _) + (p is null ? "" : "!"));

    [Fact]
    public void Grammar_terms_in_english_and_german()
    {
        var en = BibleStudy.Core.Providers.GrammarSpec.Parse("verb aorist passive participle");
        var de = BibleStudy.Core.Providers.GrammarSpec.Parse("Verb Aorist Passiv Partizip");
        Assert.Equal(en.Query, de.Query);
        Assert.Equal(new BibleStudy.Core.Providers.GrammarQuery("verb", "aorist", "passive", "participle"), en.Query);
        Assert.False(en.Hebrew);
    }

    [Fact]
    public void Grammar_takes_a_strong_number_and_reports_unknown_terms()
    {
        var r = BibleStudy.Core.Providers.GrammarSpec.Parse("noun genitive plural G26 blub");
        Assert.Equal("G26", r.Query.Word);
        Assert.Equal(["blub"], r.Unknown);
    }

    [Fact]
    public void Hebrew_is_recognised_and_renamed()
    {
        var r = BibleStudy.Core.Providers.GrammarSpec.Parse("verb piel imperative");
        Assert.True(r.Hebrew);
        Assert.Equal("piel", r.Query.Voice);
        Assert.Equal("imperative", r.Query.Tense);
        Assert.Null(r.Query.Mood);
        Assert.Equal("participle passive", BibleStudy.Core.Providers.GrammarSpec.Parse("verb qal passive participle").Query.Tense);
    }
}
