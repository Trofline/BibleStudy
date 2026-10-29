using System.Text.Json;
using BibleStudy.Core.Models;
using BibleStudy.Core.Import;
using BibleStudy.Core.Providers;

namespace BibleStudy.Core.Plugins;

public interface IPluginSource
{
    IEnumerable<PluginInfo> Allowed();
    PluginClient? ClientFor(string pluginId);
}

/// <summary>
/// Bible translations that allowed plugins deliver (ids "plugin:&lt;plugin&gt;:&lt;translation&gt;"). A plugin answers chapter by chapter;
/// nothing is stored by the program, only kept in memory while it runs. Translations of plugins count as private (never sent to others).
/// </summary>
public sealed class PluginBibleProvider(IPluginSource source) : IBibleProvider
{
    public const string Prefix = "plugin:";
    public string Name => L("Plugins");

    public static bool Owns(string translationId) => translationId.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>"plugin:abc:xyz" → ("abc", "xyz").</summary>
    public static (string Plugin, string Translation)? Split(string id)
    {
        if (!Owns(id)) return null;
        var p = id[Prefix.Length..].Split(':', 2);
        return p.Length == 2 && p[0].Length > 0 && p[1].Length > 0 ? (p[0], p[1]) : null;
    }

    readonly Dictionary<string, Chapter> _chapters = [];
    readonly Dictionary<string, IReadOnlyList<BookInfo>> _books = [];

    /// <summary>Why a plugin did not answer last time (plugin id → message), for the settings page.</summary>
    public Dictionary<string, string> Problems { get; } = [];

    static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    PluginClient ClientOf(string pluginId) => source.ClientFor(pluginId) ?? throw new PluginException(L("Das Plugin „{0}“ ist nicht erlaubt oder nicht vorhanden.", pluginId));

    public async Task<IReadOnlyList<TranslationInfo>> GetTranslationsAsync(CancellationToken ct = default)
    {
        var all = new List<TranslationInfo>();
        foreach (var plugin in source.Allowed().Where(p => p.Manifest.ProvidesBibles))
        {
            var pid = plugin.Manifest.Id;
            try
            {
                var result = await ClientOf(pid).CallAsync("translations", null, ct);
                if (result.ValueKind != JsonValueKind.Array) throw new PluginException(L("Das Plugin hat keine Liste von Übersetzungen geliefert."));
                foreach (var t in result.EnumerateArray())
                {
                    var tid = Str(t, "id");
                    if (tid.Length == 0 || Str(t, "name") is not { Length: > 0 } name) continue;
                    var shortName = Str(t, "shortName") is { Length: > 0 } s ? s : name.Length > 10 ? name[..10] : name;
                    var lang = Str(t, "language") is { Length: > 0 } l ? l : "und";
                    var langName = Str(t, "languageName") is { Length: > 0 } ln ? ln : lang;
                    var attribution = Str(t, "attribution");
                    var notice = L("Plugin {0}", plugin.Manifest.Name) + (attribution.Length > 0 ? " · " + attribution : "");
                    all.Add(new TranslationInfo($"{Prefix}{pid}:{tid}", name, name, shortName, lang, langName, Str(t, "textDirection") == "rtl" ? "rtl" : "ltr",
                        Str(t, "licenseUrl") is { Length: > 0 } u ? u : null, Str(t, "website") is { Length: > 0 } w ? w : null,
                        new LicenseInfo(notice, "-", "-", false, false), 66));
                }
                Problems.Remove(pid);
            }
            catch (Exception ex) when (ex is PluginException or OperationCanceledException or JsonException or IOException)
            {
                Problems[pid] = ex.Message;   // one broken plugin must not hide the others (or the rest of the program)
            }
        }
        return all;
    }

    (PluginClient Client, string Translation, string Plugin) Route(string translationId)
    {
        var (plugin, translation) = Split(translationId) ?? throw new PluginException(L("Unbekannte Plugin-Übersetzung."));
        return (ClientOf(plugin), translation, plugin);
    }

    public async Task<IReadOnlyList<BookInfo>> GetBooksAsync(string translationId, CancellationToken ct = default)
    {
        if (_books.TryGetValue(translationId, out var cached)) return cached;
        var (client, translation, _) = Route(translationId);
        var result = await client.CallAsync("books", new { translation }, ct);
        if (result.ValueKind != JsonValueKind.Array) throw new PluginException(L("Das Plugin hat keine Liste von Büchern geliefert."));
        var books = new List<BookInfo>();
        foreach (var b in result.EnumerateArray())
        {
            var id = Str(b, "id").ToUpperInvariant();
            var order = Array.IndexOf(BookData.UsfmIds, id);
            if (order < 0 || !b.TryGetProperty("chapters", out var n) || !n.TryGetInt32(out var chapters) || chapters < 1) continue;   // only the 66 known books (USFM ids)
            var name = Str(b, "name") is { Length: > 0 } nm ? nm : BookData.NameOf(id, "de");
            books.Add(new BookInfo(id, name, name, order + 1, chapters));
        }
        books.Sort((a, b) => a.Order.CompareTo(b.Order));
        return _books[translationId] = books;
    }

    public async Task<Chapter> GetChapterAsync(string translationId, string bookId, int chapter, CancellationToken ct = default)
    {
        var key = $"{translationId}|{bookId}|{chapter}";
        if (_chapters.TryGetValue(key, out var cached)) return cached;
        var (client, translation, _) = Route(translationId);
        var result = await client.CallAsync("chapter", new { translation, book = bookId, chapter }, ct);
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("verses", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new PluginException(L("Das Plugin hat kein Kapitel geliefert."));
        var verses = new List<Verse>();
        foreach (var v in list.EnumerateArray())
        {
            var text = Str(v, "text").Trim();
            if (!v.TryGetProperty("number", out var n) || !n.TryGetInt32(out var number) || number < 1 || text.Length == 0) continue;
            verses.Add(new Verse(number, [new Segment(text)]));
        }
        var ch = new Chapter(translationId, bookId, chapter, verses.OrderBy(v => v.Number).ToList());
        if (_chapters.Count > 300) _chapters.Clear();   // memory only; no text is kept on disk
        return _chapters[key] = ch;
    }

    /// <summary>Whole-text access (for the full-text search) is only there if the plugin offers the optional method "all".</summary>
    public async Task<IReadOnlyList<VerseText>> GetAllVersesAsync(string translationId, CancellationToken ct = default)
    {
        var (client, translation, _) = Route(translationId);
        JsonElement result;
        try { result = await client.CallAsync("all", new { translation }, ct); }
        catch (PluginException) { throw new NotSupportedException(L("Dieses Plugin liefert den Text nur Kapitel für Kapitel. Die Volltextsuche ist für diese Übersetzung nicht möglich.")); }
        if (result.ValueKind != JsonValueKind.Array) throw new NotSupportedException(L("Dieses Plugin liefert den Text nur Kapitel für Kapitel. Die Volltextsuche ist für diese Übersetzung nicht möglich."));
        var verses = new List<VerseText>();
        foreach (var v in result.EnumerateArray())
            if (Str(v, "book").ToUpperInvariant() is { Length: > 0 } book && v.TryGetProperty("chapter", out var c) && c.TryGetInt32(out var ch) && v.TryGetProperty("number", out var n) && n.TryGetInt32(out var num) && Str(v, "text") is { Length: > 0 } text)
                verses.Add(new VerseText(book, ch, num, text.Trim()));
        return verses;
    }
}
