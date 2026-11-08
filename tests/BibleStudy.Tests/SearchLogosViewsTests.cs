using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Search;

namespace BibleStudy.Tests;

/// <summary>Sentences as a distance, the operators of the clause search and what the result views show.</summary>
public class SearchLogosViewsTests
{
    static List<CorpusVerse> Corpus() =>
    [
        new(new VerseRef("ROM", 1, 1), "Die Gnade ist groß, denn"),
        new(new VerseRef("ROM", 1, 2), "die Wahrheit macht frei. Danach kommt"),
        new(new VerseRef("ROM", 1, 3), "der Friede. Gnade allein!"),
        new(new VerseRef("ROM", 1, 4), "Wahrheit bleibt."),
        new(new VerseRef("ROM", 2, 1), "Gnade am Ende des Kapitels"),
        new(new VerseRef("ROM", 3, 1), "Wahrheit am Anfang"),
    ];

    static int[] Hits(string q) => SearchEngine.Run(Corpus(), q, null, true).Results.Select(r => r.Ref.Chapter * 10 + r.Ref.Verse).ToArray();

    [Fact] public void A_sentence_may_run_over_verses() => Assert.Equal([11, 12], Hits("Gnade WITHIN 1 SENTENCE Wahrheit"));
    [Fact] public void Two_sentences_reach_the_next_one() => Assert.Contains(14, Hits("Gnade WITHIN 2 SENTENCES Wahrheit"));
    [Fact] public void A_new_chapter_is_a_new_sentence() => Assert.DoesNotContain(21, Hits("Gnade WITHIN 1 SENTENCE Wahrheit"));
    [Fact] public void Not_within_a_sentence() => Assert.Equal([13, 21], Hits("Gnade NOT WITHIN 1 SENTENCE Wahrheit"));
    [Fact] public void Paragraphs_are_refused_with_a_reason() => Assert.NotNull(SearchEngine.Run(Corpus(), "Gnade WITHIN 1 PARAGRAPH Wahrheit", null, true).Error);

    static GrammarWord W(string role, string text, int clause) => new("JHN", 1, clause, text, "", "", "", "noun", null, null, null, null, null, null, null, role, clause);
    static readonly List<GrammarWord> Words = [W("s", "Jesus", 1), W("o", "Petrus", 1), W("s", "Jesus", 2), W("o", "Johannes", 2), W("s", "Maria", 3), W("o", "Petrus", 3)];
    static int[] Clauses(params RoleCondition[] c) => ClauseSearch.Run(Words, c).Select(h => h.Verse).ToArray();

    [Fact] public void Clause_or() => Assert.Equal([1, 2, 3], Clauses(new RoleCondition("s", "Jesus / Maria")));
    [Fact] public void Clause_not() => Assert.Equal([2], Clauses(new("s", "Jesus"), new("o", "-Petrus")));
    [Fact] public void Clause_not_written_out() => Assert.Equal([2], Clauses(new("s", "Jesus"), new("o", "NOT Petrus")));
    [Fact] public void Clause_wildcards() => Assert.Equal([2], Clauses(new RoleCondition("o", "Joh*")));

    [Fact]
    public void Aligned_lines_are_one_per_found_word()
    {
        var hit = new SearchResult(new VerseRef("JHN", 1, 1), "aaa bbb Gnade ccc ddd Gnade eee", [(8, 5), (22, 5)]);
        var lines = HitViews.Align([hit], 100, 10);
        Assert.Equal(2, lines.Count);
        Assert.Equal("aaa bbb ", lines[0].Left);
        Assert.Equal("Gnade", lines[0].Word);
        Assert.Equal(" ccc ddd Gnade eee", lines[0].Right);
    }

    [Fact]
    public void Aligned_context_is_cut_at_words()
    {
        var hit = new SearchResult(new VerseRef("JHN", 1, 1), "eins zwei drei vier Gnade fünf sechs sieben acht", [(20, 5)]);
        var line = HitViews.Align([hit], 8, 10)[0];
        Assert.Equal("… vier ", line.Left);
        Assert.Equal(" fünf …", line.Right);
    }

    [Fact] public void Aligned_stops_at_the_limit() => Assert.Single(HitViews.Align([new SearchResult(new VerseRef("JHN", 1, 1), "a a", [(0, 1), (2, 1)])], 5, 1));

    [Fact]
    public void Word_forms_are_counted()
    {
        var a = new SearchResult(new VerseRef("JHN", 1, 1), "Gnade gnaden Gnade", [(0, 5), (6, 6), (13, 5)]);
        Assert.Equal([("gnade", 2), ("gnaden", 1)], HitViews.WordForms([a]));
    }
}
