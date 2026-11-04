namespace BibleStudy.Core.Search;

/// <summary>A word of the cloud after placing: <see cref="Index"/> is its position in the list that was given.</summary>
public sealed record CloudPlacement(int Index, double X, double Y, double Width, double Height, double FontSize);

/// <summary>
/// Lays out a word cloud: the heaviest word goes to the middle, the others follow on a spiral until they find a free place.
/// The size of a word grows with its weight. Words that find no room are left out (the caller can say so).
/// </summary>
public static class WordCloudLayout
{
    /// <param name="measure">Size of a text at a font size, as the screen would draw it.</param>
    public static List<CloudPlacement> Place(IReadOnlyList<(string Text, double Weight)> items, double width, double height,
        Func<string, double, (double Width, double Height)> measure, double minFont = 12, double maxFont = 46, double gap = 3)
    {
        var result = new List<CloudPlacement>();
        if (items.Count == 0 || width < 40 || height < 40) return result;
        var order = Enumerable.Range(0, items.Count).OrderByDescending(i => items[i].Weight).ThenBy(i => i).ToList();
        var top = Math.Max(items[order[0]].Weight, 1e-9);
        var low = items.Min(i => i.Weight);
        var placed = new List<(double X, double Y, double W, double H)>();
        double cx = width / 2, cy = height / 2;

        foreach (var i in order)
        {
            var (text, weight) = items[i];
            if (text.Length == 0) continue;
            // sqrt keeps the biggest word from swallowing the rest, and equal weights give equal sizes
            var share = top - low < 1e-9 ? 1 : Math.Sqrt(Math.Max(0, (weight - low) / (top - low)));
            var font = minFont + (maxFont - minFont) * share;
            for (var attempt = 0; attempt < 4; attempt++, font *= 0.82)
            {
                if (font < 8) break;
                var (w, h) = measure(text, font);
                w += gap; h += gap;
                if (w > width || h > height) continue;
                var spot = FindSpot(placed, w, h, width, height, cx, cy);
                if (spot is null) continue;
                placed.Add((spot.Value.X, spot.Value.Y, w, h));
                result.Add(new CloudPlacement(i, spot.Value.X + gap / 2, spot.Value.Y + gap / 2, w - gap, h - gap, font));
                break;
            }
        }
        return result;
    }

    /// <summary>Like <see cref="Place"/>, but the words are made as big as the area allows while all of them still find room.</summary>
    public static List<CloudPlacement> PlaceFitted(IReadOnlyList<(string Text, double Weight)> items, double width, double height,
        Func<string, double, (double Width, double Height)> measure)
    {
        var wanted = items.Count(i => i.Text.Length > 0);
        var best = Place(items, width, height, measure, 11, 22);
        double lo = 22, hi = Math.Clamp(height / 2.5, 40, 140);
        for (var i = 0; i < 7; i++)
        {
            var mid = (lo + hi) / 2;
            var tryIt = Place(items, width, height, measure, Math.Max(11, mid * 0.3), mid);
            if (tryIt.Count >= wanted) { best = tryIt; lo = mid; } else hi = mid;
        }
        return best;
    }

    static (double X, double Y)? FindSpot(List<(double X, double Y, double W, double H)> placed, double w, double h, double width, double height, double cx, double cy)
    {
        // wide and flat: the spiral is stretched like the window, so the cloud fills a landscape area
        var stretch = Math.Max(1, width / height);
        for (double t = 0; t < 900; t += 0.35)
        {
            var radius = 2.2 * t;
            var x = cx + radius * Math.Cos(t) * stretch - w / 2;
            var y = cy + radius * Math.Sin(t) - h / 2;
            if (x < 0 || y < 0 || x + w > width || y + h > height)
            {
                if (radius > Math.Max(width, height)) return null;
                continue;
            }
            var free = true;
            foreach (var p in placed)
                if (x < p.X + p.W && x + w > p.X && y < p.Y + p.H && y + h > p.Y) { free = false; break; }
            if (free) return (x, y);
        }
        return null;
    }
}
