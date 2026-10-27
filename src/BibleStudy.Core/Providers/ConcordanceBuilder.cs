using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Core.Providers;

/// <summary>Builds the local Strong's concordance of a translation by reading all its (cached) chapters once.</summary>
public static class ConcordanceBuilder
{
    public static async Task BuildAsync(IBibleProvider provider, UserStore store, string translationId,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var chapters = await OfflineDownloader.DownloadAsync(provider, translationId, progress, ct);
        store.IndexStrongs(translationId, chapters.SelectMany(c => c.Verses.SelectMany(v =>
            v.Segments.Where(s => s.Strongs is { Count: > 0 }).SelectMany(s =>
                s.Strongs!.Select(id => new StrongsOccurrence(id, new VerseRef(c.BookId, c.Number, v.Number), s.Text.Trim()))))));
    }
}
