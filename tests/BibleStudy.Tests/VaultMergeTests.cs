using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class VaultMergeTests
{
    static string TempDir() { var d = Path.Combine(Path.GetTempPath(), "bs_merge_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
    static VerseRef R(int v) => new("GEN", 1, v);

    [Fact]
    public void Merging_adds_what_is_new_and_loses_nothing()
    {
        var dir = TempDir();
        var home = new VaultStore(Path.Combine(dir, "home.db"), "home");
        var guestPath = Path.Combine(dir, "guest.db");
        var guest = new VaultStore(guestPath, "guest");

        // the same mark on both sides, with different notes
        home.AddMark("t", R(1), 0, 5, "Im An", "#111111", "meine Notiz", null, ["a"]);
        guest.AddMark("t", R(1), 0, 5, "Im An", "#222222", "Notiz vom Gast", null, ["b"]);
        // only in the guest copy, in a sub-folder
        var parent = guest.AddFolder("Oben"); var child = guest.AddFolder("Unten"); guest.SetFolderParent(child, parent);
        guest.AddMark("t", R(2), 3, 7, "Erde", "#333333", "", child, []);
        guest.SetNote(R(3), "neue Notiz");
        home.AddDocument("Gleich", "Text A", null, false, []);
        guest.AddDocument("Gleich", "Text B", null, false, []);
        guest.AddDocument("Neu", "siehe [[#1:Gleich]]", null, false, []);
        guest.AddTodo("Aufgabe", null); var task = guest.GetTodos().Single().Id;
        guest.AddTodo("Teil", null, task);

        var r = home.MergeFrom(guestPath, "Gast");

        var same = home.GetMarks().Single(m => m.Ref.Verse == 1);
        Assert.Equal("#111111", same.Color);   // mine stays
        Assert.Contains("meine Notiz", same.Note); Assert.Contains("Notiz vom Gast", same.Note);
        Assert.Equal(new[] { "a", "b" }, same.Tags.Order());
        var moved = home.GetMarks().Single(m => m.Ref.Verse == 2);
        var folders = home.GetFolders();
        Assert.Equal("Unten", folders.Single(f => f.Id == moved.FolderId).Name);
        Assert.Equal("Oben", folders.Single(f => f.Id == folders.Single(x => x.Name == "Unten").ParentId).Name);
        Assert.Equal("neue Notiz", home.GetNotes().Single(n => n.Ref.Verse == 3).Text);
        var docs = home.GetDocuments();
        Assert.Equal("Text A", docs.Single(d => d.Title == "Gleich").Body);
        Assert.Equal("Text B", docs.Single(d => d.Title == "Gleich (Gast)").Body);
        Assert.Contains("Gleich", docs.Single(d => d.Title == "Neu").Body);
        var todos = home.GetTodos();
        Assert.Equal(todos.Single(t => t.Text == "Aufgabe").Id, todos.Single(t => t.Text == "Teil").ParentId);
        Assert.Equal(2, r.Conflicts);

        // a second merge of the same file adds nothing more
        var countMarks = home.GetMarks().Count; var countDocs = home.GetDocuments().Count; var countTodos = home.GetTodos().Count;
        var again = home.MergeFrom(guestPath, "Gast");
        Assert.Equal(countMarks, home.GetMarks().Count); Assert.Equal(countDocs, home.GetDocuments().Count); Assert.Equal(countTodos, home.GetTodos().Count);
        Assert.Equal(0, again.Marks + again.Documents);
    }

    [Theory]
    [InlineData("alt", "alt und neu", "alt und neu", false)]
    [InlineData("alt und neu", "alt", "alt und neu", false)]
    [InlineData("", "neu", "neu", false)]
    [InlineData("meins", "", "meins", false)]
    [InlineData("meins", "deins", "meins\n\ndeins", true)]
    public void Two_texts_of_one_place(string mine, string theirs, string expected, bool conflict)
    {
        Assert.Equal(expected, VaultStore.MergeText(mine, theirs, out var c));
        Assert.Equal(conflict, c);
    }

    [Fact]
    public void The_fingerprint_changes_with_the_content_but_not_with_reading()
    {
        var v = new VaultStore(Path.Combine(TempDir(), "v.db"), "v");
        var before = v.Fingerprint();
        v.SetLastPosition("GEN", 3);
        Assert.Equal(before, v.Fingerprint());
        v.SetNote(R(1), "x");
        Assert.NotEqual(before, v.Fingerprint());
    }

    [Fact]
    public void A_corrected_place_goes_with_its_mark()
    {
        var v = new VaultStore(Path.Combine(TempDir(), "v.db"), "v");
        var id = v.AddMark("t", R(1), 0, 3, "abc", "#111111", "", null, []);
        v.SetMarkPlace(id, "other", 1, 4, 9);
        Assert.Equal((4, 9), v.GetMarkPlaces()[(id, "other", 1)]);
        v.DeleteMark(id);
        Assert.Empty(v.GetMarkPlaces());
    }

    [Fact]
    public void The_summary_names_the_translations_of_part_marks()
    {
        var v = new VaultStore(Path.Combine(TempDir(), "v.db"), "v");
        v.AddMark("local:x", R(1), 0, 3, "abc", "#111111", "", null, []);
        v.AddMark("local:y", R(2), -1, -1, "whole", "#111111", "", null, []);
        var s = v.Summary();
        Assert.Equal(2, s.Marks);
        Assert.Equal(new[] { "local:x" }, s.Translations);
    }

    [Fact]
    public void Position_guess_is_reported_as_an_estimate_and_same_words_are_not()
    {
        IReadOnlyList<Segment> S(string t) => [new Segment(t)];
        var src = S("eins zwei drei vier fuenf sechs");
        Assert.False(MarkLogic.ProjectChecked(src, 5, 9, S("nur zwei hier und noch mehr"))!.Value.Estimated);
        Assert.True(MarkLogic.ProjectChecked(src, 5, 9, S("uno dos tres cuatro cinco seis"))!.Value.Estimated);
    }
}

public class ImportDiffTests
{
    static ParsedVerse V(int n, string t) => new("GEN", 1, n, [new Segment(t)]);

    [Fact]
    public void Counts_changed_added_and_removed_verses()
    {
        var old = new[] { new VerseText("GEN", 1, 1, "gleich"), new VerseText("GEN", 1, 2, "alt"), new VerseText("GEN", 1, 3, "weg") };
        var r = ImportDiff.Compare(old, new ParsedBible("T", "T", "de", [V(1, "gleich"), V(2, "neu"), V(4, "dazu")], []));
        Assert.Equal((1, 1, 1), (r.Changed, r.Added, r.Removed));
        Assert.False(r.None);
        Assert.True(ImportDiff.Compare([new VerseText("GEN", 1, 1, "x")], new ParsedBible("T", "T", "de", [V(1, "x")], [])).None);
    }

    [Fact]
    public void Focus_shows_only_the_difference_with_some_text_around_it()
    {
        var prefix = new string('a', 60); var suffix = new string('b', 60);
        var (o, n) = ImportDiff.Focus(prefix + "gin g en" + suffix, prefix + "gingen" + suffix, 5);
        Assert.StartsWith("… ", o); Assert.EndsWith(" …", n);
        Assert.Contains("gin g en", o); Assert.Contains("gingen", n);
        Assert.True(o.Length < 40);
    }

    [Fact]
    public void A_moved_file_is_found_by_its_name_or_a_similar_one()
    {
        var root = Path.Combine(Path.GetTempPath(), "bs_find_" + Guid.NewGuid().ToString("N"));
        var sub = Path.Combine(root, "Bibeln"); Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "Schlachter2000.pdf"), "x");
        var gone = Path.Combine(root, "weg", "Schlachter2000.pdf");
        var exact = ImportFileFinder.Find(gone, [root]);
        Assert.True(exact!.Exact); Assert.EndsWith("Schlachter2000.pdf", exact.Path);

        var renamed = Path.Combine(root, "weg", "Schlachter2000 (1).pdf");
        var similar = ImportFileFinder.Find(renamed, [root]);
        Assert.False(similar!.Exact);

        Assert.Null(ImportFileFinder.Find(Path.Combine(root, "weg", "Luther1912.pdf"), [root]));
    }
}

