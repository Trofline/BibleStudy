using System.Text.Json;
using System.Text.RegularExpressions;

namespace BibleStudy.Core;

/// <summary>
/// Own languages: the program writes a template (every text with its German original as the value) and an instruction for an AI;
/// the translated file is checked here and copied into the data folder's "lang" folder, where <see cref="Loc"/> finds it.
/// </summary>
public static partial class LanguageFiles
{
    public sealed record Checked(string Name, Dictionary<string, string> Texts, int Accepted, int Untranslated, int Skipped);

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();

    static IEnumerable<string> Holes(string s) => Placeholder().Matches(s).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal);

    /// <summary>All texts of the program (German original as key and as value) plus "@name" to fill in.</summary>
    public static string Template(string languageName = "")
    {
        var all = new Dictionary<string, string> { ["@name"] = languageName };
        foreach (var key in Loc.Texts(Loc.DefaultCode).Keys.OrderBy(k => k, StringComparer.Ordinal)) all[key] = key;
        return JsonSerializer.Serialize(all, Pretty);
    }

    /// <summary>The instruction for the AI that translates the template.</summary>
    public static string Instruction(string languageName) =>
        "Translate the program texts in the attached JSON file into " + languageName + ".\n" +
        "Übersetze die Programmtexte der angehängten JSON-Datei in die Sprache: " + languageName + ".\n\n" +
        "Rules / Regeln:\n" +
        "1. Keep every KEY exactly as it is (the German original). Translate only the VALUES (the part after the colon).\n" +
        "2. Set \"@name\" to the name of the language written in that language itself (for example \"Italiano\").\n" +
        "3. Keep placeholders such as {0}, {1}, {2:N0} and line breaks (\\n) exactly, and the same number of them. They may move to where the grammar needs them.\n" +
        "4. Keep symbols and arrows (▶ ✓ ⋯ … ‹ ›) and shortcut names (Ctrl+K). Keep proper names; Bible book names follow the usual names in " + languageName + ".\n" +
        "5. Use the friendly, short style of a program interface. Words on buttons stay short.\n" +
        "6. Answer with the complete JSON file only, nothing before or after, valid JSON (UTF-8).\n";

    /// <summary>
    /// Checks a translated file: a JSON object of text to text with "@name" given; entries whose placeholders differ from the original
    /// are skipped (they would break the program), unknown keys are ignored.
    /// </summary>
    public static Checked Check(string json)
    {
        Dictionary<string, string>? file;
        try { file = JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch (JsonException ex) { throw new FormatException(ex.Message); }
        if (file is null || file.Count == 0) throw new FormatException("empty");
        var name = file.GetValueOrDefault("@name", "").Trim();
        if (name.Length == 0) throw new FormatException("\"@name\"");
        var known = Loc.Texts(Loc.DefaultCode).Keys.ToHashSet();
        var texts = new Dictionary<string, string> { ["@name"] = name };
        int accepted = 0, same = 0, skipped = 0;
        foreach (var (key, value) in file)
        {
            if (key.StartsWith('@') || !known.Contains(key)) continue;
            if (string.IsNullOrWhiteSpace(value) || !Holes(key).SequenceEqual(Holes(value))) { skipped++; continue; }
            texts[key] = value;
            if (value == key) same++; else accepted++;
        }
        if (accepted == 0) throw new FormatException("nothing translated");
        return new Checked(name, texts, accepted, same, skipped);
    }

    /// <summary>"it", "pt-br" … from a file name; null when it does not look like a language code.</summary>
    public static string? CodeFrom(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).Trim().ToLowerInvariant();
        return Regex.IsMatch(stem, "^[a-z]{2,3}(-[a-z0-9]{2,4})?$") ? stem : null;
    }

    /// <summary>How many of the program's texts a language has really translated (a text equal to the German original does not count).</summary>
    public static (int Done, int Total) Progress(string code)
    {
        var known = Loc.Texts(Loc.DefaultCode).Keys;
        var have = Loc.Texts(code);
        var done = known.Count(k => have.TryGetValue(k, out var v) && v.Length > 0 && v != k);
        return (done, known.Count());
    }

    /// <summary>A template with only the texts a language still lacks (so an AI gets just that part).</summary>
    public static string MissingTemplate(string code, string languageName)
    {
        var have = Loc.Texts(code);
        var all = new Dictionary<string, string> { ["@name"] = languageName };
        foreach (var key in Loc.Texts(Loc.DefaultCode).Keys.OrderBy(k => k, StringComparer.Ordinal))
            if (!have.TryGetValue(key, out var v) || v.Length == 0 || v == key) all[key] = key;
        return JsonSerializer.Serialize(all, Pretty);
    }

    /// <summary>Adds the newly translated texts to an earlier file of the same language (the newer wins, nothing is lost).</summary>
    public static Checked Merge(string? existingJson, Checked added)
    {
        if (string.IsNullOrWhiteSpace(existingJson)) return added;
        Dictionary<string, string>? old;
        try { old = JsonSerializer.Deserialize<Dictionary<string, string>>(existingJson); } catch (JsonException) { return added; }
        if (old is null) return added;
        var texts = new Dictionary<string, string>(old);
        foreach (var (k, v) in added.Texts) if (k.StartsWith('@') || v != k || !texts.ContainsKey(k)) texts[k] = v;
        return added with { Texts = texts };
    }

    public static string Serialize(Checked c) => JsonSerializer.Serialize(c.Texts, Pretty);
}
