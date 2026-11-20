using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class VaultCopyTests
{
    static VaultStore NewVault(string name) { var d = Path.Combine(Path.GetTempPath(), "bs_copy_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return new VaultStore(Path.Combine(d, name + ".db"), name); }

    [Fact]
    public void A_mark_goes_into_another_vault_with_its_color_note_tags_and_folder_and_only_once()
    {
        var a = NewVault("a"); var b = NewVault("b");
        var top = a.AddFolder("Gottes Name"); var sub = a.AddFolder("Exodus"); a.SetFolderParent(sub, top);
        var id = a.AddMark("ELB", new VerseRef("EXO", 3, 15), 4, 20, "Gott sprach weiter", "#E5484D", "der Name", sub, ["name", "gott"]);
        b.AddFolder("gottes name");   // there already, other case: it is used, not made twice

        Assert.Equal([id], a.CopyMarksTo(b, [id]));
        var m = Assert.Single(b.GetMarks());
        Assert.Equal(("Gott sprach weiter", "#E5484D", "der Name"), (m.Text, m.Color, m.Note));
        Assert.Equal(["gott", "name"], m.Tags.Order());
        var folders = b.GetFolders();
        Assert.Equal(2, folders.Count);
        var bsub = folders.Single(f => f.Id == m.FolderId);
        Assert.Equal("Exodus", bsub.Name);
        Assert.Equal("gottes name", folders.Single(f => f.Id == bsub.ParentId).Name);

        a.CopyMarksTo(b, [id]);
        Assert.Single(b.GetMarks());   // copied again: still once
        Assert.Single(a.GetMarks());   // and the original stays
    }

    [Fact]
    public void A_note_goes_into_another_vault_and_joins_a_note_there()
    {
        var a = NewVault("a"); var b = NewVault("b");
        var r = new VerseRef("JHN", 1, 1);
        a.SetNote(r, "Das Wort war Gott.");
        b.SetNote(r, "Im Anfang.");
        Assert.Equal([r], a.CopyNotesTo(b, [r]));
        var text = Assert.Single(b.GetNotes()).Text;
        Assert.Contains("Das Wort war Gott.", text);
        Assert.Contains("Im Anfang.", text);
    }
}
