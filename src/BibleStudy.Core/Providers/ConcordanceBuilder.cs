using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Core.Providers;

/// <summary>Builds the local Strong's concordance of a translation by reading all its (cached) chapters once.</summary>
public static class ConcordanceBuilder
{
    public static async Task BuildAsync(IBibleProvider provider, UserStore store, string translationId,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var expected = (await provider.GetBooksAsync(translationId, ct)).Sum(b => b.NumberOfChapters);
        var chapters = await OfflineDownloader.DownloadAsync(provider, translationId, progress, ct);
        // the index is built once and then kept for good: with chapters missing (a timeout, no network for a moment) every count would stay too small.
        // A handful may be missing in the source itself, so only a real gap stops it.
        if (expected - chapters.Count > Math.Max(2, expected / 100))
            throw new IOException(L("{0} von {1} Kapiteln konnten nicht geladen werden. Bitte versuche es noch einmal, wenn die Verbindung steht.", expected - chapters.Count, expected));
        store.IndexStrongs(translationId, chapters.SelectMany(c => c.Verses.SelectMany(v =>
            v.Segments.Where(s => s.Strongs is { Count: > 0 }).SelectMany(s =>
                s.Strongs!.Select(id => new StrongsOccurrence(id, new VerseRef(c.BookId, c.Number, v.Number), s.Text.Trim()))))));
    }
}
