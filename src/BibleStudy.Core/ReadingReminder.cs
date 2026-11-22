using System.Globalization;

namespace BibleStudy.Core;

/// <summary>When the reminder to read is due: from the chosen time on, if nothing was read today and it has not been shown today (or was put off and the pause is over).</summary>
public static class ReadingReminder
{
    /// <summary>"19:30" → 19:30; anything else (also an empty text) → null.</summary>
    public static TimeSpan? ParseTime(string? text) =>
        TimeSpan.TryParseExact(text?.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : null;

    /// <param name="now">The time now.</param>
    /// <param name="at">The chosen time ("19:00").</param>
    /// <param name="readDates">When chapters were read.</param>
    /// <param name="shownOn">The day the reminder was last shown (it comes once a day), or null.</param>
    /// <param name="pausedUntil">A put-off reminder comes again from here on, or null.</param>
    public static bool Due(DateTime now, string? at, IEnumerable<DateTime> readDates, DateOnly? shownOn, DateTime? pausedUntil = null)
    {
        if (ParseTime(at) is not { } time || now.TimeOfDay < time) return false;
        if (pausedUntil is { } pause && now < pause) return false;
        var today = DateOnly.FromDateTime(now);
        if (readDates.Any(d => DateOnly.FromDateTime(d) == today)) return false;
        return shownOn != today || pausedUntil is not null;
    }
}
