using System.Text.Json;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Providers;

/// <summary>
/// Strong's Greek and Hebrew dictionaries from the Open Scriptures "strongs" project
/// (https://github.com/openscriptures/strongs, CC BY-SA). Downloaded on first use and cached; not bundled.
/// </summary>
public sealed class StrongsDictionary(HttpClient http, string cacheDir)
{
    public const string Attribution = "Strong's Dictionary © Open Scriptures, CC BY-SA (github.com/openscriptures/strongs)";
    const string RawBase = "https://raw.githubusercontent.com/openscriptures/strongs/master/";

    readonly Dictionary<char, Task<Dictionary<int, StrongsEntry>>> _loaded = [];

    /// <summary>Looks up ids like "G0025" or "H7225"; null if unknown.</summary>
    public async Task<StrongsEntry?> LookupAsync(string id, CancellationToken ct = default)
    {
        if (id.Length < 2 || !int.TryParse(id.AsSpan(1), out var number)) return null;
        var lang = char.ToUpperInvariant(id[0]);
        if (lang is not ('G' or 'H')) return null;
        Task<Dictionary<int, StrongsEntry>> task;
        lock (_loaded) task = _loaded.TryGetValue(lang, out var t) ? t : _loaded[lang] = LoadAsync(lang, ct);
        try { return (await task).GetValueOrDefault(number); }
        catch { lock (_loaded) _loaded.Remove(lang); throw; }
    }

    /// <summary>
    /// Finds entries by number ("G3056", "h1", "3056") or by text: Hebrew/Greek lemma, transliteration or an English word of the
    /// definition. Accents and vowel points are ignored. Best matches first.
    /// </summary>
    public async Task<IReadOnlyList<StrongsEntry>> SearchAsync(string query, int limit = 60, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        var number = System.Text.RegularExpressions.Regex.Match(query, @"^([GgHh])?\s*0*(\d{1,4})$");
        if (number.Success)
        {
            var letters = number.Groups[1].Success ? [char.ToUpperInvariant(number.Groups[1].Value[0])] : new[] { 'G', 'H' };
            var found = new List<StrongsEntry>();
            foreach (var l in letters) if (await LookupAsync($"{l}{number.Groups[2].Value}", ct) is { } e) found.Add(e);
            return found;
        }
        var needle = Fold(query);
        var scored = new List<(int Score, StrongsEntry Entry)>();
        foreach (var lang in new[] { 'G', 'H' })
        {
            Task<Dictionary<int, StrongsEntry>> task;
            lock (_loaded) task = _loaded.TryGetValue(lang, out var t) ? t : _loaded[lang] = LoadAsync(lang, ct);
            foreach (var e in (await task).Values)
            {
                int score;
                if (Fold(e.Lemma) == needle || Fold(e.Translit) == needle) score = 0;
                else if (Fold(e.Lemma).StartsWith(needle) || Fold(e.Translit).StartsWith(needle)) score = 1;
                else if (WordIn(e.KjvUsage, needle)) score = 2;
                else if (WordIn(e.Definition, needle)) score = 3;
                else if (Fold(e.Lemma).Contains(needle) || Fold(e.Translit).Contains(needle)) score = 4;
                else continue;
                scored.Add((score, e));
            }
        }
        return scored.OrderBy(s => s.Score).ThenBy(s => s.Entry.Id.Length).ThenBy(s => s.Entry.Id).Take(limit).Select(s => s.Entry).ToList();
    }

    static bool WordIn(string text, string foldedNeedle) => text.Length > 0 && Fold(text).Contains(foldedNeedle);

    /// <summary>Lower case without accents, breathing marks or vowel points, so "λογος" finds "λόγος".</summary>
    static string Fold(string s)
    {
        var normalized = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    async Task<Dictionary<int, StrongsEntry>> LoadAsync(char lang, CancellationToken ct)
    {
        var (path, name) = lang == 'G' ? ("greek/strongs-greek-dictionary.js", "greek.js") : ("hebrew/strongs-hebrew-dictionary.js", "hebrew.js");
        var file = Path.Combine(cacheDir, "strongs", name);
        string js;
        if (File.Exists(file)) js = await File.ReadAllTextAsync(file, ct);
        else
        {
            js = await http.GetStringAsync(RawBase + path, ct);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, js, ct);
        }
        return await Task.Run(() => Parse(js, lang), ct);
    }

    /// <summary>The source files are JavaScript assigning one JSON object; cut the object out.</summary>
    public static Dictionary<int, StrongsEntry> Parse(string js, char lang)
    {
        var assign = js.IndexOf("Dictionary", StringComparison.Ordinal);
        var start = js.IndexOf('{', js.IndexOf('=', assign));
        var exports = js.LastIndexOf("module.exports", StringComparison.Ordinal);
        var end = js.LastIndexOf('}', exports < 0 ? js.Length - 1 : exports);
        using var doc = JsonDocument.Parse(js[start..(end + 1)], new JsonDocumentOptions { AllowTrailingCommas = true });
        var result = new Dictionary<int, StrongsEntry>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(p.Name.AsSpan(1), out var n)) continue;
            var e = p.Value;
            result[n] = new StrongsEntry(
                $"{lang}{n}", Str(e, "lemma"), Str(e, "translit") is { Length: > 0 } t ? t : Str(e, "xlit"),
                Str(e, "pron"), Str(e, "strongs_def"), Str(e, "kjv_def"), Str(e, "derivation"));
        }
        return result;
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
}
