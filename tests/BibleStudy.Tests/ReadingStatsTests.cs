using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

public class ReadingStatsTests
{
    static readonly DateTime Today = new(2026, 11, 3);

    [Fact]
    public void Longest_streak_counts_the_longest_run_of_consecutive_days()
    {
        var days = new[] { Today.AddDays(-10), Today.AddDays(-9), Today.AddDays(-8), Today.AddDays(-5), Today.AddDays(-4), Today };
        Assert.Equal(3, ReadingStats.LongestStreak(days));
        Assert.Equal(0, ReadingStats.LongestStreak([]));
    }

    [Fact]
    public void Several_chapters_on_one_day_count_as_one_day_for_the_streak()
    {
        Assert.Equal(1, ReadingStats.LongestStreak([Today.AddHours(8), Today.AddHours(20)]));
    }

    [Fact]
    public void Per_day_puts_today_last_and_ignores_older_and_future_days()
    {
        var counts = ReadingStats.PerDay([Today, Today.AddHours(5), Today.AddDays(-1), Today.AddDays(-6), Today.AddDays(-7), Today.AddDays(1)], Today, 7);
        Assert.Equal([1, 0, 0, 0, 0, 1, 2], counts);
    }

    [Fact]
    public void Per_week_sums_seven_day_blocks_ending_today()
    {
        var weeks = ReadingStats.PerWeek([Today, Today.AddDays(-3), Today.AddDays(-7), Today.AddDays(-13), Today.AddDays(-14)], Today, 2);
        Assert.Equal([2, 2], weeks);
    }
}
