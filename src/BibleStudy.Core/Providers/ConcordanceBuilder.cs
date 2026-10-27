using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.Core.Providers;

/// <summary>Builds the local Strong's concordance of a translation by reading all its (cached) chapters once.</summary>
public static class ConcordanceBuilder
{
    public static async Task BuildAsync(IBibleProvider provider, UserStore store, string translationId,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var books = await provider.GetBooksAsync(translationId, ct);
        var jobs = books.SelectMany(b => Enumerable.Range(1, b.NumberOfChapters).Select(c => (Book: b.Id, Chapter: c))).ToList();
        var results = new Chapter?[jobs.Count];
        var done = 0;
        using var gate = new SemaphoreSlim(6);
        await Task.WhenAll(jobs.Select(async (job, i) =>
        {
            await gate.WaitAsync(ct);
            try { results[i] = await provider.GetChapterAsync(translationId, job.Book, job.Chapter, ct); }
            catch (HttpRequestException) { }
            finally { gate.Release(); progress?.Report((Interlocked.Increment(ref done), jobs.Count)); }
        }));
        store.IndexStrongs(translationId, results.OfType<Chapter>().SelectMany(c => c.Verses.SelectMany(v =>
            v.Segments.Where(s => s.Strongs is { Count: > 0 }).SelectMany(s =>
                s.Strongs!.Select(id => new StrongsOccurrence(id, new VerseRef(c.BookId, c.Number, v.Number), s.Text.Trim()))))));
    }
}
