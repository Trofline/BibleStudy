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
            if (kind == "word") { SetPanelOpen(true); SideTabs.SelectedIndex = AnalysisTab; await AnalyzeWordAsync(key); }
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

    async Task<NetData?> WordNetAsync(string key, bool expand, Action<string> status)
    {
        if (_analyzer is null) await LoadAnalyzerAsync(status);
        if (_analyzer is null) return null;
        var id = WordAnalyzer.Normalize(key);
        if (id is null)
        {
            status(L("Suche das Wort im Wörterbuch …"));
            id = (await _strongs.SearchAsync(key, 1)).FirstOrDefault()?.Id is { } found ? WordAnalyzer.Normalize(found) : null;
        }
        if (id is null || _analyzer.Count(id) == 0) return null;

        status(L("Wird berechnet …"));
        var partners = _analyzer.CoOccurring(id, expand ? 10 : 14);
        var data = new NetData();
        if (!expand && await WordNodeAsync(id, 34, L("{0}× in der KJV", _analyzer.Count(id))) is { } center) data.Nodes.Add(center);
        foreach (var (other, shared) in partners)
            if (await WordNodeAsync(other, Math.Min(30, 4 + shared / 2.0), $"{shared}×") is { } node) data.Nodes.Add(node);
        foreach (var (other, shared) in partners) data.Edges.Add((id, other, shared));
        // the partners also belong together if they share verses with each other
        var sets = partners.ToDictionary(p => p.Id, p => _analyzer.Occurrences(p.Id).ToHashSet());
        for (var i = 0; i < partners.Count; i++)
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
        LexiconEntry? entry;
        try { entry = expand ? await _hello.GetLexiconEntryAsync("people", key) : await FindEntityAsync("people", key); }
        catch { return null; }
        if (entry is null) return null;

        var chapters = entry.References.GroupBy(r => (r.Book, r.Chapter)).OrderByDescending(g => g.Count()).Take(70).Select(g => g.Key).ToList();
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
                if (!people.Any(p => p.Id == entry.Id)) return;
                lock (shared)
                    foreach (var p in people.Where(p => p.Id != entry.Id))
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
        if (!expand) data.Nodes.Add(new NetNode(entry.Id, entry.Name, L("{0} Stellen", entry.References.Count), Color.Parse("#E3B65F"), 34));
        foreach (var (id, p) in top)
        {
            data.Nodes.Add(new NetNode(id, p.Name, L("{0} gemeinsame Kapitel", p.Chapters), HomeView.ColorOf(p.Book), Math.Min(30, 4 + p.Chapters * 2.0)));
            data.Edges.Add((entry.Id, id, p.Chapters));
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
        if (!expand) { _topicWords.Clear(); _topicVerses.Clear(); fresh.Add(key); }
        try { fresh.AddRange((await SynonymsAsync(key)).Where(s => !s.Contains(' ') && !_topicWords.Contains(s, StringComparer.OrdinalIgnoreCase) && !fresh.Contains(s, StringComparer.OrdinalIgnoreCase)).Take(expand ? 8 : 9)); }
        catch { /* offline: only the word itself */ }

        status(L("Zähle Stellen …"));
        var added = new List<string>();
        foreach (var word in fresh)
        {
            var hits = await Task.Run(() => SearchEngine.Run(corpus, $"L={word}", null, new SearchOptions(), null).Results.Select(r => r.Ref).ToHashSet());
            if (hits.Count == 0 && !(word == key && !expand)) continue;
            _topicVerses[word] = hits; _topicWords.Add(word); added.Add(word);
        }
        var data = new NetData();
        foreach (var word in added)
        {
            var n = _topicVerses[word].Count;
            data.Nodes.Add(new NetNode(word, word, L("{0} Verse", n), word == key && !expand ? Color.Parse("#E3B65F") : Color.Parse("#7FD1AE"), word == key && !expand ? 34 : Math.Min(30, 4 + n / 6.0)));
        }
        foreach (var word in added)
            foreach (var other in _topicWords)
            {
                if (word == other || (added.Contains(other) && string.CompareOrdinal(word, other) > 0)) continue;
                var common = _topicVerses[word].Count(_topicVerses[other].Contains);
                if (common >= 2) data.Edges.Add((word, other, common));
            }
        if (expand && data.Edges.All(e => e.A != key && e.B != key)) foreach (var word in added) data.Edges.Add((key, word, 1));   // a synonym stays tied to the node it came from
        return data;
    }
}
