using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>Helpers for the word barcode (one stripe per chapter).</summary>
public partial class MainWindow
{
    /// <summary>Every chapter that the text has, in Bible order, with the hits of the given counts (0 where there are none).</summary>
    List<BarcodeCell> BarcodeCells(IEnumerable<CorpusVerse> corpus, IReadOnlyDictionary<(string Book, int Chapter), int> counts)
    {
        var order = _books.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return corpus.Select(v => (v.Ref.Book, v.Ref.Chapter)).Distinct()
            .OrderBy(c => order.GetValueOrDefault(c.Book, int.MaxValue)).ThenBy(c => c.Chapter)
            .Select(c => new BarcodeCell(c.Book, c.Chapter, counts.GetValueOrDefault(c)))
            .ToList();
    }

    /// <summary>Every chapter of the given books (the book list knows how many it has), with the hits of the given verses.</summary>
    List<BarcodeCell> BarcodeCells(IEnumerable<BookInfo> books, IEnumerable<VerseRef> hits)
    {
        var counts = hits.GroupBy(h => (h.Book, h.Chapter)).ToDictionary(g => g.Key, g => g.Count());
        return books.OrderBy(b => b.Order)
            .SelectMany(b => Enumerable.Range(1, Math.Max(1, b.NumberOfChapters)).Select(c => new BarcodeCell(b.Id, c, counts.GetValueOrDefault((b.Id, c)))))
            .ToList();
    }
}
