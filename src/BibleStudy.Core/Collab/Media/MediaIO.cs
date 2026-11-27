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

    /// <summary>The microphone in pieces of 40 ms at <paramref name="rate"/> (16 kHz for voice messages and notes, 48 kHz in a call).</summary>
    public static IDisposable StartMicrophone(MediaDevice? mic, Action<short[]> onPiece, int rate = AudioFormat.Rate)
    {
        if (OperatingSystem.IsWindows() && !Ffmpeg.Fake) return WinAudio.StartMicrophone(mic, onPiece, rate);
        return new FfmpegReader(Ffmpeg.StartMicrophone(mic, rate), AudioFormat.FrameOf(rate) * 2, bytes => onPiece(MuLaw.FromBytes(bytes)));
    }

    public static IDisposable StartSpeaker(Func<short[]> next, int rate = AudioFormat.Rate)
    {
        if (OperatingSystem.IsWindows() && !Ffmpeg.Fake) return WinAudio.StartSpeaker(next, rate);
        return new FfmpegWriter(Ffmpeg.StartSpeaker(rate), next);
    }

    /// <summary>Starts the camera; <paramref name="onPicture"/> gets every picture (on a background thread): an H.264 access unit when
    /// <paramref name="h264"/>, else a JPEG picture. A camera that cannot give the asked size or kind is opened again more simply (see <see cref="Ffmpeg.CameraAttempts"/>).</summary>
    public static async Task<IDisposable> StartCameraAsync(MediaDevice cam, CameraQuality quality, Action<byte[]> onPicture, bool h264 = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            Func<byte[], List<byte[]>> split;
            if (h264) { var s = new H264Splitter(); split = b => s.Push(b); }
            else { var s = new MjpegSplitter(); split = b => s.Push(b); }
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var p = Ffmpeg.StartCamera(cam, quality, attempt, h264);
            var exited = p.WaitForExitAsync();
            var reader = new FfmpegReader(p, 256 * 1024, bytes => { foreach (var f in split(bytes)) { first.TrySetResult(); onPicture(f); } }, exact: false);
            if (attempt >= Ffmpeg.CameraAttempts - 1) return reader;
            var done = await Task.WhenAny(first.Task, exited, Task.Delay(8000));
            if (done != exited) return reader;   // pictures come (or it is slow to start, but running)
            reader.Dispose();                     // the camera does not offer this: ask more simply
        }
    }

    /// <summary>A decoder for the H.264 pictures of one person on this computer (through FFmpeg), or null when there is no FFmpeg.</summary>
    public static IVideoDecoder? StartH264Decoder(Action<int, int, byte[]> sink, int maxWidth = 0) => Ffmpeg.Find() is null ? null : new FfmpegH264Decoder(sink, maxWidth);

    /// <summary>
    /// H.264 to pictures through an FFmpeg process: the access units go in (each followed at once by the delimiter of the next one, so FFmpeg
    /// knows the picture is complete and does not wait for the next), plain YUV pictures come out and become BGRA here.
    /// </summary>
    sealed class FfmpegH264Decoder(Action<int, int, byte[]> sink, int maxWidth) : VideoDecoderBase(sink)
    {
        static readonly byte[] NextDelimiter = [0, 0, 0, 1, 0x09, 0xF0];
        Process? _p;
        Stream? _in;

        protected override void Start(int width, int height, byte[] sps, byte[] pps)
        {
            int w = width, h = height;
            if (maxWidth > 0 && w > maxWidth) { h = Math.Max(2, (int)Math.Round(h * (double)maxWidth / w / 2) * 2); w = maxWidth; }
            var p = Ffmpeg.StartH264Decoder(w, h, w != width || h != height);
            _p = p; _in = p.StandardInput.BaseStream;
            _ = p.StandardError.ReadToEndAsync();
            var token = Token;
            new Thread(() =>
            {
                // an error on this thread would end the whole program: whatever goes wrong only ends the reading
                try
                {
                    var s = p.StandardOutput.BaseStream;
                    var yuv = new byte[w * h * 3 / 2];
                    var bgra = new byte[w * h * 4];
                    int cw = (w + 1) / 2, ch = (h + 1) / 2;
                    if (yuv.Length < w * h + 2 * cw * ch) yuv = new byte[w * h + 2 * cw * ch];
                    while (!token.IsCancellationRequested)
                    {
                        s.ReadExactly(yuv, 0, w * h + 2 * cw * ch);
                        Yuv.ToBgra(yuv, 0, w, yuv, w * h, yuv, w * h + cw * ch, cw, 1, w, h, bgra);
                        try { Sink(w, h, bgra); } catch { }
                    }
                }
                catch (Exception) { /* the end of the stream, a stopped process */ }
            }) { IsBackground = true, Name = "video-out" }.Start();
        }

        protected override void Decode(byte[] au)
        {
            var s = _in ?? throw new IOException("closed");
            s.Write(H264.WithoutDelimiter(au));
            s.Write(NextDelimiter);
            s.Flush();
        }

        protected override void Stop()
        {
            var p = Interlocked.Exchange(ref _p, null); _in = null;
            if (p is null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
            p.Dispose();
        }

        public override void Dispose() { base.Dispose(); Stop(); }
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
                // an error on this thread would end the whole program: whatever goes wrong only ends the reading
                try
                {
                    var s = p.StandardOutput.BaseStream;   // inside: the process may already be gone (stopped right after starting)
                    var buf = new byte[piece];
                    while (!_cts.IsCancellationRequested)
                    {
                        int n;
                        if (exact) { s.ReadExactly(buf); n = buf.Length; }
                        else { n = s.Read(buf, 0, buf.Length); if (n == 0) break; }
                        try { on(buf[..n]); } catch { }
                    }
                }
                catch (Exception) { /* the end of the stream, a stopped process: the reading is over */ }
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
                // an error on this thread would end the whole program: whatever goes wrong only ends the playing
                try
                {
                    var s = p.StandardInput.BaseStream;   // inside: the process may already be gone
                    var clock = Stopwatch.StartNew(); long sent = 0;
                    while (!_cts.IsCancellationRequested)
                    {
                        // keep about 120 ms ahead of the clock, not more (else the delay grows)
                        if (sent * 40 > clock.ElapsedMilliseconds + 120) { Thread.Sleep(10); continue; }
                        s.Write(MuLaw.ToBytes(next())); s.Flush(); sent++;
                    }
                }
                catch (Exception) { /* a closed pipe, a stopped process: the playing is over */ }
            }) { IsBackground = true, Name = "ffmpeg-out" }.Start();
        }

        public void Dispose() { _cts.Cancel(); try { if (!_p.HasExited) _p.Kill(); } catch { } _p.Dispose(); }
    }
}
