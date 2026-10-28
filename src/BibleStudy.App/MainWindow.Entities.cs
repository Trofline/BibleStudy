using System.Text.RegularExpressions;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// Searches by what a text is about instead of how it is spelled:
///   Person:Abraham, Ort:Jerusalem   – every verse in which that person or place is mentioned (Theographic, also under other names)
///   Bezug:"Jer 31"                  – Old Testament passage → New Testament verses that refer to it; New Testament passage → the Old Testament verses it refers to
///   Scope "Worte Jesu"              – only what is printed in red (what Jesus says)
/// They combine with ordinary words: Person:Abraham Verheißung.
/// </summary>
public partial class MainWindow
{
    [GeneratedRegex(@"(?<k>Person|Personen|Place|Ort|Orte)\s*[:=]\s*(?:[""„“](?<q>[^""„“”]+)[""“”]|(?<w>[^\s""„“]+))", RegexOptions.IgnoreCase)]
    private static partial Regex EntityRx();

    [GeneratedRegex(@"(?<k>Bezug|Bezüge|Ref|Zitat)\s*[:=]\s*(?:[""„“](?<q>[^""„“”]+)[""“”]|(?<w>[^\s""„“]+))", RegexOptions.IgnoreCase)]
    private static partial Regex BezugRx();

    /// <summary>Verse filter and notes ("→ Ps 40,7") that belong to the entity part of a query.</summary>
    sealed record EntityResult(HashSet<VerseRef> Verses, Dictionary<VerseRef, string> Notes, string Remaining, string Title)
    {
        /// <summary>Best vote count per verse (references only): strongest first.</summary>
        public Dictionary<VerseRef, int> Best { get; init; } = [];
        /// <summary>True when the resolving already wrote the reason into the status line.</summary>
        public bool HasMessage { get; init; }
    }

    readonly Dictionary<VerseRef, string> _hitNotes = [];

    /// <summary>Splits the query into the entity part (resolved here) and the remaining ordinary search; null when there is no entity part.</summary>
    async Task<EntityResult?> ResolveEntitiesAsync(string text)
    {
        var entities = EntityRx().Matches(text).ToList();
        var bezug = BezugRx().Matches(text).ToList();
        if (entities.Count == 0 && bezug.Count == 0) return null;

        HashSet<VerseRef>? verses = null;
        var notes = new Dictionary<VerseRef, string>();
        var titles = new List<string>();
        var best = new Dictionary<VerseRef, int>();
        void Narrow(HashSet<VerseRef> set) { if (verses is null) verses = set; else verses.IntersectWith(set); }

        foreach (var m in entities)
        {
            var name = (m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["w"].Value).Trim();
            var places = m.Groups["k"].Value.StartsWith("Pl", StringComparison.OrdinalIgnoreCase) || m.Groups["k"].Value.StartsWith("Ort", StringComparison.OrdinalIgnoreCase);
            SearchInfo.Text = L("Suche {0} „{1}“ …", places ? L("Ort") : L("Person"), name);
            var entry = await FindEntityAsync(places ? "places" : "people", name);
            if (entry is null) { SearchInfo.Text = places ? L("Kein Ort „{0}“ gefunden.", name) : L("Keine Person „{0}“ gefunden.", name); return new(new(), notes, "", "") { HasMessage = true }; }
            Narrow(entry.References.ToHashSet());
            titles.Add(entry.Name);
        }
        foreach (var m in bezug)
        {
            var passage = (m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["w"].Value).Trim();
            if (PassageParser.Parse(passage, out _) is not { Count: > 0 } parsed) { SearchInfo.Text = L("„{0}“ ist keine Bibelstelle.", passage); return new(new(), notes, "", "") { HasMessage = true }; }
            var pairs = await ResolveBezugAsync(parsed[0]);
            if (pairs is null) return new(new(), notes, "", "") { HasMessage = true };
            var set = new HashSet<VerseRef>();
            foreach (var group in pairs.GroupBy(p => p.Shown))
            {
                set.Add(group.Key);
                notes[group.Key] = string.Join("  ", group.OrderByDescending(p => p.Score).Select(p => $"↔ {Label(p.Other)} ({p.Score})").Distinct().Take(4));
                best[group.Key] = group.Max(p => p.Score);
            }
            Narrow(set);
            titles.Add(PassageLabel(parsed[0]));
        }
        var rest = BezugRx().Replace(EntityRx().Replace(text, " "), " ");
        rest = Regex.Replace(rest, @"\s+(AND|UND)\s+", " ", RegexOptions.IgnoreCase).Trim();
        return new(verses ?? [], notes, rest, string.Join(" · ", titles)) { Best = best };
    }

    async Task<LexiconEntry?> FindEntityAsync(string kind, string name)
    {
        var all = _lexIndex ??= await _hello.GetLexiconIndexAsync();
        var q = SearchQuery.Fold(name, true);
        var of = all.Where(i => i.Kind == kind).ToList();
        var item = of.Where(i => SearchQuery.Fold(i.Name, true) == q).OrderByDescending(i => i.References).FirstOrDefault()
                   ?? of.Where(i => SearchQuery.Fold(i.Name, true).StartsWith(q, StringComparison.Ordinal)).OrderByDescending(i => i.References).FirstOrDefault();
        return item is null ? null : await _hello.GetLexiconEntryAsync(item.Kind, item.Id);
    }

    // ---- references between the testaments --------------------------------------------------------------------------------

    List<(VerseRef Source, CrossRef Cross)>? _ntCrossRefs;

