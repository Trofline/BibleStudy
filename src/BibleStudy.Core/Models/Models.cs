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

/// <summary>Text fragment of a verse; WordsOfJesus marks red-letter text.</summary>
public sealed record Segment(string Text, bool WordsOfJesus = false, IReadOnlyList<string>? Strongs = null);

public sealed record Verse(int Number, IReadOnlyList<Segment> Segments)
{
    public string Text => string.Concat(Segments.Select(s => s.Text));
}

/// <summary>One entry of Strong's dictionary (original-language word study).</summary>
public sealed record StrongsEntry(string Id, string Lemma, string Translit, string Pronunciation, string Definition, string KjvUsage, string Derivation);

/// <summary>Location of a verse, independent of translation (USFM book id), so notes follow across translations.</summary>
public readonly record struct VerseRef(string Book, int Chapter, int Verse);

/// <summary>Plain verse text for indexing.</summary>
public sealed record VerseText(string BookId, int Chapter, int Number, string Text);

public sealed record Chapter(string TranslationId, string BookId, int Number, IReadOnlyList<Verse> Verses);
