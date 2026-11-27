using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BibleStudy.Core.Collab.Media;

/// <summary>A camera or microphone as the system names it.</summary>
public sealed record MediaDevice(string Name, string Id, bool IsVideo);

/// <summary>
/// The camera (and on Mac/Linux the microphone and the speaker) through FFmpeg, a free program that is started on its own: it reads the
/// device and writes pictures (H.264 or JPEG) or plain sound (PCM) to us, or plays the sound we hand it. BibleStudy uses an FFmpeg that is
/// already on the computer, or the one from the download manager.
/// </summary>
public static partial class Ffmpeg
{
    /// <summary>Where the download manager puts it (set by the app).</summary>
    public static string? ToolsDir { get; set; }

    /// <summary>For the tests and the headless check: a test picture and a test tone instead of real devices.</summary>
    public static bool Fake => Environment.GetEnvironmentVariable("BIBLESTUDY_FAKEMEDIA") == "1";

    static string Exe => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>The ffmpeg program to use, or null when there is none.</summary>
    public static string? Find()
    {
        var candidates = new List<string>();
        if (ToolsDir is { } own && Directory.Exists(own))
            candidates.AddRange(Directory.EnumerateFiles(own, Exe, SearchOption.AllDirectories));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(dir.Trim('"'), Exe));
        if (!OperatingSystem.IsWindows()) candidates.AddRange(["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg"]);
        else
        {
            // winget puts it in its own folder without always adding it to PATH for running programs
            var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(winget))
                try { candidates.AddRange(Directory.EnumerateDirectories(winget, "*FFmpeg*").SelectMany(d => Directory.EnumerateFiles(d, Exe, SearchOption.AllDirectories))); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    static ProcessStartInfo Info(string exe, IEnumerable<string> args, bool input = false)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    /// <summary>The cameras and microphones (asks ffmpeg; on Linux the video devices are the /dev/video files and sound is the default of the sound server).</summary>
    public static async Task<List<MediaDevice>> ListAsync()
    {
        if (Fake) return [new("Testbild", "fake", true), new("Testton", "fake", false)];
        if (Find() is not { } exe) return [];
        if (OperatingSystem.IsLinux())
        {
            var list = Directory.Exists("/dev") ? Directory.EnumerateFiles("/dev", "video*").Order().Select(v => new MediaDevice(Path.GetFileName(v), v, true)).ToList() : [];
            list.Add(new("Standard", "default", false));
            return list;
        }
        var args = OperatingSystem.IsWindows() ? new[] { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" } : ["-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", ""];
        using var p = Process.Start(Info(exe, args))!;
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return OperatingSystem.IsWindows() ? ParseDshow(err) : ParseAvfoundation(err);
    }

    [GeneratedRegex("\"(?<name>[^\"]+)\" \\((?<kind>video|audio|none)\\)")]
    private static partial Regex DshowLine();

    /// <summary>Windows: lines like <c>"Mikrofon (USB Audio Device)" (audio)</c>.</summary>
    public static List<MediaDevice> ParseDshow(string text) =>
        DshowLine().Matches(text).Select(m => new MediaDevice(m.Groups["name"].Value, m.Groups["name"].Value, m.Groups["kind"].Value == "video"))
            .Where(d => !d.Name.StartsWith('@')).DistinctBy(d => (d.Name, d.IsVideo)).ToList();

    [GeneratedRegex(@"\[(?<i>\d+)\] (?<name>.+)$", RegexOptions.Multiline)]
    private static partial Regex AvfLine();

    /// <summary>Mac: a block "AVFoundation video devices:" then "[0] FaceTime HD Camera", then the audio devices.</summary>
    public static List<MediaDevice> ParseAvfoundation(string text)
    {
        var list = new List<MediaDevice>();
        var video = true;
        foreach (var line in text.Split('\n'))
        {
            if (line.Contains("video devices", StringComparison.OrdinalIgnoreCase)) { video = true; continue; }
            if (line.Contains("audio devices", StringComparison.OrdinalIgnoreCase)) { video = false; continue; }
            var m = AvfLine().Match(line.TrimEnd('\r'));
            if (!m.Success) continue;
            var name = m.Groups["name"].Value.Trim();
            if (video && name.StartsWith("Capture screen", StringComparison.OrdinalIgnoreCase)) continue;   // the screen is not a camera
            list.Add(new(name, m.Groups["i"].Value, video));
        }
        return list;
    }

    /// <summary>How many ways <see cref="StartCamera"/> tries to open a camera, from the most demanding to the plainest.</summary>
    public const int CameraAttempts = 4;

    /// <summary>
    /// Attempt 0 asks the camera for the size and frame rate as compressed pictures (MJPEG): that is how most webcams give HD at a full frame
    /// rate, uncompressed they often manage only a few pictures a second at that size. Attempt 1 asks for 30 a second instead (few webcams give
    /// 60), attempt 2 only for the size, attempt 3 takes what the camera offers.
    /// </summary>
    static IEnumerable<string> CameraInput(MediaDevice cam, CameraQuality q, int attempt)
    {
        if (Fake) return ["-re", "-f", "lavfi", "-i", $"testsrc=size={q.Width}x{q.Height}:rate={q.Fps}"];
        var size = $"{q.Width}x{q.Height}";
        var rate = (attempt == 0 ? q.Fps : 30).ToString(CultureInfo.InvariantCulture);
        if (OperatingSystem.IsWindows())
            return [.. new[] { "-f", "dshow", "-rtbufsize", "16M" },
                    .. attempt switch { 0 or 1 => new[] { "-vcodec", "mjpeg", "-video_size", size, "-framerate", rate }, 2 => ["-video_size", size], _ => [] },
                    "-i", "video=" + cam.Id];
        if (OperatingSystem.IsMacOS())
            return [.. new[] { "-f", "avfoundation", "-framerate", attempt == 0 ? rate : "30" }, .. attempt < 3 ? new[] { "-video_size", attempt < 2 ? size : "1280x720" } : ["-video_size", "640x480"], "-i", cam.Id + ":none"];
        return [.. new[] { "-f", "v4l2" },
                .. attempt switch { 0 or 1 => new[] { "-input_format", "mjpeg", "-video_size", size, "-framerate", rate }, 2 => ["-video_size", size], _ => [] },
                "-i", cam.Id];
    }

    static bool? _h264;

    /// <summary>True when this FFmpeg can make H.264 (libx264; every usual build can). Asked once.</summary>
    public static bool CanH264()
    {
        if (_h264 is { } known) return known;
        if (Environment.GetEnvironmentVariable("BIBLESTUDY_NOH264") == "1") return false;   // to try the picture-by-picture way
        if (Find() is not { } exe) return false;
        try
        {
            using var p = Process.Start(Info(exe, ["-hide_banner", "-encoders"]))!;
            _ = p.StandardError.ReadToEndAsync();
            var list = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);
            return (_h264 = list.Contains("libx264", StringComparison.Ordinal)).Value;
        }
        catch (Exception) { return false; }
    }

    static IEnumerable<string> MicInput(MediaDevice? mic, int rate)
    {
        if (Fake) return ["-re", "-f", "lavfi", "-i", $"sine=frequency=440:sample_rate={rate}"];
        if (OperatingSystem.IsWindows()) return ["-f", "dshow", "-audio_buffer_size", "40", "-i", "audio=" + (mic?.Id ?? "")];
        if (OperatingSystem.IsMacOS()) return ["-f", "avfoundation", "-i", ":" + (mic?.Id ?? "0")];
        return ["-f", "pulse", "-fragment_size", "1280", "-i", mic?.Id ?? "default"];
    }

    /// <summary>
    /// The camera on the standard output, each picture written out at once (no waiting in buffers): as a video stream (H.264, when
    /// <paramref name="h264"/>), or as single JPEG pictures. The video stream only sends what changed from one picture to the next, so it
    /// needs a fraction of the data and manages Full HD at 60 pictures a second. A delimiter starts every picture (to cut the stream), and every
    /// two seconds comes a key picture with the size, where someone who comes in late can begin.
    /// </summary>
    public static Process StartCamera(MediaDevice cam, CameraQuality quality, int attempt = 0, bool h264 = false)
    {
        var exe = Find() ?? throw new FileNotFoundException("ffmpeg");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-fflags", "nobuffer", "-flags", "low_delay" };
        args.AddRange(CameraInput(cam, quality, attempt));
        // never larger than the camera gives (a 640 camera is not blown up), the height follows the picture
        var scale = $"scale='trunc(min({quality.Width},iw)/2)*2':-2";
        var inv = CultureInfo.InvariantCulture;
        if (h264)
        {
            var kbps = quality.Kbps;
            var fast = quality.Width >= 1920 || quality.Fps > 30;   // the big ones: a faster setting, so that any computer keeps up
            args.AddRange(["-an", "-vf", $"fps={quality.Fps},{scale},format=yuv420p",
                "-c:v", "libx264", "-preset", fast ? "superfast" : "veryfast", "-tune", "zerolatency", "-profile:v", "main",
                "-g", (quality.Fps * 2).ToString(inv), "-b:v", kbps + "k", "-maxrate", kbps + "k", "-bufsize", (kbps / 2) + "k",
                "-bsf:v", "h264_metadata=aud=insert", "-f", "h264", "-flush_packets", "1", "-"]);
        }
        else
            args.AddRange(["-an", "-vf", $"fps={quality.JpegFps},{scale}", "-q:v", quality.Q.ToString(inv), "-f", "mjpeg", "-flush_packets", "1", "-"]);
        return Process.Start(Info(exe, args))!;
    }

    /// <summary>Decodes an H.264 stream written to the standard input into plain pictures (YUV 4:2:0, <paramref name="width"/> × <paramref name="height"/>)
    /// on the standard output, each at once; scaled when the size differs from the stream's.</summary>
    public static Process StartH264Decoder(int width, int height, bool scale)
    {
        var exe = Find() ?? throw new FileNotFoundException("ffmpeg");
        // no "-fflags nobuffer" here: with a stream from a pipe it makes FFmpeg hold back the first two seconds; "-analyzeduration 0" would mean the default (5 s)
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-flags", "low_delay", "-probesize", "32", "-analyzeduration", "1",
            "-thread_type", "slice", "-f", "h264", "-i", "-" };
        if (scale) args.AddRange(["-vf", $"scale={width}:{height}"]);
        args.AddRange(["-f", "rawvideo", "-pix_fmt", "yuv420p", "-flush_packets", "1", "-"]);
        return Process.Start(Info(exe, args, input: true))!;
    }

