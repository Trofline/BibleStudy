using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.Tests;

/// <summary>Person: and Ort: understand German names and find the verses the data leaves out, but only for names that belong to one person or place.</summary>
public class EntityNamesTests
{
    static readonly List<LexiconItem> Items =
    [
        new("people", "abraham_58", "Abraham", 187), new("people", "isaac_1", "Isaac", 120), new("people", "moses_1", "Moses", 800),
        new("people", "jacob_1", "Jacob", 300), new("people", "mary_1", "Mary", 50), new("people", "mary_2", "Mary", 14),
        new("people", "judah_1", "Judah", 40), new("people", "judah_2", "Judah", 3), new("places", "jerusalem_1", "Jerusalem", 800),
        new("places", "egypt_1", "Egypt", 600), new("places", "israel_1", "Israel", 2000), new("people", "peter_1", "Peter", 160),
    ];

    [Fact]
    public void German_names_find_the_English_entries()
    {
        Assert.Equal("isaac_1", EntityNames.Find(Items, "people", "Isaak")!.Id);
        Assert.Equal("moses_1", EntityNames.Find(Items, "people", "Mose")!.Id);
        Assert.Equal("peter_1", EntityNames.Find(Items, "people", "Petrus")!.Id);
        Assert.Equal("egypt_1", EntityNames.Find(Items, "places", "Ägypten")!.Id);
        Assert.Equal("abraham_58", EntityNames.Find(Items, "people", "abraham")!.Id);
        Assert.Equal("mary_1", EntityNames.Find(Items, "people", "Maria")!.Id);   // the one with the most references
        Assert.Equal("isaac_1", EntityNames.Find(Items, "people", "Isac")!.Id);   // a letter apart
        Assert.Null(EntityNames.Find(Items, "people", "Jerusalem"));              // a place is no person
        Assert.Null(EntityNames.Find(Items, "people", "Xyzzy"));
    }

    [Fact]
    public void Names_with_additions_in_the_data_are_found_and_the_most_named_wins()
    {
        List<LexiconItem> nt =
        [
            new("people", "jesus_1", "Jesus", 1), new("people", "jesus_905", "Jesus Christ", 1296), new("people", "peter_2745", "Simon Peter", 129),
            new("people", "john_1", "John the Baptist", 68), new("people", "john_2", "John the Apostle", 34), new("people", "john_3", "John Mark", 10),
            new("people", "mary_1", "Mary (Mother of Jesus)", 20), new("people", "mary_2", "Mary Magdalene", 14), new("people", "simon_1", "Simon", 9),
        ];
        Assert.Equal("jesus_905", EntityNames.Find(nt, "people", "Jesus")!.Id);   // not the other Jesus named once
        Assert.Equal("peter_2745", EntityNames.Find(nt, "people", "Petrus")!.Id);
        Assert.Equal("peter_2745", EntityNames.Find(nt, "people", "Kephas")!.Id);
        Assert.Equal(["john_1", "john_2", "john_3"], EntityNames.Candidates(nt, "people", "Johannes").Select(i => i.Id));
        Assert.Equal("john_2", EntityNames.Find(nt, "people", "John the Apostle")!.Id);
        Assert.Equal("mary_1", EntityNames.Find(nt, "people", "Maria")!.Id);
        Assert.Equal("mary_2", EntityNames.Find(nt, "people", "Maria Magdalena")!.Id);
    }

    [Fact]
    public void Only_names_of_one_alone_are_looked_for_in_the_text()
    {
        var abraham = new LexiconEntry("people", "abraham_58", "Abraham", ["Abram"], [], [], [], "");
        Assert.Equal(["Abraham", "Abram"], EntityNames.NamesInText(Items, abraham, "Abraham").Names);
        var jacob = new LexiconEntry("people", "jacob_1", "Jacob", ["Israel"], [], [], [], "");
        Assert.Equal(["Jakob", "Jacob"], EntityNames.NamesInText(Items, jacob, "Jakob").Names);   // Israel is a place too
        var mary = new LexiconEntry("people", "mary_1", "Mary", [], [], [], [], "");
        Assert.Empty(EntityNames.NamesInText(Items, mary, "Maria").Names);                         // several Marys, none far ahead
        var judah = new LexiconEntry("people", "judah_1", "Judah", [], [], [], [], "");
        Assert.Empty(EntityNames.NamesInText([.. Items, new("places", "judah_9", "Judah", 800)], judah, "Juda").Names);   // also the land

        // far ahead of the others with the same name: the name counts, and the others' verses are left out
        List<LexiconItem> nt = [new("people", "jesus_1", "Jesus", 1), new("people", "jesus_905", "Jesus Christ", 1296), new("people", "peter_2745", "Simon Peter", 129),
            new("people", "simon_1", "Simon", 9), new("people", "simon_2", "Simon", 4), new("places", "babylon_1", "Babylon", 300)];
        var jesus = new LexiconEntry("people", "jesus_905", "Jesus Christ", [], [], [], [], "");
        var forJesus = EntityNames.NamesInText(nt, jesus, "Jesus");
        Assert.Contains("Jesus", forJesus.Names); Assert.Contains("Jesu", forJesus.Names);
        Assert.Equal(["jesus_1"], forJesus.Others.Select(o => o.Id));
        var peter = new LexiconEntry("people", "peter_2745", "Simon Peter", ["Cephas", "Simon"], [], [], [], "");
        var forPeter = EntityNames.NamesInText(nt, peter, "Petrus").Names;
        Assert.Contains("Petrus", forPeter); Assert.Contains("Kephas", forPeter); Assert.DoesNotContain("Simon", forPeter);   // other Simons are too many
        var babylon = new LexiconEntry("places", "babylon_1", "Babylon", [], [], [], [], "");
        Assert.Contains("Babel", EntityNames.NamesInText(nt, babylon, "Babylon").Names);   // as the Elberfelder writes it
    }

    [Fact]
    public void Verses_that_name_them_are_found_with_the_genitive()
    {
        var corpus = new[] { "Und Abram zog aus.", "Der Gott Abrahams.", "Abrahamitisch ist kein Wort.", "Sie sprach zu ihm." }
            .Select((t, i) => new CorpusVerse(new VerseRef("GEN", 12, i + 1), t)).ToList();
        Assert.Equal([1, 2], EntityNames.NamedIn(corpus, ["Abraham", "Abram"]).Select(r => r.Verse).Order());
    }
}
