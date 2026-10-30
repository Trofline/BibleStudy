using BibleStudy.Core;
using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class ReadingPlanTests
{
    [Theory]
    [InlineData(1189, 365)]
    [InlineData(89, 30)]
    [InlineData(31, 31)]
    [InlineData(10, 30)]   // fewer chapters than days: some days are empty, none is lost
    public void EveryChapter_IsReadExactlyOnce_InOrder(int chapters, int days)
    {
        var items = Enumerable.Range(1, chapters).ToList();
        var all = Enumerable.Range(1, days).SelectMany(d => ReadingPlans.Day(items, days, d)).ToList();
        Assert.Equal(items, all);
        var sizes = Enumerable.Range(1, days).Select(d => ReadingPlans.Day(items, days, d).Count).ToList();
        Assert.True(sizes.Max() - sizes.Min() <= 1);
    }

    [Fact]
    public void DaysOutsideThePlan_AreEmpty()
    {
        var items = new[] { 1, 2, 3 };
        Assert.Empty(ReadingPlans.Day(items, 3, 0));
        Assert.Empty(ReadingPlans.Day(items, 3, 4));
        Assert.Empty(ReadingPlans.Day(items, 0, 1));
    }

    [Fact]
    public void Streak_CountsBackFromTodayOrYesterday()
    {
        var today = new DateTime(2026, 11, 10);
        Assert.Equal(0, ReadingPlans.Streak([], today));
        Assert.Equal(3, ReadingPlans.Streak([today, today.AddDays(-1), today.AddDays(-2)], today));
        Assert.Equal(2, ReadingPlans.Streak([today.AddDays(-1), today.AddDays(-2)], today));      // today is not over yet: the streak is still alive
        Assert.Equal(0, ReadingPlans.Streak([today.AddDays(-2), today.AddDays(-3)], today));      // a day was missed
        Assert.Equal(1, ReadingPlans.Streak([today, today.AddDays(-2)], today));
    }

    [Fact]
    public void DayOf_StaysInsideThePlan()
    {
        var start = new DateTime(2026, 11, 1);
        Assert.Equal(1, ReadingPlans.DayOf(start, new DateTime(2026, 10, 25), 30));   // not started yet
        Assert.Equal(1, ReadingPlans.DayOf(start, start, 30));
        Assert.Equal(10, ReadingPlans.DayOf(start, new DateTime(2026, 11, 10, 18, 0, 0), 30));
        Assert.Equal(30, ReadingPlans.DayOf(start, new DateTime(2027, 3, 1), 30));    // past the end: the last day
    }
}

public class FirstStartSettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));
    public FirstStartSettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void NewInstall_BacksUpWeekly_AndShowsTheTour()
    {
        var s = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        Assert.True(s.TourPending);
        Assert.Equal("weekly", s.BackupSchedule);
        s.Save(Path.Combine(_dir, "settings.json"));
        Assert.True(AppSettings.Load(Path.Combine(_dir, "settings.json")).TourPending);   // the second load (the file exists now) still knows it
    }

    [Fact]
    public void ExistingInstall_KeepsItsChoice_AndIsAskedOnce()
    {
        var path = Path.Combine(_dir, "settings.json");
        new AppSettings { BackupSchedule = "off" }.Save(path);
        var s = AppSettings.Load(path);
        Assert.False(s.TourPending);   // whoever had the program before is not sent on a tour
        Assert.Equal("off", s.BackupSchedule);   // never switched on behind the user's back
        Assert.False(s.BackupAsked);

        s.BackupAsked = true; s.CustomPlan = new CustomPlan { Name = "Evangelien", FromBook = "MAT", ToBook = "JHN", Days = 20 };
        s.Save(path);
        var again = AppSettings.Load(path);
        Assert.True(again.BackupAsked);
        Assert.Equal(("MAT", "JHN", 20), (again.CustomPlan!.FromBook, again.CustomPlan.ToBook, again.CustomPlan.Days));
    }
}
