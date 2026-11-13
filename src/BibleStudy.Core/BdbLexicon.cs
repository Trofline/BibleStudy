using System.Text;
using System.Xml.Linq;

namespace BibleStudy.Core;

/// <summary>One entry of the Brown-Driver-Briggs Hebrew and English Lexicon.</summary>
public sealed record BdbEntry(string Id, string Headword, IReadOnlyList<string> Lines);

/// <summary>A word of a root family in BDB: its entry, the Hebrew word, its meaning and Strong numbers.</summary>
public sealed record RootMember(string EntryId, string Word, string Meaning, IReadOnlyList<string> Strongs, bool IsRoot);

/// <summary>A Hebrew root and all words BDB lists under it.</summary>
public sealed record RootFamily(string Root, IReadOnlyList<RootMember> Members)
{
    public IEnumerable<string> Strongs => Members.SelectMany(m => m.Strongs).Distinct();
}

/// <summary>
/// The Brown-Driver-Briggs lexicon (1906, public domain) as digitised by the Open Scriptures Hebrew Lexicon project (CC BY 4.0).
/// Downloaded on first use into the cache folder (together about 5 MB). A Strong number is found through the lexical index of the same
/// project, which links Strong, BDB and TWOT numbers.
/// </summary>
public sealed class BdbLexicon(HttpClient http, string cacheDir)
{
    const string Base = "https://raw.githubusercontent.com/openscriptures/HebrewLexicon/master/";
    public const string Attribution = "Brown-Driver-Briggs Hebrew and English Lexicon (1906, gemeinfrei), digitalisiert vom Open Scriptures Hebrew Lexicon Project, CC BY 4.0";

    Dictionary<string, string>? _byStrong;      // "H1" -> BDB id
    Dictionary<string, XElement>? _entries;     // BDB id -> entry
    readonly SemaphoreSlim _gate = new(1, 1);

