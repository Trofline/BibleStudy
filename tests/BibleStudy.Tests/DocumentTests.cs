using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class DocumentTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Links_FindVerseReferencesInGermanAndEnglishSpellings()
    {
        var refs = DocumentLinks.VerseRefs("Siehe Joh 3,16 und 1. Kor 13,4-7, auch Rom 5:8 sowie Psalm 23,1. Genesis 1:1 gilt; \"Preis 3,50 Euro\" nicht, Hiob 1,2.");
        Assert.Equal([new VerseRef("JHN", 3, 16), new("1CO", 13, 4), new("ROM", 5, 8), new("PSA", 23, 1), new("GEN", 1, 1), new("JOB", 1, 2)], refs);
        Assert.Equal(["Gnade", "Liebe Gottes"], DocumentLinks.WikiLinks("[[Gnade]] und [[Liebe Gottes|Liebe]] und nochmal [[gnade]]"));
    }

    [Fact]
    public void Documents_StoreSearchTagsAndTemplates()
    {
        var s = new VaultStore(Path.Combine(_dir, "v.db"), "t");
        var a = s.AddDocument("Gnade", "Epheser 2,8 erklärt", null, false, ["Heil", "heil", "Paulus"]);
        s.AddDocument("Predigtvorlage", "Einleitung, Hauptteil", null, true, []);
        s.AddDocument("studie.pdf", "", @"C:\Docs\studie.pdf", false, []);
        Assert.Equal(3, s.GetDocuments().Count);
        Assert.Equal(["Heil", "Paulus"], s.GetDocuments("epheser").Single().Tags.OrderBy(t => t));
        Assert.Equal("Predigtvorlage", s.GetDocuments(templates: true).Single().Title);
        Assert.Equal("Gnade", s.GetDocuments(tag: "Paulus").Single().Title);
        Assert.Equal("Gnade", s.GetDocuments("HEIL").Single().Title);                   // tags are searched too
        Assert.Empty(s.GetDocuments("100%"));                                              // LIKE wildcards are escaped
        s.UpdateDocument(a, "Gnade (neu)", "Text", null, false, []);
        Assert.Equal("Gnade (neu)", s.FindDocument("gnade (NEU)")!.Title);
        s.DeleteDocument(a);
        Assert.Equal(2, s.GetDocuments().Count);
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }
}
