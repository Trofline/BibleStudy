using BibleStudy.Core.Providers;

namespace BibleStudy.Tests;

public class SyntaxTests
{
    // trimmed from MACULA Greek (3 John 1:1); CC BY 4.0
    const string Sample = """
        <book lang="el" id="3JN"><sentence><p>3JN 1:1 Ὁ πρεσβύτερος Γαΐῳ</p>
        <wg class="cl" rule="S-IO">
          <wg class="np" role="s"><w ref="3JN 1:1!1" class="det" lemma="ὁ" strong="3588" number="singular" gender="masculine" case="nominative" gloss="The">Ὁ</w>
            <w ref="3JN 1:1!2" class="adj" lemma="πρεσβύτερος" strong="4245" number="singular" gender="masculine" case="nominative" gloss="elder">πρεσβύτερος</w></wg>
          <wg class="np" role="io"><w ref="3JN 1:1!3" class="noun" lemma="Γάϊος" strong="1050" case="dative" gloss="to Gaius">Γαΐῳ</w></wg>
          <w ref="3JN 1:2!1" class="verb" role="v" lemma="ἀγαπάω" strong="25" tense="present" voice="active" mood="indicative" gloss="love">ἀγαπῶ</w>
        </wg></sentence></book>
        """;

    [Fact]
    public void Words_of_the_verse_carry_their_grammar()
    {
        var v = SyntaxProvider.Parse(Sample, 1, 1);
        Assert.Equal(3, v.Words.Count);
        Assert.Equal("G3588", v.Words[0].Strong);
        Assert.Contains(v.Words[1].Features, f => f.Key == "case" && f.Value == "nominative");
    }

    [Fact]
    public void Outline_lists_clause_and_roles_and_skips_other_verses()
    {
        var v = SyntaxProvider.Parse(Sample, 1, 1);
        Assert.Equal("clause", v.Outline[0].Kind);
        Assert.Contains(v.Outline, l => l.Role == "s" && l.Text == "Ὁ πρεσβύτερος" && l.Depth == 1);
        Assert.Contains(v.Outline, l => l.Role == "io");
        Assert.DoesNotContain(v.Outline, l => l.Role == "v");   // that word belongs to verse 2
    }

    [Fact]
    public void Books_are_known()
    {
        Assert.True(SyntaxProvider.IsAvailable("JHN"));
        Assert.True(SyntaxProvider.IsAvailable("GEN"));
        Assert.False(SyntaxProvider.IsAvailable("XXX"));
    }
}
