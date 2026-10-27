namespace BibleStudy.Core.Models;

public enum EdgeDirection { Outgoing, Incoming, Both }

public readonly record struct ChapterId(string Book, int Chapter);

/// <summary>Directed connection between two chapters; Weight = number of verse-level cross references.</summary>
public sealed record GraphEdge(int From, int To, int Weight);

/// <summary>
/// All cross references aggregated to chapter level. Nodes are the chapters of the Bible in canonical order
/// (index 0 = first chapter of the first book).
/// </summary>
public sealed class CrossRefGraph
{
    readonly List<(string Book, int Chapters)> _books;
    readonly int[] _starts;
    readonly Dictionary<string, int> _bookIndex = [];
    readonly Dictionary<long, int> _weights = [];
    readonly List<(int Other, int Weight)>[] _out, _in;

    public int NodeCount { get; }
    /// <summary>Verse-level links; set by the builder.</summary>
    public VerseLinkSet? Links { get; set; }
    public IReadOnlyList<(string Book, int Chapters)> Books => _books;
    /// <summary>Strongest connections first.</summary>
    public IReadOnlyList<GraphEdge> Edges { get; }

    public CrossRefGraph(IEnumerable<(string Book, int Chapters)> books, IEnumerable<GraphEdge> edges)
    {
        _books = books.ToList();
        _starts = new int[_books.Count + 1];
        for (var i = 0; i < _books.Count; i++) { _starts[i + 1] = _starts[i] + _books[i].Chapters; _bookIndex[_books[i].Book] = i; }
        NodeCount = _starts[^1];
        _out = Enumerable.Range(0, NodeCount).Select(_ => new List<(int, int)>()).ToArray();
        _in = Enumerable.Range(0, NodeCount).Select(_ => new List<(int, int)>()).ToArray();
        var list = new List<GraphEdge>();
        foreach (var e in edges)
        {
            if (e.From == e.To || e.From < 0 || e.To < 0 || e.From >= NodeCount || e.To >= NodeCount || e.Weight <= 0) continue;
            list.Add(e);
            _weights[Key(e.From, e.To)] = _weights.GetValueOrDefault(Key(e.From, e.To)) + e.Weight;
            _out[e.From].Add((e.To, e.Weight)); _in[e.To].Add((e.From, e.Weight));
        }
        Edges = list.OrderByDescending(e => e.Weight).ToList();
    }

    long Key(int a, int b) => (long)a * NodeCount + b;

    public int Index(string book, int chapter) =>
        _bookIndex.TryGetValue(book, out var i) && chapter >= 1 && chapter <= _books[i].Chapters ? _starts[i] + chapter - 1 : -1;

    public int BookIndexOf(int node)
    {
        var i = Array.BinarySearch(_starts, node);
        return i >= 0 ? Math.Min(i, _books.Count - 1) : ~i - 1;
    }

    public ChapterId At(int node) { var b = BookIndexOf(node); return new(_books[b].Book, node - _starts[b] + 1); }

    public int BookStart(int bookIndex) => _starts[bookIndex];

    public int Weight(int a, int b, EdgeDirection dir) => dir switch
    {
        EdgeDirection.Outgoing => _weights.GetValueOrDefault(Key(a, b)),
        EdgeDirection.Incoming => _weights.GetValueOrDefault(Key(b, a)),
        _ => _weights.GetValueOrDefault(Key(a, b)) + _weights.GetValueOrDefault(Key(b, a)),
    };

    /// <summary>Connected chapters of <paramref name="node"/>, strongest first (merged for Both).</summary>
    public IReadOnlyList<(int Other, int Weight)> Neighbors(int node, EdgeDirection dir)
    {
        IEnumerable<(int Other, int Weight)> all = dir switch { EdgeDirection.Outgoing => _out[node], EdgeDirection.Incoming => _in[node], _ => _out[node].Concat(_in[node]) };
        return all.GroupBy(x => x.Other).Select(g => (g.Key, g.Sum(x => x.Weight))).OrderByDescending(x => x.Item2).ToList();
    }
}

