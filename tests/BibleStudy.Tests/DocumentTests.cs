using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class DocumentTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Renaming_a_document_keeps_its_links_working()
    {
        var body = "Siehe [[Alt]], [[alt|hier klicken]], [[Alt#Teil]] und [[Anderes]]. Alt bleibt.";
        Assert.Equal("Siehe [[Neu]], [[Neu|hier klicken]], [[Neu#Teil]] und [[Anderes]]. Alt bleibt.", DocumentLinks.RenameLink(body, "Alt", "Neu"));
    }

    [Fact]
    public void A_link_leads_by_the_number_of_the_document_however_often_the_title_changes()
    {
        var s = new VaultStore(Path.Combine(_dir, "rename.db"), "t");
        var target = s.AddDocument("Alt", "Ziel", null, false, []);
        var other = s.AddDocument("Anderes", "Siehe [[Alt|hier klicken]] und [[Alt]].", null, false, []);
        Assert.Equal($"Siehe [[#{target}:Alt|hier klicken]] und [[#{target}:Alt]].", s.GetDocuments().First(d => d.Id == other).Body);
        var before = s.GetDocuments().First(d => d.Id == other).Updated;
        s.SetNote(new VerseRef("JHN", 3, 16), "Vgl. [[Alt]]");

        s.UpdateDocument(target, "Neu", "Ziel", null, false, []);
        s.UpdateDocument(target, "Noch neuer", "Ziel", null, false, []);

        var after = s.GetDocuments().First(d => d.Id == other);
        Assert.Equal($"Siehe [[#{target}:Noch neuer|hier klicken]] und [[#{target}:Noch neuer]].", after.Body);
        Assert.Equal(before, after.Updated);   // the user did not write there
        Assert.Equal($"Vgl. [[#{target}:Noch neuer]]", s.GetNotes().Single().Text);
        Assert.True(DocumentLinks.LinksTo(after.Body, target, "Noch neuer"));
    }

    [Fact]
    public void Two_documents_with_the_same_title_keep_their_links_apart()
    {
        var s = new VaultStore(Path.Combine(_dir, "rename2.db"), "t");
        var a = s.AddDocument("Gnade 2", "", null, false, []);
        var b = s.AddDocument("Gnade", "", null, false, []);
        var c = s.AddDocument("C", "[[Gnade 2]] und [[Gnade]]", null, false, []);

        s.UpdateDocument(a, "Gnade", "", null, false, []);   // now both have this title
        s.UpdateDocument(a, "Gnade zwei", "", null, false, []);

        Assert.Equal($"[[#{a}:Gnade zwei]] und [[#{b}:Gnade]]", s.GetDocuments().First(d => d.Id == c).Body);
    }

    [Fact]
    public void Links_from_an_older_version_get_their_number_when_the_vault_opens()
    {
        var path = Path.Combine(_dir, "old.db");
        var s = new VaultStore(path, "t");
        var a = s.AddDocument("Ziel", "", null, false, []);
        var b = s.AddDocument("Quelle", "", null, false, []);
        using (var con = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE documents SET body='[[Ziel|Hier]] und [[Gibt es nicht]]' WHERE id=" + b;
            cmd.ExecuteNonQuery();
        }

        var again = new VaultStore(path, "t");

        Assert.Equal($"[[#{a}:Ziel|Hier]] und [[Gibt es nicht]]", again.GetDocuments().First(d => d.Id == b).Body);
    }

    [Fact]
    public void A_link_waits_for_its_document_and_a_number_is_never_given_twice()
    {
        var s = new VaultStore(Path.Combine(_dir, "seq.db"), "t");
        var source = s.AddDocument("Quelle", "[[Kommt noch]]", null, false, []);
        var late = s.AddDocument("Kommt noch", "", null, false, []);
        Assert.Equal($"[[#{late}:Kommt noch]]", s.GetDocuments().First(d => d.Id == source).Body);

        s.DeleteDocument(late);
        var next = s.AddDocument("Etwas anderes", "", null, false, []);

        Assert.NotEqual(late, next);
        Assert.Equal($"[[#{late}:Kommt noch]]", s.GetDocuments().First(d => d.Id == source).Body);   // it does not lead to the new one
    }

    [Fact]
    public void Numbers_stay_inside_the_program()
    {
        Assert.Equal("[[Titel|Text]] und [[Titel]]", DocumentLinks.WithoutNumbers("[[#5:Titel|Text]] und [[#5:Titel]]"));
        Assert.Equal("Text und Titel", DocumentLinks.WithLinkTexts("[[#5:Titel|Text]] und [[#5:Titel]]"));
        var target = new Document(5, "Neuer Titel", "", null, false, DateTime.Now, []);
        (string Title, string File)? Find(DocumentLinks.DocLink l) => l.Id == 5 ? (target.Title, "Neuer Titel.md") : null;
        var md = DocumentLinks.ToMarkdownLinks("Lies [[#5:Alt|hier]], [[#5:Alt]], [[Fehlt]] und Joh 3,16.", Find, span => span == "Joh 3,16" ? "Bibelstellen.md#vers-1" : null);
        Assert.Equal("Lies [hier](Neuer%20Titel.md), [Neuer Titel](Neuer%20Titel.md), Fehlt und [Joh 3,16](Bibelstellen.md#vers-1).", md);
    }

    [Fact]
    public void A_first_heading_that_repeats_the_title_is_left_out_of_the_export()
    {
        Assert.Equal("Text", DocumentLinks.WithoutTitleHeading("# Der Titel\n\nText", "Der Titel"));
        Assert.Equal("# Etwas anderes\n\nText", DocumentLinks.WithoutTitleHeading("# Etwas anderes\n\nText", "Der Titel"));
    }

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
