namespace BibleStudy.Core.Collab.Media;

/// <summary>
/// The small parts of H.264 the camera needs, without a library: the byte stream (Annex B) is a row of pieces ("NAL units") that each start
/// with 00 00 01; one picture ("access unit") begins with a delimiter piece (type 9, which the encoder is told to write). A key picture (type 5)
/// can be shown on its own and carries the picture size in the piece before it (the "SPS", type 7) and the PPS (type 8).
/// </summary>
public static class H264
{
    public const int Aud = 9, Sps = 7, Pps = 8, Idr = 5, Sei = 6;

    /// <summary>The pieces of an access unit, without their start codes.</summary>
    public static List<(int Start, int Length)> Nals(ReadOnlySpan<byte> au)
    {
        var list = new List<(int, int)>();
        var start = -1;
        for (var i = 0; i + 2 < au.Length; i++)
        {
            if (au[i] != 0 || au[i + 1] != 0 || au[i + 2] != 1) continue;
            if (start >= 0)
            {
                var end = i;
                while (end > start && au[end - 1] == 0) end--;   // a four-byte start code (or zero padding) belongs to the next piece
                list.Add((start, end - start));
            }
            start = i + 3; i += 2;
        }
        if (start >= 0 && start < au.Length) list.Add((start, au.Length - start));
        return list;
    }

    public static int Type(ReadOnlySpan<byte> au, (int Start, int Length) nal) => nal.Length > 0 ? au[nal.Start] & 0x1F : -1;

    /// <summary>True when the picture can be shown without the ones before it.</summary>
    public static bool IsKeyframe(ReadOnlySpan<byte> au)
    {
        foreach (var n in Nals(au)) if (Type(au, n) == Idr) return true;
        return false;
    }

    /// <summary>The SPS and PPS of a key picture (without start codes), or null when it carries none.</summary>
    public static (byte[] Sps, byte[] Pps)? ParameterSets(ReadOnlySpan<byte> au)
    {
        byte[]? sps = null, pps = null;
        foreach (var n in Nals(au))
        {
            var t = Type(au, n);
            if (t == Sps && sps is null) sps = au.Slice(n.Start, n.Length).ToArray();
            else if (t == Pps && pps is null) pps = au.Slice(n.Start, n.Length).ToArray();
        }
        return sps is not null && pps is not null ? (sps, pps) : null;
    }

    /// <summary>The access unit without its delimiter (the decoders on the computer get the delimiter of the next picture right after it).</summary>
    public static byte[] WithoutDelimiter(byte[] au)
    {
        var nals = Nals(au);
        if (nals.Count < 2 || Type(au, nals[0]) != Aud) return au;
        var from = nals[1].Start - 3;
        if (from > 0 && au[from - 1] == 0) from--;
        return au[from..];
    }

    /// <summary>The access unit with a four-byte length before each piece instead of a start code ("AVCC", as an iPhone decodes it);
    /// delimiter, SPS and PPS are left out (they go to the decoder separately).</summary>
    public static byte[] ToLengthPrefixed(ReadOnlySpan<byte> au)
    {
        using var ms = new MemoryStream(au.Length + 16);
        foreach (var n in Nals(au))
        {
            var t = Type(au, n);
            if (t is Aud or Sps or Pps || n.Length == 0) continue;
            ms.WriteByte((byte)(n.Length >> 24)); ms.WriteByte((byte)(n.Length >> 16)); ms.WriteByte((byte)(n.Length >> 8)); ms.WriteByte((byte)n.Length);
            ms.Write(au.Slice(n.Start, n.Length));
        }
        return ms.ToArray();
    }

