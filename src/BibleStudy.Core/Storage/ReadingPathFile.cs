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

    public const int MaxSentPaths = 200, MaxSentSteps = 2_000, MaxSentMarks = 20_000;

    /// <summary>
    /// Paths that came from someone else (sent while studying together): only books that exist, chapters and verses in a sane range, a color
    /// that is a color, names and texts cut to size, and not more paths, steps and marks than a person could have made. Nothing that fails is kept.
    /// </summary>
    public static List<Imported> Checked(IEnumerable<Imported> found, Func<string, bool> isBook)
    {
        static string Cut(string? s, int max) => s is null ? "" : s.Length > max ? s[..max] : s;
        static bool IsColor(string? c) => c is { Length: 7 } && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit);
        var marks = 0;
        var list = new List<Imported>();
        foreach (var f in found.Take(MaxSentPaths))
        {
            var name = Cut(f.Path.Name.Trim(), 100);
            var steps = f.Path.Steps.Where(s => isBook(s.Book) && s.Chapter is >= 1 and <= 200 && s.From is >= 0 and <= 200 && s.To is >= 0 and <= 200)
                .Take(MaxSentSteps).ToList();
            if (name.Length == 0 || steps.Count == 0) continue;
            var kept = new List<Mark>();
            foreach (var m in f.Marks)
            {
                if (marks >= MaxSentMarks) break;
                if (!isBook(m.Ref.Book) || m.Ref.Chapter is < 1 or > 200 || m.Ref.Verse is < 1 or > 200 || !IsColor(m.Color)) continue;
                kept.Add(m with
                {
                    TranslationId = Cut(m.TranslationId, 40), Text = Cut(m.Text, 2_000), Note = Cut(m.Note, 5_000),
                    EndVerse = m.EndVerse > m.Ref.Verse && m.EndVerse <= 200 ? m.EndVerse : 0,
                    Start = Math.Max(-1, m.Start), End = Math.Max(-1, m.End),
                });
                marks++;
            }
            list.Add(new Imported(new ReadingPath(0, name, IsColor(f.Path.Color) ? f.Path.Color : "", steps), kept));
        }
        return list;
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
