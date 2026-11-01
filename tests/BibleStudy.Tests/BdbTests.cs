using System.Xml.Linq;
using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

public class BdbTests
{
    const string Index = """
        <index xmlns="http://openscriptures.github.com/morphhb/namespace"><part>
          <entry id="aac"><w>אָב</w><xref bdb="a.ae.ab" strong="1" twot="4a"/></entry>
          <entry id="aab"><w>אֵב</w><xref bdb="a.ab.ab" strong="3a"/></entry>
          <entry id="aaa"><w>א</w><xref bdb="a.aa.aa"/></entry>
        </part></index>
        """;

    const string Lexicon = """
        <lexicon xmlns="http://openscriptures.github.com/morphhb/namespace"><part><section>
          <entry id="a.ac.ab" cite="full"><w>אָבִיב</w> <pos>n.m</pos>. <ref r="Lev.2.14">Lv 2:14</ref>
            <sense n="1"><def>fresh</def>, <def>young ears</def> of barley <ref r="Exod.9.31">Ex 9:31</ref></sense>
            <sense n="2">month <def>Abib</def></sense>
            <status p="1">done</status>
          </entry>
        </section></part></lexicon>
        """;

    [Fact]
    public void IndexMapsStrongNumbersToBdbIds()
    {
        var map = BdbLexicon.ParseIndex(XDocument.Parse(Index));
        Assert.Equal("a.ae.ab", map["H1"]);
        Assert.Equal("a.ab.ab", map["H3"]);     // "3a": the number counts
        Assert.Equal(2, map.Count);             // an entry without a Strong number is not listed
    }

    [Fact]
    public void EntryIsRenderedAsPlainLinesWithNumberedMeanings()
    {
        var entries = BdbLexicon.ParseEntries(XDocument.Parse(Lexicon));
        var e = BdbLexicon.Render("a.ac.ab", entries["a.ac.ab"]);
        Assert.Equal("אָבִיב", e.Headword);
        Assert.Equal(3, e.Lines.Count);
        Assert.Contains("Lv 2:14", e.Lines[0]);
        Assert.Equal("1. fresh, young ears of barley Ex 9:31", e.Lines[1]);
        Assert.DoesNotContain("done", string.Join(' ', e.Lines));
    }
}
