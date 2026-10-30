namespace BibleStudy.Core.Models;

public sealed record LicenseInfo(string Attribution, string CommercialUse, string Derivatives, bool Copyleft, bool PublicDomain);

public sealed record TranslationInfo(
    string Id, string Name, string EnglishName, string ShortName, string Language, string LanguageName,
    string TextDirection, string? LicenseUrl, string? Website, LicenseInfo License, int NumberOfBooks)
{
    public override string ToString() => $"{ShortName} – {Name}";
}

public sealed record BookInfo(string Id, string Name, string CommonName, int Order, int NumberOfChapters)
{
    public override string ToString() => CommonName;
}

/// <summary>Text fragment of a verse (WordsOfJesus marks red-letter text); Strongs are the numbers of its word, Estimated marks numbers that were added afterwards (not from the source).</summary>
public sealed record Segment(string Text, bool WordsOfJesus = false, IReadOnlyList<string>? Strongs = null, int? NoteId = null, bool Estimated = false);

public sealed record Verse(int Number, IReadOnlyList<Segment> Segments)
{
    /// <summary>Plain text; a footnote marker stands where the source has no space, so it counts as one.</summary>
    public string Text => System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(
        string.Concat(Segments.Select(s => s.NoteId is null ? s.Text : " ")), @"\s+", " "), @" ([.,;:!?])", "$1").Trim();
}

/// <summary>A footnote of a chapter; referenced from verse segments by <see cref="Segment.NoteId"/>.</summary>
public sealed record Footnote(int NoteId, int Verse, string Text);

/// <summary>One word of the original text with transliteration, grammar and the English meaning (Berean interlinear, public domain).</summary>
public sealed record InterlinearWord(int Position, string Language, string Text, string Translit, string Parsing, string ParsingFull, string Strongs, string Gloss)
{
    public bool IsHebrew => Language == "hebrew";
}

/// <summary>One entry of Strong's dictionary (original-language word study).</summary>
public sealed record StrongsEntry(string Id, string Lemma, string Translit, string Pronunciation, string Definition, string KjvUsage, string Derivation);

/// <summary>Location of a verse, independent of translation (USFM book id), so notes follow across translations.</summary>
public readonly record struct VerseRef(string Book, int Chapter, int Verse);

/// <summary>Plain verse text for indexing.</summary>
public sealed record VerseText(string BookId, int Chapter, int Number, string Text);

public sealed record Chapter(string TranslationId, string BookId, int Number, IReadOnlyList<Verse> Verses, IReadOnlyList<Footnote>? Footnotes = null);

/// <summary>A cross reference target (EndVerse = Target.Verse for single verses) with its relevance score.</summary>
public sealed record CrossRef(VerseRef Target, int EndVerse, int Score);

/// <summary>One tagged word in a translation, used for the Strong's concordance.</summary>
public sealed record StrongsOccurrence(string Strong, VerseRef Ref, string Word);

public sealed record CommentaryInfo(string Id, string Name, string LicenseUrl, string Language);
public sealed record CommentaryNote(int Verse, string Text);
public sealed record CommentaryChapter(string Introduction, IReadOnlyList<CommentaryNote> Notes);
public sealed record LexiconItem(string Kind, string Id, string Name, int References);
public sealed record LexiconEntry(string Kind, string Id, string Name, IReadOnlyList<string> AlsoCalled, IReadOnlyList<string> Description,
    IReadOnlyList<VerseRef> References, IReadOnlyList<LexiconItem> Related, string FeatureType);
