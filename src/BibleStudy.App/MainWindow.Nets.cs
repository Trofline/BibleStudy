using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// The data behind the word, person and topic networks of the graph view:
///   word    – words that often stand in the same verses as a word (Strong's numbers, KJV)
///   person  – people who appear in the same chapters as a person (Theographic)
///   topic   – related words of a topic (German synonyms) and how often they share a verse in the text you read
/// Each network can be widened by double-clicking a node.
/// </summary>
public partial class MainWindow
{
    readonly List<string> _topicWords = [];
    readonly Dictionary<string, HashSet<VerseRef>> _topicVerses = [];

    void InitNets()
    {
        GraphPane.NetProvider = NetAsync;
        GraphPane.NetOpen += async (kind, key) =>
        {
            ShowReader();
            if (kind == "word") { SetPanelOpen(true); SideTabs.SelectedIndex = AnalysisTab; await AnalyzeWordAsync(Terms(key)[0]); }
            else if (kind == "person") { OpenWork(NavGroups.First(g => g.Name == "Bücher"), LexiconTab); await ShowLexEntryAsync(new LexiconItem("people", key, "", 0)); }
            else { SetPanelOpen(true); OpenWork(NavGroups.First(g => g.Name == "Suchen")); SearchBox.Text = key.Contains(' ') ? $"\"{key}\"" : $"L={key}"; await SearchAsync(); }
        };
    }

    async Task<NetData?> NetAsync(string kind, string key, bool expand, Action<string> status) => kind switch
    {
        "word" => await WordNetAsync(key, expand, status),
        "person" => await PersonNetAsync(key, expand, status),
        _ => await TopicNetAsync(key, expand, status),
    };

    // ---- words -----------------------------------------------------------------------------------------------------------

    async Task<NetNode?> WordNodeAsync(string id, double weight, string? extra)
    {
        var e = await EntryAsync(id);
        var gloss = e is null ? "" : Mean(Gloss(e));
        return new NetNode(id, e?.Lemma ?? id, $"{id} {gloss}{(extra is null ? "" : "  " + extra)}".Trim(), Color.Parse(id[0] == 'H' ? "#E3B65F" : "#6AA9FF"), weight);
    }

