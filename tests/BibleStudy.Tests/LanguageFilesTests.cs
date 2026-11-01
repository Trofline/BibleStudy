using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

public class LanguageFilesTests
{
    [Fact]
    public void TemplateHoldsEveryTextAndCanBeChecked()
    {
        var json = LanguageFiles.Template("Test");
        Assert.Contains("\"@name\": \"Test\"", json);
        // an untranslated template is refused: nothing translated yet
        Assert.Throws<FormatException>(() => LanguageFiles.Check(json));
    }

    [Fact]
    public void TranslatedEntriesAreTakenAndBrokenPlaceholdersSkipped()
    {
        var file = new Dictionary<string, string>
        {
            ["@name"] = "Italiano",
            ["Version {0}"] = "Versione {0}",          // fine
            ["Patch Notes"] = "Note di rilascio",       // fine
            ["Jetzt: {0} · noch {1} offen"] = "Ora: {0}", // placeholder missing -> skipped
            ["Kein solcher Text im Programm"] = "x",     // unknown key -> ignored
        };
        var result = LanguageFiles.Check(System.Text.Json.JsonSerializer.Serialize(file));
        Assert.Equal("Italiano", result.Name);
        Assert.Equal(2, result.Accepted);
        Assert.Equal(1, result.Skipped);
        Assert.False(result.Texts.ContainsKey("Kein solcher Text im Programm"));
    }

    [Theory]
    [InlineData("it.json", "it")]
    [InlineData("C:\\x\\pt-BR.json", "pt-br")]
    [InlineData("meine sprache.json", null)]
    public void CodeComesFromTheFileName(string file, string? code) => Assert.Equal(code, LanguageFiles.CodeFrom(file));

    [Fact]
    public void ShippedLanguagesAreCompleteAndMissingTemplateIsEmptyForThem()
    {
        var (done, total) = LanguageFiles.Progress("fr");
        Assert.True(done > total * 0.9, $"{done}/{total}");
        Assert.DoesNotContain("\"Suchen\":", LanguageFiles.MissingTemplate("fr", "Français"));
        // an unknown language lacks everything
        Assert.Equal(0, LanguageFiles.Progress("zz").Done);
    }

    [Fact]
    public void MergeKeepsEarlierTranslationsAndAddsNewOnes()
    {
        var old = "{\"@name\":\"Italiano\",\"A\":\"a\"}";
        var added = new LanguageFiles.Checked("Italiano", new() { ["@name"] = "Italiano", ["B"] = "b" }, 1, 0, 0);
        var merged = LanguageFiles.Merge(old, added);
        Assert.Equal("a", merged.Texts["A"]);
        Assert.Equal("b", merged.Texts["B"]);
    }
}