    /// <summary>The size of the pictures as shown (after the cropping the SPS asks for), read from an SPS (without start code); null when it cannot be read.</summary>
    public static (int Width, int Height)? Size(ReadOnlySpan<byte> sps)
    {
        try
        {
            var r = new BitReader(Unescape(sps[1..]));   // [0] is the NAL header
            var profile = r.Bits(8); r.Bits(16);          // constraint flags, level
            r.Ue();                                       // sps id
            var chroma = 1;
            if (profile is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)
            {
                chroma = (int)r.Ue();
                if (chroma == 3) r.Bits(1);
                r.Ue(); r.Ue(); r.Bits(1);
                if (r.Bits(1) == 1)
                    for (var i = 0; i < (chroma != 3 ? 8 : 12); i++)
                        if (r.Bits(1) == 1)
                        {
                            int last = 8, next = 8;
                            for (var j = 0; j < (i < 6 ? 16 : 64); j++)
                            {
                                if (next != 0) next = (last + r.Se() + 256) % 256;
                                last = next == 0 ? last : next;
                            }
                        }
            }
            r.Ue();                                       // log2_max_frame_num
            var pocType = r.Ue();
            if (pocType == 0) r.Ue();
            else if (pocType == 1) { r.Bits(1); r.Se(); r.Se(); var n = r.Ue(); for (var i = 0u; i < n; i++) r.Se(); }
            r.Ue(); r.Bits(1);                            // reference frames, gaps
            var wMbs = r.Ue() + 1; var hMaps = r.Ue() + 1;
            var frameOnly = r.Bits(1);
            if (frameOnly == 0) r.Bits(1);
            r.Bits(1);
            long w = wMbs * 16, h = (2 - frameOnly) * hMaps * 16;
            if (r.Bits(1) == 1)
            {
                long l = r.Ue(), rt = r.Ue(), t = r.Ue(), b = r.Ue();
                int cx = chroma is 1 or 2 ? 2 : 1, cy = (chroma == 1 ? 2 : 1) * (int)(2 - frameOnly);
                if (chroma == 0) { cx = 1; cy = (int)(2 - frameOnly); }
                w -= (l + rt) * cx; h -= (t + b) * cy;
            }
            return w is > 0 and <= 8192 && h is > 0 and <= 8192 ? ((int)w, (int)h) : null;
        }
        catch (IndexOutOfRangeException) { return null; }
    }

    /// <summary>Takes out the 03 the encoder puts after two zero bytes (so that a start code never appears inside a piece).</summary>
    static byte[] Unescape(ReadOnlySpan<byte> b)
    {
        var o = new List<byte>(b.Length);
        var zeros = 0;
        foreach (var x in b)
        {
            if (zeros >= 2 && x == 3) { zeros = 0; continue; }
            o.Add(x);
            zeros = x == 0 ? zeros + 1 : 0;
        }
        return [.. o];
    }

    sealed class BitReader(byte[] data)
    {
        int _bit;
        public uint Bits(int n)
        {
            uint v = 0;
            for (var i = 0; i < n; i++, _bit++)
            {
                if (_bit >> 3 >= data.Length) throw new IndexOutOfRangeException();
                v = v << 1 | (uint)(data[_bit >> 3] >> (7 - (_bit & 7)) & 1);
            }
            return v;
        }
        public uint Ue()
        {
            var zeros = 0;
            while (Bits(1) == 0) if (++zeros > 31) throw new IndexOutOfRangeException();
            return zeros == 0 ? 0 : (1u << zeros) - 1 + Bits(zeros);
        }
        public int Se() { var k = Ue(); return (k & 1) == 1 ? (int)((k + 1) / 2) : -(int)(k / 2); }
    }
}

/// <summary>Cuts the byte stream of the encoder into pictures: each picture begins with a delimiter, so a picture is complete when the next one begins.</summary>
public sealed class H264Splitter
{
    readonly MemoryStream _buf = new();
    public const int MaxPicture = 4 * 1024 * 1024;

    public List<byte[]> Push(ReadOnlySpan<byte> data)
    {
        _buf.Write(data);
        var frames = new List<byte[]>();
        var all = _buf.GetBuffer().AsSpan(0, (int)_buf.Length);
        var start = -1; var consumed = 0;
        for (var i = 0; i + 3 < all.Length; i++)
        {
            if (all[i] != 0 || all[i + 1] != 0 || all[i + 2] != 1 || (all[i + 3] & 0x1F) != H264.Aud) continue;
            var at = i > 0 && all[i - 1] == 0 ? i - 1 : i;
            if (start >= 0 && at > start) frames.Add(all[start..at].ToArray());
            start = at; consumed = at; i += 3;
        }
        if (start < 0) consumed = Math.Max(0, all.Length - 4);   // no picture begun: keep only what might be the start of a delimiter
        var rest = all[consumed..].ToArray();
        if (rest.Length > MaxPicture) rest = [];
        _buf.SetLength(0); _buf.Write(rest);
        return frames;
    }
}

/// <summary>Turns decoded pictures (YUV 4:2:0, as every H.264 decoder gives them) into the BGRA pixels the screen shows.</summary>
public static class Yuv
{
    /// <summary>
    /// The planes may be separate (I420: u and v each with a step of 1) or woven together (NV12/NV21: u and v in one array, a step of 2).
    /// The colors are the usual ones of video (BT.601, limited range). With <paramref name="every"/> above 1 only every n-th pixel and row is
    /// taken (a smaller picture, for a phone, where the picture is small anyway). Rows are done in parallel.
    /// </summary>
    public static void ToBgra(byte[] y, int yOff, int yStride, byte[] u, int uOff, byte[] v, int vOff, int uvStride, int uvStep, int width, int height, byte[] dst, int every = 1)
    {
        int ow = width / every, oh = height / every;
        Parallel.For(0, (oh + 7) / 8, band =>
        {
            for (var orow = band * 8; orow < Math.Min(oh, band * 8 + 8); orow++)
            {
                var row = orow * every;
                var yi = yOff + row * yStride;
                var ci = (row >> 1) * uvStride;
                var o = orow * ow * 4;
                for (var ox = 0; ox < ow; ox++, o += 4)
                {
                    var x = ox * every;
                    var c = 298 * (y[yi + x] - 16);
                    var k = (x >> 1) * uvStep + ci;
                    var d = u[uOff + k] - 128; var e = v[vOff + k] - 128;
                    dst[o] = Clamp((c + 516 * d + 128) >> 8);
                    dst[o + 1] = Clamp((c - 100 * d - 208 * e + 128) >> 8);
                    dst[o + 2] = Clamp((c + 409 * e + 128) >> 8);
                    dst[o + 3] = 255;
                }
            }
        });
    }

