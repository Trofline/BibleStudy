using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BibleStudy.Core.Collab.Media;

/// <summary>A camera or microphone as the system names it.</summary>
public sealed record MediaDevice(string Name, string Id, bool IsVideo);

/// <summary>
/// The camera (and on Mac/Linux the microphone and the speaker) through FFmpeg, a free program that is started on its own: it reads the
/// device and writes plain pictures (JPEG) or plain sound (16 kHz PCM) to us, or plays the sound we hand it. BibleStudy uses an FFmpeg that is
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

    static IEnumerable<string> CameraInput(MediaDevice cam)
    {
        if (Fake) return ["-re", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=8"];
        if (OperatingSystem.IsWindows()) return ["-f", "dshow", "-video_size", "640x480", "-i", "video=" + cam.Id];
        if (OperatingSystem.IsMacOS()) return ["-f", "avfoundation", "-framerate", "30", "-video_size", "640x480", "-i", cam.Id + ":none"];
        return ["-f", "v4l2", "-i", cam.Id];
    }

    static IEnumerable<string> MicInput(MediaDevice? mic)
    {
        if (Fake) return ["-re", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=16000"];
        if (OperatingSystem.IsWindows()) return ["-f", "dshow", "-audio_buffer_size", "40", "-i", "audio=" + (mic?.Id ?? "")];
        if (OperatingSystem.IsMacOS()) return ["-f", "avfoundation", "-i", ":" + (mic?.Id ?? "0")];
        return ["-f", "pulse", "-fragment_size", "1280", "-i", mic?.Id ?? "default"];
    }

    /// <summary>The camera as small JPEG pictures (320 px wide, about 8 a second) on the standard output.</summary>
    public static Process StartCamera(MediaDevice cam, int fps = 8)
    {
        var exe = Find() ?? throw new FileNotFoundException("ffmpeg");
        var args = new List<string> { "-hide_banner", "-loglevel", "error" };
        args.AddRange(CameraInput(cam));
        args.AddRange(["-an", "-vf", $"fps={fps},scale=320:-2", "-q:v", "7", "-f", "mjpeg", "-"]);
        return Process.Start(Info(exe, args))!;
    }

    /// <summary>The microphone as 16 kHz 16-bit mono PCM on the standard output.</summary>
    public static Process StartMicrophone(MediaDevice? mic)
    {
        var exe = Find() ?? throw new FileNotFoundException("ffmpeg");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-fflags", "nobuffer" };
        args.AddRange(MicInput(mic));
        args.AddRange(["-vn", "-ac", "1", "-ar", AudioFormat.Rate.ToString(CultureInfo.InvariantCulture), "-f", "s16le", "-flush_packets", "1", "-"]);
        return Process.Start(Info(exe, args))!;
    }

    /// <summary>Plays 16 kHz 16-bit mono PCM written to the standard input (Mac and Linux; Windows plays it itself).</summary>
    public static Process StartSpeaker()
    {
        var exe = Find() ?? throw new FileNotFoundException("ffmpeg");
        var output = Fake ? new[] { "-f", "null", "-" } : OperatingSystem.IsMacOS() ? ["-f", "audiotoolbox", "-"] : ["-f", "pulse", "BibleStudy"];
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-fflags", "nobuffer", "-f", "s16le", "-ar", AudioFormat.Rate.ToString(CultureInfo.InvariantCulture), "-ac", "1", "-i", "-" };
        args.AddRange(output);
        return Process.Start(Info(exe, args, input: true))!;
    }
}
