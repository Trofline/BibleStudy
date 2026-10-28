using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// The word data (Strong's dictionary, word glosses) exists only in English. This translates short entries into the program's language
/// with the user's own Claude Code (no API key) and remembers every result in a file, so each entry is translated once.
/// It is an AI translation of dictionary text and is marked as such where it is shown.
/// </summary>
public sealed class MeaningTranslator(string cacheDir)
{
    const int Batch = 30;

    readonly Dictionary<string, string> _cache = [];
    readonly HashSet<string> _failed = [];
    string? _loadedFor;
    string? _exe;
    bool _exeSearched;

    static readonly Dictionary<string, string> LanguageNames = new() { ["de"] = "German", ["es"] = "Spanish", ["fr"] = "French", ["ru"] = "Russian" };

    public bool Available
    {
        get
        {
            if (Loc.Code == "en") return false;
            if (!_exeSearched) { _exe = ClaudeCli.FindExecutable(); _exeSearched = true; }
            return _exe is not null;
        }
    }

    string CacheFile => Path.Combine(cacheDir, $"meanings-{Loc.Code}.json");

    void Load()
    {
        if (_loadedFor == Loc.Code) return;
        _loadedFor = Loc.Code; _cache.Clear();
        try
        {
            if (File.Exists(CacheFile) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CacheFile)) is { } d)
                foreach (var (k, v) in d) _cache[k] = v;
        }
        catch { /* a damaged cache is simply rebuilt */ }
    }

    /// <summary>The translation if there is one, otherwise the English original.</summary>
    public string Get(string english)
    {
        Load();
        return Available && _cache.TryGetValue(english, out var t) ? t : english;
    }

    public bool IsMissing(string english)
    {
        Load();
        return Available && english.Trim().Length > 0 && !_cache.ContainsKey(english) && !_failed.Contains(english);
    }

    /// <summary>Translates what is not cached yet; returns true when something new was translated.</summary>
    public async Task<bool> WarmAsync(IEnumerable<string> english, string? model, CancellationToken ct = default)
    {
        Load();
        if (!Available) return false;
        var todo = english.Select(s => s.Trim()).Where(IsMissing).Distinct().ToList();
        var any = false;
        for (var i = 0; i < todo.Count; i += Batch)
        {
            var chunk = todo.Skip(i).Take(Batch).ToList();
            var result = await TranslateAsync(chunk, model, ct);
            if (result is null) { foreach (var s in chunk) _failed.Add(s); continue; }
            for (var j = 0; j < chunk.Count; j++) _cache[chunk[j]] = result[j];
            any = true;
            try { Directory.CreateDirectory(cacheDir); await File.WriteAllTextAsync(CacheFile, JsonSerializer.Serialize(_cache), ct); } catch { /* keep going without saving */ }
        }
        return any;
    }

    async Task<List<string>?> TranslateAsync(List<string> items, string? model, CancellationToken ct)
    {
        var language = LanguageNames.GetValueOrDefault(Loc.Code, Loc.Code);
        var prompt = $"Translate each of these entries from a Greek/Hebrew Bible word dictionary from English into {language}. They are short glosses and definitions. "
                   + "Keep the meaning exact and concise, keep Strong's numbers (G123, H123), abbreviations and Greek/Hebrew words unchanged. "
                   + $"Reply with ONLY a JSON array of exactly {items.Count} strings, in the same order, nothing else.\n\n" + JsonSerializer.Serialize(items);
        var psi = new ProcessStartInfo(_exe!)
        {
            WorkingDirectory = Path.GetTempPath(), RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-p", "--output-format", "json", "--tools", "" }) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(string.IsNullOrWhiteSpace(model) ? "haiku" : model);
        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            using var kill = timeout.Token.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
            await process.StandardInput.WriteAsync(prompt);
            process.StandardInput.Close();
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            using var doc = JsonDocument.Parse(output);
            var text = doc.RootElement.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
            var start = text.IndexOf('['); var end = text.LastIndexOf(']');
            if (start < 0 || end <= start) return null;
            var list = JsonSerializer.Deserialize<List<string>>(text[start..(end + 1)]);
            return list is not null && list.Count == items.Count ? list : null;
        }
        catch { return null; }
    }
}
