using BibleStudy.Core.Search;
using Xunit;

namespace BibleStudy.Tests;

public class WordCloudLayoutTests
{
    static (double, double) Measure(string t, double size) => (t.Length * size * 0.55, size * 1.2);

    static List<(string, double)> Items(int n) => Enumerable.Range(0, n).Select(i => ($"wort{i}", (double)(n - i))).ToList();

    [Fact]
    public void Words_stay_inside_and_do_not_overlap()
    {
        var items = Items(40);
        var placed = WordCloudLayout.Place(items, 800, 500, Measure);
        Assert.True(placed.Count >= 30);
        foreach (var p in placed)
        {
            Assert.InRange(p.X, 0, 800 - p.Width + 0.01);
            Assert.InRange(p.Y, 0, 500 - p.Height + 0.01);
        }
        for (var i = 0; i < placed.Count; i++)
            for (var j = i + 1; j < placed.Count; j++)
            {
                var a = placed[i]; var b = placed[j];
                var apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                Assert.True(apart, $"{items[a.Index].Item1} overlaps {items[b.Index].Item1}");
            }
    }

    [Fact]
    public void The_heaviest_word_is_biggest_and_in_the_middle()
    {
        var placed = WordCloudLayout.Place(Items(20), 800, 500, Measure);
        var first = placed.Single(p => p.Index == 0);
        Assert.Equal(placed.Max(p => p.FontSize), first.FontSize);
        Assert.InRange(first.X + first.Width / 2, 350, 450);
        Assert.InRange(first.Y + first.Height / 2, 200, 300);
    }

    [Fact]
    public void Nothing_to_place_or_no_room_gives_an_empty_list()
    {
        Assert.Empty(WordCloudLayout.Place([], 800, 500, Measure));
        Assert.Empty(WordCloudLayout.Place(Items(5), 10, 10, Measure));
    }

    [Fact]
    public void Equal_weights_give_equal_sizes()
    {
        var placed = WordCloudLayout.Place([("aaa", 3), ("bbb", 3), ("ccc", 3)], 800, 500, Measure);
        Assert.Single(placed.Select(p => p.FontSize).Distinct());
    }

    [Fact]
    public void Fitted_cloud_places_every_word_and_uses_the_room()
    {
        var items = Items(40);
        var small = WordCloudLayout.Place(items, 800, 500, Measure);
        var fitted = WordCloudLayout.PlaceFitted(items, 800, 500, Measure);
        Assert.Equal(items.Count, fitted.Count);
        Assert.True(fitted.Max(p => p.FontSize) > small.Max(p => p.FontSize));
        for (var i = 0; i < fitted.Count; i++)
            for (var j = i + 1; j < fitted.Count; j++)
            {
                var a = fitted[i]; var b = fitted[j];
                Assert.True(a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y);
            }
    }
}