/// <summary>Downloads the cross references of every chapter (cached on disk by the provider) and builds the graph.</summary>
public static class CrossRefGraphBuilder
{
    public static async Task<CrossRefGraph> BuildAsync(Providers.HelloAoProvider provider, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var books = await provider.GetCrossRefBooksAsync(ct);
        var skeleton = new CrossRefGraph(books, []);
        var jobs = books.SelectMany(b => Enumerable.Range(1, b.Chapters).Select(c => (b.Book, Chapter: c))).ToList();
        var results = new List<GraphEdge>[jobs.Count];
        var rawResults = new List<RawLink>[jobs.Count];
        var done = 0;
        using var gate = new SemaphoreSlim(24);
        await Task.WhenAll(jobs.Select(async (job, i) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var from = skeleton.Index(job.Book, job.Chapter);
                var perTarget = new Dictionary<int, int>();
                var raw = new List<RawLink>();
                foreach (var (verse, refs) in await provider.GetChapterCrossRefsAsync(job.Book, job.Chapter, ct))
                    foreach (var r in refs)
                    {
                        var to = skeleton.Index(r.Target.Book, r.Target.Chapter);
                        if (to < 0) continue;
                        perTarget[to] = perTarget.GetValueOrDefault(to) + 1;
                        raw.Add(new RawLink(from, verse, to, r.Target.Verse, r.EndVerse, r.Score));
                    }
                results[i] = perTarget.Select(kv => new GraphEdge(from, kv.Key, kv.Value)).ToList();
                rawResults[i] = raw;
            }
            catch (HttpRequestException) { results[i] = []; rawResults[i] = []; }
            finally { gate.Release(); progress?.Report((Interlocked.Increment(ref done), jobs.Count)); }
        }));
        var graph = new CrossRefGraph(books, results.SelectMany(r => r));
        graph.Links = new VerseLinkSet(graph, rawResults.SelectMany(r => r ?? []).ToList());
        return graph;
    }
}

/// <summary>One verse-level cross reference before verse indexes are known.</summary>
public readonly record struct RawLink(int SrcNode, int SrcVerse, int TgtNode, int TgtStart, int TgtEnd, int Votes);

/// <summary>
/// Every cross reference at verse level. Verses of the Bible are numbered 0..VerseCount-1 in canonical order
/// (chapter sizes are taken from the verses that occur in the data). Used by the arc diagram and the thread view.
/// </summary>
public sealed class VerseLinkSet
{
    readonly int[] _chapterStart;
    public int VerseCount { get; }
    public int[] Src { get; }
    public int[] TgtStart { get; }
    public int[] TgtEnd { get; }
    public int[] Votes { get; }
    /// <summary>Genre ordinal (0..9, see <see cref="BookCategory"/>) of every verse.</summary>
    public byte[] VerseGenre { get; }
    /// <summary>Link ids, most voted first.</summary>
    public int[] ByVotes { get; }

    public int Count => Src.Length;

    public VerseLinkSet(CrossRefGraph graph, IReadOnlyList<RawLink> raw)
    {
        var counts = new int[graph.NodeCount];
        foreach (var l in raw)
        {
            counts[l.SrcNode] = Math.Max(counts[l.SrcNode], l.SrcVerse);
            counts[l.TgtNode] = Math.Max(counts[l.TgtNode], Math.Max(l.TgtStart, l.TgtEnd));
        }
        _chapterStart = new int[graph.NodeCount + 1];
        for (var i = 0; i < graph.NodeCount; i++) _chapterStart[i + 1] = _chapterStart[i] + Math.Max(1, counts[i]);
        VerseCount = _chapterStart[^1];
        VerseGenre = new byte[VerseCount];
        for (var n = 0; n < graph.NodeCount; n++)
        {
            var genre = (byte)Math.Min(9, (int)BookCategories.Of(graph.Books[graph.BookIndexOf(n)].Book));
            Array.Fill(VerseGenre, genre, _chapterStart[n], _chapterStart[n + 1] - _chapterStart[n]);
        }
        Src = new int[raw.Count]; TgtStart = new int[raw.Count]; TgtEnd = new int[raw.Count]; Votes = new int[raw.Count];
        for (var i = 0; i < raw.Count; i++)
        {
            Src[i] = IndexOf(raw[i].SrcNode, raw[i].SrcVerse);
            TgtStart[i] = IndexOf(raw[i].TgtNode, raw[i].TgtStart);
            TgtEnd[i] = IndexOf(raw[i].TgtNode, Math.Max(raw[i].TgtStart, raw[i].TgtEnd));
            Votes[i] = raw[i].Votes;
        }
        ByVotes = Enumerable.Range(0, raw.Count).OrderByDescending(i => Votes[i]).ToArray();
    }

    /// <summary>Verse index of verse <paramref name="verse"/> (1-based) in chapter node <paramref name="node"/>.</summary>
    public int IndexOf(int node, int verse) => _chapterStart[node] + Math.Clamp(verse, 1, _chapterStart[node + 1] - _chapterStart[node]) - 1;

    public int ChapterStart(int node) => _chapterStart[node];

    public int ChapterVerseCount(int node) => _chapterStart[node + 1] - _chapterStart[node];

    public int NodeOfVerse(int verse)
    {
        var i = Array.BinarySearch(_chapterStart, Math.Clamp(verse, 0, VerseCount - 1));
        return Math.Min(i >= 0 ? i : ~i - 1, _chapterStart.Length - 2);
    }
}
