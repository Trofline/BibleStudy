using BibleStudy.Core;
using BibleStudy.Core.Collab.Media;

namespace BibleStudy.App;

/// <summary>
/// One recording from the microphone, as 16 kHz mono samples (<see cref="AudioFormat"/>). The computer and Android hand over the sound in pieces while it is
/// recorded; the iPhone records into a file that is read when the recording stops.
/// </summary>
sealed class VoiceRecording : IDisposable
{
    readonly List<short> _pcm = [];
    readonly int _max;
    internal IDisposable? Source;
    internal Func<short[]>? Finish;      // the iPhone: stops and returns the sound of the file
    internal Func<double>? LevelSource;  // the iPhone: asks the recorder how loud it is
    double _level;

    public VoiceRecording(int maxSeconds) => _max = AudioFormat.Rate * maxSeconds;

    /// <summary>How loud the last moment was, 0 … 1.</summary>
    public double Level => LevelSource is { } f ? f() : _level;

    /// <summary>A piece of sound (on a background thread).</summary>
    internal void Add(ReadOnlySpan<short> piece)
    {
        _level = MuLaw.Level(piece);
        lock (_pcm) if (_pcm.Count < _max) _pcm.AddRange(piece[..Math.Min(piece.Length, _max - _pcm.Count)]);
    }

    /// <summary>Ends the recording and returns what was recorded (empty when it was stopped before).</summary>
    public short[] Stop()
    {
        var source = Source; Source = null;
        try { source?.Dispose(); } catch { /* the device is gone: what was recorded stays */ }
        if (Finish is { } finish) { Finish = null; try { return finish(); } catch { return []; } }
        lock (_pcm) { var all = _pcm.ToArray(); _pcm.Clear(); return all; }
    }

    public void Dispose() => Stop();
}

static class VoiceRecorder
{
    /// <summary>Starts recording. Throws <see cref="InvalidOperationException"/> with a message for the user when there is no microphone or no permission.</summary>
    public static async Task<VoiceRecording> StartAsync(int maxSeconds)
    {
        var rec = new VoiceRecording(maxSeconds);
#if ANDROID
        if (!await BibleStudy.Droid.MainActivity.AskPermissionAsync(Android.Manifest.Permission.RecordAudio))
            throw new InvalidOperationException(Loc.L("Ohne Erlaubnis für das Mikrofon kann BibleStudy nichts aufnehmen. Du kannst sie in den Einstellungen des Handys unter Apps → BibleStudy → Berechtigungen geben."));
        rec.Source = AndroidMic.Start(rec);
#elif IOS
        await IosMic.StartAsync(rec);
#else
        if (MediaIO.SoundNeedsFfmpeg && Ffmpeg.Find() is null)
            throw new InvalidOperationException(Loc.L("Für das Mikrofon braucht BibleStudy auf diesem System das kostenlose FFmpeg. Lade es im Download-Manager unter „Kamera und Ton“ herunter."));
        var mics = await MediaIO.MicrophonesAsync();
        if (mics.Count == 0) throw new InvalidOperationException(Loc.L("Kein Mikrofon gefunden."));
        rec.Source = MediaIO.StartMicrophone(null, piece => rec.Add(piece));
#endif
        return rec;
    }

    /// <summary>The sound of a 16-bit mono WAV file (the iPhone's recordings); chunks other than "data" are skipped.</summary>
    internal static short[] ReadWav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            var size = BitConverter.ToInt32(bytes, pos + 4);
            if (size < 0) break;
            if (id == "data")
            {
                var n = Math.Min(size, bytes.Length - pos - 8) / 2;
                var pcm = new short[n];
                Buffer.BlockCopy(bytes, pos + 8, pcm, 0, n * 2);
                return pcm;
            }
            pos += 8 + size + (size & 1);
        }
        return [];
    }
}

