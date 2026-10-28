using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace BibleStudy.Core;

public sealed record UpdateInfo(string Version, string Notes, string AssetName, string DownloadUrl, string? Sha256, long Size, string PageUrl);

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
