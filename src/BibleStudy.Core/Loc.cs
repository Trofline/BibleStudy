using System.Globalization;
using System.Text.Json;

namespace BibleStudy.Core;

/// <summary>
/// UI language. The German source text is the key: <c>L("Suchen")</c> returns the text in the chosen language, or the German original
/// when there is no translation (a test makes sure that never happens for the shipped languages). Languages are flat JSON files
/// (Lang\*.json, embedded; the same files in the data folder's "lang" folder add or override languages). The language is chosen at
/// start, so strings can be looked up whenever they are needed.
/// </summary>
public static class Loc
{
    public sealed record Language(string Code, string Name);

    static Dictionary<string, string> _map = [];

    public static string Code { get; private set; } = "de";

    /// <summary>The default for new installations; the source text itself is German.</summary>
    public const string DefaultCode = "en";

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Texts asked for that had no translation (for tests and the leak scanner).</summary>
    public static HashSet<string> Missing { get; } = [];

    static IReadOnlyList<Language>? _languages;
    public static IReadOnlyList<Language> Languages => _languages ??= Discover();

    static Dictionary<string, Dictionary<string, string>> LoadAll()
    {
        var all = new Dictionary<string, Dictionary<string, string>>();
        void Read(string code, string json)
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            if (!all.TryGetValue(code, out var existing)) all[code] = existing = [];
            foreach (var (k, v) in d) existing[k] = v;
        }
        var asm = typeof(Loc).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("lang/") && n.EndsWith(".json")))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            Read(Path.GetFileNameWithoutExtension(name), r.ReadToEnd());
        }
        try
        {
            var dir = Path.Combine(AppPaths.Root, "lang");
            if (Directory.Exists(dir))
                foreach (var f in Directory.EnumerateFiles(dir, "*.json")) Read(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f));
        }
        catch { /* a broken user file must never stop the program */ }
        return all;
    }

    static List<Language> Discover() =>
        LoadAll().Select(p => new Language(p.Key, p.Value.GetValueOrDefault("@name", p.Key))).OrderBy(l => l.Code == DefaultCode ? 0 : l.Code == "de" ? 1 : 2).ThenBy(l => l.Name).ToList();

    /// <summary>All translations of one language (without the "@" entries); used by tests and the settings.</summary>
    public static IReadOnlyDictionary<string, string> Texts(string code) =>
        LoadAll().TryGetValue(code, out var d) ? d.Where(p => !p.Key.StartsWith('@')).ToDictionary(p => p.Key, p => p.Value) : new Dictionary<string, string>();

    public static void Use(string? code)
    {
        code = string.IsNullOrWhiteSpace(code) ? DefaultCode : code;
        var all = LoadAll();
        if (!all.ContainsKey(code)) code = DefaultCode;
        Code = code;
        _map = code == "de" ? [] : all.TryGetValue(code, out var d) ? d.Where(p => !p.Key.StartsWith('@')).ToDictionary(p => p.Key, p => p.Value) : [];
        try { Culture = CultureInfo.GetCultureInfo(code switch { "en" => "en-US", "de" => "de-DE", "es" => "es-ES", "fr" => "fr-FR", _ => code }); }
        catch (CultureNotFoundException) { Culture = CultureInfo.InvariantCulture; }
    }

    public static string L(string de)
    {
        if (Code == "de" || de.Length == 0) return de;
        if (_map.TryGetValue(de, out var t) && t.Length > 0) return t;
        lock (Missing) Missing.Add(de);
        return de;
    }

    /// <summary>Marks a text that is translated later (at the place where it is shown): returns it unchanged, but the text is registered as a translation key.</summary>
    public static string K(string de) => de;

    /// <summary>Translated text with {0}, {1} … filled in (numbers and dates follow the language).</summary>
    public static string L(string de, params object?[] args) => string.Format(Culture, L(de), args);
}
