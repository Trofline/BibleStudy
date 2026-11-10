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
}
