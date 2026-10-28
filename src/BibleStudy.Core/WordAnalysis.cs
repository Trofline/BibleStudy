using System.Text.RegularExpressions;
using BibleStudy.Core.Models;

namespace BibleStudy.Core;

/// <summary>
/// Word statistics from the Strong's tags of one translation (the KJV): which words belong together, what a chapter is about,
/// which words are rare. Everything is computed from the stored occurrences, nothing is downloaded here.
/// </summary>
public sealed partial class WordAnalyzer
{
    readonly Dictionary<string, List<VerseRef>> _verses = [];      // Strong id -> verses (one entry per occurrence)
    readonly Dictionary<VerseRef, List<(string Id, string Word)>> _words = [];
    readonly int _verseTotal;

    public WordAnalyzer(IEnumerable<StrongsOccurrence> occurrences)
    {
        foreach (var o in occurrences)
        {
            var id = Normalize(o.Strong);
            if (id is null) continue;
            if (!_verses.TryGetValue(id, out var list)) _verses[id] = list = [];
            list.Add(o.Ref);
            if (!_words.TryGetValue(o.Ref, out var w)) _words[o.Ref] = w = [];
            w.Add((id, o.Word.Trim().Trim('.', ',', ';', ':', '!', '?', '(', ')')));
        }
        _verseTotal = Math.Max(1, _words.Count);
    }

    /// <summary>"g25", "G025", "G0025a" → "G25"; null when it is no Strong number.</summary>
    public static string? Normalize(string s)
    {
        var m = IdPattern().Match(s.Trim());
        return m.Success ? char.ToUpperInvariant(m.Groups[1].Value[0]) + int.Parse(m.Groups[2].Value).ToString() : null;
    }

    [GeneratedRegex(@"^([GgHh])0*(\d{1,4})")] private static partial Regex IdPattern();
    [GeneratedRegex(@"([GH])0*(\d{1,4})")] private static partial Regex RelatedPattern();

    public int Count(string id) => _verses.TryGetValue(id, out var l) ? l.Count : 0;

    public IReadOnlyList<VerseRef> Occurrences(string id) => _verses.TryGetValue(id, out var l) ? l : [];

    int VerseCount(string id) => _verses.TryGetValue(id, out var l) ? l.Distinct().Count() : 0;

    double Idf(string id) => Math.Log((double)_verseTotal / Math.Max(1, VerseCount(id)));

    /// <summary>The numbers a dictionary text points to ("from G26 (ἀγάπη)"), without the entry's own.</summary>
    public static IReadOnlyList<string> Related(string derivation, string own) =>
        RelatedPattern().Matches(derivation).Select(m => m.Groups[1].Value + int.Parse(m.Groups[2].Value)).Where(i => i != own).Distinct().ToList();

    public IReadOnlyList<(string Id, string Word)> WordsOf(VerseRef r) =>
        _words.TryGetValue(r, out var w) ? w.DistinctBy(x => x.Id).ToList() : [];

    /// <summary>Words that appear in the same verses as the given one, rare partners first (frequent filler words score low).</summary>
    public IReadOnlyList<(string Id, int Shared)> CoOccurring(string id, int top = 12) => CoOccurring([id], top);

    /// <summary>The verses in which all the given words stand together (a phrase like "day of the Lord" is several words).</summary>
    public HashSet<VerseRef> Together(IReadOnlyList<string> ids)
    {
        HashSet<VerseRef>? common = null;
        foreach (var id in ids)
        {
            var set = Occurrences(id).ToHashSet();
            if (common is null) common = set; else common.IntersectWith(set);
        }
        return common ?? [];
    }

    /// <summary>Words that appear in the same verses as all of the given ones (rare partners first).</summary>
    public IReadOnlyList<(string Id, int Shared)> CoOccurring(IReadOnlyList<string> ids, int top = 12)
    {
        var target = Together(ids);
        if (target.Count == 0) return [];
        var scored = new List<(string Id, int Shared, double Score)>();
        foreach (var (other, list) in _verses)
        {
            if (ids.Contains(other)) continue;
            var shared = list.Where(target.Contains).Distinct().Count();
            if (shared < (ids.Count > 1 ? 1 : 2)) continue;
            scored.Add((other, shared, shared * Idf(other)));
        }
        return scored.OrderByDescending(s => s.Score).Take(top).Select(s => (s.Id, s.Shared)).ToList();
    }

    /// <summary>The words that characterize a chapter: frequent in it, but not frequent everywhere (tf × idf).</summary>
    public IReadOnlyList<(string Id, int InChapter, int Total)> Keywords(string book, int chapter, int top = 14)
    {
        var counts = new Dictionary<string, int>();
        foreach (var (r, words) in _words)
            if (r.Book == book && r.Chapter == chapter)
                foreach (var (id, _) in words) counts[id] = counts.GetValueOrDefault(id) + 1;
        return counts.Where(c => c.Value >= 2)
            .OrderByDescending(c => c.Value * Idf(c.Key)).Take(top)
            .Select(c => (c.Key, c.Value, Count(c.Key))).ToList();
    }

    /// <summary>Words used only once in the whole Bible (hapax legomena) that occur in this chapter.</summary>
    public IReadOnlyList<(string Id, string Word, int Verse)> Hapax(string book, int chapter) =>
        _words.Where(p => p.Key.Book == book && p.Key.Chapter == chapter)
            .SelectMany(p => p.Value.Select(w => (w.Id, w.Word, p.Key.Verse)))
            .Where(w => Count(w.Id) == 1).DistinctBy(w => w.Id).OrderBy(w => w.Verse).ToList();
}
