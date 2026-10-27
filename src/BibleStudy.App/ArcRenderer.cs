using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Renders the arc diagram like trekhleb.dev/bible-cross-references (MIT): every verse-level link is a hairline half-ellipse,
/// colored from the genre of its source to the genre of its target. Light is added up per pixel and tone-mapped with
/// log(1 + light) / log(1 + ceiling) while keeping the hue, so dense regions glow instead of burning out.
/// </summary>
static class ArcRenderer
{
    public const double SurfaceGray = 0x0d / 255.0;

    public sealed record Layout(int Width, int Height, double Scale)
    {
        public double AxisStart => 32 * Scale;
        public double AxisEnd => Width - 32 * Scale;
        public double Baseline => Height - 58 * Scale;
        public double MaxHeight => Baseline - 16 * Scale;
    }

    static readonly float[][] Palette = Enumerable.Range(0, 10).Select(i =>
    {
        var c = HomeView.Colors[(BookCategory)i];
        return new[] { c.R / 255f, c.G / 255f, c.B / 255f };
    }).ToArray();

    /// <summary>Brightness of a link: unvoted links stay faint, well-voted ones glow.</summary>
    public static double VoteWeight(int votes) => Math.Min(1, 0.2 + 0.8 * Math.Log10(1 + Math.Max(votes, 0)) / 2);

    public static Color GenreColor(int genre) => HomeView.Colors[(BookCategory)Math.Clamp(genre, 0, 9)];

    /// <summary>Returns BGRA pixels (opaque) of the links in <paramref name="ids"/> for the verse window [start, end].</summary>
    public static byte[] Render(VerseLinkSet links, int[] ids, double viewStart, double viewEnd, Layout layout, CancellationToken ct)
    {
        int w = layout.Width, h = layout.Height;
        var workers = Math.Clamp(Environment.ProcessorCount, 1, 8);
        var buffers = new float[workers][];
        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct }, worker =>
        {
            var buffer = new float[w * h * 3];
            buffers[worker] = buffer;
            for (var i = worker; i < ids.Length; i += workers)
            {
                if ((i & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
                Plot(links, ids[i], viewStart, viewEnd, layout, buffer);
            }
        });

        var ceiling = (float)Math.Max(ids.Length / 250.0, 20);
        var logCeiling = MathF.Log(1 + ceiling);
        var pixels = new byte[w * h * 4];
        Parallel.For(0, h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var at = (y * w + x) * 3;
                float r = 0, g = 0, b = 0;
                for (var k = 0; k < workers; k++) { r += buffers[k][at]; g += buffers[k][at + 1]; b += buffers[k][at + 2]; }
                var peak = MathF.Max(r, MathF.Max(g, b));
                float gr = 0, gg = 0, gb = 0;
                if (peak > 0)
                {
                    var intensity = Math.Clamp(MathF.Log(1 + peak) / logCeiling, 0f, 1f);
                    gr = r / peak * intensity; gg = g / peak * intensity; gb = b / peak * intensity;
                }
                var o = (y * w + x) * 4;
                pixels[o] = (byte)(255 * (SurfaceGray + gb * (1 - SurfaceGray)));
                pixels[o + 1] = (byte)(255 * (SurfaceGray + gg * (1 - SurfaceGray)));
                pixels[o + 2] = (byte)(255 * (SurfaceGray + gr * (1 - SurfaceGray)));
                pixels[o + 3] = 255;
            }
        });
        return pixels;
    }

    static void Plot(VerseLinkSet links, int id, double viewStart, double viewEnd, Layout layout, float[] buffer)
    {
        var scale = (layout.AxisEnd - layout.AxisStart) / (viewEnd - viewStart);
        double a = layout.AxisStart + (links.Src[id] + 0.5 - viewStart) * scale;
        double b = layout.AxisStart + ((links.TgtStart[id] + links.TgtEnd[id] + 1) / 2.0 - viewStart) * scale;
        double lo = Math.Min(a, b), hi = Math.Max(a, b);
        if (hi < 0 || lo > layout.Width) return;
        var radius = 0.5 * (b - a);
        var height = Math.Min(Math.Abs(radius), layout.MaxHeight);
        var mid = 0.5 * (a + b);
        var ar = Math.Abs(radius);
        var perimeter = Math.PI * (3 * (ar + height) - Math.Sqrt((3 * ar + height) * (ar + 3 * height))) / 2;
        var steps = (int)Math.Clamp(perimeter * 1.3 + 2, 4, 8000);
        var weight = (float)VoteWeight(links.Votes[id]);
        var from = Palette[links.VerseGenre[links.Src[id]]];
        var to = Palette[links.VerseGenre[links.TgtStart[id]]];
        int lastX = int.MinValue, lastY = int.MinValue, w = layout.Width, h = layout.Height;
        for (var k = 0; k <= steps; k++)
        {
            var t = (double)k / steps;
            var theta = Math.PI * t;
            var px = (int)Math.Floor(mid - radius * Math.Cos(theta));
            var py = (int)Math.Floor(layout.Baseline - height * Math.Sin(theta));
            if (px == lastX && py == lastY) continue;
            lastX = px; lastY = py;
            if ((uint)px >= (uint)w || (uint)py >= (uint)h) continue;
            var at = (py * w + px) * 3;
            var f = (float)t;
            buffer[at] += (from[0] + (to[0] - from[0]) * f) * weight;
            buffer[at + 1] += (from[1] + (to[1] - from[1]) * f) * weight;
            buffer[at + 2] += (from[2] + (to[2] - from[2]) * f) * weight;
        }
    }
}
