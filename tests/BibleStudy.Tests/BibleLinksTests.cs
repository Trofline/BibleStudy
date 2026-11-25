using BibleStudy.Core.Search;

namespace BibleStudy.Tests;

public class BibleLinksTests
{
    [Theory]
    [InlineData("JHN", 3, 16, 16, null, "JHN.3.16")]
    [InlineData("ROM", 8, 28, 30, null, "ROM.8.28-30")]
    [InlineData("JHN", 3, 16, 2, 4, "JHN.3.16-4.2")]
    public void Code_of_verses(string book, int chapter, int from, int to, int? toChapter, string expected) =>
        Assert.Equal(expected, BibleLinks.Code(new PassageRef(book, chapter, from, to, toChapter)));

    [Fact]
    public void Code_of_chapters()
    {
        Assert.Equal("PSA.23", BibleLinks.Code(new PassageRef("PSA", 23, null, null)));
        Assert.Equal("PSA.23-25", BibleLinks.Code(new PassageRef("PSA", 23, null, null, 25)));
    }

    [Theory]
    [InlineData("biblestudy://open/JHN.3.16")]
    [InlineData("biblestudy://JHN.3.16")]
    [InlineData("biblestudy:jhn.3.16")]
    [InlineData("https://trofline.github.io/BibleStudy/v/?JHN.3.16")]
    [InlineData("https://trofline.github.io/BibleStudy/v/index.html?r=JHN.3.16")]
    [InlineData("https://trofline.github.io/BibleStudy/v/#JHN.3.16")]
    [InlineData("biblestudy://open/Joh%203,16")]
    [InlineData("biblestudy://Johannes 3:16")]
    public void Every_form_of_a_link_reads_the_same(string link)
    {
        var p = Assert.Single(BibleLinks.Parse(link)!);
        Assert.Equal(new PassageRef("JHN", 3, 16, 16), p);
    }

    [Fact]
    public void A_link_round_trips()
    {
        var passages = new[] { new PassageRef("ROM", 8, 28, 30), new PassageRef("PSA", 23, null, null), new PassageRef("1CO", 13, 1, 3, 14) };
        Assert.Equal(passages, BibleLinks.Parse(BibleLinks.AppLink(passages)));
        Assert.Equal(passages, BibleLinks.Parse(BibleLinks.WebLink(passages)));
    }

    [Theory]
    [InlineData("biblestudy-sync://pair?k=ABC")]
    [InlineData("https://example.org/?JHN.3.16")]
    [InlineData("biblestudy://open/XYZ.3.16")]
    [InlineData("biblestudy://open/")]
    public void Other_addresses_are_no_passage_links(string link) => Assert.Null(BibleLinks.Parse(link));

    [Fact]
    public void The_pairing_link_is_not_mistaken_for_a_passage_link()
    {
        Assert.False(BibleLinks.IsLink("biblestudy-sync://pair?k=ABC"));
        Assert.True(BibleLinks.IsLink("biblestudy://open/JHN.3.16"));
        Assert.True(BibleLinks.IsLink("https://trofline.github.io/BibleStudy/v/?JHN.3.16"));
        Assert.False(BibleLinks.IsLink("Joh 3,16"));
    }

    [Fact]
    public void A_shared_text_gives_its_link_or_its_passages()
    {
        var link = BibleLinks.FromSharedText("Lies mal das:\nJoh 3,16\nhttps://trofline.github.io/BibleStudy/v/?JHN.3.16-18 🙂");
        Assert.Equal(new PassageRef("JHN", 3, 16, 18), Assert.Single(link));

        var list = BibleLinks.FromSharedText("Predigttexte:\n- Röm 5,8\n- Ps 23");
        Assert.Equal([new PassageRef("ROM", 5, 8, 8), new PassageRef("PSA", 23, null, null)], list);

        Assert.Empty(BibleLinks.FromSharedText("Bis morgen!"));
    }
}