    static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
}

/// <summary>Turns the pictures of one person's camera (H.264 access units) back into pictures for the screen.</summary>
public interface IVideoDecoder : IDisposable
{
    /// <summary>The next picture as it came (never blocks: a decoder that falls behind skips to the next key picture).</summary>
    void Push(byte[] accessUnit);
}

/// <summary>
/// What every decoder does the same way: it waits for a key picture before it starts (someone who comes in late), learns the picture size
/// from it (and starts again when the size changes), and decodes on a thread of its own, so the connection is never held up. When more
/// than about two seconds wait, it drops them and goes on at the next key picture: the picture is never late, it only stands still a moment.
/// </summary>
public abstract class VideoDecoderBase : IVideoDecoder
{
    readonly Queue<byte[]> _queue = new();
    readonly SemaphoreSlim _ready = new(0);
    readonly CancellationTokenSource _cts = new();
    readonly Thread _thread;
    bool _haveKey;
    (int W, int H) _size;
    (byte[] Sps, byte[] Pps)? _sets;
    protected const int MaxWaiting = 120;
    /// <summary>Gets each picture: width, height and BGRA pixels (4 bytes a pixel, rows without gaps). Called on the decoder's thread; the pixels may be reused afterwards.</summary>
    protected readonly Action<int, int, byte[]> Sink;

    protected VideoDecoderBase(Action<int, int, byte[]> sink)
    {
        Sink = sink;
        _thread = new Thread(Run) { IsBackground = true, Name = "video-decode" };
        _thread.Start();
    }

    protected CancellationToken Token => _cts.Token;

    public void Push(byte[] au)
    {
        lock (_queue)
        {
            if (_cts.IsCancellationRequested) return;
            if (!_haveKey)
            {
                if (!H264.IsKeyframe(au)) return;
                _haveKey = true;
            }
            if (_queue.Count >= MaxWaiting)
            {
                _queue.Clear();
                _haveKey = H264.IsKeyframe(au);
                if (!_haveKey) return;
            }
            _queue.Enqueue(au);
        }
        _ready.Release();
    }

    /// <summary>After a picture could not be decoded: wait for the next key picture.</summary>
    protected void Resync() { lock (_queue) { _queue.Clear(); _haveKey = false; } }

    void Run()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                _ready.Wait(_cts.Token);
                byte[] au;
                lock (_queue) { if (!_queue.TryDequeue(out au!)) continue; }
                if (H264.ParameterSets(au) is { } sets && H264.Size(sets.Sps) is { } size)
                {
                    var changed = size != _size || _sets is null || !_sets.Value.Sps.AsSpan().SequenceEqual(sets.Sps) || !_sets.Value.Pps.AsSpan().SequenceEqual(sets.Pps);
                    if (changed)
                    {
                        if (_size != default) Stop();
                        _size = size; _sets = sets;
                        try { Start(size.Width, size.Height, sets.Sps, sets.Pps); }
                        catch (Exception) { _size = default; _sets = null; Resync(); continue; }
                    }
                }
                if (_size == default) continue;
                try { Decode(au); } catch (Exception) when (!_cts.IsCancellationRequested) { Resync(); }
            }
        }
        catch (Exception) { /* stopped, or something broke: an error on this thread would end the whole program, so only the decoding ends */ }
        finally { try { Stop(); } catch { } }
    }

    /// <summary>Prepares for pictures of this size (SPS and PPS without start codes).</summary>
    protected abstract void Start(int width, int height, byte[] sps, byte[] pps);
    /// <summary>One access unit (with start codes, as it came). May throw: the decoder then waits for the next key picture.</summary>
    protected abstract void Decode(byte[] au);
    /// <summary>Ends what <see cref="Start"/> began.</summary>
    protected abstract void Stop();

    public virtual void Dispose()
    {
        lock (_queue) { if (_cts.IsCancellationRequested) return; _cts.Cancel(); _queue.Clear(); }
    }
}
