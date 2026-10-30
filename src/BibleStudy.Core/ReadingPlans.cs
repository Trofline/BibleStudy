namespace BibleStudy.Core;

/// <summary>The arithmetic of reading plans: a list of chapters is spread evenly over the days of a plan.</summary>
public static class ReadingPlans
{
    /// <summary>The items of one day (1-based). Over all days every item is used exactly once, in order; the days differ by at most one item.</summary>
    public static List<T> Day<T>(IReadOnlyList<T> items, int days, int day)
    {
        if (days < 1 || day < 1 || day > days) return [];
        int from = (int)((long)(day - 1) * items.Count / days), to = (int)((long)day * items.Count / days);
        return items.Skip(from).Take(to - from).ToList();
    }

    /// <summary>Consecutive days, ending today or yesterday, on which a plan day was finished.</summary>
    public static int Streak(IEnumerable<DateTime> doneDays, DateTime today)
    {
        var days = doneDays.Select(d => d.Date).ToHashSet();
        var cursor = days.Contains(today.Date) ? today.Date : today.Date.AddDays(-1);
        var n = 0;
        while (days.Contains(cursor)) { n++; cursor = cursor.AddDays(-1); }
        return n;
    }

    /// <summary>The number of the plan day that belongs to <paramref name="today"/> (1 before the start, the last day after the end).</summary>
    public static int DayOf(DateTime start, DateTime today, int days) => Math.Clamp((today.Date - start.Date).Days + 1, 1, Math.Max(1, days));
}
