using BibleStudy.Core;

namespace BibleStudy.Tests;

public class ReadingReminderTests
{
    static readonly DateTime Noon = new(2026, 11, 22, 12, 0, 0);

    [Fact]
    public void Time_is_read_as_hours_and_minutes_only()
    {
        Assert.Equal(new TimeSpan(19, 30, 0), ReadingReminder.ParseTime("19:30"));
        Assert.Equal(new TimeSpan(7, 5, 0), ReadingReminder.ParseTime("7:05"));
        Assert.Null(ReadingReminder.ParseTime("25:00"));
        Assert.Null(ReadingReminder.ParseTime("abends"));
        Assert.Null(ReadingReminder.ParseTime(""));
    }

    [Fact]
    public void Not_before_the_time_and_only_while_nothing_was_read_today()
    {
        Assert.False(ReadingReminder.Due(Noon, "19:00", [], null));
        var evening = Noon.AddHours(8);
        Assert.True(ReadingReminder.Due(evening, "19:00", [], null));
        Assert.False(ReadingReminder.Due(evening, "19:00", [new DateTime(2026, 11, 22, 8, 0, 0)], null));
        Assert.True(ReadingReminder.Due(evening, "19:00", [new DateTime(2026, 11, 21, 8, 0, 0)], null));   // yesterday does not count
    }

    [Fact]
    public void Once_a_day_unless_it_was_put_off()
    {
        var evening = Noon.AddHours(8);
        Assert.False(ReadingReminder.Due(evening, "19:00", [], DateOnly.FromDateTime(evening)));
        Assert.True(ReadingReminder.Due(evening, "19:00", [], DateOnly.FromDateTime(evening.AddDays(-1))));
        Assert.False(ReadingReminder.Due(evening, "19:00", [], DateOnly.FromDateTime(evening), evening.AddMinutes(30)));   // pause not over
        Assert.True(ReadingReminder.Due(evening, "19:00", [], DateOnly.FromDateTime(evening), evening.AddMinutes(-1)));    // pause over
    }
}
