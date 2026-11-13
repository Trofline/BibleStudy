using BibleStudy.Core.Providers;
using BibleStudy.Core.Search;
using Xunit;

namespace BibleStudy.Tests;

public class OriginalSearchTests
{
    static GrammarWord W(int verse, string text, string lemma, string strong, string gloss, string cls = "noun", string? id = null, string? domains = null,
        string? refers = null, string? subject = null, string? lxx = null, string? number = null, string? gender = null, string? @case = null, string book = "JHN") =>
        new(book, 1, verse, text, lemma, strong, gloss, cls, null, null, null, null, number, gender, @case, null, 0, id, domains, refers, subject, lxx);

    // ---- meaning --------------------------------------------------------------------------------------------------------

    static readonly GrammarWord[] Forgive =
    [
        W(1, "ἀφῆκεν", "ἀφίημι", "G863", "forgave", "verb", domains: "040002"),
        W(2, "ἀφίησιν", "ἀφίημι", "G863", "leaves", "verb", domains: "015048"),   // the other meaning, rare here
        W(3, "ἀφῆτε", "ἀφίημι", "G863", "forgive", "verb", domains: "040002"),
        W(4, "ἀφίεμεν", "ἀφίημι", "G863", "forgive", "verb", domains: "040002"),
        W(5, "ἀφῆκα", "ἀφίημι", "G863", "forgave", "verb", domains: "040002"),
        W(6, "ἀφῆκεν", "ἀφίημι", "G863", "forgave", "verb", domains: "040002"),
        W(7, "χαρισάμενος", "χαρίζομαι", "G5483", "forgiving", "verb", domains: "040002 057102"),
        W(8, "ἀπέλυσεν", "ἀπολύω", "G630", "released", "verb", domains: "015043"),
        W(9, "λόγος", "λόγος", "G3056", "word", domains: "033006"),
    ];

    [Fact]
    public void Meaning_finds_other_words_of_the_same_field()
    {
        var r = OriginalSearch.Meaning(Forgive, OriginalSearch.Matcher("G863"));
        var verses = r.Hits.Select(h => h.Word.Verse).ToHashSet();
        Assert.Contains(7, verses);                    // χαρίζομαι shares the field "forgive"
        Assert.DoesNotContain(9, verses);              // λόγος does not
        Assert.Contains(r.Members, m => m.Lemma == "χαρίζομαι");
    }

    [Fact]
    public void A_rare_meaning_of_the_word_does_not_pull_in_its_field()
    {
        // "leave" is one in six uses of ἀφίημι: below a fifth, so ἀπολύω (release, in that field's neighbourhood) stays out
        var r = OriginalSearch.Meaning(Forgive, OriginalSearch.Matcher("ἀφιημι"));
        Assert.DoesNotContain("015048", r.Codes);
        Assert.DoesNotContain(r.Hits, h => h.Word.Verse == 2);
    }

    [Fact]
    public void The_word_may_be_given_as_english_gloss_or_through_the_numbers_of_a_translation()
    {
        Assert.NotEmpty(OriginalSearch.Meaning(Forgive, OriginalSearch.Matcher("forgive")).Hits);
        Assert.NotEmpty(OriginalSearch.Meaning(Forgive, OriginalSearch.Matcher("vergeben", ["G5483"])).Hits);
        Assert.Empty(OriginalSearch.Meaning(Forgive, OriginalSearch.Matcher("vergeben")).Hits);
    }

    [Fact]
    public void A_word_with_several_meanings_names_them_and_one_can_be_chosen_by_its_gloss()
    {
        GrammarWord[] words =
        [
            .. Enumerable.Range(1, 6).Select(i => W(i, "ἀφῆκεν", "ἀφίημι", "G863", "having.left", "verb", domains: "015048")),
            .. Enumerable.Range(7, 3).Select(i => W(i, "ἀφῆκεν", "ἀφίημι", "G863", "forgave", "verb", domains: "040002")),
            W(10, "ἀπῆλθεν", "ἀπέρχομαι", "G565", "went.away", "verb", domains: "015048"),
            W(11, "χαρισάμενος", "χαρίζομαι", "G5483", "forgiving", "verb", domains: "040002"),
        ];
        var main = OriginalSearch.Meaning(words, OriginalSearch.Matcher("G863"));
        Assert.Contains(main.Hits, h => h.Word.Verse == 10);   // "leave" is the main meaning
        Assert.Equal(["left", "forgave"], main.Senses.Select(s => s.Gloss));
        Assert.True(main.Senses[0].Used);

        var chosen = OriginalSearch.Meaning(words, OriginalSearch.Matcher("G863=forgave"));
        Assert.Contains(chosen.Hits, h => h.Word.Verse == 11);
        Assert.DoesNotContain(chosen.Hits, h => h.Word.Verse == 10);
    }

    // ---- what a word stands for ------------------------------------------------------------------------------------------

    static readonly GrammarWord[] Story =
    [
        W(1, "Ἰησοῦς", "Ἰησοῦς", "G2424", "Jesus", id: "1"),
        W(1, "Πέτρος", "Πέτρος", "G4074", "Peter", id: "2"),
        W(2, "αὐτόν", "αὐτός", "G846", "him", "pron", id: "3", refers: "1"),
        W(3, "αὐτοῦ", "αὐτός", "G846", "his", "pron", id: "4", refers: "3"),        // a chain: his → him → Jesus
        W(4, "εἶπεν", "λέγω", "G3004", "said", "verb", id: "5", subject: "1"),     // [he] said, he = Jesus
        W(5, "ἔκλαυσεν", "κλαίω", "G2799", "wept", "verb", id: "6", subject: "2"),  // Peter wept
        W(6, "αὐτοί", "αὐτός", "G846", "they", "pron", id: "7", refers: "2 1"),     // several: Peter and Jesus
    ];

