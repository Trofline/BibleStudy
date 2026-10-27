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
