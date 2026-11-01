using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

public class GreekLsjTests
{
    [Fact]
    public void ParsesLinesAndRemovesMarkupAndHiddenReferences()
    {
        var row = "G0001\tG0001G =\tG0001G\tα, Ἀλφα\tAlpha\tG:N-LI\tAlpha\t<b> ἄλφα</b>, τό, <br /> <i>indeclinable</i> [<a href=\"javascript:void(0)\" title=\"x\">Refs 4th c.BC+</a>]<br /><Level3><b>__2</b></Level3> T<b>-square,</b>";
        var map = GreekLsj.Parse("header\n" + row + "\nG0001\tG0001H\tG0001H\tἆ\ta\tG:INJ\tah!\t<b>ἔᾱ</b>\n");
        Assert.Equal(2, map["G0001"].Count);
        var lines = map["G0001"][0].Lines;
        Assert.Equal("ἄλφα, τό", lines[0]);
        Assert.DoesNotContain(lines, l => l.Contains('<') || l.Contains("Refs") || l.Contains("[]"));
        Assert.Contains(lines, l => l.StartsWith("2 T-square"));
    }
}