    [Fact]
    public void Pronouns_and_unwritten_subjects_count_for_the_person_they_stand_for()
    {
        var verses = OriginalSearch.RefersTo(Story, OriginalSearch.Matcher("Jesus")).Select(h => h.Word.Verse).ToList();
        Assert.Equal([1, 2, 3, 4, 6], verses);
        var note = OriginalSearch.RefersTo(Story, OriginalSearch.Matcher("G2424")).First(h => h.Word.Verse == 4).Note;
        Assert.Contains("→ Ἰησοῦς", note);
    }

    // ---- Septuagint -------------------------------------------------------------------------------------------------------

    static readonly GrammarWord[] Hebrew =
    [
        W(1, "חֶסֶד", "חֶסֶד", "H2617", "kindness", lxx: "G1656", book: "PSA"),
        W(2, "חֶסֶד", "חֶסֶד", "H2617", "kindness", lxx: "G1656", book: "PSA"),
        W(3, "אַהֲבָה", "אַהֲבָה", "H160", "love", lxx: "G26", book: "SNG"),
        W(4, "חֶסֶד", "חֶסֶד", "H2617", "kindness", lxx: "G1343", book: "PRO"),
    ];

    [Fact]
    public void Septuagint_shows_the_hebrew_behind_a_greek_word_and_the_greek_for_a_hebrew_word()
    {
        var hebrewFor = OriginalSearch.Septuagint(Hebrew, "G26", null, out var eqA);
        Assert.Single(hebrewFor);
        Assert.Contains("H160", eqA[0].Key);

        var greekFor = OriginalSearch.Septuagint(Hebrew, "H2617", null, out var eqB);
        Assert.Equal(3, greekFor.Count);
        Assert.Equal(("G1656", 2), eqB[0]);
    }

    // ---- agreement --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Agreement_needs_the_same_case_number_and_gender_close_together()
    {
        GrammarWord[] words =
        [
            W(1, "ἀγαθός", "ἀγαθός", "G18", "good", "adj", number: "singular", gender: "masculine", @case: "nominative"),
            W(1, "ἄνθρωπος", "ἄνθρωπος", "G444", "man", number: "singular", gender: "masculine", @case: "nominative"),
            W(2, "ἀγαθήν", "ἀγαθός", "G18", "good", "adj", number: "singular", gender: "feminine", @case: "accusative"),
            W(2, "ἄνθρωπος", "ἄνθρωπος", "G444", "man", number: "singular", gender: "masculine", @case: "nominative"),
        ];
        var hits = OriginalSearch.Agreeing(words, new GrammarQuery(Class: "adj"), new GrammarQuery(Class: "noun"), hebrew: false);
        Assert.Single(hits);
        Assert.Equal(1, hits[0].Word.Verse);
    }

    [Fact]
    public void Hebrew_common_gender_agrees_with_both()
    {
        var a = W(1, "x", "x", "H1", "x", number: "plural", gender: "common");
        var b = W(1, "y", "y", "H2", "y", number: "plural", gender: "feminine");
        Assert.True(OriginalSearch.Agree(a, b, hebrew: true));
        Assert.False(OriginalSearch.Agree(a, b with { Number = "singular" }, hebrew: true));
    }

    [Theory]
    [InlineData("Jesus Baptized", "baptism", true)]
    [InlineData("Crossing the Red Sea", "red sea", true)]
    [InlineData("Crossing the Red Sea", "Crossing", true)]
    [InlineData("Birth of Jesus", "baptism", false)]
    [InlineData("Paul's Shipwreck", "", false)]
    public void Event_names_match_by_the_stem_of_each_word(string name, string term, bool expected) =>
        Assert.Equal(expected, OriginalSearch.NameMatches(name, term));

    // ---- reading the MACULA fields ------------------------------------------------------------------------------------------

    [Fact]
    public void The_new_fields_are_read_from_the_macula_files()
    {
        const string greek = """
            <book><sentence><wg class="cl">
              <w ref="JHN 1:1!1" xml:id="n43001001005" class="noun" lemma="λόγος" strong="3056" gloss="Word" domain="033006 033100">Λόγος</w>
              <w ref="JHN 1:2!1" xml:id="n43001002001" class="pron" lemma="οὗτος" strong="3778" gloss="He" referent="n43001001005">Οὗτος</w>
              <w ref="JHN 1:2!2" xml:id="n43001002002" class="verb" lemma="εἰμί" strong="1510" gloss="was" subjref="n43001002001">ἦν</w>
            </wg></sentence></book>
            """;
        var g = SyntaxProvider.ParseWords(greek, "JHN");
        Assert.Equal("43001001005", g[0].Id);
        Assert.Equal("033006 033100", g[0].Domains);
        Assert.Equal("43001001005", g[1].Refers);
        Assert.Equal("43001002001", g[2].Subject);

        const string hebrew = """
            <book><sentence><wg class="cl">
              <w ref="GEN 1:1!1" xml:id="o010010010012" class="noun" lemma="רֵאשִׁית" strongnumberx="7225" gloss="beginning" coredomain="168" lexdomain="002003003004" greekstrong="746">רֵאשִׁ֖ית</w>
              <w ref="GEN 1:11!13" xml:id="o010010110131" class="pron" lemma="הוּא" strongnumberx="2050c" gloss="its" participantref="010010110091">וֹ</w>
            </wg></sentence></book>
            """;
        var h = SyntaxProvider.ParseWords(hebrew, "GEN", hebrew: true);
        Assert.Equal("010010010012", h[0].Id);
        Assert.Equal("002003003004", h[0].Domains);
        Assert.Equal("G746", h[0].Lxx);
        Assert.Equal("010010110091", h[1].Refers);
    }
}
