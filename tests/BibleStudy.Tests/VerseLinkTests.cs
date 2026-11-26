using BibleStudy.Core.Import;
using BibleStudy.Core.Search;

namespace BibleStudy.Tests;

/// <summary>Links that open a passage in the app from outside: biblestudy://Joh3,16 from WhatsApp, e-mail or Obsidian.</summary>
public class VerseLinkTests
{
    [Theory]
    [InlineData("biblestudy://Joh3,16", "Joh 3,16")]
    [InlineData("biblestudy://Joh%203,16-18", "Joh 3,16-18")]
    [InlineData("biblestudy:R%C3%B6m%208,28", "Röm 8,28")]
    [InlineData("biblestudy://open?ref=Joh+3,16", "Joh 3,16")]
    [InlineData("biblestudy://John.3.16", "John 3,16")]
    [InlineData("biblestudy://1Kor13,4-7/", "1Kor 13,4-7")]
    [InlineData("BibleStudy://Ps23", "Ps 23")]
    [InlineData("biblestudy://Joh3,16;Röm8,28", "Joh 3,16;Röm 8,28")]
    public void A_link_names_its_passage(string url, string expected) => Assert.Equal(expected, VerseLink.Passage(url));

    [Theory]
    [InlineData("biblestudy://Hallo")]
    [InlineData("biblestudy://")]
    [InlineData("biblestudy-sync://pair?k=ABCDEFGHJKMNPQRSTVWXYZ0123")]
    [InlineData("https://example.org/Joh3,16")]
    [InlineData(null)]
    public void Other_addresses_name_no_passage(string? url) => Assert.Null(VerseLink.Passage(url));

    [Fact]
    public void The_pairing_scheme_is_not_a_verse_link()
    {
        Assert.False(VerseLink.IsVerseLink("biblestudy-sync://pair?k=x"));
        Assert.True(VerseLink.IsVerseLink("biblestudy://Joh3,16"));
    }

    [Fact]
    public void Links_are_plain_and_short()
    {
        Assert.Equal("biblestudy://Joh3,16", VerseLink.ToUrl("JHN", 3, 16));
        Assert.Equal("biblestudy://1Kor13,4-7", VerseLink.ToUrl("1CO", 13, 4, 7));
        Assert.Equal("biblestudy://Ps23", VerseLink.ToUrl("PSA", 23));
        Assert.Equal("biblestudy://Joh3,16", VerseLink.ToUrl("JHN", 3, 16, 16));
    }

    [Fact]
    public void Every_book_reads_back_from_its_link()
    {
        foreach (var id in BookData.UsfmIds)
        {
            var url = VerseLink.ToUrl(id, 1, 2, 3);
            Assert.True(url.All(char.IsAscii), url);
            var passage = VerseLink.Passage(url);
            Assert.NotNull(passage);
            var parsed = PassageParser.Parse(passage!, out var single);
            Assert.True(single, url);
            Assert.Equal(new PassageRef(id, 1, 2, 3), parsed![0]);
        }
    }
}