    async Task<string> FileAsync(string name, CancellationToken ct)
    {
        var path = Path.Combine(cacheDir, "bdb", name);
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = await http.GetByteArrayAsync(Base + name, ct);
        XDocument.Parse(Encoding.UTF8.GetString(bytes).TrimStart('﻿')).Root!.ToString();   // only a complete, readable file is kept
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes, CancellationToken.None);
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    async Task LoadAsync(CancellationToken ct)
    {
        if (_entries is not null) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_entries is not null) return;
            var index = await FileAsync("LexicalIndex.xml", ct);
            var bdb = await FileAsync("BrownDriverBriggs.xml", ct);
            var indexDoc = await Task.Run(() => XDocument.Load(index), ct);
            _byStrong = ParseIndex(indexDoc);
            _strongsOf = ParseStrongsByEntry(indexDoc);
            _entries = await Task.Run(() => ParseEntries(XDocument.Load(bdb, LoadOptions.PreserveWhitespace)), ct);
        }
        finally { _gate.Release(); }
    }

    Dictionary<string, List<string>>? _strongsOf;   // BDB id → every Strong number that points to it

    /// <summary>
    /// The family of a Hebrew word: all words BDB puts under the same root (the root comes first), each with its Strong numbers and
    /// meaning. In the Open Scriptures BDB every root opens a section ("a.ac"), and its derivatives follow in it ("a.ac.ab" …).
    /// Null when the number has no entry or the entry stands alone.
    /// </summary>
    public async Task<RootFamily?> RootFamilyAsync(string strongId, CancellationToken ct = default)
    {
        await LoadAsync(ct);
        if (!_byStrong!.TryGetValue(strongId.ToUpperInvariant(), out var id)) return null;
        return Family(id, _entries!, _strongsOf!);
    }

    public static RootFamily? Family(string bdbId, IReadOnlyDictionary<string, XElement> entries, IReadOnlyDictionary<string, List<string>> strongsOf)
    {
        var dot = bdbId.LastIndexOf('.');
        if (dot < 0) return null;
        var section = bdbId[..(dot + 1)];
        var members = entries.Where(kv => kv.Key.StartsWith(section, StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new RootMember(kv.Key, kv.Value.Elements().FirstOrDefault(x => x.Name.LocalName == "w")?.Value ?? "",
                kv.Value.Elements().FirstOrDefault(x => x.Name.LocalName == "def")?.Value ?? "", strongsOf.GetValueOrDefault(kv.Key) ?? [],
                (string?)kv.Value.Attribute("type") == "root"))
            .ToList();
        // a section can hold more than one root (homonyms I, II …): the family runs from the root before the word to the next root
        var at = members.FindIndex(m => m.EntryId == bdbId);
        if (at < 0) return null;
        var start = members.FindLastIndex(at, m => m.IsRoot);
        if (start < 0) return null;
        var next = members.FindIndex(start + 1, m => m.IsRoot);
        var family = members.GetRange(start, (next < 0 ? members.Count : next) - start);
        if (family.Count(m => m.Strongs.Count > 0) < 2) return null;
        return new RootFamily(family[0].Word, family);
    }

    /// <summary>"a.ac.ab" → ["H8"]: every Strong number of an entry (several numbers can point to one entry).</summary>
    public static Dictionary<string, List<string>> ParseStrongsByEntry(XDocument doc)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var x in doc.Descendants().Where(e => e.Name.LocalName == "xref"))
        {
            var bdb = (string?)x.Attribute("bdb"); var strong = (string?)x.Attribute("strong");
            if (string.IsNullOrEmpty(bdb) || string.IsNullOrEmpty(strong)) continue;
            var digits = new string(strong.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0) continue;
            if (!map.TryGetValue(bdb, out var list)) map[bdb] = list = [];
            var id = "H" + int.Parse(digits);
            if (!list.Contains(id)) list.Add(id);
        }
        return map;
    }

    /// <summary>The entry for a Strong number such as "H1" (null when the index has no BDB entry for it).</summary>
    public async Task<BdbEntry?> GetAsync(string strongId, CancellationToken ct = default)
    {
        await LoadAsync(ct);
        return _byStrong!.TryGetValue(strongId.ToUpperInvariant(), out var id) && _entries!.TryGetValue(id, out var e) ? Render(id, e) : null;
    }

    // ---- reading the files (public for tests) -----------------------------------------------------------------------------------

    /// <summary>"H3" → "a.ab.ab" from the lexical index ("xref" with bdb and strong); the first entry for a number wins.</summary>
    public static Dictionary<string, string> ParseIndex(XDocument doc)
    {
        var map = new Dictionary<string, string>();
        foreach (var x in doc.Descendants().Where(e => e.Name.LocalName == "xref"))
        {
            var bdb = (string?)x.Attribute("bdb"); var strong = (string?)x.Attribute("strong");
            if (string.IsNullOrEmpty(bdb) || string.IsNullOrEmpty(strong)) continue;
            var digits = new string(strong.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length > 0) map.TryAdd("H" + int.Parse(digits), bdb);
        }
        return map;
    }

    public static Dictionary<string, XElement> ParseEntries(XDocument doc)
    {
        var map = new Dictionary<string, XElement>();
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "entry"))
            if ((string?)e.Attribute("id") is { Length: > 0 } id) map.TryAdd(id, e);
        return map;
    }

    /// <summary>Plain text lines: the entry, then every numbered meaning on its own line. Scripture references keep their printed form.</summary>
    public static BdbEntry Render(string id, XElement entry)
    {
        string Flat(XElement e)
        {
            var sb = new StringBuilder();
            void Walk(XNode n)
            {
                switch (n)
                {
                    case XText t: sb.Append(t.Value); break;
                    case XElement x when x.Name.LocalName is "status" or "sense" or "entry": break;   // senses are lines of their own
                    case XElement x: foreach (var c in x.Nodes()) Walk(c); break;
                }
            }
            foreach (var c in e.Nodes()) Walk(c);
            return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
        var head = entry.Elements().FirstOrDefault(x => x.Name.LocalName == "w")?.Value ?? "";
        var lines = new List<string>();
        var first = Flat(entry);
        if (first.Length > 0) lines.Add(first);
        void Senses(XElement parent, int depth)
        {
            foreach (var s in parent.Elements().Where(x => x.Name.LocalName == "sense"))
            {
                var n = (string?)s.Attribute("n");
                var text = Flat(s);
                if (text.Length > 0) lines.Add(new string(' ', depth * 2) + (n is null ? "" : n + ". ") + text);
                Senses(s, depth + 1);
            }
        }
        Senses(entry, 0);
        return new BdbEntry(id, head, lines);
    }
}
