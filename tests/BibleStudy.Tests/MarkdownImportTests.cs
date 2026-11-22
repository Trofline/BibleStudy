using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

public class MarkdownImportTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_md_" + Guid.NewGuid().ToString("N"));
    public MarkdownImportTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Head_with_title_and_tags_and_heading_that_repeats_the_title()
    {
        var p = MarkdownImport.Parse("---\ntitle: \"Gnade\"\ndate: 2026-01-01\ntags: [\"Lehre\", \"Heil\"]\n---\n\n# Gnade\n\nText mit #Paulus und [Römer 8,28](Bibelstellen.md#v1).\n", "irgendwas.md");
        Assert.Equal(MarkdownImport.PageKind.Document, p.Kind);
        Assert.Equal("Gnade", p.Title);
        Assert.Equal(["Lehre", "Heil", "Paulus"], p.Tags);
        Assert.Equal("Text mit #Paulus und Römer 8,28.", p.Body);
    }

    [Fact]
    public void Block_list_of_tags_and_title_from_the_file_name()
    {
        var p = MarkdownImport.Parse("---\ntags:\n  - a\n  - b c\n---\nNur Text", "Mein Titel.md");
        Assert.Equal("Mein Titel", p.Title);
        Assert.Equal(["a", "b c"], p.Tags);
        Assert.Equal("Nur Text", p.Body);
    }

    [Fact]
    public void Markdown_links_between_files_become_document_links_and_web_links_stay()
    {
        var body = MarkdownImport.FromMarkdownLinks("Siehe [Gnade](Gnade.md), [mehr dazu](Ordner/Gnade%20und%20Frieden.md#teil), [Web](https://example.org/a.md) und [[Ordner/Tiefe]].");
        Assert.Equal("Siehe [[Gnade]], [[Gnade und Frieden|mehr dazu]], [Web](https://example.org/a.md) und [[Tiefe]].", body);
    }

    [Fact]
    public void A_note_of_the_export_gives_back_only_the_note()
    {
        var text = "---\nverse: \"Römer 8,28\"\n---\n\n# Römer 8,28\n\n> Wir wissen aber, dass denen, die Gott lieben...\n\nMeine Notiz.\n\n## Erwähnt in Notizen zu\n\n- [[Johannes 3,16]]\n";
        var p = MarkdownImport.Parse(text, "Römer 8,28.md");
        Assert.Equal(MarkdownImport.PageKind.Note, p.Kind);
        Assert.Equal(new VerseRef("ROM", 8, 28), p.Verse);
        Assert.Equal("Meine Notiz.", p.Body);
    }

    [Fact]
    public void Folder_becomes_vault_folders_documents_merge_instead_of_copying()
    {
        var store = new VaultStore(Path.Combine(_dir, "v.db"), "t");
        var have = store.AddDocument("Gnade", "Zeile eins\nZeile zwei", null, false, ["alt"]);
        var src = Path.Combine(_dir, "src");
        Directory.CreateDirectory(Path.Combine(src, "Lehre", "Heil")); Directory.CreateDirectory(Path.Combine(src, ".obsidian"));
        File.WriteAllText(Path.Combine(src, "Gnade.md"), "---\ntags: [neu]\n---\nZeile eins\nZeile zwei\nZeile drei");
        File.WriteAllText(Path.Combine(src, "Lehre", "Heil", "Rettung.md"), "# Rettung\n\nSiehe [Gnade](../../Gnade.md).");
        File.WriteAllText(Path.Combine(src, ".obsidian", "x.md"), "nicht lesen");
        File.WriteAllText(Path.Combine(src, "Start.md"), "# Start\n\nExport aus BibleStudy");

        var r = MarkdownImport.ImportFolder(store, src, new HashSet<string> { "Start" });
        Assert.Equal((1, 1, 0, 1, 2), (r.Added, r.Merged, r.Notes, r.Skipped, r.Folders));
        var docs = store.GetDocuments();
        Assert.Equal(2, docs.Count);
        var gnade = docs.Single(d => d.Title == "Gnade");
        Assert.Equal(have, gnade.Id);
        Assert.Equal("Zeile eins\nZeile zwei\nZeile drei", gnade.Body.ReplaceLineEndings("\n"));
        Assert.Contains("neu", gnade.Tags);
        var rettung = docs.Single(d => d.Title == "Rettung");
        var folders = store.GetFolders();
        Assert.Equal("Heil", folders.Single(f => f.Id == rettung.FolderId).Name);
        Assert.Equal("Lehre", folders.Single(f => f.Id == folders.Single(x => x.Name == "Heil").ParentId).Name);

        // the same folder again changes nothing
        var again = MarkdownImport.ImportFolder(store, src, new HashSet<string> { "Start" });
        Assert.Equal((0, 0, 2), (again.Added, again.Merged, again.Unchanged));
        Assert.Equal(2, store.GetDocuments().Count);
    }
}
