using BibleStudy.Core.Providers;

namespace BibleStudy.Tests;

public class HebrewGrammarTests
{
    const string Xml = """
        <sentence><wg class="cl" rule="V-O">
          <w ref="GEN 1:1!1" class="verb" type="qatal" stem="qal" person="third" number="singular" gender="masculine" lemma="בָּרָא" strongnumberx="1254" gloss="created" role="v">בָּרָא</w>
          <wg role="o" class="np"><w ref="GEN 1:1!2" class="noun" type="common" state="absolute" number="plural" gender="masculine" lemma="אֱלֹהִים" strongnumberx="0430" gloss="God">אֱלֹהִים</w></wg>
        </wg></sentence>
        """;

    [Fact]
    public void Hebrew_fields_are_mapped_to_verb_form_stem_and_state()
    {
        var words = SyntaxProvider.ParseWords(Xml, "GEN", hebrew: true);
        Assert.Equal(2, words.Count);
        var verb = words[0];
        Assert.Equal("H1254", verb.Strong);
        Assert.Equal("qatal", verb.Tense);
        Assert.Equal("qal", verb.Voice);
        Assert.Null(verb.Mood);
        Assert.Equal("absolute", words[1].Case);
        Assert.Null(words[1].Tense);   // the type of a noun ("common") is no verb form
    }

    [Fact]
    public void Hebrew_words_can_be_found_by_grammar_and_by_clause_role()
    {
        var words = SyntaxProvider.ParseWords(Xml, "GEN", hebrew: true);
        Assert.Single(words.Where(new GrammarQuery(Class: "verb", Tense: "qatal", Voice: "qal").Matches));
        Assert.Single(words.Where(new GrammarQuery(Word: "ברא").Matches));   // without vowel signs
        Assert.Single(ClauseSearch.Run(words, [new RoleCondition("v", null), new RoleCondition("o", "H430")]));
    }
}
