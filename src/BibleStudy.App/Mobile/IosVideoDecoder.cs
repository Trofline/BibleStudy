#if IOS
using System.Runtime.InteropServices;
using BibleStudy.Core.Collab.Media;
using CoreMedia;
using CoreVideo;
using VideoToolbox;

namespace BibleStudy.App;

/// <summary>
/// The camera pictures of the others on an iPhone: the phone's own H.264 decoder (VideoToolbox, in the chip). It takes the pieces with a
/// length before each instead of start codes, and SPS/PPS once in a "format description"; it gives the pictures as BGRA.
/// </summary>
sealed class IosVideoDecoder(Action<int, int, byte[]> sink) : VideoDecoderBase(sink)
{
    CMVideoFormatDescription? _format;
    VTDecompressionSession? _session;
    byte[] _bgra = [];

    protected override void Start(int width, int height, byte[] sps, byte[] pps)
    {
        _format = CMVideoFormatDescription.FromH264ParameterSets(new List<byte[]> { sps, pps }, 4, out var error);
        if (_format is null || error != CMFormatDescriptionError.None) throw new InvalidOperationException("format " + error);
        var attributes = new CVPixelBufferAttributes { PixelFormatType = CVPixelFormatType.CV32BGRA };
        _session = VTDecompressionSession.Create(OnPicture, _format, null, attributes) ?? throw new InvalidOperationException("session");
    }

    protected override void Decode(byte[] au)
    {
        var session = _session ?? throw new InvalidOperationException();
        var data = H264.ToLengthPrefixed(au);
        if (data.Length == 0) return;
        using var block = CMBlockBuffer.FromMemoryBlock(data, 0, CMBlockBufferFlags.AssureMemoryNow, out var blockError);
        if (block is null || blockError != CMBlockBufferError.None) throw new InvalidOperationException("block " + blockError);
        using var sample = CMSampleBuffer.CreateReady(block, _format, 1, null, [(nuint)data.Length], out var sampleError);
        if (sample is null || sampleError != CMSampleBufferError.None) throw new InvalidOperationException("sample " + sampleError);
        // without the flag for asynchronous work the picture is decoded before this returns, on this thread
        var status = session.DecodeFrame(sample, VTDecodeFrameFlags.EnableTemporalProcessing, IntPtr.Zero, out _);
        if (status != VTStatus.Ok) throw new InvalidOperationException("decode " + status);
    }

    void OnPicture(IntPtr sourceFrame, VTStatus status, VTDecodeInfoFlags flags, CVImageBuffer? buffer, CMTime presentationTimeStamp, CMTime presentationDuration)
    {
        if (status != VTStatus.Ok || buffer is not CVPixelBuffer pixels) return;
        if (pixels.Lock(CVPixelBufferLock.ReadOnly) != CVReturn.Success) return;
        try
        {
            int w = (int)pixels.Width, h = (int)pixels.Height, stride = (int)pixels.BytesPerRow;
            if (_bgra.Length != w * h * 4) _bgra = new byte[w * h * 4];
            var at = pixels.BaseAddress;
            for (var row = 0; row < h; row++) Marshal.Copy(at + row * stride, _bgra, row * w * 4, w * 4);
            Sink(w, h, _bgra);
        }
        catch (Exception) { }
        finally { pixels.Unlock(CVPixelBufferLock.ReadOnly); }
    }

    protected override void Stop()
    {
        var session = Interlocked.Exchange(ref _session, null);
        session?.Dispose();   // ends the session (the binding has no Invalidate of its own)
        _format?.Dispose(); _format = null;
    }
}
#endif
