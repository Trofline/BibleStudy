using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using StrongsAdder;
using Xunit;

namespace BibleStudy.Tests;

public class StrongsOverlayTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static (string Word, string[]? Strongs) W(string w, string? s = null) => (w, s is null ? null : [s]);

    [Fact]
    public void Aligner_GivesNumbersToSameWordsAndStems_InOrder()
    {
        var reference = new[] { W("Denn", "G1063"), W("also", "G3779"), W("hat", "G25"), W("Gott", "G2316"), W("die"), W("Welt", "G2889"), W("geliebt", "G25") };
        var tags = Aligner.Align(["Denn", "so", "hat", "Gott", "die", "Welt", "geliebt"], reference, "deu");
        Assert.Equal("G1063", tags[0][0]);
        Assert.False(tags.ContainsKey(1));                  // "so" has no counterpart: no number rather than a wrong one
        Assert.Equal("G2316", tags[3][0]);
        Assert.False(tags.ContainsKey(4));                  // "die" is untagged in the reference
        Assert.Equal("G2889", tags[5][0]);
        Assert.Equal("G25", tags[6][0]);
    }

    [Fact]
    public void Aligner_MatchesOldSpelling_AndKeepsTheOrder()
    {
        var reference = new[] { W("Thut", "G4160"), W("Buße", "G3340"), W("Herr", "G2962") };
        var tags = Aligner.Align(["Herrn", "tut", "Busse"], reference, "deu");
        Assert.Equal("G4160", tags[1][0]);   // old spelling (th) and ß are matched
        Assert.Equal("G3340", tags[2][0]);
        Assert.False(tags.ContainsKey(0));   // a stem match that would cross the order of the other words is not taken
    }

    [Fact]
    public void Overlay_TagsWordsOfTheVerse_AndIgnoresAChangedText()
    {
        var overlay = new StrongsOverlay(_dir);
        var verse = new Verse(1, [new Segment("Am Anfang schuf Gott Himmel und Erde.")]);
        var words = StrongsOverlay.Words(verse.Text);
        var gott = Array.FindIndex(words.ToArray(), w => verse.Text.Substring(w.Start, w.Length) == "Gott");
        overlay.Set("xyz", "deu_l12", "GEN", 1, 1, verse.Text, new Dictionary<int, string[]> { [gott] = ["H430"] });
        overlay.Commit("xyz");

        var reloaded = new StrongsOverlay(_dir);
        Assert.True(reloaded.Has("xyz"));
        var tagged = reloaded.Apply(new Chapter("xyz", "GEN", 1, [verse])).Verses[0];
        Assert.Equal(verse.Text, tagged.Text);                                       // the text itself is not changed
        var seg = Assert.Single(tagged.Segments, s => s.Strongs is { Count: > 0 });
        Assert.Equal("Gott", seg.Text);
        Assert.True(seg.Estimated);

        var changed = new Verse(1, [new Segment("Im Anfang schuf Gott Himmel und Erde.")]);
        Assert.DoesNotContain(reloaded.Apply(new Chapter("xyz", "GEN", 1, [changed])).Verses[0].Segments, s => s.Strongs is { Count: > 0 });
    }

    [Fact]
    public void Overlay_LeavesChaptersWithSourceNumbersAlone()
    {
        var overlay = new StrongsOverlay(_dir);
        var verse = new Verse(1, [new Segment("Gott", false, ["H430"])]);
        overlay.Set("kjv", "x", "GEN", 1, 1, verse.Text, new Dictionary<int, string[]> { [0] = ["H999"] });
        Assert.Equal("H430", overlay.Apply(new Chapter("kjv", "GEN", 1, [verse])).Verses[0].Segments[0].Strongs![0]);
    }
}

public class LexiconTests
{
    static (string Word, string[]? Strongs) W(string w, string? s = null) => (w, s is null ? null : [s]);

    [Fact]
    public void FrequentWords_DoNotTakeTheNumbersOfAPhraseTheyStandIn()
    {
        var lexicon = new Lexicon();
        // "the" is seen often, always inside phrases whose number belongs to the noun
        lexicon.Learn([W("the", "G4561"), W("flesh", "G4561")], "eng");   // once, inside a phrase
        for (var i = 0; i < 30; i++) lexicon.Learn([W("the"), W("flesh", "G4561"), W("and"), W("the"), W("spirit", "G4151")], "eng");
        var tags = Aligner.Align(["the", "flesh"], [W("the", "G4561"), W("flesh", "G4561")], "eng", lexicon);
        Assert.Equal("G4561", tags[1][0]);       // "flesh" keeps its number
        Assert.False(tags.ContainsKey(0));       // "the" does not get it just because it stands in the phrase
    }

    [Fact]
    public void Lexicon_FindsAWordThatStandsElsewhereInTheVerse()
    {
        var lexicon = new Lexicon();
        for (var i = 0; i < 6; i++) lexicon.Learn([W("Erde", "H776"), W("Himmel", "H8064")], "deu");
        // the reference calls the word "Land" (other order, other word): only the verse's numbers and what the word usually carries decide
        var tags = Aligner.Align(["Himmel", "und", "Erde"], [W("Land", "H776"), W("und"), W("Himmel", "H8064")], "deu", lexicon);
        Assert.Equal("H8064", tags[0][0]);
        Assert.Equal("H776", tags[2][0]);
    }
}
