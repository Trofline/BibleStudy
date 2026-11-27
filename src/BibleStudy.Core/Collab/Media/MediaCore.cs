using System.Collections.Concurrent;

namespace BibleStudy.Core.Collab.Media;

/// <summary>
/// The sound formats: voice messages and spoken notes are 16 kHz, mono, 16 bit, packed with G.711 μ-law (small, plain and fast). A call
/// (talking live) is 48 kHz, 16 bit, unpacked: the full range of the voice, like a good microphone records it. Both go in pieces of 40 ms.
/// The relay counts messages, not their size, and everything is gathered per 100 ms anyway, so the better sound costs no limit.
/// </summary>
public static class AudioFormat
{
    public const int Rate = 16000;
    public const int FrameSamples = 640;        // 40 ms
    public const int CallRate = 48000;
    public const int CallFrameSamples = 1920;   // 40 ms

    /// <summary>40 ms at a sample rate.</summary>
    public static int FrameOf(int rate) => rate / 25;

    /// <summary>48 kHz to 16 kHz (every three samples averaged: that also takes away what 16 kHz cannot hold).</summary>
    public static short[] Down3(ReadOnlySpan<short> pcm)
    {
        var o = new short[pcm.Length / 3];
        for (var i = 0; i < o.Length; i++) o[i] = (short)((pcm[3 * i] + pcm[3 * i + 1] + pcm[3 * i + 2]) / 3);
        return o;
    }

    /// <summary>16 kHz to 48 kHz (two samples in between, on the straight line from one to the next).</summary>
    public static short[] Up3(ReadOnlySpan<short> pcm)
    {
        var o = new short[pcm.Length * 3];
        for (var i = 0; i < pcm.Length; i++)
        {
            int a = pcm[i], b = i + 1 < pcm.Length ? pcm[i + 1] : a;
            o[3 * i] = (short)a; o[3 * i + 1] = (short)((2 * a + b) / 3); o[3 * i + 2] = (short)((a + 2 * b) / 3);
        }
        return o;
    }
}

/// <summary>G.711 μ-law: 16-bit samples to 8 bits and back (the telephone codec; good enough for speech, no library needed).</summary>
public static class MuLaw
{
    const int Bias = 0x84, Clip = 32635;
    static readonly short[] Table = Enumerable.Range(0, 256).Select(i => DecodeOne((byte)i)).ToArray();

    public static byte[] Encode(ReadOnlySpan<short> pcm)
    {
        var o = new byte[pcm.Length];
        for (var i = 0; i < pcm.Length; i++) o[i] = EncodeOne(pcm[i]);
        return o;
    }

    public static short[] Decode(ReadOnlySpan<byte> data)
    {
        var o = new short[data.Length];
        for (var i = 0; i < data.Length; i++) o[i] = Table[data[i]];
        return o;
    }

    static byte EncodeOne(short s)
    {
        int sample = s;
        var sign = (sample >> 8) & 0x80;
        if (sign != 0) sample = -sample;
        if (sample > Clip) sample = Clip;
        sample += Bias;
        var exponent = 7;
        for (var mask = 0x4000; (sample & mask) == 0 && exponent > 0; mask >>= 1) exponent--;
        var mantissa = (sample >> (exponent + 3)) & 0x0F;
        return (byte)~(sign | (exponent << 4) | mantissa);
    }

    static short DecodeOne(byte b)
    {
        int u = ~b;
        var sign = u & 0x80; var exponent = (u >> 4) & 0x07; var mantissa = u & 0x0F;
        var sample = (((mantissa << 3) + Bias) << exponent) - Bias;
        return (short)(sign != 0 ? -sample : sample);
    }

    /// <summary>16-bit little-endian bytes (as a sound device or ffmpeg delivers them) to samples.</summary>
    public static short[] FromBytes(ReadOnlySpan<byte> bytes)
    {
        var o = new short[bytes.Length / 2];
        for (var i = 0; i < o.Length; i++) o[i] = (short)(bytes[2 * i] | bytes[2 * i + 1] << 8);
        return o;
    }

    public static byte[] ToBytes(ReadOnlySpan<short> pcm)
    {
        var o = new byte[pcm.Length * 2];
        for (var i = 0; i < pcm.Length; i++) { o[2 * i] = (byte)pcm[i]; o[2 * i + 1] = (byte)(pcm[i] >> 8); }
        return o;
    }

