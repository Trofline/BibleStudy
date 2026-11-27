using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BibleStudy.Core.Collab;
using BibleStudy.Core.Collab.Media;

namespace BibleStudy.App;

/// <summary>
/// The camera as a video stream (H.264): the computer that sends makes it with FFmpeg; each receiver has one decoder per person (FFmpeg on a
/// computer, the phone's own decoder on Android and iPhone). The pictures are written into three bitmaps per person in turn: one is shown,
/// one waits for the screen, one is being written, so nothing is made anew for every picture.
/// </summary>
public partial class MainWindow
{
    /// <summary>Three bitmaps of one size for one person's pictures (see above).</summary>
    sealed class VideoSurface(int width, int height) : IDisposable
    {
        readonly WriteableBitmap[] _bitmaps = [.. Enumerable.Range(0, 3).Select(_ => new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque))];
        int _shown = -1, _waiting = -1;
        bool _disposed;

        public bool Fits(int w, int h) => w == width && h == height;

        /// <summary>A new picture (BGRA, rows without gaps), on the decoder's thread.</summary>
        public void Write(byte[] bgra)
        {
            lock (_bitmaps)
            {
                if (_disposed) return;
                var i = Enumerable.Range(0, 3).First(n => n != _shown && n != _waiting);
                using (var fb = _bitmaps[i].Lock())
                {
                    if (fb.RowBytes == width * 4) Marshal.Copy(bgra, 0, fb.Address, width * height * 4);
                    else for (var row = 0; row < height; row++) Marshal.Copy(bgra, row * width * 4, fb.Address + row * fb.RowBytes, width * 4);
                }
                _waiting = i;
            }
        }

        /// <summary>The newest picture for the screen, or null when none came since the last one.</summary>
        public WriteableBitmap? Take()
        {
            lock (_bitmaps)
            {
                if (_disposed || _waiting < 0) return null;
                _shown = _waiting; _waiting = -1;
                return _bitmaps[_shown];
            }
        }

        public void Dispose()
        {
            lock (_bitmaps) { if (_disposed) return; _disposed = true; foreach (var b in _bitmaps) b.Dispose(); }
        }
    }

    sealed class VideoIn
    {
        public IVideoDecoder? Decoder;
        public VideoSurface? Surface;
        public int Posted;
        public bool Ended;
    }

    readonly Dictionary<string, VideoIn> _videoIn = [];
    const int MaxVideoUnit = 2_000_000;   // one picture of the video stream; a Full HD key picture is about 100 to 300 KB

    /// <summary>A decoder for this platform: the phone's own, or FFmpeg on a computer (null when there is none).</summary>
    static IVideoDecoder? NewVideoDecoder(Action<int, int, byte[]> sink, int maxWidth)
    {
#if ANDROID
        return new AndroidVideoDecoder(sink);
#elif IOS
        return new IosVideoDecoder(sink);
#else
        return MediaIO.StartH264Decoder(sink, maxWidth);
#endif
    }

    /// <summary>The decoder of one person (made at the first picture). <paramref name="maxWidth"/> makes the pictures smaller (the own picture is only a preview).</summary>
    VideoIn VideoInOf(string id, int maxWidth = 0)
    {
        lock (_videoIn)
        {
            if (_videoIn.TryGetValue(id, out var v)) return v;
            v = new VideoIn();
            try { v.Decoder = NewVideoDecoder((w, h, bgra) => OnDecoded(id, v, w, h, bgra), maxWidth); } catch (Exception) { }
            _videoIn[id] = v;
            return v;
        }
    }

    /// <summary>A decoded picture (on the decoder's thread): into the person's bitmaps, then to the screen when the screen took the one before.</summary>
    void OnDecoded(string id, VideoIn v, int w, int h, byte[] bgra)
    {
        VideoSurface surface;
        VideoSurface? old = null;
        lock (v)
        {
            if (v.Ended) return;
            if (v.Surface is null || !v.Surface.Fits(w, h)) { old = v.Surface; v.Surface = new VideoSurface(w, h); }
            surface = v.Surface;
        }
        // another size: the old bitmaps go once the new picture is surely on the screen
        if (old is not null) OnUi(() => Avalonia.Threading.DispatcherTimer.RunOnce(old.Dispose, TimeSpan.FromSeconds(2)));
        surface.Write(bgra);
        if (Interlocked.Exchange(ref v.Posted, 1) != 0) return;
        OnUi(() =>
        {
            Interlocked.Exchange(ref v.Posted, 0);
            if (v.Ended || v.Surface?.Take() is not { } bmp) return;
            if (id == "me") { if (_camOn) ShowPicture("me", L("Du"), PeopleInkOf(null), bmp); return; }
            if (_people.TryGetValue(id, out var p)) ShowPicture(id, p.Name, p.Ink, bmp);
        });
    }

    /// <summary>Ends the decoder of one person and frees its bitmaps (on the UI thread, after the picture left the screen).</summary>
    void StopVideoIn(string id)
    {
        VideoIn? v;
        lock (_videoIn) _videoIn.Remove(id, out v);
        if (v is null) return;
        lock (v) v.Ended = true;
        v.Decoder?.Dispose();
        v.Surface?.Dispose();
    }

    /// <summary>A picture of someone's video stream, straight from the connection (not on the UI thread): every one is needed, as each builds on the one before.</summary>
    void OnVideoUnit(ICollabLink link, string fid, string fn, string raw)
    {
        byte[] au;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.String) return;
            au = Convert.FromBase64String(d.GetString()!);
        }
        catch (Exception) { return; }
        if (au.Length is < 5 or > MaxVideoUnit) return;
        bool known;
        lock (_videoIn) known = _videoIn.ContainsKey(fid);
        if (!known) OnUi(() => TouchPerson(link, fid, fn));   // the person is shown with name and color
        VideoInOf(fid).Decoder?.Push(au);
    }
}
