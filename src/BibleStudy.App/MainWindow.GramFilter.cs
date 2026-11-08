using System.Text.RegularExpressions;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>
/// Grammar inside the normal search (like a Logos morph search combined with words): Gram:"verb aorist passive" Gnade finds the verses in which the
/// Greek (or Hebrew) text has such a word and in which the other words of the query occur too. The verses with their Greek/Hebrew words are
/// shown as a note below the hit. The grammar data is downloaded once, as for the grammar search.
/// </summary>
public partial class MainWindow
{
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:Gram|Grammatik|Morph)\s*[:=]\s*(?:[""„“](?<q>[^""„“”]+)[""“”]|(?<w>[^\s""„“]+))", RegexOptions.IgnoreCase)]
    private static partial Regex GramRx();

    sealed record GramResult(HashSet<VerseRef> Verses, Dictionary<VerseRef, string> Notes, string Rest, bool Failed);

    /// <summary>Resolves the Gram:… parts of a query to a set of verses; null when the query has none.</summary>
    async Task<GramResult?> ResolveGrammarAsync(string text)
    {
        var parts = GramRx().Matches(text).ToList();
        if (parts.Count == 0) return null;
        HashSet<VerseRef>? verses = null;
        var notes = new Dictionary<VerseRef, List<string>>();
        foreach (var m in parts)
        {
            var spec = GrammarSpec.Parse(m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["w"].Value);
            if (spec.Unknown.Count > 0) { SearchInfo.Text = L("Unbekannt in der Grammatik-Angabe: {0}", string.Join(", ", spec.Unknown)); return new([], [], "", true); }
            var q = spec.Query;
            if (q.Class is null && q.Tense is null && q.Voice is null && q.Mood is null && q.Person is null && q.Number is null && q.Gender is null && q.Case is null && q.Word is null)
            { SearchInfo.Text = L("Die Grammatik-Angabe ist leer, z. B. Gram:\"verb aorist passive\"."); return new([], [], "", true); }
            var progress = new Progress<(int Done, int Total)>(p => SearchInfo.Text = L("Lade Grammatikdaten … {0}/{1}", p.Done, p.Total));
            var words = spec.Hebrew ? _hebrewWords ??= await _syntax.LoadHebrewWordsAsync(progress) : _greekWords ??= await _syntax.LoadGreekWordsAsync(progress);
            var hits = await Task.Run(() => words.Where(q.Matches).ToList());
            var set = hits.Select(h => new VerseRef(h.Book, h.Chapter, h.Verse)).ToHashSet();
            if (verses is null) verses = set; else verses.IntersectWith(set);
            foreach (var h in hits)
            {
                var r = new VerseRef(h.Book, h.Chapter, h.Verse);
                if (!verses.Contains(r)) continue;
                if (!notes.TryGetValue(r, out var list)) notes[r] = list = [];
                if (list.Count < 4 && !list.Contains(h.Text)) list.Add(h.Text);
            }
        }
        var shown = notes.Where(kv => verses!.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => L("Grundtext: ") + string.Join(", ", kv.Value));
        return new(verses ?? [], shown, GramRx().Replace(text, " ").Trim(), false);
    }
}
