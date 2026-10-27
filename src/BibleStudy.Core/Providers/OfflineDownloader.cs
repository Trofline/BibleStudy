using BibleStudy.Core.Models;

namespace BibleStudy.Core.Providers;

/// <summary>Downloads every chapter of a translation (the provider caches them on disk for offline use).</summary>
public static class OfflineDownloader
{
    public static async Task<IReadOnlyList<Chapter>> DownloadAsync(IBibleProvider provider, string translationId,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var books = await provider.GetBooksAsync(translationId, ct);
        var jobs = books.SelectMany(b => Enumerable.Range(1, b.NumberOfChapters).Select(c => (Book: b.Id, Chapter: c))).ToList();
        var results = new Chapter?[jobs.Count];
        var done = 0;
        using var gate = new SemaphoreSlim(24);
        await Task.WhenAll(jobs.Select(async (job, i) =>
        {
            await gate.WaitAsync(ct);
            try { results[i] = await provider.GetChapterAsync(translationId, job.Book, job.Chapter, ct); }
            catch (HttpRequestException) { }
            finally { gate.Release(); progress?.Report((Interlocked.Increment(ref done), jobs.Count)); }
        }));
        return results.OfType<Chapter>().ToList();
    }
}