    /// <summary>The microphone as 16-bit mono PCM at <paramref name="rate"/> on the standard output.</summary>
    public static Process StartMicrophone(MediaDevice? mic, int rate = AudioFormat.Rate)
    {
        var exe = Find() ?? throw new FileNotFoundException("ffmpeg");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-fflags", "nobuffer" };
        args.AddRange(MicInput(mic, rate));
        args.AddRange(["-vn", "-ac", "1", "-ar", rate.ToString(CultureInfo.InvariantCulture), "-f", "s16le", "-flush_packets", "1", "-"]);
        return Process.Start(Info(exe, args))!;
    }

    /// <summary>Plays 16-bit mono PCM at <paramref name="rate"/> written to the standard input (Mac and Linux; Windows plays it itself).</summary>
    public static Process StartSpeaker(int rate = AudioFormat.Rate)
    {
        var exe = Find() ?? throw new FileNotFoundException("ffmpeg");
        var output = Fake ? new[] { "-f", "null", "-" } : OperatingSystem.IsMacOS() ? ["-f", "audiotoolbox", "-"] : ["-f", "pulse", "BibleStudy"];
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-fflags", "nobuffer", "-f", "s16le", "-ar", rate.ToString(CultureInfo.InvariantCulture), "-ac", "1", "-i", "-" };
        args.AddRange(output);
        return Process.Start(Info(exe, args, input: true))!;
    }
}