#if ANDROID
/// <summary>The phone's microphone (AudioRecord), 16 kHz mono, read in pieces of 40 ms on a thread of its own.</summary>
sealed class AndroidMic : IDisposable
{
    readonly Android.Media.AudioRecord _rec;
    volatile bool _stop;
    readonly Thread _thread;

    AndroidMic(Android.Media.AudioRecord rec, VoiceRecording into)
    {
        _rec = rec;
        _thread = new Thread(() =>
        {
            var buf = new short[AudioFormat.FrameSamples];
            try
            {
                while (!_stop)
                {
                    var n = _rec.Read(buf, 0, buf.Length);
                    if (n > 0) into.Add(buf.AsSpan(0, n));
                    else if (n < 0) break;
                }
            }
            catch (Exception) { /* the microphone was taken away: the recording ends */ }
        }) { IsBackground = true, Name = "voice-in" };
        _thread.Start();
    }

    public static AndroidMic Start(VoiceRecording into)
    {
        var min = Android.Media.AudioRecord.GetMinBufferSize(AudioFormat.Rate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit);
        var rec = new Android.Media.AudioRecord(Android.Media.AudioSource.Mic, AudioFormat.Rate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit, Math.Max(min, AudioFormat.FrameSamples * 2 * 4));
        if (rec.State != Android.Media.State.Initialized) { rec.Release(); throw new InvalidOperationException(Loc.L("Das Mikrofon ließ sich nicht öffnen: {0}", "AudioRecord")); }
        rec.StartRecording();
        return new AndroidMic(rec, into);
    }

    public void Dispose()
    {
        _stop = true;
        try { _rec.Stop(); } catch (Java.Lang.IllegalStateException) { }
        _thread.Join(500);
        _rec.Release();
    }
}
#endif

#if IOS
/// <summary>The iPhone's microphone: AVAudioRecorder writes a 16 kHz mono WAV file, which is read when the recording stops.</summary>
static class IosMic
{
    public static async Task StartAsync(VoiceRecording into)
    {
        var session = AVFoundation.AVAudioSession.SharedInstance();
        var asked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.RequestRecordPermission(granted => asked.TrySetResult(granted));
        if (!await asked.Task)
            throw new InvalidOperationException(Loc.L("Ohne Erlaubnis für das Mikrofon kann BibleStudy nichts aufnehmen. Du kannst sie in den Einstellungen des iPhones unter BibleStudy geben."));
        session.SetCategory(AVFoundation.AVAudioSession.CategoryPlayAndRecord, out _);
        session.SetActive(true, out _);
        var path = Path.Combine(Path.GetTempPath(), "bs_voice_rec.wav");
        try { File.Delete(path); } catch (IOException) { }
        var settings = new AVFoundation.AudioSettings
        {
            Format = AudioToolbox.AudioFormatType.LinearPCM, SampleRate = AudioFormat.Rate, NumberChannels = 1,
            LinearPcmBitDepth = 16, LinearPcmBigEndian = false, LinearPcmFloat = false,
        };
        var rec = AVFoundation.AVAudioRecorder.Create(Foundation.NSUrl.FromFilename(path), settings, out var error);
        if (rec is null) throw new InvalidOperationException(Loc.L("Das Mikrofon ließ sich nicht öffnen: {0}", error?.LocalizedDescription ?? ""));
        rec.MeteringEnabled = true;
        if (!rec.Record()) { rec.Dispose(); throw new InvalidOperationException(Loc.L("Das Mikrofon ließ sich nicht öffnen: {0}", "AVAudioRecorder")); }
        into.LevelSource = () =>
        {
            rec.UpdateMeters();
            var db = rec.AveragePower(0);   // −160 … 0 dB
            return Math.Clamp((db + 50) / 50, 0, 1);
        };
        into.Finish = () =>
        {
            rec.Stop();
            rec.Dispose();
            session.SetCategory(AVFoundation.AVAudioSession.CategoryPlayback, out _);
            try { return VoiceRecorder.ReadWav(path); } finally { try { File.Delete(path); } catch (IOException) { } }
        };
    }
}
#endif
