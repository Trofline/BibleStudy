using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace BibleStudy.App;

/// <summary>
/// The closing animation "particle dissolve": a picture of the window crumbles into sand. A wave runs over the picture from the upper left to
/// the lower right (broken up by noise); every grain lets go when the wave reaches it, is taken by the wind, whirls, shrinks and goes out.
/// A few grains glow in the accent colour like sparks. The window is see-through for it, so the grains fly over the desktop.
/// </summary>
public partial class MainWindow
{
    Action? _dissolveRestore;

    /// <summary>The window must already be able to be see-through when the animation starts (changing that later flashes the desktop).</summary>
    void PrepareCloseTransparency() =>
        TransparencyLevelHint = CloseStyle == "dissolve" ? [WindowTransparencyLevel.Transparent] : [WindowTransparencyLevel.None];

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
        var spark = Res("BsAccent") is ISolidColorBrush sb ? sb.Color : Color.Parse("#E3B65F");

        // the grains: small blocks of the picture, each with its own start, speed and whirl
        var b = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(pw * (double)ph / 520000.0)));
        var cols = (pw + b - 1) / b; var rows = (ph + b - 1) / b; var n = cols * rows;
        const float Total = 1.7f;       // seconds until the last grain has let go
        var g = await Task.Run(() => MakeGrains(picture, stride, pw, ph, b, cols, rows, n, scale, Total, spark));

        // the window shows the picture; the grains are drawn into a bitmap. The picture stands before the window is switched, so nothing flashes.
        var oldBackground = Background; var oldContent = Content;
        var bmp = new WriteableBitmap(new PixelSize(pw, ph), new Vector(96 * scale, 96 * scale), PixelFormat.Bgra8888, AlphaFormat.Premul);
        PushFrame(bmp, picture, stride, ph);
        var image = new Image { Source = bmp, Stretch = Stretch.Fill };
        _dissolveRestore = () => { Content = oldContent; Background = oldBackground; };
        Background = Brushes.Transparent;
        Content = image;
        await Task.Delay(40);

        var still = (byte[])picture.Clone();       // what has not let go yet
        var frame = new byte[picture.Length];
        var next = 0;                               // the grains in order[..next] have let go
        var lo = 0;                                 // the grains in order[..lo] are gone
        const float MaxLife = 1.4f;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var t = (float)clock.Elapsed.TotalSeconds;
            while (next < n && g.T0[g.Order[next]] <= t)
            {
                var i = g.Order[next++];
                var w = Math.Min(b, pw - g.X[i]) * 4;
                for (var y = g.Y[i]; y < Math.Min(ph, g.Y[i] + b); y++) Array.Clear(still, y * stride + g.X[i] * 4, w);
            }
            while (lo < next && t - g.T0[g.Order[lo]] > MaxLife) lo++;
            var from = lo; var to = next;
            await Task.Run(() =>
            {
                Buffer.BlockCopy(still, 0, frame, 0, frame.Length);
                Parallel.For(0, 8, part =>
                {
                    for (var k = from + part; k < to; k += 8) DrawGrain(g, g.Order[k], t, frame, pw, ph, b, scale);
                });
            });
            PushFrame(bmp, frame, stride, ph);
            image.InvalidateVisual();
            if (next >= n && lo >= n) break;
            if (t > Total + MaxLife + 0.3f) break;   // never hold the program back
            await Task.Delay(1);
        }
    }

    sealed class Grains
    {
        public int[] X = [], Y = [], Order = [];
        public uint[] Color = [];
        public float[] T0 = [], Life = [], Vx = [], Vy = [], Swirl = [], Phase = [], Glow = [];
    }

    static Grains MakeGrains(byte[] picture, int stride, int pw, int ph, int b, int cols, int rows, int n, double scale, float total, Color spark)
    {
        var g = new Grains
        {
            X = new int[n], Y = new int[n], Color = new uint[n], T0 = new float[n], Life = new float[n],
            Vx = new float[n], Vy = new float[n], Swirl = new float[n], Phase = new float[n], Glow = new float[n],
        };
        var rnd = new Random(7);
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var i = r * cols + c;
                g.X[i] = c * b; g.Y[i] = r * b;
                var sx = Math.Min(pw - 1, c * b + b / 2); var sy = Math.Min(ph - 1, r * b + b / 2);
                var col = BitConverter.ToUInt32(picture, sy * stride + sx * 4);
                double x = (double)g.X[i] / pw, y = (double)g.Y[i] / ph;
                // the wave runs from the upper left to the lower right, ragged by big clumps and by single grains
                var clump = 0.5 + 0.5 * Math.Sin(x * 8.0 + Math.Sin(y * 6.0) * 1.8) * Math.Cos(y * 10.0 - x * 3.0);
                var front = x * 0.8 + y * 0.2;
                g.T0[i] = (float)(0.03 + (front * 0.62 + clump * 0.22 + rnd.NextDouble() * 0.16) * total);
                g.Life[i] = (float)(0.7 + rnd.NextDouble() * 0.6);
                // the wind: strongly to the right and a little up, grains differ a lot; a few are thrown far
                var gust = rnd.NextDouble() < 0.12 ? 1.9 : 1.0;
                g.Vx[i] = (float)((260 + rnd.NextDouble() * 760) * gust * scale);
                g.Vy[i] = (float)(((rnd.NextDouble() - 0.5) * 620 - 150) * scale);
                g.Swirl[i] = (float)((14 + rnd.NextDouble() * 46) * scale);
                g.Phase[i] = (float)(rnd.NextDouble() * Math.PI * 2);
                if (rnd.NextDouble() < 0.045)
                {
                    // a spark: the accent colour, brightened, and it flickers
                    g.Glow[i] = (float)(0.6 + rnd.NextDouble() * 0.4);
                    var k = 0.35 + rnd.NextDouble() * 0.4;
                    byte Mix(byte a, byte bb) => (byte)(a + (bb - a) * k);
                    col = 0xFF000000u | ((uint)Mix(spark.R, 255) << 16) | ((uint)Mix(spark.G, 245) << 8) | Mix(spark.B, 220);
                }
                else
                {
                    // each grain a little lighter or darker, so the sand does not look flat
                    var j = (float)(0.85 + rnd.NextDouble() * 0.3);
                    uint a = col >> 24 & 255, rd = (uint)Math.Min(255, (col >> 16 & 255) * j), gr = (uint)Math.Min(255, (col >> 8 & 255) * j), bl = (uint)Math.Min(255, (col & 255) * j);
                    col = (a << 24) | (Math.Min(a, rd) << 16) | (Math.Min(a, gr) << 8) | Math.Min(a, bl);
                }
                g.Color[i] = col;
            }
        // order by start time (counting sort: fast for many grains)
        const int Buckets = 4096;
        var count = new int[Buckets + 1];
        var maxT = g.T0.Max() + 0.001f;
        int Bucket(int i) => Math.Min(Buckets - 1, (int)(g.T0[i] / maxT * Buckets));
        for (var i = 0; i < n; i++) count[Bucket(i) + 1]++;
        for (var k = 0; k < Buckets; k++) count[k + 1] += count[k];
        g.Order = new int[n];
        for (var i = 0; i < n; i++) g.Order[count[Bucket(i)]++] = i;
        return g;
    }

    static void DrawGrain(Grains g, int i, float t, byte[] frame, int pw, int ph, int b, double scale)
    {
        var travel = t - g.T0[i];
        var age = travel / g.Life[i];
        if (age >= 1 || age < 0) return;
        var e = age * age * (1.5f - 0.5f * age);                 // the grain first lets go slowly, then the wind takes it
        var x = g.X[i] + g.Vx[i] * travel * (0.25f + 0.75f * e) + (float)Math.Sin(travel * 8 + g.Phase[i]) * g.Swirl[i] * age;
        var y = g.Y[i] + g.Vy[i] * travel * e + (float)Math.Cos(travel * 6 + g.Phase[i]) * g.Swirl[i] * age + 70f * (float)scale * age * age;
        var alpha = age < 0.35f ? 1f : 1f - (age - 0.35f) / 0.65f;
        alpha *= alpha;
        var size = Math.Max(1, (int)Math.Round(b * (1 - 0.5f * age)));
        var col = g.Color[i];
        if (g.Glow[i] > 0)
        {
            // a spark: a little bigger, flickering
            alpha *= 0.6f + 0.4f * (float)Math.Sin(travel * 40 + g.Phase[i]);
            alpha = Math.Max(0, alpha) * g.Glow[i] + (1 - g.Glow[i]) * alpha;
            size += 1;
        }
        uint a = col >> 24 & 255, rd = col >> 16 & 255, gr = col >> 8 & 255, bl = col & 255;
        var f = (uint)(Math.Clamp(alpha, 0, 1) * 256);
        var shaded = ((a * f >> 8) << 24) | ((rd * f >> 8) << 16) | ((gr * f >> 8) << 8) | (bl * f >> 8);
        var px = (int)x; var py = (int)y;
        for (var yy = Math.Max(0, py); yy < Math.Min(ph, py + size); yy++)
        {
            var row = yy * pw;
            for (var xx = Math.Max(0, px); xx < Math.Min(pw, px + size); xx++)
                MemoryMarshal.Write(frame.AsSpan((row + xx) * 4, 4), in shaded);
        }
    }

    static void PushFrame(WriteableBitmap bmp, byte[] pixels, int stride, int height)
    {
        using var fb = bmp.Lock();
        if (fb.RowBytes == stride) Marshal.Copy(pixels, 0, fb.Address, stride * height);
        else for (var y = 0; y < height; y++) Marshal.Copy(pixels, y * stride, fb.Address + y * fb.RowBytes, stride);
    }
}
