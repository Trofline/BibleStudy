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
