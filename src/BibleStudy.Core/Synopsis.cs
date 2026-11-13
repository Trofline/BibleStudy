using System.Globalization;
using System.Text;

namespace BibleStudy.Core;

/// <summary>A piece of a gospel: from chapter:verse to chapter:verse.</summary>
public sealed record GospelRange(int Chapter, int From, int ToChapter, int To)
{
    public bool Contains(int chapter, int verse) =>
        (chapter > Chapter || (chapter == Chapter && verse >= From)) && (chapter < ToChapter || (chapter == ToChapter && verse <= To));

    public override string ToString() => ToChapter == Chapter ? (To > From ? $"{Chapter}:{From}-{To}" : $"{Chapter}:{From}") : $"{Chapter}:{From}-{ToChapter}:{To}";

    /// <summary>"8:31-9:1", "3:13-17", "13:33".</summary>
    public static GospelRange? Parse(string s)
    {
        var parts = s.Trim().Split('-', 2);
        var a = parts[0].Split(':');
        if (a.Length != 2 || !int.TryParse(a[0], out var c) || !int.TryParse(a[1], out var v)) return null;
        if (parts.Length == 1) return new(c, v, c, v);
        var b = parts[1].Split(':');
        if (b.Length == 2 && int.TryParse(b[0], out var c2) && int.TryParse(b[1], out var v2)) return new(c, v, c2, v2);
        return int.TryParse(b[0], out var e) ? new(c, v, c, e) : null;
    }
}

/// <summary>One event or saying with where each gospel tells it (empty where it does not).</summary>
public sealed record SynopsisSection(int Number, IReadOnlyList<string> Titles, IReadOnlyDictionary<string, IReadOnlyList<GospelRange>> Places)
{
    static readonly string[] Languages = ["de", "en", "es", "fr", "ru"];

    /// <summary>The title in the language of the program; English for a language the list does not have.</summary>
    public string Title(string language)
    {
        var i = Array.IndexOf(Languages, language);
        return Titles[i >= 0 && i < Titles.Count ? i : 1];
    }

    public bool Covers(string book, int chapter, int verse) => Places.TryGetValue(book, out var ranges) && ranges.Any(r => r.Contains(chapter, verse));
    public bool Covers(string book, int chapter) => Places.TryGetValue(book, out var ranges) && ranges.Any(r => chapter >= r.Chapter && chapter <= r.ToChapter);
}

/// <summary>
/// The gospels side by side (a synopsis): which event or saying stands where in Matthew, Mark, Luke and John. The list of the parallels
/// was put together for this program; it follows the usual division of the gospels into pericopes.
/// </summary>
public static class Synopsis
{
    public static readonly string[] Gospels = ["MAT", "MRK", "LUK", "JHN"];
    static IReadOnlyList<SynopsisSection>? _all;

    public static IReadOnlyList<SynopsisSection> All => _all ??= Load();

    static List<SynopsisSection> Load()
    {
        using var s = typeof(Synopsis).Assembly.GetManifestResourceStream("data/synopsis.txt")!;
        using var r = new StreamReader(s, Encoding.UTF8);
        return Parse(r.ReadToEnd());
    }

    public static List<SynopsisSection> Parse(string text)
    {
        var list = new List<SynopsisSection>();
        foreach (var line in text.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.Length == 0 || l.StartsWith('#')) continue;
            var f = l.Split('|');
            if (f.Length < 9) continue;
            var places = new Dictionary<string, IReadOnlyList<GospelRange>>();
            for (var g = 0; g < 4; g++)
            {
                var ranges = f[5 + g].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(GospelRange.Parse).OfType<GospelRange>().ToList();
                if (ranges.Count > 0) places[Gospels[g]] = ranges;
            }
            list.Add(new SynopsisSection(list.Count + 1, f[..5], places));
        }
        return list;
    }

    /// <summary>The sections that tell this verse (several where a passage belongs to more than one); the narrowest first.</summary>
    public static List<SynopsisSection> At(string book, int chapter, int verse) =>
        All.Where(s => s.Covers(book, chapter, verse)).OrderBy(s => s.Places[book].Sum(r => (r.ToChapter - r.Chapter) * 100 + r.To - r.From)).ToList();

    /// <summary>
    /// For each column the words that the other columns do not have (compared without accents and case): they show what one gospel tells
    /// differently. Short words (and, the, he …) are left out, they would only colour the text.
    /// </summary>
    public static List<HashSet<string>> OwnWords(IReadOnlyList<string> texts)
    {
        var sets = texts.Select(t => Words(t).Where(w => w.Length > 3).ToHashSet()).ToList();
        return sets.Select((set, i) => set.Where(w => !sets.Where((_, j) => j != i).Any(o => o.Contains(w))).ToHashSet()).ToList();
    }

    /// <summary>The words of a text, folded (lower case, no accents).</summary>
    public static IEnumerable<string> Words(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
            else if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            else if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    public static string Fold(string word) => string.Concat(Words(word));
}
