using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;
using BibleStudy.Core.Storage;
using Xunit;

namespace BibleStudy.Tests;

/// <summary>What the walk through the whole program found: each test fails as long as the fault is there.</summary>
public class ReviewFindingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    public ReviewFindingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("1CO")]
    [InlineData("2TH")]
    [InlineData("3JN")]
    [InlineData("ROM")]
    public async Task TextVariants_AreFoundForBooksThatStartWithANumber(string book)
    {
        // the file of the second half of the New Testament, as the program keeps it in its cache (the rows name the books "1Co", "2Th", "3Jn", "Rom")
        var name = char.ToUpperInvariant(book[0]) + (char.IsDigit(book[0]) ? char.ToUpperInvariant(book[1]) + book[2..].ToLowerInvariant() : book[1..].ToLowerInvariant());
        var file = Path.Combine(_dir, "tagnt", "act-rev.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, $"{name}.1.1#01=k\tὁ (ho)\tthe\tG3588=T-NSM\tὁ=the\tTR+Byz\t\t\n");
        var variants = new TextVariants(new HttpClient(), _dir);
        Assert.Single(await variants.GetAsync(book, 1, 1));
    }

    [Fact]
    public void Search_DoesNotBreakOnACharacterOutsideTheBasicPlane()
    {
        var text = "Freude \U0001F600 und 𐤀 Zeichen";   // an emoji and a Phoenician letter, as a PDF or web page may carry them
        var verse = new CorpusVerse(new VerseRef("GEN", 1, 1), text);
        Assert.Equal(text.Length, verse.Fold.Length);
        Assert.NotEmpty(SearchEngine.Run([verse], "Freude", null).Results);
    }

    [Fact]
    public void Wipe_LeavesNothingOfTheUserBehind()
    {
        var vault = new VaultStore(Path.Combine(_dir, "v.db"), "t");
        var verse = new VerseRef("JHN", 3, 16);
        vault.AddHeading(verse, "Meine Überschrift");
        var mark = vault.AddMark("eng_kjv", verse, 0, 3, "For", "#FFD54F", "", null, []);
        vault.SetMarkPlace(mark, "deu_elbbk", 16, 0, 4);
        vault.TrashMark(vault.AddMark("eng_kjv", verse, 4, 7, "God", "#FFD54F", "geheim", null, []));
        vault.Wipe();
        Assert.Empty(vault.GetHeadings());
        Assert.Empty(vault.GetMarkPlaces());
        Assert.Empty(vault.GetTrash());
    }

    [Fact]
    public void EverydayWords_AreNoBibleReferences()
    {
        // "mal" and "hab" are words of every German sentence; written with a capital letter they are Malachi and Habakkuk
        var refs = DocumentLinks.VerseRefs("Schau mal 3,5 Meter weiter, ich hab 3,5 Euro. Mal 3,6 und Hab 2,4 gelten, auch joh 3,16.");
        Assert.Equal([new VerseRef("MAL", 3, 6), new VerseRef("HAB", 2, 4), new VerseRef("JHN", 3, 16)], refs);
    }

    [Fact]
    public void RelayPassword_IsNotReadableInTheSettingsFile()
    {
        var path = Path.Combine(_dir, "settings.json");
        new AppSettings { RelayPassword = "geheim123" }.Save(path);
        if (OperatingSystem.IsWindows()) Assert.DoesNotContain("geheim123", File.ReadAllText(path));
        Assert.Equal("geheim123", AppSettings.Load(path).RelayPassword);
        // a file of an older version holds it as plain text
        File.WriteAllText(path, "{\"RelayPassword\":\"alt\"}");
        Assert.Equal("alt", AppSettings.Load(path).RelayPassword);
    }

    [Fact]
    public void Plugin_MustBeAllowedAgain_WhenItsProgramChanges()
    {
        var dir = Path.Combine(_dir, "plugins", "demo");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), "{\"id\":\"demo\",\"name\":\"Demo\",\"command\":\"run.bat\"}");
        File.WriteAllText(Path.Combine(dir, "run.bat"), "echo 1");
        var approved = new Dictionary<string, string>();
        using var manager = new BibleStudy.Core.Plugins.PluginManager(Path.Combine(_dir, "plugins"), () => approved);
        var first = Assert.Single(manager.Discover());
        approved["demo"] = first.Hash;
        Assert.True(manager.IsAllowed(first));
        File.WriteAllText(Path.Combine(dir, "data.txt"), "what the plugin writes does not count");
        Assert.True(manager.IsAllowed(Assert.Single(manager.Discover())));
        File.WriteAllText(Path.Combine(dir, "run.bat"), "echo 2");
        Assert.False(manager.IsAllowed(Assert.Single(manager.Discover())));
    }

    [Fact]
    public void GetNote_ReadsTheOneVerse()
    {
        var vault = new VaultStore(Path.Combine(_dir, "n.db"), "t");
        vault.SetNote(new VerseRef("JHN", 3, 16), "eins");
        vault.SetNote(new VerseRef("JHN", 3, 17), "zwei");
        Assert.Equal("zwei", vault.GetNote(new VerseRef("JHN", 3, 17))?.Text);
        Assert.Null(vault.GetNote(new VerseRef("JHN", 3, 18)));
    }
}
