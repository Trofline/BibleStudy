namespace BibleStudy.Core.Search;

/// <summary>What the different views of the search results show, independent of how it is drawn.</summary>
public static class HitViews
{
    /// <summary>One found word with the text around it, for the view in which all hits stand under each other.</summary>
    public sealed record Aligned(SearchResult Hit, string Left, string Word, string Right);

    /// <summary>A line per found word; the text on both sides is cut to about <paramref name="context"/> characters, at a word boundary.</summary>
    public static List<Aligned> Align(IEnumerable<SearchResult> hits, int context, int max)
    {
        var lines = new List<Aligned>();
        foreach (var h in hits)
            foreach (var (start, len) in h.Spans)
            {
                if (lines.Count >= max) return lines;
                var left = h.Text[..start]; var right = h.Text[(start + len)..];
                if (left.Length > context) { var cut = left.IndexOf(' ', left.Length - context); left = "… " + left[((cut < 0 ? left.Length - context : cut + 1))..]; }
                if (right.Length > context) { var cut = right.LastIndexOf(' ', context); right = right[..(cut <= 0 ? context : cut)] + " …"; }
                lines.Add(new(h, left, h.Text.Substring(start, len), right));
            }
        return lines;
    }

    /// <summary>How often each spelling was found, most frequent first (capitals do not count).</summary>
    public static List<(string Form, int Count)> WordForms(IEnumerable<SearchResult> hits) => hits
        .SelectMany(h => h.Spans.Select(s => h.Text.Substring(s.Start, s.Len).ToLowerInvariant()))
        .GroupBy(f => f).Select(g => (Form: g.Key, Count: g.Count()))
        .OrderByDescending(x => x.Count).ThenBy(x => x.Form, StringComparer.CurrentCulture).ToList();
}
