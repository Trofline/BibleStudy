using System.Text.Json;
using System.Text.Json.Serialization;
using BibleStudy.Core.Models;

namespace BibleStudy.Core;

/// <summary>The verse of the day: a fixed list of well-known verses, one per day of the year (the same on every device).</summary>
public static class DailyVerses
{
    public static readonly VerseRef[] All =
    [
        new("JHN", 3, 16), new("PSA", 23, 1), new("ROM", 8, 28), new("PHP", 4, 13), new("PRO", 3, 5), new("ISA", 41, 10), new("JER", 29, 11), new("MAT", 11, 28), new("PSA", 46, 1), new("JOS", 1, 9),
        new("ROM", 12, 2), new("GAL", 2, 20), new("2TI", 3, 16), new("HEB", 11, 1), new("PSA", 119, 105), new("MAT", 6, 33), new("1CO", 13, 4), new("EPH", 2, 8), new("JHN", 14, 6), new("ROM", 5, 8),
        new("PSA", 27, 1), new("ISA", 40, 31), new("MAT", 28, 19), new("1JN", 1, 9), new("PRO", 22, 6), new("PSA", 91, 1), new("JAS", 1, 5), new("1PE", 5, 7), new("REV", 3, 20), new("DEU", 31, 6),
        new("MIC", 6, 8), new("LAM", 3, 22), new("JHN", 1, 1), new("GEN", 1, 1), new("PSA", 1, 1), new("ECC", 3, 1), new("ROM", 6, 23), new("COL", 3, 23), new("1TH", 5, 16), new("JHN", 15, 5), new("REV", 21, 4),
    ];

    public static int IndexOf(DateTime day) => day.DayOfYear % All.Length;

    public static VerseRef Of(DateTime day) => All[IndexOf(day)];
}

/// <summary>
/// What the "Today" widget on the home screen of a phone shows. The program writes it (it knows the translation, the names of the books
/// and the reading plan); the widget only reads it, so it needs nothing of the program to draw itself. The verses of the day are all in it,
/// with their text: the widget picks the one of the day itself and stays right on the days the program is not opened.
/// </summary>
public sealed class TodayCard
{
    public const string FileName = "widget.json";

    public sealed record Verse(string Link, string Label, string Text);

    /// <summary>The reading plan of one day: what is to read (or "done"), the plan and its day, and where a tap leads.</summary>
    public sealed record Plan(string Day, string Title, string Hint, string Link);

    public string Translation { get; set; } = "";
    public string TranslationName { get; set; } = "";
    public List<Verse> Verses { get; set; } = [];
    public Plan? Reading { get; set; }
    /// <summary>The words of the widget in the language of the program: "verse", "plan", "empty".</summary>
    public Dictionary<string, string> Words { get; set; } = [];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static TodayCard? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<TodayCard>(json, Json); }
        catch (JsonException) { return null; }
    }

    public static TodayCard? Load(string folder)
    {
        try { var path = Path.Combine(folder, FileName); return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Writes the file only when something changed (true then): the widget is redrawn only for a change.</summary>
    public bool Save(string folder)
    {
        var json = ToJson();
        var path = Path.Combine(folder, FileName);
        try
        {
            if (File.Exists(path) && File.ReadAllText(path) == json) return false;
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The verse of <paramref name="day"/>; null when the program has not written the verses yet.</summary>
    public Verse? VerseOf(DateTime day) => Verses.Count == DailyVerses.All.Length ? Verses[DailyVerses.IndexOf(day)] : Verses.FirstOrDefault();

    /// <summary>The reading plan only on the day it was written for: a day later the program has to say what is next.</summary>
    public Plan? PlanOf(DateTime day) => Reading is { } p && p.Day == day.ToString("yyyy-MM-dd") ? p : null;

    public string Word(string key, string fallback) => Words.TryGetValue(key, out var w) && w.Length > 0 ? w : fallback;
}
