#if ANDROID
using System.Runtime.InteropServices;
using Android.Media;
using Android.Runtime;
using BibleStudy.Core.Collab.Media;
using Java.Nio;

namespace BibleStudy.App;

/// <summary>
/// The camera pictures of the others on an Android phone: the phone's own H.264 decoder (MediaCodec, in the chip, so it hardly costs battery).
/// The access units go in on the decoder's thread; a second thread takes the decoded pictures (YUV) out and turns them into screen pixels,
/// at most about 960 wide (the picture is small on a phone).
/// </summary>
sealed class AndroidVideoDecoder(Action<int, int, byte[]> sink) : VideoDecoderBase(sink)
{
    const int ColorFormatYuv420Flexible = 0x7F420888;
    const int MaxWidth = 960;
    MediaCodec? _codec;
    Thread? _out;
    volatile bool _running;
    long _pts;

    protected override void Start(int width, int height, byte[] sps, byte[] pps)
    {
        var format = MediaFormat.CreateVideoFormat("video/avc", width, height);
        format.SetByteBuffer("csd-0", ByteBuffer.Wrap([0, 0, 0, 1, .. sps]));
        format.SetByteBuffer("csd-1", ByteBuffer.Wrap([0, 0, 0, 1, .. pps]));
        format.SetInteger(MediaFormat.KeyColorFormat, ColorFormatYuv420Flexible);
        format.SetInteger("low-latency", 1);   // Android 11 and newer: a picture comes out as soon as it is decoded
        var codec = MediaCodec.CreateDecoderByType("video/avc");
        codec.Configure(format, null, null, MediaCodecConfigFlags.None);
        codec.Start();
        _codec = codec;
        _running = true;
        _out = new Thread(() => Drain(codec)) { IsBackground = true, Name = "video-out" };
        _out.Start();
    }

    protected override void Decode(byte[] au)
    {
        var codec = _codec ?? throw new InvalidOperationException();
        var index = codec.DequeueInputBuffer(100_000);
        if (index < 0) throw new TimeoutException();   // the decoder is stuck: begin again at the next key picture
        var buffer = codec.GetInputBuffer(index) ?? throw new InvalidOperationException();
        buffer.Clear();
        buffer.Put(au);
        codec.QueueInputBuffer(index, 0, au.Length, _pts += 16_000, MediaCodecBufferFlags.None);
    }

    void Drain(MediaCodec codec)
    {
        // an error on this thread would end the whole program: whatever goes wrong only ends the pictures
        try
        {
            var info = new MediaCodec.BufferInfo();
            byte[] y = [], u = [], v = [], bgra = [];
            while (_running)
            {
                var index = codec.DequeueOutputBuffer(info, 50_000);
                if (index < 0) continue;   // nothing yet, or a change of format
                Android.Media.Image? image = null;
                try
                {
                    image = codec.GetOutputImage(index);
                    var planes = image?.GetPlanes();
                    if (image is null || planes is not { Length: >= 3 }) continue;
                    var crop = image.CropRect;
                    int left = crop?.Left ?? 0, top = crop?.Top ?? 0;
                    int w = crop is { } c1 && c1.Width() > 0 ? c1.Width() : image.Width, h = crop is { } c2 && c2.Height() > 0 ? c2.Height() : image.Height;
                    left &= ~1; top &= ~1;
                    Copy(planes[0].Buffer, ref y); Copy(planes[1].Buffer, ref u); Copy(planes[2].Buffer, ref v);
                    int ys = planes[0].RowStride, cs = planes[1].RowStride, step = planes[1].PixelStride;
                    var every = Math.Max(1, (w + MaxWidth - 1) / MaxWidth);
                    int ow = w / every, oh = h / every;
                    if (bgra.Length != ow * oh * 4) bgra = new byte[ow * oh * 4];
                    Yuv.ToBgra(y, top * ys + left, ys, u, (top / 2) * cs + (left / 2) * step, v, (top / 2) * cs + (left / 2) * step, cs, step, w, h, bgra, every);
                    Sink(ow, oh, bgra);
                }
                finally
                {
                    try { image?.Close(); } catch (Exception) { }
                    image?.Dispose();
                    codec.ReleaseOutputBuffer(index, false);
                }
            }
        }
        catch (Exception) { }
    }

    static void Copy(ByteBuffer? from, ref byte[] to)
    {
        if (from is null) return;
        from.Rewind();
        var n = from.Remaining();
        if (to.Length < n) to = new byte[n];
        var address = JNIEnv.GetDirectBufferAddress(from.Handle);
        if (address != IntPtr.Zero) Marshal.Copy(address, to, 0, n);
        else from.Get(to, 0, n);
    }

    protected override void Stop()
    {
        _running = false;
        var codec = Interlocked.Exchange(ref _codec, null);
        _out?.Join(500); _out = null;
        if (codec is null) return;
        try { codec.Stop(); } catch (Exception) { }
        try { codec.Release(); } catch (Exception) { }
    }

    public override void Dispose()
    {
        base.Dispose();
        _running = false;   // the decoder's thread ends it (Stop)
    }
}
#endif