public class MergeReviewTests
{
    static string TempDir() { var d = Path.Combine(Path.GetTempPath(), "bs_review_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
    static VerseRef R(int v) => new("GEN", 1, v);

    [Fact]
    public void A_conflict_can_be_settled_either_way()
    {
        var dir = TempDir();
        var home = new VaultStore(Path.Combine(dir, "h.db"), "h"); var gp = Path.Combine(dir, "g.db"); var guest = new VaultStore(gp, "g");
        home.SetNote(R(1), "A"); guest.SetNote(R(1), "B");
        home.AddDocument("D", "eins", null, false, []); guest.AddDocument("D", "zwei", null, false, []);
        var r = home.MergeFrom(gp, "Gast");
        Assert.Equal(2, r.Items!.Count);
        home.ResolveConflict(r.Items.Single(i => i.Kind == "note"), 1);
        Assert.Equal("B", home.GetNotes().Single().Text);
        home.ResolveConflict(r.Items.Single(i => i.Kind == "doc"), 1);
        Assert.Equal("zwei", home.GetDocuments().Single().Body);
    }

    [Fact]
    public void What_was_deleted_as_a_guest_is_found_unless_it_changed_here()
    {
        var dir = TempDir();
        var bp = Path.Combine(dir, "b.db"); var gp = Path.Combine(dir, "g.db");
        var home = new VaultStore(Path.Combine(dir, "h.db"), "h"); var b = new VaultStore(bp, "b"); var g = new VaultStore(gp, "g");
        foreach (var v in new[] { home, b }) { v.AddMark("t", R(1), 0, 3, "abc", "#111111", "", null, []); v.SetNote(R(2), "weg"); v.SetNote(R(3), "alt"); }
        home.SetNote(R(3), "hier geändert");
        var found = home.FindDeletions(bp, gp);
        Assert.Equal(2, found.Count);
        foreach (var d in found) home.ApplyDeletion(d);
        Assert.Empty(home.GetMarks());
        Assert.Equal("hier geändert", home.GetNotes().Single().Text);
        Assert.Equal(2, home.GetTrash().Count);
    }

    [Fact]
    public void Marks_over_several_verses_are_found_by_their_words()
    {
        var m = new Mark(1, "gone", R(1), 6, 4, "Anfang schuf Gott die Himmel und die Erde", "#111111", "", null, [], 3);
        Assert.Equal((3, 15), MarkLogic.ProjectByStoredText(m, "Im Anfang schuf", 1));
        Assert.Equal((0, 8), MarkLogic.ProjectByStoredText(m, "Gott die", 2));
        Assert.Equal((0, 19), MarkLogic.ProjectByStoredText(m, "Himmel und die Erde und mehr", 3));
        Assert.Null(MarkLogic.ProjectByStoredText(m, "etwas ganz anderes", 2));
    }
}

public class AiCorrectionTests
{
    static ParsedBible B(params ParsedVerse[] v) => new("T", "T", "de", v, []);

    [Fact]
    public void Only_the_differing_part_changes_and_footnotes_stay()
    {
        IReadOnlyList<Segment> segs = [new("Da gin g "), new("", NoteId: 1), new("en sie hinaus")];
        var r = ImportCheck.WithText(segs, "Da gingen sie hinaus");
        Assert.Equal("Da gingen sie hinaus", string.Concat(r.Where(s => s.NoteId is null).Select(s => s.Text)));
        Assert.Contains(r, s => s.NoteId == 1);
    }

    [Fact]
    public void An_answer_is_applied_and_a_reworded_verse_refused()
    {
        var b = B(new ParsedVerse("GEN", 1, 1, [new Segment("Im Anfang schuf Gott d en Himmel")]), new ParsedVerse("GEN", 1, 2, [new Segment("Und die Erde war wüst und leer")]));
        var (x, ok, no) = ImportCheck.ApplyCorrections(b, "Hier die Korrektur:\nGEN 1,1: Im Anfang schuf Gott den Himmel\nGEN 1,2: Ganz etwas anderes steht jetzt hier\nEXO 9,9: gibt es nicht");
        Assert.Equal((1, 2), (ok, no));
        Assert.Equal("Im Anfang schuf Gott den Himmel", x.Verses[0].Segments[0].Text);
        Assert.Equal("Und die Erde war wüst und leer", x.Verses[1].Segments[0].Text);
    }

    [Fact]
    public void Unticked_changes_keep_the_old_text()
    {
        var b = B(new ParsedVerse("GEN", 1, 1, [new Segment("neu")]), new ParsedVerse("GEN", 1, 3, [new Segment("dazu")]));
        var r = ImportDiff.Without(b, [new("GEN", 1, 1, "alt", "neu"), new("GEN", 1, 3, "", "dazu"), new("GEN", 1, 4, "weg", "")]);
        Assert.Equal(new[] { "alt", "weg" }, r.Verses.Select(v => v.Segments[0].Text));
    }

    [Fact]
    public void Bibles_travel_in_a_file_of_their_own_and_never_replace()
    {
        var d = Path.Combine(Path.GetTempPath(), "bs_bib_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        var a = new LocalBibleStore(Path.Combine(d, "a.db"));
        var id = a.Save(B(new ParsedVerse("GEN", 1, 1, [new Segment("Text")])), @"C:\privat\datei.pdf");
        var file = Path.Combine(d, "x.db");
        a.CopyTo([id], file);
        var c = new LocalBibleStore(Path.Combine(d, "c.db"));
        Assert.Equal(new[] { id }, c.AddFrom(file));
        Assert.Null(c.GetOutdated().FirstOrDefault()?.Source);
        Assert.Equal("Text", c.GetChapterAsync(id, "GEN", 1).Result.Verses[0].Text);
        Assert.Empty(c.AddFrom(file));
    }
}
