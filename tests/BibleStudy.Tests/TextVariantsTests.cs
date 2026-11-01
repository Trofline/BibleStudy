using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

public class TextVariantsTests
{
    [Fact]
    public void KeepsOnlyWordsWhereEditionsDiffer()
    {
        var all = "NA28+NA27+Tyn+SBL+WH+Treg+TR+Byz";
        var text = string.Join("\n",
            $"Mat.1.1#01=NKO\tΒίβλος (Biblos)\t[The] book\tG0976=N-NSF\tβίβλος=book\t{all}\t\t",
            "Mat.1.6#10=k\tὁ (ho)\tthe\tG3588=T-NSM\tὁ=the\tTR+Byz\t\t",
            "Mat.1.10#10=N(k)O\tἈμώς· (Amōs)\tAmos;\tG0301\tἈμώς=Amos\tNA28+NA27+Tyn+SBL+WH+Treg\tἈμών (t=Amōn) Amon in: TR+Byz\t",
            $"Mat.2.3#05=NKO\tἩρῴδης (Hērōdēs)\tHerod\tG2264\tx=y\tNA28+NA27+Tyn+SBL+WH+Treg+TR«3+Byz«3\t\t");
        var map = TextVariants.Parse(text);
        Assert.False(map.ContainsKey("Mat.1.1"));
        Assert.False(map.ContainsKey("Mat.2.3"));            // only an order mark on an otherwise complete row
        Assert.Equal("ὁ", map["Mat.1.6"][0].Greek);
        Assert.Equal("TR+Byz", map["Mat.1.6"][0].Editions);
        Assert.Contains("Amon", map["Mat.1.10"][0].Alternative);
    }
}
