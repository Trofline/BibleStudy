using BibleStudy.Core;
using BibleStudy.L10n;

namespace BibleStudy.Tests;

/// <summary>Every text the program asks for must exist in every shipped language, with the same {0} placeholders.</summary>
public class LocalizationTests
{
    static string SrcRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "BibleStudy.slnx"))) d = d.Parent;
        return Path.Combine(d!.FullName, "src");
    }

    static readonly Lazy<HashSet<string>> Keys = new(() => Extractor.AllKeys(SrcRoot()));

    public static IEnumerable<object[]> Languages => [["en"], ["es"], ["fr"], ["ru"]];

    [Theory, MemberData(nameof(Languages))]
    public void Every_text_is_translated(string code)
    {
        var map = Loc.Texts(code);
        var missing = Keys.Value.Where(k => !map.TryGetValue(k, out var v) || v.Length == 0).Order().ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} texts missing in {code}, e.g.:\n" + string.Join("\n", missing.Take(15)));
    }

    [Theory, MemberData(nameof(Languages))]
    public void Placeholders_match_the_source(string code)
    {
        var bad = Loc.Texts(code).Where(p => Keys.Value.Contains(p.Key) && Extractor.Placeholders(p.Key) != Extractor.Placeholders(p.Value)).Select(p => p.Key).ToList();
        Assert.True(bad.Count == 0, "different placeholders in " + code + ":\n" + string.Join("\n", bad.Take(15)));
    }

    [Theory, MemberData(nameof(Languages))]
    public void No_unused_texts(string code)
    {
        var unused = Loc.Texts(code).Keys.Where(k => !Keys.Value.Contains(k)).Order().ToList();
        Assert.True(unused.Count == 0, "texts that the program no longer uses in " + code + ":\n" + string.Join("\n", unused.Take(15)));
    }

    [Fact]
    public void Unknown_language_falls_back_to_the_default()
    {
        Loc.Use("xx");
        Assert.Equal(Loc.DefaultCode, Loc.Code);
        Loc.Use("de");
        Assert.Equal("Suchen", Loc.L("Suchen"));
        Loc.Use("en");
        Assert.Equal("Search", Loc.L("Suchen"));
        Assert.Equal("1 verses", Loc.L("{0} Verse", 1));
        Loc.Use("de");
    }
}

