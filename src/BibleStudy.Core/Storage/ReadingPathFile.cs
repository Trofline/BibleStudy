using System.Text.Json;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Storage;

/// <summary>
/// Reading paths in a file of their own, to give to others: the steps in their order and, if wished, the marks on those verses
/// (exactly where and in which color). Read back, the paths can be kept or only looked at once, and the marks taken over or only used to tint the verses.
/// </summary>
public static class ReadingPathFile
{
    public const string Format = "biblestudy-reading-paths", Extension = ".bspath";

    public sealed record FileMark(string Translation, string Book, int Chapter, int Verse, int EndVerse, int Start, int End, string Text, string Color, string Note);
    public sealed record FilePath(string Name, string Color, List<string> Steps, List<FileMark> Marks);
    public sealed record FileData(string Format, int Version, List<FilePath> Paths);

    /// <summary>A path read from a file, with the marks that came with it (ids 0: they belong to no vault yet).</summary>
    public sealed record Imported(ReadingPath Path, List<Mark> Marks);

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The marks of the vault that lie on a step of the path (also one that only touches it).</summary>
    public static List<Mark> MarksOn(VaultStore vault, ReadingPath path)
    {
        var found = new Dictionary<long, Mark>();
        foreach (var chapter in path.Steps.GroupBy(s => (s.Book, s.Chapter)))
            foreach (var m in vault.GetMarks(chapter.Key.Book, chapter.Key.Chapter))
                if (chapter.Any(s => m.Ref.Verse <= s.To && m.LastVerse >= s.From)) found.TryAdd(m.Id, m);
        return [.. found.Values.OrderBy(m => m.Ref.Chapter).ThenBy(m => m.Ref.Verse).ThenBy(m => m.Start)];
    }

    public static string Write(VaultStore vault, IEnumerable<ReadingPath> paths, bool withMarks) =>
        JsonSerializer.Serialize(new FileData(Format, 1, [.. paths.Select(p => new FilePath(p.Name, p.Color, [.. p.Steps.Select(s => s.ToString())],
            withMarks ? [.. MarksOn(vault, p).Select(m => new FileMark(m.TranslationId, m.Ref.Book, m.Ref.Chapter, m.Ref.Verse, m.LastVerse, m.Start, m.End, m.Text, m.Color, m.Note))] : []))]), Options);

    /// <summary>The paths of a file; a file of another kind or a broken one throws <see cref="InvalidDataException"/>.</summary>
    public static List<Imported> Read(string json)
    {
        FileData? data;
        try { data = JsonSerializer.Deserialize<FileData>(json, Options); }
        catch (JsonException ex) { throw new InvalidDataException(ex.Message, ex); }
        if (data is null || data.Format != Format || data.Paths is null) throw new InvalidDataException("not a file of reading paths");
        return [.. data.Paths.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => new Imported(
            new ReadingPath(0, p.Name.Trim(), p.Color ?? "", StudyPassage.ParseList(string.Join(";", p.Steps ?? [])).Where(s => s.From > 0).ToList()),
            [.. (p.Marks ?? []).Where(m => !string.IsNullOrEmpty(m.Book) && m.Chapter > 0 && m.Verse > 0).Select(m => new Mark(0, string.IsNullOrEmpty(m.Translation) ? Mark.AnyTranslation : m.Translation,
                new VerseRef(m.Book, m.Chapter, m.Verse), m.Start, m.End, m.Text ?? "", m.Color ?? "", m.Note ?? "", null, [], m.EndVerse > m.Verse ? m.EndVerse : 0))]))];
    }

    /// <summary>Keeps paths in the vault (a name that is taken gets a number); returns the new ids.</summary>
    public static List<long> KeepPaths(VaultStore vault, IEnumerable<ReadingPath> paths)
    {
        var names = vault.GetReadingPaths().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = new List<long>();
        foreach (var p in paths)
        {
            var name = p.Name;
            for (var n = 2; names.Contains(name); n++) name = $"{p.Name} ({n})";
            names.Add(name);
            ids.Add(vault.AddReadingPath(name, p.Color, p.Steps));
        }
        return ids;
    }

    /// <summary>Takes the marks over into the vault; one that is there already (same place, same verses) is not added twice. Returns how many were added.</summary>
    public static int KeepMarks(VaultStore vault, IEnumerable<Mark> marks)
    {
        var added = 0;
        foreach (var m in marks)
        {
            if (vault.GetMarks(m.Ref.Book, m.Ref.Chapter).Any(x => x.Ref == m.Ref && x.Start == m.Start && x.End == m.End && x.LastVerse == m.LastVerse && x.TranslationId == m.TranslationId)) continue;
            vault.AddMark(m.TranslationId, m.Ref, m.Start, m.End, m.Text, m.Color, m.Note, null, [], m.EndVerse);
            added++;
        }
        return added;
    }
}
