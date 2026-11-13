using System.Text.RegularExpressions;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// The searches by meaning in the search field, each combinable with ordinary words like Person: and Gram::
///   Sinn:vergeben        – every word in the same meaning field of the Greek/Hebrew text (Louw-Nida, SDBH)
///   Meint:Jesus          – where Jesus is named or meant: "he", "him", and verbs whose unwritten subject he is
///   LXX:G26 / LXX:H2617  – the Hebrew words the Septuagint renders with G26; where a Hebrew word stands and how the Septuagint renders it
///   Kongruenz:"adjective ~ noun"  – two words close together that agree in case, number and gender
///   Ereignis:Baptism     – the passages of an event of the timeline (Theographic)
/// The verses are a filter for the rest of the query; the found Greek/Hebrew words stand below each hit.
/// </summary>
public partial class MainWindow
{
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<k>Sinn|Bedeutung|Sense|Domain|Meint|Referent|Refers|LXX|Septuaginta|Kongruenz|Agree|Agreement|Ereignis|Event|Wurzel|Root)\s*[:=]\s*(?:[""„“](?<q>[^""„“”]+)[""“”]|(?<w>[^\s""„“]+))", RegexOptions.IgnoreCase)]
    private static partial Regex OriginalFilterRx();

    /// <summary>A line added below the count of the hits (which words make up a meaning field, how the Septuagint renders a word).</summary>
    string _searchExtra = "";

    /// <summary>Resolves the meaning parts of a query to a set of verses; null when the query has none.</summary>
    async Task<GramResult?> ResolveOriginalFiltersAsync(string text, TranslationInfo t)
    {
        var parts = OriginalFilterRx().Matches(text).ToList();
        if (parts.Count == 0) return null;
        HashSet<VerseRef>? verses = null;
        var notes = new Dictionary<VerseRef, List<string>>();
        var extras = new List<string>();
        GramResult Fail(string message) { SearchInfo.Text = message; return new([], [], "", true); }

        foreach (var m in parts)
        {
            var kind = m.Groups["k"].Value.ToLowerInvariant();
            var term = (m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["w"].Value).Trim();
            if (term.Length == 0) return Fail(L("Nach „{0}:“ fehlt, wonach gesucht wird.", m.Groups["k"].Value));
            List<OriginalSearch.Hit> hits;
            switch (kind)
            {
                case "sinn" or "bedeutung" or "sense" or "domain":
                {
                    hits = [];
                    var corpora = await CorporaForAsync(term);
                    var strongs = await TranslationStrongsAsync(t, term, corpora);
                    var members = new List<(string Lemma, string Gloss, int Count)>();
                    var senses = new List<(string Gloss, int Count, bool Used)>();
                    foreach (var words in corpora)
                    {
                        var r = await Task.Run(() => OriginalSearch.Meaning(words, OriginalSearch.Matcher(term, strongs)));
                        hits.AddRange(r.Hits); members.AddRange(r.Members); senses.AddRange(r.Senses);
                    }
                    if (hits.Count == 0) return Fail(L("Für „{0}“ ist kein Bedeutungsfeld bekannt. Nimm eine Strong-Nummer (G863), die Grundform oder ein englisches Wort (forgive).", term));
                    extras.Add(L("Bedeutungsfeld von „{0}“: {1}", term, string.Join(", ", members.OrderByDescending(x => x.Count).Take(12).Select(x => $"{x.Lemma} ({x.Gloss}) {x.Count}×")) + (members.Count > 12 ? " …" : "")));
                    // a word with several meanings: say which one was taken and how to choose another
                    senses = senses.GroupBy(s => s.Gloss).Select(g => (g.Key, g.Sum(s => s.Count), g.Any(s => s.Used))).OrderByDescending(s => s.Item2).ToList();
                    if (senses.Count > 1 && !term.Contains('='))
                    {
                        var other = senses.FirstOrDefault(s => !s.Used).Gloss ?? senses[^1].Gloss;
                        extras.Add(L("„{0}“ hat mehrere Bedeutungen: {1}. Gesucht wurde mit der markierten (✓); eine andere wählst du so: Sinn:{0}={2}", term,
                            string.Join(", ", senses.Take(6).Select(s => $"{s.Gloss} {s.Count}×" + (s.Used ? " ✓" : ""))), other));
                    }
                    break;
                }
                case "meint" or "referent" or "refers":
                {
                    hits = [];
                    var corpora = await CorporaForAsync(term);
                    var strongs = await TranslationStrongsAsync(t, term, corpora);
                    foreach (var words in corpora)
                        hits.AddRange(await Task.Run(() => OriginalSearch.RefersTo(words, OriginalSearch.Matcher(term, strongs))));
                    if (hits.Count == 0) return Fail(L("„{0}“ kommt im Grundtext nicht vor. Nimm eine Strong-Nummer (G2424), die Grundform oder ein englisches Wort (Jesus).", term));
                    var named = hits.Count(h => !h.Note.Contains('→'));
                    extras.Add(L("„{0}“: {1}× genannt, {2}× gemeint (er, ihn, sein …, auch als nicht ausgeschriebenes Subjekt)", term, named, hits.Count - named));
                    break;
                }
                case "lxx" or "septuaginta":
                {
                    var hebrew = await HebrewWordsAsync();
                    var strongs = await TranslationStrongsAsync(t, term, [hebrew]);
                    List<(string Key, int Count)> eq = [];
                    hits = await Task.Run(() => OriginalSearch.Septuagint(hebrew, term, strongs, out eq));
                    if (hits.Count == 0) return Fail(L("Dazu gibt es im Alten Testament keine Entsprechung. Nimm eine griechische Strong-Nummer (LXX:G26) oder ein hebräisches Wort (LXX:H2617)."));
                    var greek = SearchQuery.IsStrongId(term) && char.ToUpperInvariant(term[0]) == 'G';
                    extras.Add((greek ? L("Hebräisch hinter {0} in der Septuaginta: ", term.ToUpperInvariant()) : L("Die Septuaginta übersetzt „{0}“ mit: ", term))
                        + string.Join(", ", eq.Take(10).Select(e => $"{e.Key} {e.Count}×")) + (eq.Count > 10 ? " …" : ""));
                    break;
                }
                case "kongruenz" or "agree" or "agreement":
                {
                    var sides = term.Split('~', 2);
                    if (sides.Length < 2) return Fail(L("Kongruenz braucht zwei Wörter mit ~ dazwischen, z. B. Kongruenz:\"adjective ~ noun G26\"."));
                    var a = GrammarSpec.Parse(sides[0]); var b = GrammarSpec.Parse(sides[1]);
                    if (a.Unknown.Concat(b.Unknown).ToList() is { Count: > 0 } unknown) return Fail(L("Unbekannt in der Grammatik-Angabe: {0}", string.Join(", ", unknown)));
                    var hebrew = a.Hebrew || b.Hebrew;
                    var words = hebrew ? await HebrewWordsAsync() : await GreekWordsAsync();
                    hits = await Task.Run(() => OriginalSearch.Agreeing(words, a.Query, b.Query, hebrew));
                    break;
                }
                case "wurzel" or "root":
                {
                    var hebrew = await HebrewWordsAsync();
                    var strongs = await TranslationStrongsAsync(t, term, [hebrew]);
                    // the word asked for: a Strong number as it is, otherwise the number the matching Hebrew words have most often
                    var strong = SearchQuery.IsStrongId(term) ? NormalStrong(term) : await Task.Run(() =>
                    {
                        var test = OriginalSearch.Matcher(term, strongs);
                        return hebrew.Where(test).GroupBy(w => NormalStrong(w.Strong)).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
                    });
                    if (strong is null || !strong.StartsWith('H')) return Fail(L("„{0}“ ist kein hebräisches Wort. Nimm eine Strong-Nummer (H6), die Grundform oder ein englisches Wort (perish)."));
                    _bdb ??= new BdbLexicon(_http, Path.Combine(_root, "cache"));
                    RootFamily? family;
                    try { family = await _bdb.RootFamilyAsync(strong); }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or System.Xml.XmlException or TaskCanceledException) { return Fail(L("Das BDB-Lexikon lässt sich nicht laden: {0}", ex.Message)); }
                    if (family is null) return Fail(L("Zu {0} kennt das BDB-Lexikon keine Wortfamilie (das Wort steht allein).", strong));
                    var members = family.Strongs.ToHashSet();
                    hits = await Task.Run(() => hebrew.Where(w => members.Contains(NormalStrong(w.Strong)))
                        .Select(w => new OriginalSearch.Hit(w, $"{w.Text} ({w.Lemma}, {NormalStrong(w.Strong)})")).ToList());
                    var counts = hits.GroupBy(h => NormalStrong(h.Word.Strong)).ToDictionary(g => g.Key, g => g.Count());
                    extras.Add(L("Wurzel {0}: ", family.Root) + string.Join(", ", family.Members.Where(m => m.Strongs.Count > 0)
                        .Select(m => $"{m.Word} ({m.Meaning.Trim()}) {m.Strongs.Sum(s => counts.GetValueOrDefault(s))}×")));
                    break;
                }
                default:   // event
                {
                    var result = await EventVersesAsync(term);
                    if (result is null) return Fail(L("Kein Ereignis „{0}“ gefunden. Die Ereignisse haben englische Namen, z. B. Ereignis:Baptism oder Ereignis:Exodus.", term));
                    var (set, names) = result.Value;
                    verses = verses is null ? set : verses.Intersect(set).ToHashSet();
                    extras.Add(L("Ereignis: {0}", string.Join(", ", names)));
                    continue;
                }
            }
            var found = hits.Select(h => new VerseRef(h.Word.Book, h.Word.Chapter, h.Word.Verse)).ToHashSet();
            if (verses is null) verses = found; else verses.IntersectWith(found);
            foreach (var h in hits)
            {
                var r = new VerseRef(h.Word.Book, h.Word.Chapter, h.Word.Verse);
                if (!notes.TryGetValue(r, out var list)) notes[r] = list = [];
                if (list.Count < 4 && !list.Contains(h.Note)) list.Add(h.Note);
            }
        }
        _searchExtra = string.Join("\n", extras);
        var shown = notes.Where(kv => verses!.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => L("Grundtext: ") + string.Join(", ", kv.Value));
        return new(verses ?? [], shown, OriginalFilterRx().Replace(text, " ").Trim(), false);
    }

    Progress<(int Done, int Total)> GrammarProgress() => new(p => SearchInfo.Text = L("Lade Grammatikdaten … {0}/{1}", p.Done, p.Total));
    async Task<List<GrammarWord>> GreekWordsAsync() => _greekWords ??= await _syntax.LoadGreekWordsAsync(GrammarProgress());
    async Task<List<GrammarWord>> HebrewWordsAsync() => _hebrewWords ??= await _syntax.LoadHebrewWordsAsync(GrammarProgress());

    /// <summary>Greek for a G number or Greek letters, Hebrew for an H number or Hebrew letters, otherwise both testaments.</summary>
    async Task<List<List<GrammarWord>>> CorporaForAsync(string term)
    {
        term = term.Split('=')[0];   // G863=forgive: the word decides the language
        var strong = SearchQuery.IsStrongId(term.Trim()) ? char.ToUpperInvariant(term.Trim()[0]) : ' ';
        if (strong == 'G' || OriginalSearch.IsGreek(term)) return [await GreekWordsAsync()];
        if (strong == 'H' || OriginalSearch.IsHebrew(term)) return [await HebrewWordsAsync()];
        return [await HebrewWordsAsync(), await GreekWordsAsync()];
    }

    /// <summary>
    /// The Strong numbers the chosen translation puts behind a word (vergeben → G863, G5483 …), so that a word of any language works;
    /// none for a Strong number, Greek or Hebrew letters, or a translation without Strong numbers.
    /// </summary>
    async Task<IReadOnlyCollection<string>?> TranslationStrongsAsync(TranslationInfo t, string term, IEnumerable<List<GrammarWord>> corpora)
    {
        term = term.Split('=')[0];
        if (SearchQuery.IsStrongId(term.Trim()) || OriginalSearch.IsHebrew(term) || OriginalSearch.IsGreek(term) || !TranslationHasStrongs(t)) return null;
        // an English gloss (forgive) is found without the Strong's index of the translation, which takes a while to build the first time
        var plain = OriginalSearch.Matcher(term);
        if (await Task.Run(() => corpora.Any(c => c.Any(plain)))) return null;
        var index = _strongIndexes.GetValueOrDefault(t.Id) ?? await StrongsForAsync(t);
        if (index is null) return null;
        var folded = SearchQuery.Fold(term.Trim().Trim('"', '„', '“', '”'), true);
        return index.IdsForWords(w => w == folded);
    }

    List<TimelineEvent>? _events;

    /// <summary>The verses of the events whose name contains the term (at most 8 events, the most often told first).</summary>
    async Task<(HashSet<VerseRef> Verses, List<string> Names)?> EventVersesAsync(string term)
    {
        var all = _events ??= (await _hello.GetTimelineAsync()).ToList();
        var matching = all.Where(e => OriginalSearch.NameMatches(e.Name, term)).OrderByDescending(e => e.References).Take(8).ToList();
        if (matching.Count == 0) return null;
        var verses = new HashSet<VerseRef>();
        foreach (var e in matching)
        {
            var detail = await _hello.GetEventAsync(e.Id);
            foreach (var (r, end) in detail.References)
                for (var v = r.Verse; v <= Math.Max(r.Verse, end); v++) verses.Add(r with { Verse = v });
        }
        return verses.Count == 0 ? null : (verses, matching.Select(e => e.Name).ToList());
    }

    /// <summary>"H2050c" → "H2050", "h06" → "H6": the number without letters after it and without leading zeros.</summary>
    static string NormalStrong(string strong)
    {
        var s = strong.Trim().ToUpperInvariant();
        if (s.Length < 2 || s[0] is not ('H' or 'G')) return s;
        var digits = new string(s.Skip(1).TakeWhile(char.IsDigit).ToArray());
        return digits.Length == 0 ? s : s[0] + int.Parse(digits).ToString();
    }
}
