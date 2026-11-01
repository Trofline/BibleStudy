using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace BibleStudy.Core;

public sealed record UpdateInfo(string Version, string Notes, string AssetName, string DownloadUrl, string? Sha256, long Size, string PageUrl);

public sealed record ReleaseNote(string Version, string Notes, DateTime? Date, string PageUrl, UpdateInfo? Setup = null, UpdateInfo? Portable = null);

/// <summary>Looks for a newer release on GitHub and downloads it (installer for installed copies, zip for portable ones).</summary>
public sealed class Updater(HttpClient http)
{
    public const string Repo = "trofline/BibleStudy";

    public static Version? Parse(string v)
    {
        v = v.Trim().TrimStart('v', 'V');
        var dash = v.IndexOfAny(['-', '+']);
        if (dash >= 0) v = v[..dash];
        return Version.TryParse(v, out var parsed) ? parsed : null;
    }

    /// <summary>The newest release if it is newer than the running version, otherwise null.</summary>
    public async Task<UpdateInfo?> CheckAsync(string currentVersion, bool portable, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("BibleStudy", currentVersion));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;   // no release yet
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (Parse(tag) is not { } latest || Parse(currentVersion) is not { } current || latest <= current) return null;
        var wanted = portable ? "Portable" : "Setup";
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (!name.Contains(wanted, StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sha256")) continue;
            string? sha = null;
            if (a.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:")) sha = digest["sha256:".Length..];
            return new UpdateInfo(latest.ToString(), root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "", name,
                a.GetProperty("browser_download_url").GetString() ?? "", sha, a.GetProperty("size").GetInt64(), root.GetProperty("html_url").GetString() ?? "");
        }
        return null;
    }

    /// <summary>All published releases, newest first (for the patch notes). Cached in the data folder, so they can be read offline.</summary>
    public async Task<List<ReleaseNote>> ListAsync(string currentVersion, CancellationToken ct = default)
    {
        var cache = Path.Combine(AppPaths.Root, "releases-cache.json");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases?per_page=40");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("BibleStudy", currentVersion));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            var list = ParseReleases(json);
            try { Directory.CreateDirectory(AppPaths.Root); File.WriteAllText(cache, JsonSerializer.Serialize(list)); } catch (IOException) { /* the cache is optional */ }
            return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (File.Exists(cache)) { try { return JsonSerializer.Deserialize<List<ReleaseNote>>(File.ReadAllText(cache)) ?? []; } catch (JsonException) { } }
            throw;
        }
    }

    /// <summary>The installer ("Setup") or the portable zip of a release, with the checksum GitHub gives; null when the release has none.</summary>
    static UpdateInfo? PackageOf(JsonElement release, string version, string notes, string page, string wanted)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (!name.Contains(wanted, StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sha256")) continue;
            string? sha = null;
            if (a.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:")) sha = digest["sha256:".Length..];
            return new UpdateInfo(version, notes, name, a.GetProperty("browser_download_url").GetString() ?? "", sha, a.GetProperty("size").GetInt64(), page);
        }
        return null;
    }

    public static List<ReleaseNote> ParseReleases(string json)
    {
        var result = new List<ReleaseNote>();
        using var doc = JsonDocument.Parse(json);
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
            if (r.TryGetProperty("prerelease", out var p) && p.GetBoolean()) continue;
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            if (Parse(tag) is not { } v) continue;
            DateTime? date = r.TryGetProperty("published_at", out var pa) && pa.GetString() is { } s && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt.ToLocalTime() : null;
            var body = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var page = r.GetProperty("html_url").GetString() ?? "";
            result.Add(new ReleaseNote(v.ToString(), body, date, page, PackageOf(r, v.ToString(), body, page, "Setup"), PackageOf(r, v.ToString(), body, page, "Portable")));
        }
        return result.OrderByDescending(x => Parse(x.Version)).ToList();
    }

    /// <summary>
    /// The notes are written twice, German ("## Neu in …") and English ("## New in …"). Returns the part for the language
    /// (German for "de", English for all others), without the list of files at the end; the whole text when it is not in that form.
    /// </summary>
    public static string NotesFor(string notes, string lang)
    {
        var sections = new List<(string Head, List<string> Lines)>();
        foreach (var line in notes.Replace("\r", "").Split('\n'))
        {
            if (line.StartsWith("## ")) sections.Add((line[3..].Trim(), []));
            else if (sections.Count > 0) sections[^1].Lines.Add(line);
        }
        var wanted = lang == "de" ? "Neu in" : "New in";
        var hit = sections.FirstOrDefault(s => s.Head.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));
        if (hit.Lines is null) return notes.Trim();
        return string.Join("\n", hit.Lines.TakeWhile(l => !l.StartsWith("Dateien:", StringComparison.OrdinalIgnoreCase) && !l.StartsWith("Files:", StringComparison.OrdinalIgnoreCase))).Trim();
    }

    /// <summary>Downloads the asset into a temp folder and checks size and SHA-256 (when GitHub provides it).</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BibleStudy-Update-" + info.Version);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, info.AssetName);
        using var req = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("BibleStudy", AppPaths.Version));
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? info.Size;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(path))
        {
            var buf = new byte[81920]; long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0) { await dst.WriteAsync(buf.AsMemory(0, n), ct); done += n; if (total > 0) progress?.Report((double)done / total); }
        }
        if (info.Size > 0 && new FileInfo(path).Length != info.Size) { File.Delete(path); throw new InvalidDataException(L("Der Download ist unvollständig.")); }
        if (info.Sha256 is { } expected)
        {
            await using var fs = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) { fs.Close(); File.Delete(path); throw new InvalidDataException(L("Die Prüfsumme der Datei stimmt nicht – Update abgebrochen.")); }
        }
        return path;
    }
}
