using BibleStudy.Core.Diagram;
using BibleStudy.Core.Providers;
using Xunit;

namespace BibleStudy.Tests;

public class PericopeStatsTests
{
    static GrammarWord W(int verse, string lemma, string cls, string strong = "", string? tense = null, string? mood = null, int clause = 1) =>
        new("JHN", 3, verse, lemma, lemma, strong, lemma, cls, tense, null, mood, null, null, null, null, null, clause);

    static readonly GrammarWord[] Words =
    [
        W(1, "ἄνθρωπος", "noun"), W(1, "ἦν", "verb", tense: "imperfect", mood: "indicative"), W(1, "καί", "conj", "G2532"),
        W(2, "ἄνθρωπος", "noun", clause: 2), W(2, "λέγω", "verb", tense: "present", mood: "indicative", clause: 2), W(2, "γάρ", "conj", "G1063", clause: 2), W(2, "αὐτός", "pron", clause: 2),
        W(3, "ἄνθρωπος", "noun", clause: 3), W(3, "ἔρχομαι", "verb", tense: "aorist", mood: "imperative", clause: 3), W(3, "λέγω", "verb", tense: "present", mood: "indicative", clause: 3),
        W(9, "ἄνθρωπος", "noun", clause: 4),
    ];

    [Fact]
    public void Counts_words_verses_clauses_and_classes_inside_the_range_only()
    {
        var r = PericopeStats.Compute(Words, 1, 3, null, false);
        Assert.Equal(10, r.Words);
        Assert.Equal(3, r.Verses);
        Assert.Equal(3, r.Clauses);
        Assert.Equal(4, r.Verbs);
        Assert.Equal(3, r.Classes.First(c => c.Class == "noun").Count);
        Assert.Equal(1, r.Imperatives);
    }

    [Fact]
    public void Key_words_are_content_words_that_come_back_most_frequent_first()
    {
        var r = PericopeStats.Compute(Words, 1, 3, null, false);
        Assert.Equal("ἄνθρωπος", r.Keywords[0].Lemma);
        Assert.Equal([1, 2, 3], r.Keywords[0].Verses);
        Assert.Equal("λέγω", r.Keywords[1].Lemma);
        Assert.DoesNotContain(r.Keywords, k => k.Lemma is "αὐτός" or "καί" or "ἦν");
    }

    [Fact]
    public void The_verb_grid_and_the_verse_rhythm_are_filled()
    {
        var r = PericopeStats.Compute(Words, 1, 3, null, false);
        Assert.Contains(r.VerbGrid, c => c is { Row: "present", Col: "indicative", Count: 2 });
        Assert.Equal(3, r.VerseRows.Count);
        Assert.Equal((1, 1), (r.VerseRows[0].Nouns, r.VerseRows[0].Connectors));
    }

    [Fact]
    public void Connecting_words_are_sorted_into_relations()
    {
        var r = PericopeStats.Compute(Words, 1, 3, null, false);
        Assert.Contains(r.Connectors, c => c.Relation == "Addition" && c.Count == 1);
        Assert.Contains(r.Connectors, c => c.Relation == "Begründung" && c.Words.Contains("γάρ ×1"));
    }

    [Fact]
    public void Hebrew_class_names_are_folded_to_the_common_ones()
    {
        Assert.Equal("det", PericopeStats.ClassOf("art"));
        Assert.Equal("conj", PericopeStats.ClassOf("cj"));
        Assert.Equal("noun", PericopeStats.ClassOf("noun"));
    }
}