    /// <summary>Several terms in one field ("Tag + Herr", "Glaube, Werke"): they are combined.</summary>
    static string[] Terms(string key) => key.Split(['+', ',', ';', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    async Task<NetData?> WordNetAsync(string key, bool expand, Action<string> status)
    {
        if (_analyzer is null) await LoadAnalyzerAsync(status);
        if (_analyzer is null) return null;
        var ids = new List<string>();
        foreach (var term in Terms(key))
        {
            var one = WordAnalyzer.Normalize(term);
            if (one is null)
            {
                status(L("Suche das Wort im Wörterbuch …"));
                one = (await _strongs.SearchAsync(term, 1)).FirstOrDefault()?.Id is { } found ? WordAnalyzer.Normalize(found) : null;
            }
            if (one is null || _analyzer.Count(one) == 0) return null;
            if (!ids.Contains(one)) ids.Add(one);
        }
        if (ids.Count == 0) return null;
        var id = string.Join('+', ids);                 // the key of the centre; expanding it starts from all of its words again

        status(L("Wird berechnet …"));
        var partners = _analyzer.CoOccurring(ids, expand ? 10 : 14);
        var data = new NetData();
        if (!expand)
        {
            if (ids.Count == 1)
            {
                if (await WordNodeAsync(id, 34, L("{0}× in der KJV", _analyzer.Count(id))) is { } center) data.Nodes.Add(center);
            }
            else
            {
                // the phrase as one node, its words around it
                var names = new List<string>();
                foreach (var w in ids) names.Add((await WordNodeAsync(w, 20, null))?.Label ?? w);
                data.Nodes.Add(new NetNode(id, string.Join(" + ", names), L("{0} Verse, in denen alle zusammen stehen", _analyzer.Together(ids).Count), Color.Parse("#E3B65F"), 34));
                foreach (var w in ids)
                    if (await WordNodeAsync(w, 20, L("{0}× in der KJV", _analyzer.Count(w))) is { } part) { data.Nodes.Add(part); data.Edges.Add((id, w, 8)); }
            }
        }
        foreach (var (other, shared) in partners)
            if (await WordNodeAsync(other, Math.Min(30, 4 + shared / 2.0), $"{shared}×") is { } node) data.Nodes.Add(node);
        foreach (var (other, shared) in partners) data.Edges.Add((id, other, shared));
        // the partners also belong together if they share verses with each other
        var sets = partners.ToDictionary(p => p.Id, p => _analyzer.Occurrences(p.Id).ToHashSet());        for (var i = 0; i < partners.Count; i++)
            for (var j = i + 1; j < partners.Count; j++)
            {
                var common = sets[partners[i].Id].Count(sets[partners[j].Id].Contains);
                if (common >= 3) data.Edges.Add((partners[i].Id, partners[j].Id, common));
            }
        return data;
    }

    // ---- people ----------------------------------------------------------------------------------------------------------

    async Task<NetData?> PersonNetAsync(string key, bool expand, Action<string> status)
    {
        status(L("Suche die Person …"));
        var entries = new List<LexiconEntry>();
        try
        {
            foreach (var term in Terms(key))
            {
                var one = expand && !key.Contains('+') ? await _hello.GetLexiconEntryAsync("people", term) : await FindEntityAsync("people", term);
                if (one is null) return null;
                entries.Add(one);
            }
        }
        catch { return null; }
        if (entries.Count == 0) return null;
        var entry = entries[0];
        var ownIds = entries.Select(e => e.Id).ToHashSet();
        // several people: only the chapters in which all of them appear
        var chapterSets = entries.Select(e => e.References.Select(r => (r.Book, r.Chapter)).ToHashSet()).ToList();
        var common = chapterSets[0];
        foreach (var s in chapterSets.Skip(1)) common.IntersectWith(s);
        var chapters = common.Select(c => (Chapter: c, N: entry.References.Count(r => (r.Book, r.Chapter) == c))).OrderByDescending(c => c.N).Take(70).Select(c => c.Chapter).ToList();
        if (chapters.Count == 0) return null;
        var shared = new Dictionary<string, (string Name, int Chapters, string Book)>();
        using var gate = new SemaphoreSlim(6);
        var done = 0;
        await Task.WhenAll(chapters.Select(async ch =>
        {
            await gate.WaitAsync();
            try
            {
                var list = await _hello.GetChapterLexiconAsync(ch.Book, ch.Chapter);
                var people = list.Where(l => l.Item.Kind == "people").Select(l => l.Item).DistinctBy(i => i.Id).ToList();
                if (!ownIds.All(o => people.Any(p => p.Id == o))) return;
                lock (shared)
                    foreach (var p in people.Where(p => !ownIds.Contains(p.Id)))
                        shared[p.Id] = shared.TryGetValue(p.Id, out var s) ? (s.Name, s.Chapters + 1, s.Book) : (p.Name, 1, ch.Book);
            }
            catch (HttpRequestException) { /* a chapter without data is skipped */ }
            finally
            {
                gate.Release();
                var n = Interlocked.Increment(ref done);
                if (n % 8 == 0) Avalonia.Threading.Dispatcher.UIThread.Post(() => status(L("Kapitel werden gelesen … {0}/{1}", n, chapters.Count)));
            }
        }));

        var top = shared.OrderByDescending(p => p.Value.Chapters).Take(expand ? 10 : 14).ToList();
        var data = new NetData();
        if (!expand)
            foreach (var e in entries)
                data.Nodes.Add(new NetNode(e.Id, e.Name, L("{0} Stellen", e.References.Count), Color.Parse("#E3B65F"), entries.Count == 1 ? 34 : 26));
        if (entries.Count > 1)   // the chosen people belong together
            for (var i = 1; i < entries.Count; i++) data.Edges.Add((entries[0].Id, entries[i].Id, common.Count));
        foreach (var (id, p) in top)
        {
            data.Nodes.Add(new NetNode(id, p.Name, L("{0} gemeinsame Kapitel", p.Chapters), HomeView.ColorOf(p.Book), Math.Min(30, 4 + p.Chapters * 2.0)));
            foreach (var e in entries) data.Edges.Add((e.Id, id, p.Chapters));
        }
        return data;
    }

    // ---- topics ----------------------------------------------------------------------------------------------------------

    async Task<List<CorpusVerse>?> CorpusForNetsAsync(TranslationInfo t, Action<string> status)
    {
        if (!_store.IsIndexed(t.Id))
        {
            status(L("Einmalig: {0} wird für die Suche geladen und indexiert …", t.ShortName));
            var verses = await _provider.GetAllVersesAsync(t.Id);
            await Task.Run(() => _store.Index(t.Id, verses));
            _corpora.Remove(t.Id);
        }
        if (!_corpora.TryGetValue(t.Id, out var corpus))
            _corpora[t.Id] = corpus = await Task.Run(() => _store.GetCorpus(t.Id).Select(c => new CorpusVerse(c.Ref, c.Text)).ToList());
        return corpus;
    }

    async Task<NetData?> TopicNetAsync(string key, bool expand, Action<string> status)
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return null;
        var corpus = await CorpusForNetsAsync(t, status);
        if (corpus is null) return null;

        status(L("Suche verwandte Wörter …"));
        var fresh = new List<string>();
        string[] starts = expand ? [key] : Terms(key);
        if (!expand) { _topicWords.Clear(); _topicVerses.Clear(); fresh.AddRange(starts); }
        foreach (var start in starts)
        {
            try { fresh.AddRange((await SynonymsAsync(start)).Where(s => !s.Contains(' ') && !_topicWords.Contains(s, StringComparer.OrdinalIgnoreCase) && !fresh.Contains(s, StringComparer.OrdinalIgnoreCase)).Take(expand ? 8 : Math.Max(3, 9 / starts.Length))); }
            catch { /* offline: only the word itself */ }
        }

        status(L("Zähle Stellen …"));
        var added = new List<string>();
        foreach (var word in fresh)
        {
            var hits = await Task.Run(() => SearchEngine.Run(corpus, $"L={word}", null, new SearchOptions(), null).Results.Select(r => r.Ref).ToHashSet());
            var isStart = !expand && starts.Contains(word, StringComparer.OrdinalIgnoreCase);
            if (hits.Count == 0 && !isStart) continue;
            _topicVerses[word] = hits; _topicWords.Add(word); added.Add(word);
        }
        var data = new NetData();
        foreach (var word in added)
        {
            var n = _topicVerses[word].Count;
            var start = !expand && starts.Contains(word, StringComparer.OrdinalIgnoreCase);
            data.Nodes.Add(new NetNode(word, word, L("{0} Verse", n), start ? Color.Parse("#E3B65F") : Color.Parse("#7FD1AE"), start ? 34 : Math.Min(30, 4 + n / 6.0)));
        }
        foreach (var word in added)
            foreach (var other in _topicWords)
            {
                if (word == other || (added.Contains(other) && string.CompareOrdinal(word, other) > 0)) continue;
                var common = _topicVerses[word].Count(_topicVerses[other].Contains);
                if (common >= 2) data.Edges.Add((word, other, common));
            }
        if (expand && data.Edges.All(e => e.A != key && e.B != key)) foreach (var word in added) data.Edges.Add((key, word, 1));   // a synonym stays tied to the node it came from
        if (!expand && starts.Length > 1)   // the chosen words belong together even when they share no verse
            for (var i = 1; i < starts.Length; i++) if (!data.Edges.Any(e => (e.A == starts[0] && e.B == starts[i]) || (e.B == starts[0] && e.A == starts[i]))) data.Edges.Add((starts[0], starts[i], 1));
        return data;
    }
}
