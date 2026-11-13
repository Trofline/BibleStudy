using BibleStudy.Core.Materials;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class MaterialSearchTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    static List<Block> Doc() =>
    [
        Block.Of(BlockKind.Heading1, "Die Hölle"),
        new(BlockKind.Image, [], ImagePath: "x.png"),
        Block.Of(BlockKind.Paragraph, "Lies dazu Röm 8,26-30 und denke an Joh 3,16."),
        Block.Of(BlockKind.Page, "Seite 2"),
        Block.Of(BlockKind.Paragraph, "Von der HÖLLE spricht Jesus oft; die Hölle ist ernst."),
    ];

    [Fact]
    public void Words_are_found_whatever_their_case_and_accents()
    {
        var hits = MaterialSearch.Find("a.docx", Doc(), "holle");
        Assert.Equal(3, hits.Count);
        Assert.Equal(0, hits[0].TextBlock);
        Assert.Equal(3, hits[1].TextBlock);   // the picture is not counted, the page mark is
        Assert.Equal(1, hits[1].Page);
        Assert.Equal("HÖLLE", Doc()[4].Text.Substring(hits[1].Start, hits[1].Length));
    }

    [Fact]
    public void A_verse_is_found_also_inside_a_passage()
    {
        Assert.Single(MaterialSearch.VerseHits("a.docx", Doc(), new VerseRef("ROM", 8, 28)));
        Assert.Single(MaterialSearch.VerseHits("a.docx", Doc(), new VerseRef("JHN", 3, 16)));
        Assert.Empty(MaterialSearch.VerseHits("a.docx", Doc(), new VerseRef("ROM", 8, 31)));
        Assert.Empty(MaterialSearch.VerseHits("a.docx", Doc(), new VerseRef("JHN", 3, 17)));
    }

    [Theory]
    [InlineData("Lies 5. Mose 6,4 laut.", "DEU", "5. Mose 6,4")]
    [InlineData("Der Segen in 4. Mose 6,24-26", "NUM", "4. Mose 6,24-26")]
    [InlineData("Christus tritt für uns ein (Hebr 7,25)", "HEB", "Hebr 7,25")]
    [InlineData("Koh 3,1 sagt es", "ECC", "Koh 3,1")]
    [InlineData("Hi 19,25", "JOB", "Hi 19,25")]
    [InlineData("Am 5,24", "AMO", "Am 5,24")]
    [InlineData("Seite 4 Joh 3,16", "JHN", "Joh 3,16")]
    public void German_short_forms_and_the_fourth_and_fifth_book_of_Moses_are_found(string text, string book, string span)
    {
        var (index, length, r) = Assert.Single(DocumentLinks.VerseSpans(text));
        Assert.Equal(book, r.Book);
        Assert.Equal(span, text.Substring(index, length));
    }

    [Theory]
    [InlineData("wir treffen uns am 5,30 Uhr")]
    [InlineData("mi 6,8")]
    public void Everyday_words_in_small_letters_are_not_books(string text) => Assert.Empty(DocumentLinks.VerseSpans(text));

    [Fact]
    public void A_snippet_is_cut_at_words_around_the_place()
    {
        var text = string.Concat(Enumerable.Repeat("Wort ", 60)) + "Gnade " + string.Concat(Enumerable.Repeat("Wort ", 60));
        var s = MaterialSearch.Snippet(text, text.IndexOf("Gnade"), 5, 80);
        Assert.Contains("Gnade", s);
        Assert.StartsWith("… ", s);
        Assert.EndsWith(" …", s);
        Assert.True(s.Length <= 90);
    }

    [Fact]
    public void A_mark_is_found_again_after_the_file_changed()
    {
        var doc = Doc();
        Assert.Equal((3, 0), MaterialSearch.Locate(doc, 3, 0, "Von der HÖLLE"));
        // a paragraph came in before it: the same words one place further on
        var changed = doc.Take(2).Append(Block.Of(BlockKind.Paragraph, "Neu.")).Concat(doc.Skip(2)).ToList();
        Assert.Equal((4, 0), MaterialSearch.Locate(changed, 3, 0, "Von der HÖLLE"));
        Assert.Null(MaterialSearch.Locate(doc, 3, 0, "gibt es nicht"));
    }

    [Fact]
    public void Marks_in_materials_are_kept_in_the_vault_and_follow_a_moved_file()
    {
        var s = new VaultStore(Path.Combine(_dir, "m.db"), "t");
        var old = Path.Combine(_dir, "alt", "Mappe.docx");
        var id = s.AddMaterialMark(old, 3, 0, "Von der HÖLLE", "#FFD54F", "wichtig");
        var marks = s.GetMaterialMarks(old);
        Assert.Equal("wichtig", Assert.Single(marks).Note);
        // the file was moved (the old one is not there): found by its name
        var moved = Path.Combine(_dir, "neu", "Mappe.docx");
        Assert.Single(s.GetMaterialMarks(moved));
        s.MoveMaterialMark(id, moved, 4, 0);
        Assert.Equal(moved, Assert.Single(s.GetMaterialMarks(moved)).Path);
        s.UpdateMaterialMark(id, "#6FA8F2", "");
        Assert.Equal("#6FA8F2", s.GetMaterialMarks(moved)[0].Color);
        s.DeleteMaterialMark(id);
        Assert.Empty(s.GetAllMaterialMarks());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
