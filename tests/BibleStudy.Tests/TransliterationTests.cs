using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

public class TransliterationTests
{
    [Theory]
    [InlineData("logos", "λογος")]
    [InlineData("charis", "χαρις")]
    [InlineData("agapē", "αγαπη")]
    [InlineData("pneuma theou", "πνευμα θεου")]
    [InlineData("hagios", "αγιος")]
    [InlineData("Iēsous", "Ιησους")]
    public void Greek_from_latin_letters(string latin, string greek) => Assert.Equal(greek, Transliteration.Greek(latin));

    [Theory]
    [InlineData("shalom", "שלם")]
    [InlineData("ruwach", "רוח")]
    [InlineData("melek", "מלך")]
    [InlineData("tov", "תב")]
    [InlineData("'adam", "אדם")]
    public void Hebrew_from_latin_letters_without_vowels(string latin, string hebrew) => Assert.Equal(hebrew, Transliteration.Hebrew(latin));
}
