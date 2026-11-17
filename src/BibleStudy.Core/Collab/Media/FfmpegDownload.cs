using System.IO.Compression;

namespace BibleStudy.Core.Collab.Media;

/// <summary>
/// FFmpeg for the camera, offered in the download manager (Windows): the "essentials" build of gyan.dev, the build the FFmpeg project itself
/// links for Windows. Only ffmpeg.exe is kept. On Mac and Linux FFmpeg comes from the system's own package tools (Homebrew, apt …).
/// </summary>
public static class FfmpegDownload
{
    public const string WindowsUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    public const int SizeMb = 110;

    public static string Dir(string root) => Path.Combine(root, "tools", "ffmpeg");
    public static bool IsInstalled(string root) => File.Exists(Path.Combine(Dir(root), OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"));
    public static void Remove(string root) { try { if (Directory.Exists(Dir(root))) Directory.Delete(Dir(root), true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    public static async Task EnsureAsync(HttpClient http, string root, IProgress<string>? progress, CancellationToken ct)
    {
        if (IsInstalled(root)) return;
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(OperatingSystem.IsMacOS() ? "brew install ffmpeg" : "sudo apt install ffmpeg");
        var dir = Dir(root);
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "ffmpeg.zip.part");
        try
        {
            using (var response = await http.GetAsync(WindowsUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(zip);
                var buffer = new byte[256 * 1024]; long done = 0; var last = DateTime.MinValue;
                for (int n; (n = await input.ReadAsync(buffer, ct)) > 0;)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    done += n;
                    if ((DateTime.UtcNow - last).TotalMilliseconds > 300) { last = DateTime.UtcNow; progress?.Report(total > 0 ? $"{done * 100 / total} %" : $"{done / 1048576} MB"); }
                }
            }
            using (var archive = ZipFile.OpenRead(zip))
            {
                var exe = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("ffmpeg.exe");
                exe.ExtractToFile(Path.Combine(dir, "ffmpeg.exe"), true);
            }
        }
        finally { try { File.Delete(zip); } catch (IOException) { } }
    }
}