    /// <summary>All cross references that start in the New Testament, loaded once per session (about 260 small requests, then cached on disk).</summary>
    async Task<List<(VerseRef Source, CrossRef Cross)>> NtCrossRefsAsync()
    {
        if (_ntCrossRefs is not null) return _ntCrossRefs;
        var chapters = _books.Where(b => BookCategories.IsNewTestament(BookCategories.Of(b.Id))).SelectMany(b => Enumerable.Range(1, b.NumberOfChapters).Select(c => (b.Id, c))).ToList();
        var result = new List<(VerseRef, CrossRef)>();
        var gate = new SemaphoreSlim(6); var done = 0; var finished = false;
        await Task.WhenAll(chapters.Select(async ch =>
        {
            await gate.WaitAsync();
            try
            {
                var refs = await _hello.GetChapterCrossRefsAsync(ch.Id, ch.c);
                lock (result) foreach (var (verse, list) in refs) foreach (var x in list) result.Add((new VerseRef(ch.Id, ch.c, verse), x));
            }
            catch { /* a chapter that cannot be loaded is skipped */ }
            finally { gate.Release(); Interlocked.Increment(ref done); }
            if (done % 10 == 0) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!finished) SearchInfo.Text = L("Einmalig: Verweise des Neuen Testaments werden geladen … {0}/{1}", done, chapters.Count); });
        }));
        finished = true;
        return _ntCrossRefs = result;
    }

    const int MinCrossScore = 20;   // the votes behind a reference (OpenBible); a real quotation has far more than a loose parallel

    /// <summary>Pairs of (verse to show, the verse at the other end). For an Old Testament passage the verses to show are the New Testament verses that point to it; for a New Testament passage they are its own verses that point to the Old Testament.</summary>
    async Task<List<(VerseRef Shown, VerseRef Other, int Score)>?> ResolveBezugAsync(PassageRef p)
    {
        var chapters = Enumerable.Range(p.Chapter, (p.ToChapter ?? p.Chapter) - p.Chapter + 1).ToHashSet();
        bool InRange(VerseRef v, int end)
        {
            if (v.Book != p.Book || !chapters.Contains(v.Chapter)) return false;
            if (p.FromVerse is not int from || p.ToChapter is not null) return true;
            var to = p.ToVerse ?? from;
            return v.Verse <= to && Math.Max(v.Verse, end) >= from;
        }
        var pairs = new List<(VerseRef, VerseRef, int)>();
        try
        {
            if (BookCategories.IsNewTestament(BookCategories.Of(p.Book)))
            {
                foreach (var c in chapters)
                    foreach (var (verse, list) in await _hello.GetChapterCrossRefsAsync(p.Book, c))
                    {
                        var source = new VerseRef(p.Book, c, verse);
                        if (!InRange(source, verse)) continue;
                        foreach (var x in list.Where(x => x.Score >= MinCrossScore && BookCategories.IsOldTestament(BookCategories.Of(x.Target.Book)))) pairs.Add((source, x.Target, x.Score));
                    }
            }
            else
            {
                SearchInfo.Text = L("Einmalig: Verweise des Neuen Testaments werden geladen …");
                foreach (var (source, x) in await NtCrossRefsAsync())
                    if (x.Score >= MinCrossScore && InRange(x.Target, x.EndVerse)) pairs.Add((source, x.Target, x.Score));
            }
        }
        catch (Exception ex) { SearchInfo.Text = L("Fehler: ") + ex.Message; return null; }
        return pairs;
    }

    // ---- words of Jesus --------------------------------------------------------------------------------------------------

    readonly Dictionary<string, List<CorpusVerse>?> _jesusCorpora = [];

    /// <summary>The red-letter text of the New Testament as its own corpus, so that a search can be limited to what Jesus says. Null when the translation does not mark it.</summary>
    async Task<List<CorpusVerse>?> JesusCorpusAsync(TranslationInfo t)
    {
        if (_jesusCorpora.TryGetValue(t.Id, out var cached)) return cached;
        var chapters = _books.Where(b => BookCategories.IsNewTestament(BookCategories.Of(b.Id))).SelectMany(b => Enumerable.Range(1, b.NumberOfChapters).Select(c => (b.Id, c))).ToList();
        var found = new List<CorpusVerse>();
        var gate = new SemaphoreSlim(6); var done = 0; var finished = false;
        await Task.WhenAll(chapters.Select(async ch =>
        {
            await gate.WaitAsync();
            try
            {
                var chapter = await _provider.GetChapterAsync(t.Id, ch.Id, ch.c);
                var local = new List<CorpusVerse>();
                foreach (var v in chapter.Verses)
                {
                    var words = string.Concat(v.Segments.Where(s => s.WordsOfJesus && s.NoteId is null).Select(s => s.Text)).Trim();
                    if (words.Length > 0) local.Add(new CorpusVerse(new VerseRef(ch.Id, ch.c, v.Number), words));
                }
                lock (found) found.AddRange(local);
            }
            catch { /* skipped */ }
            finally { gate.Release(); Interlocked.Increment(ref done); }
            if (done % 20 == 0) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!finished) SearchInfo.Text = L("Einmalig: Die Worte Jesu werden gesammelt … {0}/{1}", done, chapters.Count); });
        }));
        finished = true;
        var order = _books.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
        found = found.OrderBy(v => order.GetValueOrDefault(v.Ref.Book, 999)).ThenBy(v => v.Ref.Chapter).ThenBy(v => v.Ref.Verse).ToList();
        return _jesusCorpora[t.Id] = found.Count > 0 ? found : null;
    }
}
