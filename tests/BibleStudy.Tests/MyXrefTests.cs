using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class MyXrefTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void OwnCrossReferences_AreFoundFromBothEndsAndCanBeDeleted()
    {
        Directory.CreateDirectory(_dir);
        var s = new VaultStore(Path.Combine(_dir, "v.db"), "t");
        var from = new VerseRef("JHN", 3, 16); var to = new VerseRef("ROM", 5, 8);
        s.AddMyXref(from, to, 10, "Gottes Liebe");
        s.AddMyXref(from, to, 10, "doppelt");   // the same link is not stored twice
        Assert.Single(s.GetMyXrefs("JHN", 3));
        var back = Assert.Single(s.GetMyXrefs("ROM", 5));   // the way back is found from the target's chapter
        Assert.Equal((from, to, 10, "Gottes Liebe"), (back.From, back.To, back.EndVerse, back.Note));
        Assert.Empty(s.GetMyXrefs("GEN", 1));
        s.DeleteMyXref(back.Id);
        Assert.Empty(s.GetMyXrefs());
    }

    [Fact]
    public void Together_NeedsAllWordsInTheSameVerse()
    {
        var a = new WordAnalyzer([
            new StrongsOccurrence("G1", new VerseRef("A", 1, 1), "x"), new StrongsOccurrence("G2", new VerseRef("A", 1, 1), "y"), new StrongsOccurrence("G3", new VerseRef("A", 1, 1), "z"),
            new StrongsOccurrence("G1", new VerseRef("A", 1, 2), "x"), new StrongsOccurrence("G3", new VerseRef("A", 1, 2), "z"),
            new StrongsOccurrence("G2", new VerseRef("A", 1, 3), "y"),
        ]);
        Assert.Equal([new VerseRef("A", 1, 1)], a.Together(["G1", "G2"]));
        Assert.Equal([("G3", 1)], a.CoOccurring(["G1", "G2"], 5));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}