    /// <summary>How loud a piece is, 0..1 (for the "is speaking" mark).</summary>
    public static double Level(ReadOnlySpan<short> pcm)
    {
        if (pcm.Length == 0) return 0;
        long sum = 0;
        foreach (var s in pcm) sum += Math.Abs((int)s);
        return Math.Min(1, sum / (double)pcm.Length / 8000);
    }
}

/// <summary>Cuts a stream of JPEG pictures (as ffmpeg writes a camera in "mjpeg") into single pictures.</summary>
public sealed class MjpegSplitter
{
    readonly MemoryStream _buf = new();
    public const int MaxFrame = 2 * 1024 * 1024;

    /// <summary>Adds bytes; returns the pictures that are complete now.</summary>
    public List<byte[]> Push(ReadOnlySpan<byte> data)
    {
        _buf.Write(data);
        var frames = new List<byte[]>();
        var all = _buf.GetBuffer().AsSpan(0, (int)_buf.Length);
        var start = -1; var consumed = 0;
        for (var i = 0; i + 1 < all.Length; i++)
        {
            if (start < 0) { if (all[i] == 0xFF && all[i + 1] == 0xD8) start = i; continue; }
            if (all[i] == 0xFF && all[i + 1] == 0xD9)
            {
                frames.Add(all[start..(i + 2)].ToArray());
                consumed = i + 2; start = -1; i++;
            }
        }
        if (start < 0 && consumed == 0) consumed = Math.Max(0, all.Length - 1);   // nothing begun: keep only a possible half marker
        else if (start >= 0 && consumed < start) consumed = start;
        var rest = all[consumed..].ToArray();
        if (rest.Length > MaxFrame) rest = [];   // a broken picture never ends: start again
        _buf.SetLength(0); _buf.Write(rest);
        return frames;
    }
}

/// <summary>
/// Mixes the voices of several people into one: every person has a small waiting line (it starts playing once 120 ms are there, so short delays
/// in the network do not stutter), and every 40 ms one piece of each line is added up.
/// </summary>
public sealed class AudioMixer
{
    sealed class Line { public readonly ConcurrentQueue<short[]> Q = new(); public bool Playing; public DateTime Last; }
    readonly ConcurrentDictionary<string, Line> _lines = new();
    readonly int _frame;
    public double Volume { get; set; } = 1.0;

    /// <summary>Pieces of <paramref name="frameSamples"/> samples (40 ms at the rate the sound plays).</summary>
    public AudioMixer(int frameSamples = AudioFormat.FrameSamples) => _frame = frameSamples;

    /// <summary>A piece of sound of one source (a person, or a voice message being played).</summary>
    public void Add(string source, short[] pcm)
    {
        var line = _lines.GetOrAdd(source, _ => new Line());
        line.Last = DateTime.UtcNow;
        for (var i = 0; i < pcm.Length; i += _frame)
            line.Q.Enqueue(pcm.AsSpan(i, Math.Min(_frame, pcm.Length - i)).ToArray());
        while (line.Q.Count > 50) line.Q.TryDequeue(out _);   // more than 2 s behind: drop the oldest, so it catches up
    }

    /// <summary>The sources that sent sound in the last moment (for "is speaking").</summary>
    public IEnumerable<string> Active => _lines.Where(l => (DateTime.UtcNow - l.Value.Last).TotalMilliseconds < 400).Select(l => l.Key);

    public int Queued(string source) => _lines.TryGetValue(source, out var l) ? l.Q.Count : 0;

    /// <summary>The next 40 ms: the sum of all sources that are playing (silence when none is).</summary>
    public short[] Next()
    {
        var mix = new int[_frame];
        foreach (var line in _lines.Values)
        {
            if (!line.Playing && line.Q.Count >= 3) line.Playing = true;
            if (!line.Playing) continue;
            if (!line.Q.TryDequeue(out var piece)) { line.Playing = false; continue; }
            for (var i = 0; i < piece.Length; i++) mix[i] += piece[i];
        }
        var o = new short[mix.Length];
        for (var i = 0; i < mix.Length; i++) o[i] = (short)Math.Clamp(mix[i] * Volume, short.MinValue, short.MaxValue);
        return o;
    }

    public void Clear(string source) => _lines.TryRemove(source, out _);
    public void ClearAll() => _lines.Clear();
}

/// <summary>A plain WAV file (16 kHz mono 16 bit), for players that only play files (the phones).</summary>
public static class Wav
{
    public static void Write(string path, ReadOnlySpan<short> pcm)
    {
        using var w = new BinaryWriter(File.Create(path));
        var bytes = pcm.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(AudioFormat.Rate); w.Write(AudioFormat.Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        w.Write(MuLaw.ToBytes(pcm));
    }
}
