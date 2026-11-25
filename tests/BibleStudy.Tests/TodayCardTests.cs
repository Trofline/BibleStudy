using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.Tests;

public class TodayCardTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static TodayCard Full() => new()
    {
        Translation = "deu_elbbk", TranslationName = "ELB",
        Verses = DailyVerses.All.Select(v => new TodayCard.Verse($"biblestudy://open/{v.Book}.{v.Chapter}.{v.Verse}", $"{v.Book} {v.Chapter}:{v.Verse}", "Text " + v.Book)).ToList(),
        Reading = new("2026-11-25", "Weiterlesen: Johannes 3", "Neues Testament · Tag 12 von 260", "biblestudy://open/JHN.3"),
        Words = new() { ["verse"] = "Vers des Tages" },
    };

    [Fact]
    public void The_widget_picks_the_verse_of_the_day_itself()
    {
        var card = Full();
        var day = new DateTime(2026, 11, 26);
        Assert.Equal(DailyVerses.Of(day).Book, card.VerseOf(day)!.Text[5..]);
        Assert.NotEqual(card.VerseOf(day), card.VerseOf(day.AddDays(1)));
    }

    [Fact]
    public void The_plan_counts_only_on_the_day_it_was_written_for()
    {
        var card = Full();
        Assert.NotNull(card.PlanOf(new DateTime(2026, 11, 25, 22, 0, 0)));
        Assert.Null(card.PlanOf(new DateTime(2026, 11, 26)));
    }

    [Fact]
    public void It_is_written_only_when_something_changed_and_reads_back()
    {
        var card = Full();
        Assert.True(card.Save(_dir));
        Assert.False(card.Save(_dir));
        var back = TodayCard.Load(_dir)!;
        Assert.Equal(card.Verses, back.Verses);
        Assert.Equal(card.Reading, back.Reading);
        Assert.Equal("Vers des Tages", back.Word("verse", "x"));
        Assert.Equal("x", back.Word("plan", "x"));
    }

    [Fact]
    public void A_broken_file_is_no_card() => Assert.Null(TodayCard.FromJson("{nicht json"));

    [Fact]
    public void The_daily_verses_are_the_same_list_as_before() =>
        Assert.Equal(new VerseRef("JHN", 3, 16), DailyVerses.All[0]);
}
