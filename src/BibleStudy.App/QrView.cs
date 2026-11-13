using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using QRCoder;

namespace BibleStudy.App;

/// <summary>
/// A QR code, always square and always whole: it takes the room it gets (up to <see cref="Size"/>) and never more, and keeps the white
/// quiet zone around it that cameras need. Black on white also in the dark look, because phone cameras read it best that way.
/// The code is a small image with one pixel per module, enlarged without smoothing: so it stays sharp at every size, and scaled up on
/// the projector no light seams appear between the modules (drawn as single squares, they would).
/// </summary>
public sealed class QrView : Control
{
    WriteableBitmap? _image;

    /// <summary>The largest size in pixels; smaller when the room is narrower.</summary>
    public double Size { get; set; } = 240;

    public string Text
    {
        set
        {
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(value, QRCodeGenerator.ECCLevel.M);
            var rows = data.ModuleMatrix;   // with a quiet zone of 4
            var n = rows.Count;
            var bitmap = new WriteableBitmap(new PixelSize(n, n), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var fb = bitmap.Lock())
            {
                var pixels = new int[n * n];
                for (var y = 0; y < n; y++)
                    for (var x = 0; x < n; x++)
                        pixels[y * n + x] = rows[y][x] ? unchecked((int)0xFF000000) : unchecked((int)0xFFFFFFFF);
                for (var y = 0; y < n; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels, y * n, fb.Address + y * fb.RowBytes, n);
            }
            _image?.Dispose();
            _image = bitmap;
            InvalidateMeasure(); InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size available)
    {
        var side = Math.Min(Size, Math.Min(double.IsInfinity(available.Width) ? Size : available.Width, double.IsInfinity(available.Height) ? Size : available.Height));
        return new Size(side, side);
    }

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (_image is not { } image || side <= 0) return;
        var n = image.PixelSize.Width;
        // whole pixels per module where there is room for it, so that all modules are equally wide
        var drawn = side / n >= 2 ? Math.Floor(side / n) * n : side;
        var rect = new Rect((Bounds.Width - side) / 2, (Bounds.Height - side) / 2, side, side);
        context.FillRectangle(Brushes.White, rect);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            context.DrawImage(image, new Rect(0, 0, n, n), rect.CenterRect(new Rect(0, 0, drawn, drawn)));
    }
}
