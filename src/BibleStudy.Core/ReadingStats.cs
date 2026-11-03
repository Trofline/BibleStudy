namespace BibleStudy.Core;

/// <summary>Numbers for the statistics page, worked out from the days on which chapters were marked as read.</summary>
public static class ReadingStats
{
    /// <summary>The longest run of consecutive days with at least one chapter read.</summary>
    public static int LongestStreak(IEnumerable<DateTime> readAt)
    {
        var days = readAt.Select(d => d.Date).Distinct().Order().ToList();
        int best = 0, run = 0;
        for (var i = 0; i < days.Count; i++)
        {
            run = i > 0 && (days[i] - days[i - 1]).Days == 1 ? run + 1 : 1;
            best = Math.Max(best, run);
        }
        return best;
    }

    /// <summary>Chapters read per day for the last <paramref name="count"/> days, the oldest first and today last.</summary>
    public static int[] PerDay(IEnumerable<DateTime> readAt, DateTime today, int count)
    {
        var result = new int[count];
        foreach (var d in readAt)
        {
            var back = (today.Date - d.Date).Days;
            if (back >= 0 && back < count) result[count - 1 - back]++;
        }
        return result;
    }

    /// <summary>Chapters read per week for the last <paramref name="weeks"/> weeks (the last entry is the 7 days ending today).</summary>
    public static int[] PerWeek(IEnumerable<DateTime> readAt, DateTime today, int weeks)
    {
        var days = PerDay(readAt, today, weeks * 7);
        return Enumerable.Range(0, weeks).Select(w => days.Skip(w * 7).Take(7).Sum()).ToArray();
    }
}
