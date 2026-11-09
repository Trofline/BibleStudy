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
