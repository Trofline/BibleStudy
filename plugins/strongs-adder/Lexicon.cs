namespace StrongsAdder;

/// <summary>
/// What a reference translation teaches about its own words: how often a word carries a certain Strong's number, and how often it
/// carries none at all (the, and, of …). It is learned from the whole reference translation and then helps to decide, word by word,
/// between the numbers a verse has.
/// </summary>
public sealed class Lexicon
{
    readonly Dictionary<string, Dictionary<string, int>> _numbers = [];
    readonly Dictionary<string, int> _total = [];
    readonly Dictionary<string, int> _tagged = [];

    /// <summary>Adds the words of one verse of the reference (words with 3 or more numbers belong to a longer phrase and teach nothing certain).</summary>
    public void Learn(IEnumerable<(string Word, string[]? Strongs)> verse, string language)
    {
        foreach (var (word, strongs) in verse)
        {
            var w = Aligner.Normalize(word, language);
            _total[w] = _total.GetValueOrDefault(w) + 1;
            if (strongs is not { Length: > 0 and <= 2 }) continue;
            _tagged[w] = _tagged.GetValueOrDefault(w) + 1;
            if (!_numbers.TryGetValue(w, out var d)) _numbers[w] = d = [];
            foreach (var s in strongs) d[s] = d.GetValueOrDefault(s) + 1;
        }
    }

    public int Seen(string word) => _total.GetValueOrDefault(word);

    /// <summary>How often this word carries any number at all, 0–1 (1 if the word is rare: no opinion).</summary>
    public double TaggedRate(string word) => _total.GetValueOrDefault(word) is var t && t >= 5 ? (double)_tagged.GetValueOrDefault(word) / t : 1;

    /// <summary>The share of the word's occurrences that carry this number (0 if seen too rarely).</summary>
    public double Score(string word, string strongs)
    {
        if (!_numbers.TryGetValue(word, out var d) || !d.TryGetValue(strongs, out var n) || n < 2) return 0;
        return (double)n / _total[word];
    }
}
