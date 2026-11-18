using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace BibleStudy.App;

/// <summary>
/// Pictures in documents: a button of the ribbon puts a picture from a file into the vault and a line "![name](bild:id)" into the text;
/// the finished view, the print page and the HTML page show it, a Markdown export saves it next to the files.
/// </summary>
public partial class MainWindow
{
    /// <summary>A line that is only a picture.</summary>
    [GeneratedRegex(@"^\s*!\[(?<n>[^\]]*)\]\(bild:(?<id>[0-9a-f]{8,64})\)\s*$")]
    private static partial Regex DocImageLine();

    /// <summary>A picture anywhere in a text.</summary>
    [GeneratedRegex(@"!\[(?<n>[^\]]*)\]\(bild:(?<id>[0-9a-f]{8,64})\)")]
    private static partial Regex DocImageRef();

    readonly Dictionary<string, Bitmap> _imageCache = [];

    /// <summary>The text of a document for a one-line preview: pictures as their name, links and marks without their signs.</summary>
    static string PlainPreview(string body) =>
        BibleStudy.Core.Models.DocumentLinks.WithLinkTexts(DocImageRef().Replace(body, m => "▣ " + m.Groups["n"].Value + " "))
            .ReplaceLineEndings(" ").Replace("#", "").Replace("**", "").Replace(">", "").Trim();

    async Task InsertImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L("Bild einfügen"), AllowMultiple = true, FileTypeFilter = [FilePickerFileTypes.ImageAll] });
        if (files.Count == 0) return;
        var text = new System.Text.StringBuilder();
        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                var (data, mime) = ShrinkImage(memory.ToArray(), file.Name);
                var id = _vault.AddImage(data, mime);
                var name = Regex.Replace(Path.GetFileNameWithoutExtension(file.Name), @"[\[\]()]", " ").Trim();
                text.Append("\n\n![" + name + "](bild:" + id + ")\n\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                DocInfo.Text = L("Das Bild ließ sich nicht laden: {0}", ex.Message);
            }
        }
        if (text.Length == 0) return;
        if (DocReadBtn.IsChecked == true) DocReadBtn.IsChecked = false;
        InsertIntoDoc(text.ToString(), null);
    }

    /// <summary>
    /// A big picture is made smaller before it goes into the vault (at most 1600 pixels wide), so a few photos do not make the vault huge (the vault
    /// travels whole to the other devices); a small one is kept exactly as it is. A photo becomes a JPEG (as a PNG it would be bigger than before),
    /// a picture with see-through parts stays a PNG.
    /// </summary>
    internal static (byte[] Data, string Mime) ShrinkImage(byte[] original, string fileName)
    {
        using var input = new MemoryStream(original);
        using var bitmap = new Bitmap(input);   // also checks that it is a picture
        var mime = Path.GetExtension(fileName).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", ".bmp" => "image/bmp", _ => "image/png" };
        if (original.Length <= 1_500_000 && bitmap.PixelSize.Width <= 1800) return (original, mime);
        try
        {
            using var decoded = SkiaSharp.SKBitmap.Decode(original);
            if (decoded is { Width: > 0, Height: > 0 })
            {
                var w = Math.Min(1600, decoded.Width);
                var h = Math.Max(1, (int)Math.Round(decoded.Height * (w / (double)decoded.Width)));
                using var scaled = w == decoded.Width ? decoded.Copy() : decoded.Resize(new SkiaSharp.SKImageInfo(w, h, decoded.ColorType, decoded.AlphaType), new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell));
                var photo = mime == "image/jpeg" || decoded.AlphaType == SkiaSharp.SKAlphaType.Opaque || !HasSeeThrough(scaled);
                using var encoded = scaled?.Encode(photo ? SkiaSharp.SKEncodedImageFormat.Jpeg : SkiaSharp.SKEncodedImageFormat.Png, 85);
                if (encoded is not null && encoded.Size > 0) return (encoded.ToArray(), photo ? "image/jpeg" : "image/png");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { /* the way below */ }
        var width = Math.Min(1600, bitmap.PixelSize.Width);
        var size = new PixelSize(width, Math.Max(1, (int)Math.Round(bitmap.PixelSize.Height * (width / (double)bitmap.PixelSize.Width))));
        using var small = bitmap.CreateScaledBitmap(size, BitmapInterpolationMode.HighQuality);
        using var output = new MemoryStream();
        small.Save(output);
        return (output.ToArray(), "image/png");
    }

    /// <summary>True when some pixel is not fully opaque (a logo or drawing on a see-through background must stay a PNG).</summary>
    static bool HasSeeThrough(SkiaSharp.SKBitmap? b)
    {
        if (b is null) return false;
        for (var y = 0; y < b.Height; y += Math.Max(1, b.Height / 200))
            for (var x = 0; x < b.Width; x += Math.Max(1, b.Width / 200))
                if (b.GetPixel(x, y).Alpha < 255) return true;
        return false;
    }

    /// <summary>A picture of the finished view; one that is not in this vault says so.</summary>
    Control PictureControl(string id, string name)
    {
        if (!_imageCache.TryGetValue(id, out var bitmap))
        {
            try { if (_vault.GetImage(id) is { } img) _imageCache[id] = bitmap = new Bitmap(new MemoryStream(img.Data)); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { /* shown as missing */ }
        }
        if (bitmap is null) return new TextBlock { Text = L("Bild fehlt: {0}", name), Classes = { "caption" } };
        var panel = new StackPanel { Spacing = 4, Margin = new(0, 6) };
        panel.Children.Add(new Image { Source = bitmap, MaxWidth = 720, MaxHeight = 720, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
        if (name.Length > 0) panel.Children.Add(new TextBlock { Text = name, Classes = { "caption" } });
        return panel;
    }
}
