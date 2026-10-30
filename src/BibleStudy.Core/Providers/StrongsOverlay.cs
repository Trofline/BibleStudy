using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Providers;

/// <summary>Which translations carry Strong's numbers from their source (checked against the server's word files).</summary>
public static class StrongsSupport
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ENGWEBP", "eng_abt", "eng_asv", "eng_bbe", "eng_kja", "eng_f35", "eng_dra", "eng_lsv", "eng_glw", "eng_msb", "eng_fbv", "eng_t4t", "eng_net",
        "eng_dby", "eng_webc", "eng_webpb", "eng_wmu", "eng_webu", "eng_wmb", "eng_web", "eng_tce", "eng_weu", "eng_kjv", "eng_rv5",
        "deu_l12", "fra_ncl", "fra_ost", "spa_r09", "spa_v2p", "spa_pdt", "spa_bll", "spa_blm", "spa_vbl", "spa_rvg", "spa_bes",
        "grc_byz", "grc_f35", "grc_gtr", "grc_tis", "grc_srg", "grc_sr", "grc_sbl", "grc_mtk", "grc_tcg", "hbo_wlc",
        "emi_wbt", "gvs_wbt", "hke_seed", "hlt_mcs", "krx_wbt", "liy_seed", "mpp_wbt", "myk_wbt", "mya_jvb", "onn_wbt", "pma_wbt", "ver_seed", "wol_rwbt", "wol_mbs",
    };
}

public sealed class OverlayVerse
{
    /// <summary>Short hash of the verse text the numbers were made for; a changed text is not tagged (the numbers would sit on wrong words).</summary>
    public string H { get; set; } = "";
    /// <summary>Word number in the verse (counted over the words of the text, from 0) → Strong's numbers.</summary>
    public Dictionary<int, string[]> T { get; set; } = [];
}

public sealed class OverlayFile
{
    public string Reference { get; set; } = "";
    public string Created { get; set; } = "";
    public Dictionary<string, OverlayVerse> Verses { get; set; } = [];
}

/// <summary>
/// Strong's numbers that were added to a translation afterwards (by a plugin) and are only an estimate. They are kept as a small file per
/// translation next to the program's data: only word positions and numbers, not the text.
/// </summary>
public sealed class StrongsOverlay(string dir)
{
    readonly Dictionary<string, OverlayFile> _loaded = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    readonly object _lock = new();

    static readonly Regex Word = new(@"[\p{L}\p{M}][\p{L}\p{M}'’\-]*", RegexOptions.Compiled);

    /// <summary>The words of a text (offset, length); the same counting is used when the numbers are made and when they are applied.</summary>
    public static IReadOnlyList<(int Start, int Length)> Words(string text) => Word.Matches(text).Select(m => (m.Index, m.Length)).ToList();

    public static string Hash(string verseText) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(verseText.Trim())), 0, 6);

    static string Key(string book, int chapter, int verse) => $"{book}|{chapter}|{verse}";

    string PathOf(string translationId) => Path.Combine(dir, SafeName(translationId) + ".json");

    static string SafeName(string id) => string.Concat(id.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));

    OverlayFile? Load(string translationId)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue(translationId, out var f)) return f;
            var path = PathOf(translationId);
            if (!File.Exists(path)) return null;
            try { f = JsonSerializer.Deserialize<OverlayFile>(File.ReadAllBytes(path)); }
            catch (Exception e) when (e is JsonException or IOException) { f = null; }   // a damaged file: as if there were none
            if (f is not null) _loaded[translationId] = f;
            return f;
        }
    }

    public bool Has(string translationId) => Load(translationId) is { Verses.Count: > 0 };

    /// <summary>Translations that have estimated numbers, with the translation they were taken over from.</summary>
    public IReadOnlyList<(string Id, string Reference, string Created)> List()
    {
        if (!Directory.Exists(dir)) return [];
        var result = new List<(string, string, string)>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (Load(id) is { } f && f.Verses.Count > 0) result.Add((id, f.Reference, f.Created));
        }
        return result;
    }

    /// <summary>Stores the numbers of one verse (word position → numbers) for the given verse text.</summary>
    public void Set(string translationId, string reference, string book, int chapter, int verse, string verseText, IReadOnlyDictionary<int, string[]> tags)
    {
        lock (_lock)
        {
            var f = Load(translationId) ?? (_loaded[translationId] = new OverlayFile { Created = DateTime.UtcNow.ToString("o") });
            f.Reference = reference;
            var key = Key(book, chapter, verse);
            if (tags.Count == 0) f.Verses.Remove(key);
            else f.Verses[key] = new OverlayVerse { H = Hash(verseText), T = tags.ToDictionary(t => t.Key, t => t.Value) };
            _dirty.Add(translationId);
        }
    }

    /// <summary>Writes the changes of a translation to disk (all at once, so a long run does not write the file again and again).</summary>
    public void Commit(string translationId)
    {
        lock (_lock)
        {
            if (!_dirty.Remove(translationId) || !_loaded.TryGetValue(translationId, out var f)) return;
            Directory.CreateDirectory(dir);
            var path = PathOf(translationId);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(f));
            File.Move(tmp, path, overwrite: true);
        }
    }

    public void CommitAll() { string[] ids; lock (_lock) ids = [.. _dirty]; foreach (var id in ids) Commit(id); }

    public void Remove(string translationId)
    {
        lock (_lock)
        {
            _loaded.Remove(translationId); _dirty.Remove(translationId);
            try { File.Delete(PathOf(translationId)); } catch (IOException) { /* in use: stays until next time */ }
        }
    }

    /// <summary>Gives the words of a chapter their estimated numbers. A chapter that already has numbers from its source stays as it is.</summary>
    public Chapter Apply(Chapter chapter)
    {
        if (Load(chapter.TranslationId) is not { } f || chapter.Verses.Any(v => v.Segments.Any(s => s.Strongs is { Count: > 0 }))) return chapter;
        var verses = new List<Verse>(chapter.Verses.Count);
        foreach (var v in chapter.Verses)
        {
            if (!f.Verses.TryGetValue(Key(chapter.BookId, chapter.Number, v.Number), out var ov) || ov.H != Hash(v.Text)) { verses.Add(v); continue; }
            var segments = new List<Segment>();
            var index = 0;
            foreach (var s in v.Segments)
            {
                if (s.NoteId is not null) { segments.Add(s); continue; }
                var pos = 0;
                foreach (var (start, length) in Words(s.Text))
                {
                    var tagged = ov.T.TryGetValue(index++, out var numbers);
                    if (!tagged) continue;
                    if (start > pos) segments.Add(s with { Text = s.Text[pos..start] });
                    segments.Add(s with { Text = s.Text.Substring(start, length), Strongs = numbers, Estimated = true });
                    pos = start + length;
                }
                if (pos < s.Text.Length) segments.Add(s with { Text = s.Text[pos..] });
            }
            verses.Add(v with { Segments = segments });
        }
        return chapter with { Verses = verses };
    }

    /// <summary>The words of a verse with their numbers, as a plugin needs them to compare translations.</summary>
    public static IReadOnlyList<(string Word, string[]? Strongs)> TokensOf(Verse verse)
    {
        var tokens = new List<(string, string[]?)>();
        foreach (var s in verse.Segments)
        {
            if (s.NoteId is not null) continue;
            var numbers = s.Strongs is { Count: > 0 } ? s.Strongs.ToArray() : null;
            foreach (var (start, length) in Words(s.Text)) tokens.Add((s.Text.Substring(start, length), numbers));
        }
        return tokens;
    }
}
