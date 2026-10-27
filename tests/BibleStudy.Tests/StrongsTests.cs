using BibleStudy.Core.Providers;

namespace BibleStudy.Tests;

public class StrongsTests
{
    const string Js = """
        /** header { with braces } */
        var strongsGreekDictionary = {"G1":{"lemma":"Α","translit":"A","strongs_def":" first letter","kjv_def":"--Alpha","derivation":"of Hebrew origin;"},
        "G2316":{"lemma":"θεός","translit":"theós","kjv_def":"God","strongs_def":" a deity","derivation":"of uncertain affinity;"}
        }; module.exports = strongsGreekDictionary;
        """;

    [Fact]
    public void Parse_ReadsEntriesFromJavaScriptFile()
    {
        var d = StrongsDictionary.Parse(Js, 'G');
        Assert.Equal(2, d.Count);
        Assert.Equal("θεός", d[2316].Lemma);
        Assert.Equal("G2316", d[2316].Id);
        Assert.Equal("a deity", d[2316].Definition);
    }
}

public class StrongsSearchTests
{
    [Fact]
    public async Task Search_ByNumberLemmaAndMeaning_IgnoringAccents()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "strongs"));
        File.WriteAllText(Path.Combine(dir, "strongs", "greek.js"),
            "var strongsGreekDictionary = {\"G26\":{\"lemma\":\"ἀγάπη\",\"translit\":\"agápē\",\"strongs_def\":\" love\",\"kjv_def\":\"charity, love\",\"derivation\":\"\"},\"G3056\":{\"lemma\":\"λόγος\",\"translit\":\"lógos\",\"strongs_def\":\" a word\",\"kjv_def\":\"word\",\"derivation\":\"\"}};");
        File.WriteAllText(Path.Combine(dir, "strongs", "hebrew.js"), "var strongsHebrewDictionary = {\"H157\":{\"lemma\":\"אָהַב\",\"xlit\":\"ʼâhab\",\"strongs_def\":\"to have affection\",\"kjv_def\":\"love\",\"derivation\":\"\"}};");
        var d = new StrongsDictionary(new HttpClient(), dir);
        Assert.Equal("G3056", Assert.Single(await d.SearchAsync("G3056")).Id);
        Assert.Equal("G3056", (await d.SearchAsync("λογος"))[0].Id);       // no accents typed
        Assert.Equal("G26", (await d.SearchAsync("agape"))[0].Id);
        Assert.Contains(await d.SearchAsync("love"), e => e.Id == "H157");   // English meaning, both languages
        Assert.Equal(2, (await d.SearchAsync("love")).Count);
        Assert.Empty(await d.SearchAsync("zzzz"));
    }
}
