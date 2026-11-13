using System.Globalization;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Materials;

/// <summary>
/// A place in a file of the materials: the paragraph (counted without pictures, so the same paragraph is found whether a file was read
/// with or without its pictures), where in it, and a short piece of the text around it to show in a list.
/// </summary>
public sealed record MaterialHit(string Path, int TextBlock, int Start, int Length, string Snippet, int Page = 0);

/// <summary>Finding words and Bible references in the files of the materials.</summary>
public static class MaterialSearch
{
    static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;   // "Hölle" also finds "holle", "ROM" finds "Röm"

    static bool Searchable(Block b) => b.Kind is not (BlockKind.Image or BlockKind.TocEntry or BlockKind.Page);

    /// <summary>The paragraphs with text, each with its number among the blocks that are not pictures, and the page it stands on (PDF).</summary>
    public static IEnumerable<(Block Block, int TextBlock, int Page)> TextBlocks(IReadOnlyList<Block> blocks)
    {
        var n = 0; var page = 0;
        foreach (var b in blocks)
        {
            if (b.Kind == BlockKind.Image) continue;
            if (b.Kind == BlockKind.Page) page++;
            yield return (b, n++, page);
        }
    }

    /// <summary>The number of a block among those that are not pictures (what a hit points to).</summary>
    public static int TextBlockOf(IReadOnlyList<Block> blocks, int index) => blocks.Take(index).Count(b => b.Kind != BlockKind.Image);

    /// <summary>Every place where the words stand (as they are written, case and accents do not matter), in order.</summary>
    public static List<MaterialHit> Find(string path, IReadOnlyList<Block> blocks, string query, int max = 200)
    {
        var hits = new List<MaterialHit>();
        query = query.Trim();
        if (query.Length == 0) return hits;
        foreach (var (b, n, page) in TextBlocks(blocks))
        {
            if (!Searchable(b)) continue;
            var text = b.Text;
            var at = 0;
            while (at < text.Length && hits.Count < max)
            {
                var i = Compare.IndexOf(text, query, at, Loose);
                if (i < 0) break;
                hits.Add(new MaterialHit(path, n, i, query.Length, Snippet(text, i, query.Length), page));
                at = i + Math.Max(1, query.Length);
            }
            if (hits.Count >= max) break;
        }
        return hits;
    }

    /// <summary>The places that name a verse: "Röm 8,28" and also a passage around it ("Röm 8,26-30").</summary>
    public static List<MaterialHit> VerseHits(string path, IReadOnlyList<Block> blocks, VerseRef verse, int max = 50)
    {
        var hits = new List<MaterialHit>();
        foreach (var (b, n, page) in TextBlocks(blocks))
        {
            if (!Searchable(b)) continue;
            var text = b.Text;
            foreach (var (index, length, first) in DocumentLinks.VerseSpans(text))
            {
                if (first.Book != verse.Book || first.Chapter != verse.Chapter) continue;
                var end = DocumentLinks.VerseRanges(text.Substring(index, length)).FirstOrDefault().EndVerse;
                if (verse.Verse < first.Verse || verse.Verse > Math.Max(first.Verse, end)) continue;
                hits.Add(new MaterialHit(path, n, index, length, Snippet(text, index, length), page));
                break;   // one hit per paragraph is enough
            }
            if (hits.Count >= max) break;
        }
        return hits;
    }

    /// <summary>A short piece of the text around a place, cut at words, with "…" where it was cut.</summary>
    public static string Snippet(string text, int start, int length, int width = 150)
    {
        text = text.Replace('\t', ' ').Replace('\n', ' ');
        if (text.Length <= width) return text.Trim();
        var from = Math.Max(0, start - (width - length) / 2);
        var to = Math.Min(text.Length, from + width);
        from = Math.Max(0, to - width);
        if (from > 0) { var sp = text.IndexOf(' ', from); if (sp > 0 && sp < start) from = sp + 1; }
        if (to < text.Length) { var sp = text.LastIndexOf(' ', to - 1); if (sp > start + length) to = sp; }
        return (from > 0 ? "… " : "") + text[from..to].Trim() + (to < text.Length ? " …" : "");
    }

    /// <summary>
    /// Where a marked piece of text is now, after the file may have changed: at its old place if the words are still there, otherwise the
    /// same words in the paragraph nearest to the old one; null if they are gone.
    /// </summary>
    public static (int TextBlock, int Start)? Locate(IReadOnlyList<Block> blocks, int textBlock, int start, string text)
    {
        if (text.Length == 0) return null;
        var list = TextBlocks(blocks).ToList();
        if (textBlock >= 0 && textBlock < list.Count)
        {
            var t = list[textBlock].Block.Text;
            if (start >= 0 && start + text.Length <= t.Length && string.CompareOrdinal(t, start, text, 0, text.Length) == 0) return (textBlock, start);
        }
        foreach (var i in list.Select(x => x.TextBlock).OrderBy(i => Math.Abs(i - textBlock)))
        {
            var at = list[i].Block.Text.IndexOf(text, StringComparison.Ordinal);
            if (at >= 0) return (i, at);
        }
        return null;
    }
}
