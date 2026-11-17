using BibleStudy.Core.Models;

namespace BibleStudy.Tests;

/// <summary>Bible references the way people write them in their own papers: all of them become links.</summary>
public class VerseSpellingTests
{
    static List<string> Found(string text) => DocumentLinks.Spans(text).Select(s => text.Substring(s.Index, s.Length)).ToList();
    static List<string> Refs(string text) => DocumentLinks.Spans(text).Select(s => $"{s.Ref.Book} {s.Ref.Chapter},{s.Ref.Verse}" + (s.EndChapter != s.Ref.Chapter ? $"–{s.EndChapter},{s.EndVerse}" : s.EndVerse != s.Ref.Verse ? $"–{s.EndVerse}" : "")).ToList();

    [Fact]
    public void Short_forms_of_the_books_are_known()
    {
        Assert.Equal(["1KI 8,44–48"], Refs("vgl. 1Kö 8,44–48."));
        Assert.Equal(["2KI 24,1"], Refs("2Kö 24,1"));
        Assert.Equal(["1KI 3,5"], Refs("1. Kön 3,5"));
        Assert.Equal(["GEN 1,1"], Refs("1Mo 1,1"));
        Assert.Equal(["1CH 29,11"], Refs("1Chron 29,11"));
        Assert.Equal(["EZK 37,1"], Refs("Ezek 37:1"));
    }

    [Fact]
    public void A_range_into_another_chapter_is_one_link()
    {
        Assert.Equal(["Jes 44,27–45,3"], Found("Voraussagen: Jes 44,27–45,3 (Flüsse trocknen aus)"));
        Assert.Equal(["ISA 44,27–45,3"], Refs("Jes 44,27–45,3"));
        Assert.Equal(["Dan 2,4b–7,28"], Found("Dan 2,4b–7,28 aramäisch"));
        Assert.Equal(["Dan 1,1–2,4a"], Found("Sprache: Dan 1,1–2,4a hebräisch"));
        Assert.Equal(999, DocumentLinks.VerseRanges("Jes 44,27–45,3")[0].EndVerse);   // to the end of chapter 44
    }

    [Fact]
    public void A_list_of_verses_belongs_to_its_reference()
    {
        Assert.Equal(["Dan 6,9.13.16"], Found("Dan 6,9.13.16 sagt es"));
        Assert.Equal([new VerseRef("DAN", 6, 9), new VerseRef("DAN", 6, 13), new VerseRef("DAN", 6, 16)], DocumentLinks.VerseRefs("Dan 6,9.13.16"));
        Assert.Equal(["Joh 3,16f."], Found("Joh 3,16f. sagt"));
    }

    [Fact]
    public void A_place_without_a_book_takes_the_book_just_named()
    {
        // the screenshot of the materials: none of these was a link
        Assert.Equal(["Jer 50,38", "51,30–32.39.57"], Found("Voraussagen: Jer 50,38; 51,30–32.39.57."));
        Assert.Equal(["JER 50,38", "JER 51,30–32"], Refs("Jer 50,38; 51,30–32.39.57."));
        Assert.Equal(["Dan 4,10–14", "4,13.20.22.29"], Found("Dan 4,10–14: Traum vom Baum; \"sieben Zeiten\" (4,13.20.22.29) gehen über ihn hin."));
        Assert.Equal(["Dan 2,21", "4,14.22.29", "5,21"], Found("Leitgedanke: Gott lenkt (Dan 2,21; 4,14.22.29; 5,21)."));
        Assert.Equal(["Dan 6,11", "1Kö 8,44–48"], Found("Dan 6,11: Daniel betet (vgl. 1Kö 8,44–48)."));
        Assert.Equal(["Dan 6,7–10", "6,9.13.16"], Found("Dan 6,7–10: Gesetz der Meder und Perser (6,9.13.16)."));
    }

    [Fact]
    public void A_place_without_a_book_takes_the_nearest_book_before_it()
    {
        Assert.Equal(["DAN 2,21", "DAN 4,14", "JER 5,1", "JER 6,2"], Refs("Dan 2,21; 4,14; Jer 5,1; 6,2"));
        Assert.Equal(["MAT 5,3", "MAT 6,9", "LUK 6,20", "LUK 11,2", "LUK 12,1"], Refs("Mt 5,3; 6,9; Lk 6,20; 11,2; 12,1"));
    }

    [Fact]
    public void A_reference_covers_its_range_its_list_and_the_next_chapter()
    {
        var range = DocumentLinks.Spans("Jes 44,27–45,3")[0];
        Assert.True(range.Covers(new VerseRef("ISA", 44, 27)));
        Assert.True(range.Covers(new VerseRef("ISA", 44, 30)));
        Assert.True(range.Covers(new VerseRef("ISA", 45, 1)));
        Assert.True(range.Covers(new VerseRef("ISA", 45, 3)));
        Assert.False(range.Covers(new VerseRef("ISA", 45, 4)));
        Assert.False(range.Covers(new VerseRef("ISA", 44, 26)));
        Assert.False(range.Covers(new VerseRef("JER", 44, 30)));
        var list = DocumentLinks.Spans("Dan 6,9.13")[0];
        Assert.True(list.Covers(new VerseRef("DAN", 6, 13)));
        Assert.False(list.Covers(new VerseRef("DAN", 6, 10)));
        Assert.True(BibleStudy.Core.Storage.OwnSearch.Mentions("siehe Jes 44,27–45,3", new VerseRef("ISA", 45, 2)));
    }

    [Fact]
    public void Numbers_that_are_no_places_stay_text()
    {
        Assert.Empty(Found("Es kostet 3,50 Euro."));
        Assert.Equal(["Joh 3,16"], Found("Joh 3,16 (3,5 Millionen Leser)"));   // in brackets but not ending there
        Assert.Equal(["Joh 3,16"], Found("Joh 3,16\n(4,5)"));                  // another line: no book to take
        Assert.Empty(Found("Seite 4,5 und 6,7"));                            // no reference before them
    }
}

/// <summary>Parallel passages at the start of a verse (as some imported files have them) are taken out of the verse text.</summary>
public class LeadingReferencesTests
{
    [Fact]
    public void Parallel_passages_before_the_text_are_split_off()
    {
        var split = BibleStudy.Core.Import.LocalBibleStore.LeadingReferences("2Kö 20,12-19; 2Chr 32,25-26 Zu jener Zeit sandte Merodach-Baladan …");
        Assert.NotNull(split);
        Assert.Equal("2Kö 20,12-19; 2Chr 32,25-26", split.Value.References);
        Assert.StartsWith("Zu jener Zeit", split.Value.Text);
        Assert.Equal("Mt 3,1-12; Lk 3,1-18", BibleStudy.Core.Import.LocalBibleStore.LeadingReferences("Mt 3,1-12; Lk 3,1-18 In jenen Tagen")!.Value.References);
    }

    [Fact]
    public void An_ordinary_verse_stays_as_it_is()
    {
        Assert.Null(BibleStudy.Core.Import.LocalBibleStore.LeadingReferences("Und Hiskia freute sich über sie."));
        Assert.Null(BibleStudy.Core.Import.LocalBibleStore.LeadingReferences("Joh 3,16"));                     // nothing but a reference
        Assert.Null(BibleStudy.Core.Import.LocalBibleStore.LeadingReferences("Joh 3,16 und dann der Rest"));   // no new sentence follows
    }
}
