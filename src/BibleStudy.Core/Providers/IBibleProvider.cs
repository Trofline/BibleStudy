using BibleStudy.Core.Models;

namespace BibleStudy.Core.Providers;

/// <summary>A source of Bible texts. Each provider must expose per-translation license info.</summary>
public interface IBibleProvider
{
    string Name { get; }
    Task<IReadOnlyList<TranslationInfo>> GetTranslationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BookInfo>> GetBooksAsync(string translationId, CancellationToken ct = default);
    Task<Chapter> GetChapterAsync(string translationId, string bookId, int chapter, CancellationToken ct = default);
    Task<IReadOnlyList<VerseText>> GetAllVersesAsync(string translationId, CancellationToken ct = default);
}
