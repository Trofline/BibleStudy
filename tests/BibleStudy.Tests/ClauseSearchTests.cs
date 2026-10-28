using BibleStudy.Core.Providers;
using Xunit;

namespace BibleStudy.Tests;

public class ClauseSearchTests
{
    static GrammarWord W(int verse, string text, string lemma, string strong, string gloss, string role, int clause) =>
        new("JHN", 1, verse, text, lemma, strong, gloss, "", null, null, null, null, null, null, null, role, clause);

    static readonly GrammarWord[] Words =
    [
        // clause 1: Jesus loved Peter
        W(1, "Ἰησοῦς", "Ἰησοῦς", "G2424", "Jesus", "s", 1), W(1, "ἠγάπα", "ἀγαπάω", "G25", "love", "v", 1), W(1, "Πέτρον", "Πέτρος", "G4074", "Peter", "o", 1),
        // clause 2: Peter loved Jesus
        W(2, "Πέτρος", "Πέτρος", "G4074", "Peter", "s", 2), W(2, "ἠγάπα", "ἀγαπάω", "G25", "love", "v", 2), W(2, "Ἰησοῦν", "Ἰησοῦς", "G2424", "Jesus", "o", 2),
    ];

    [Fact]
    public void Subject_and_object_must_be_in_the_same_clause_with_the_right_roles()
    {
        var hits = ClauseSearch.Run(Words, [new("s", "Jesus"), new("o", "Peter")]);
        Assert.Single(hits);
        Assert.Equal(1, hits[0].Verse);
    }

    [Fact]
    public void The_term_may_be_a_strong_number_a_greek_lemma_or_a_gloss_without_accents()
    {
        Assert.Single(ClauseSearch.Run(Words, [new("s", "G2424")]));
        Assert.Single(ClauseSearch.Run(Words, [new("o", "ιησους")]));
        Assert.Equal(2, ClauseSearch.Run(Words, [new("v", "ἀγαπάω")]).Count);
    }

    [Fact]
    public void A_role_without_a_term_means_any_word_in_that_role()
    {
        Assert.Equal(2, ClauseSearch.Run(Words, [new("s", null), new("o", null)]).Count);
        Assert.Empty(ClauseSearch.Run(Words, [new("io", null)]));
    }
}