/// <summary>
/// How the camera is sent: picture size, pictures a second, JPEG quality (2 is best, 31 worst; for the picture-by-picture way) and the data
/// rate of the video stream in kbit/s. Higher needs a faster internet connection.
/// </summary>
public sealed record CameraQuality(int Width, int Height, int Fps, int Q, int Kbps)
{
    public static readonly CameraQuality Low = new(320, 240, 15, 7, 300);
    public static readonly CameraQuality Medium = new(640, 360, 30, 5, 1000);
    public static readonly CameraQuality High = new(1280, 720, 30, 5, 2500);
    public static readonly CameraQuality FullHd = new(1920, 1080, 30, 5, 5000);

    /// <summary>Pictures a second the picture-by-picture way manages (every picture is whole there, so it stays at 10 to 15).</summary>
    public int JpegFps => Math.Min(Fps, Width <= 320 ? 10 : 15);

    /// <summary>0 low, 1 medium, 2 HD (720p), 3 Full HD (1080p); <paramref name="fps"/> 30 or 60 (the low level stays at 15). 60 pictures need
    /// about half as much data again, not twice as much: the pictures in between differ less.</summary>
    public static CameraQuality Of(int level, int fps = 30)
    {
        var q = level switch { <= 0 => Low, 1 => Medium, 2 => High, _ => FullHd };
        if (level <= 0 || fps < 60) return q;
        return q with { Fps = 60, Kbps = q.Kbps * 3 / 2 };
    }
}