using System.Diagnostics;

namespace BibleStudy.Core.Collab.Media;

/// <summary>Microphone, speaker and camera behind one plain surface: start with a callback, stop with Dispose. Windows plays and records sound
/// itself; the camera (and sound on Mac and Linux) goes through FFmpeg.</summary>
public static class MediaIO
{
    /// <summary>True when sound needs FFmpeg here (Mac, Linux); on Windows it does not.</summary>
    public static bool SoundNeedsFfmpeg => !OperatingSystem.IsWindows() || Ffmpeg.Fake;

    public static async Task<List<MediaDevice>> MicrophonesAsync()
    {
        if (OperatingSystem.IsWindows() && !Ffmpeg.Fake) return WinAudio.Microphones();
        return (await Ffmpeg.ListAsync()).Where(d => !d.IsVideo).ToList();
    }

    public static async Task<List<MediaDevice>> CamerasAsync() => (await Ffmpeg.ListAsync()).Where(d => d.IsVideo).ToList();

    public static IDisposable StartMicrophone(MediaDevice? mic, Action<short[]> onPiece)
    {
        if (OperatingSystem.IsWindows() && !Ffmpeg.Fake) return WinAudio.StartMicrophone(mic, onPiece);
        return new FfmpegReader(Ffmpeg.StartMicrophone(mic), AudioFormat.FrameSamples * 2, bytes => onPiece(MuLaw.FromBytes(bytes)));
    }

    public static IDisposable StartSpeaker(Func<short[]> next)
    {
        if (OperatingSystem.IsWindows() && !Ffmpeg.Fake) return WinAudio.StartSpeaker(next);
        return new FfmpegWriter(Ffmpeg.StartSpeaker(), next);
    }

    public static IDisposable StartCamera(MediaDevice cam, Action<byte[]> onPicture)
    {
        var split = new MjpegSplitter();
        return new FfmpegReader(Ffmpeg.StartCamera(cam), 64 * 1024, bytes => { foreach (var f in split.Push(bytes)) onPicture(f); }, exact: false);
    }

    /// <summary>Reads what an FFmpeg process writes: sound in exact pieces, pictures as they come.</summary>
    sealed class FfmpegReader : IDisposable
    {
        readonly Process _p;
        readonly CancellationTokenSource _cts = new();

        public FfmpegReader(Process p, int piece, Action<byte[]> on, bool exact = true)
        {
            _p = p;
            _ = p.StandardError.ReadToEndAsync();   // keeps the pipe empty (a full pipe would stop ffmpeg)
            new Thread(() =>
            {
                var s = p.StandardOutput.BaseStream;
                var buf = new byte[piece];
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        int n;
                        if (exact) { s.ReadExactly(buf); n = buf.Length; }
                        else { n = s.Read(buf, 0, buf.Length); if (n == 0) break; }
                        try { on(buf[..n]); } catch { }
                    }
                }
                catch (EndOfStreamException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }) { IsBackground = true, Name = "ffmpeg-in" }.Start();
        }

        public void Dispose() { _cts.Cancel(); try { if (!_p.HasExited) _p.Kill(); } catch { } _p.Dispose(); }
    }

    /// <summary>Hands FFmpeg the next 40 ms of sound every 40 ms.</summary>
    sealed class FfmpegWriter : IDisposable
    {
        readonly Process _p;
        readonly CancellationTokenSource _cts = new();

        public FfmpegWriter(Process p, Func<short[]> next)
        {
            _p = p;
            _ = p.StandardError.ReadToEndAsync();
            new Thread(() =>
            {
                var s = p.StandardInput.BaseStream;
                var clock = Stopwatch.StartNew(); long sent = 0;
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        // keep about 120 ms ahead of the clock, not more (else the delay grows)
                        if (sent * 40 > clock.ElapsedMilliseconds + 120) { Thread.Sleep(10); continue; }
                        s.Write(MuLaw.ToBytes(next())); s.Flush(); sent++;
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }) { IsBackground = true, Name = "ffmpeg-out" }.Start();
        }

        public void Dispose() { _cts.Cancel(); try { if (!_p.HasExited) _p.Kill(); } catch { } _p.Dispose(); }
    }
}
