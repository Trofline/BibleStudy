using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace BibleStudy.App;

/// <summary>
/// The closing animation "particle dissolve": a picture of the window crumbles into sand. A wave of wind runs from left to right (broken up by
/// noise), every grain of the picture lets go when the wave reaches it, drifts away on the wind, whirls a little, shrinks and goes out.
/// The window itself is made see-through for it, so the grains fly over the desktop.
/// </summary>
public partial class MainWindow
{
    Action? _dissolveRestore;

    async Task PlayDissolveAsync(Control root)
    {
        var scale = RenderScaling;
        var pw = Math.Max(16, (int)Math.Ceiling(root.Bounds.Width * scale));
        var ph = Math.Max(16, (int)Math.Ceiling(root.Bounds.Height * scale));
        var stride = pw * 4;

        // a picture of the window (premultiplied BGRA)
        var picture = new byte[stride * ph];
        using (var rtb = new RenderTargetBitmap(new PixelSize(pw, ph), new Vector(96 * scale, 96 * scale)))
        {
            rtb.Render(root);
            var pin = GCHandle.Alloc(picture, GCHandleType.Pinned);
            try { rtb.CopyPixels(new PixelRect(0, 0, pw, ph), pin.AddrOfPinnedObject(), picture.Length, stride); }
            finally { pin.Free(); }
        }

        // the grains: blocks of the picture, each with its own start, speed and whirl
        var b = Math.Max(3, (int)Math.Ceiling(Math.Sqrt(pw * (double)ph / 130000.0)));
        var cols = (pw + b - 1) / b; var rows = (ph + b - 1) / b; var n = cols * rows;
        var rnd = new Random(7);
        var ox = new int[n]; var oy = new int[n]; var color = new uint[n];
        var t0 = new float[n]; var life = new float[n]; var vx = new float[n]; var vy = new float[n]; var swirl = new float[n]; var phase = new float[n];
        const float Total = 1.9f;       // seconds for the whole thing
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var i = r * cols + c;
                ox[i] = c * b; oy[i] = r * b;
                var sx = Math.Min(pw - 1, c * b + b / 2); var sy = Math.Min(ph - 1, r * b + b / 2);
                color[i] = BitConverter.ToUInt32(picture, sy * stride + sx * 4);
                // the wave: from the left to the right, ragged by large clumps and by single grains
                double x = (double)ox[i] / pw, y = (double)oy[i] / ph;
                var clump = 0.5 + 0.5 * Math.Sin(x * 9.0 + Math.Sin(y * 7.0) * 1.6) * Math.Cos(y * 11.0 - x * 4.0);
                t0[i] = (float)(0.04 + (x * 0.58 + clump * 0.17 + rnd.NextDouble() * 0.13) * Total * 0.62);
                life[i] = (float)(0.75 + rnd.NextDouble() * 0.45);
                var wind = (170 + rnd.NextDouble() * 520) * scale;
                vx[i] = (float)wind;
                vy[i] = (float)(((rnd.NextDouble() - 0.5) * 300 - 70) * scale);
                swirl[i] = (float)((8 + rnd.NextDouble() * 22) * scale);
                phase[i] = (float)(rnd.NextDouble() * Math.PI * 2);
            }
        var order = Enumerable.Range(0, n).OrderBy(i => t0[i]).ToArray();

        // the window becomes see-through and shows the picture; the grains are drawn into a bitmap
        var oldBackground = Background; var oldHint = TransparencyLevelHint; var oldContent = Content;
        var bmp = new WriteableBitmap(new PixelSize(pw, ph), new Vector(96 * scale, 96 * scale), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var image = new Image { Source = bmp, Stretch = Stretch.Fill };
        _dissolveRestore = () => { Content = oldContent; Background = oldBackground; TransparencyLevelHint = oldHint; };
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Content = image;
        PushFrame(bmp, picture, stride, ph);
        await Task.Delay(60);

        var still = (byte[])picture.Clone();       // what has not let go yet
        var frame = new byte[picture.Length];
        var next = 0;                               // the grains in order[..next] have let go
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var t = (float)clock.Elapsed.TotalSeconds;
            while (next < n && t0[order[next]] <= t)
            {
                var i = order[next++];
                for (var y = oy[i]; y < Math.Min(ph, oy[i] + b); y++) Array.Clear(still, y * stride + ox[i] * 4, Math.Min(b, pw - ox[i]) * 4);
            }
            await Task.Run(() =>
            {
                Buffer.BlockCopy(still, 0, frame, 0, frame.Length);
                Parallel.For(0, 8, part =>
                {
                    for (var k = part; k < next; k += 8)
                    {
                        var i = order[k];
                        var age = (t - t0[i]) / life[i];
                        if (age >= 1) continue;
                        var e = age * age * (1.4f - 0.4f * age);              // slow at first: the grain lets go, then the wind takes it
                        var travel = t - t0[i];
                        var x = ox[i] + vx[i] * travel * (0.35f + 0.65f * e) + (float)Math.Sin(travel * 7 + phase[i]) * swirl[i] * age;
                        var y = oy[i] + vy[i] * travel * e + (float)Math.Cos(travel * 5 + phase[i]) * swirl[i] * age + 40 * scale * age * age;
                        var alpha = age < 0.4f ? 1f : 1f - (age - 0.4f) / 0.6f;
                        alpha *= alpha;
                        var size = Math.Max(1, (int)Math.Round(b * (1 - 0.55f * age)));
                        var px = (int)x; var py = (int)y;
                        var col = color[i];
                        uint a = (col >> 24) & 255, bl = col & 255, g = (col >> 8) & 255, rd = (col >> 16) & 255;
                        var f = (uint)(alpha * 256);
                        var shaded = ((a * f >> 8) << 24) | ((rd * f >> 8) << 16) | ((g * f >> 8) << 8) | (bl * f >> 8);
                        for (var yy = Math.Max(0, py); yy < Math.Min(ph, py + size); yy++)
                        {
                            var row = yy * pw;
                            for (var xx = Math.Max(0, px); xx < Math.Min(pw, px + size); xx++)
                                MemoryMarshal.Write(frame.AsSpan((row + xx) * 4, 4), in shaded);
                        }
                    }
                });
            });
            PushFrame(bmp, frame, stride, ph);
            image.InvalidateVisual();
            if (t > Total + 0.1f && next >= n) break;
            if (t > Total * 1.9f) break;           // never hold the program back
            await Task.Delay(1);
        }
    }

    static void PushFrame(WriteableBitmap bmp, byte[] pixels, int stride, int height)
    {
        using var fb = bmp.Lock();
        if (fb.RowBytes == stride) Marshal.Copy(pixels, 0, fb.Address, stride * height);
        else for (var y = 0; y < height; y++) Marshal.Copy(pixels, y * stride, fb.Address + y * fb.RowBytes, stride);
    }
}
