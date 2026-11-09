using BibleStudy.Core.Models;

namespace BibleStudy.Core.Import;

/// <summary>What changes when a Bible that is already here is imported again: shown before the old text is replaced.</summary>
public static class ImportDiff
{
    public sealed record Change(string Book, int Chapter, int Verse, string Old, string New);

    public sealed record Result(int Changed, int Added, int Removed, IReadOnlyList<Change> Samples)
    {
        public bool None => Changed + Added + Removed == 0;
    }

    public static Result Compare(IEnumerable<VerseText> old, ParsedBible bible, int maxSamples = 200)
    {
        var before = new Dictionary<(string, int, int), string>();
        foreach (var v in old) before[(v.BookId, v.Chapter, v.Number)] = v.Text;
        int changed = 0, added = 0;
        var samples = new List<Change>();
        var seen = new HashSet<(string, int, int)>();
        foreach (var v in bible.Verses)
        {
            var key = (v.Book, v.Chapter, v.Verse);
            if (!seen.Add(key)) continue;
            var text = new Verse(v.Verse, v.Segments).Text;
            if (!before.TryGetValue(key, out var was)) { added++; if (samples.Count < maxSamples) samples.Add(new Change(v.Book, v.Chapter, v.Verse, "", text)); continue; }
            if (was == text) continue;
            changed++;
            if (samples.Count < maxSamples) samples.Add(new Change(v.Book, v.Chapter, v.Verse, was, text));
        }
        var gone = before.Keys.Where(k => !seen.Contains(k)).ToList();
        foreach (var (b, c, n) in gone.Take(Math.Max(0, maxSamples - samples.Count))) samples.Add(new Change(b, c, n, before[(b, c, n)], ""));
        return new Result(changed, added, gone.Count, samples);
    }

    /// <summary>The part of two versions of a verse that differs, with a little of the text around it ("… words …").</summary>
    public static (string Old, string New) Focus(string a, string b, int context = 24)
    {
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start]) start++;
        var end = 0;
        while (end < a.Length - start && end < b.Length - start && a[a.Length - 1 - end] == b[b.Length - 1 - end]) end++;
        string Cut(string s)
        {
            var from = Math.Max(0, start - context); var to = Math.Min(s.Length, s.Length - end + context);
            return (from > 0 ? "… " : "") + s[from..to] + (to < s.Length ? " …" : "");
        }
        return (Cut(a), Cut(b));
    }
}

/// <summary>Looks for a Bible file that is no longer where it was imported from.</summary>
public static class ImportFileFinder
{
    public sealed record Hit(string Path, bool Exact);

    const int MaxFolders = 500;

    public static Hit? Find(string remembered, IEnumerable<string> searchDirs)
    {
        var name = Path.GetFileName(remembered);
        if (name.Length == 0) return null;
        var ext = Path.GetExtension(name);
        var wanted = Plain(Path.GetFileNameWithoutExtension(name));
        var folders = new List<string>();
        var original = Path.GetDirectoryName(remembered);
        var roots = new[] { original }.Concat(searchDirs).OfType<string>().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var root in roots) Collect(root, 2, folders);

        Hit? similar = null; var best = 0.0;
        foreach (var dir in folders)
        {
            string[] files;
            try { files = Directory.GetFiles(dir, "*" + ext); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var f in files)
            {
                if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) return new Hit(f, true);
                var score = Similarity(wanted, Plain(Path.GetFileNameWithoutExtension(f)));
                if (score >= 0.75 && score > best) { best = score; similar = new Hit(f, false); }
            }
        }
        return similar;
    }

    static void Collect(string dir, int depth, List<string> into)
    {
        if (into.Count >= MaxFolders || into.Contains(dir, StringComparer.OrdinalIgnoreCase)) return;
        into.Add(dir);
        if (depth == 0) return;
        try { foreach (var d in Directory.GetDirectories(dir)) Collect(d, depth - 1, into); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a folder that cannot be read is left out */ }
    }

    static string Plain(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>1 = the same, 0 = nothing alike (edit distance against the longer name).</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var previous = row[0]; row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var keep = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                previous = keep;
            }
        }
        return 1.0 - (double)row[b.Length] / Math.Max(a.Length, b.Length);
    }
}
