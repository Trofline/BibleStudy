using System.Text;
using System.Xml.Linq;

namespace BibleStudy.Core;

/// <summary>One entry of the Brown-Driver-Briggs Hebrew and English Lexicon.</summary>
public sealed record BdbEntry(string Id, string Headword, IReadOnlyList<string> Lines);

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
            _byStrong = await Task.Run(() => ParseIndex(XDocument.Load(index)), ct);
            _entries = await Task.Run(() => ParseEntries(XDocument.Load(bdb, LoadOptions.PreserveWhitespace)), ct);
        }
        finally { _gate.Release(); }
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
