using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BibleStudy.App;

/// <summary>
/// Plays one MP3 file, with nothing added to the program: Windows uses its built-in media control interface (winmm), macOS the system
/// player "afplay", Linux the first of mpv, ffplay, mpg123, cvlc that is installed. Only Windows can jump to a position and tell the length.
/// </summary>
sealed class AudioPlayer : IDisposable
{
    public enum PlayState { Idle, Playing, Paused, Ended, Failed }

    public PlayState State { get; private set; } = PlayState.Idle;
    public string? Error { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan Length { get; private set; }
    public bool CanSeek => OperatingSystem.IsWindows();

    const string Alias = "bsaudio";
    bool _mciOpen;
    Process? _proc;
    readonly Stopwatch _clock = new();
    bool _stoppedByUs;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    static extern int MciSend(string command, StringBuilder? result, int resultLen, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
    static extern bool MciError(int code, StringBuilder text, int len);

    static string MciText(string command)
    {
        var sb = new StringBuilder(128);
        return MciSend(command, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString().Trim() : "";
    }

    bool Mci(string command)
    {
        var code = MciSend(command, null, 0, IntPtr.Zero);
        if (code == 0) return true;
        var sb = new StringBuilder(256);
        MciError(code, sb, sb.Capacity);
        Error = sb.ToString();
        return false;
    }

    /// <summary>Starts the file from the beginning. False (and <see cref="Error"/>) when it cannot be played.</summary>
    public bool Play(string path)
    {
        Stop();
        Error = null; Position = TimeSpan.Zero; Length = TimeSpan.Zero; _stoppedByUs = false;
        if (OperatingSystem.IsWindows())
        {
            if (!Mci($"open \"{path}\" type mpegvideo alias {Alias}")) { State = PlayState.Failed; return false; }
            _mciOpen = true;
            Mci($"set {Alias} time format milliseconds");
            if (long.TryParse(MciText($"status {Alias} length"), out var ms)) Length = TimeSpan.FromMilliseconds(ms);
            if (!Mci($"play {Alias}")) { Stop(); State = PlayState.Failed; return false; }
            State = PlayState.Playing;
            return true;
        }

        var (exe, args) = Command(path);
        if (exe is null) { Error = "no player"; State = PlayState.Failed; return false; }
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            _proc = Process.Start(psi);
            if (_proc is null) { Error = exe; State = PlayState.Failed; return false; }
            _proc.EnableRaisingEvents = true;
            _clock.Restart();
            State = PlayState.Playing;
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Error = ex.Message; State = PlayState.Failed; return false; }
    }

    /// <summary>The player program for this system, or null when none is installed.</summary>
    public static bool HasPlayer => OperatingSystem.IsWindows() || Command("x.mp3").Exe is not null;

    static (string? Exe, string[] Args) Command(string path)
    {
        if (OperatingSystem.IsMacOS()) return ("afplay", [path]);
        (string, string[])[] candidates =
        [
            ("mpv", ["--no-video", "--really-quiet", path]),
            ("ffplay", ["-nodisp", "-autoexit", "-loglevel", "quiet", path]),
            ("mpg123", ["-q", path]),
            ("cvlc", ["--play-and-exit", "--intf", "dummy", path]),
        ];
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var (exe, args) in candidates)
            if (dirs.Any(d => File.Exists(Path.Combine(d, exe)))) return (exe, args);
        return (null, []);
    }

    static void Signal(Process p, string sig)
    {
        try { using var k = Process.Start(new ProcessStartInfo("kill") { ArgumentList = { sig, p.Id.ToString() }, UseShellExecute = false, CreateNoWindow = true }); k?.WaitForExit(500); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    public void Pause()
    {
        if (State != PlayState.Playing) return;
        if (_mciOpen) Mci($"pause {Alias}");
        else if (_proc is { HasExited: false }) { Signal(_proc, "-STOP"); _clock.Stop(); }
        State = PlayState.Paused;
    }

    public void Resume()
    {
        if (State != PlayState.Paused) return;
        if (_mciOpen) Mci($"play {Alias}");
        else if (_proc is { HasExited: false }) { Signal(_proc, "-CONT"); _clock.Start(); }
        State = PlayState.Playing;
    }

    /// <summary>Jumps to a position (Windows only).</summary>
    public void Seek(TimeSpan to)
    {
        if (!_mciOpen) return;
        var keepPaused = State == PlayState.Paused;
        var ms = (long)Math.Clamp(to.TotalMilliseconds, 0, Math.Max(0, Length.TotalMilliseconds - 200));
        if (Mci($"seek {Alias} to {ms}")) { Mci($"play {Alias}"); if (keepPaused) Mci($"pause {Alias}"); Position = TimeSpan.FromMilliseconds(ms); }
    }

    /// <summary>Asks the player how far it is and whether it has finished; call a few times per second.</summary>
    public void Poll()
    {
        if (State is not (PlayState.Playing or PlayState.Paused)) return;
        if (_mciOpen)
        {
            if (long.TryParse(MciText($"status {Alias} position"), out var ms)) Position = TimeSpan.FromMilliseconds(ms);
            if (State == PlayState.Playing && MciText($"status {Alias} mode") == "stopped") State = PlayState.Ended;
        }
        else if (_proc is not null)
        {
            Position = _clock.Elapsed;
            if (_proc.HasExited && !_stoppedByUs) { State = _proc.ExitCode == 0 ? PlayState.Ended : PlayState.Failed; if (State == PlayState.Failed) Error = "exit " + _proc.ExitCode; }
        }
    }

    public void Stop()
    {
        _stoppedByUs = true;
        if (_mciOpen) { MciSend($"stop {Alias}", null, 0, IntPtr.Zero); MciSend($"close {Alias}", null, 0, IntPtr.Zero); _mciOpen = false; }
        if (_proc is not null)
        {
            try { if (!_proc.HasExited) { if (State == PlayState.Paused) Signal(_proc, "-CONT"); _proc.Kill(); } } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            _proc.Dispose(); _proc = null;
        }
        _clock.Reset();
        if (State is PlayState.Playing or PlayState.Paused) State = PlayState.Idle;
    }

    public void Dispose() => Stop();
}
