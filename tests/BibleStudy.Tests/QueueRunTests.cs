using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

public class QueueRunTests
{
    [Fact]
    public void Only_entries_reached_count_as_visited()
    {
        var run = new QueueRun([1, 2, 3, 4]);
        Assert.True(run.Move(1));
        Assert.True(run.MoveTo(4));
        Assert.True(run.IsLast);
        Assert.Equal([1L, 2L, 4L], run.Visited.Order());
    }

    [Fact]
    public void Cannot_leave_the_list()
    {
        var run = new QueueRun([1, 2]);
        Assert.False(run.Move(-1));
        run.Move(1);
        Assert.False(run.Move(1));
        Assert.Equal(2, run.Current);
    }

    [Fact]
    public void Going_back_keeps_what_was_visited()
    {
        var run = new QueueRun([1, 2, 3]);
        run.Move(1); run.Move(-1);
        Assert.Equal(1, run.Current);
        Assert.Contains(2L, run.Visited);
    }

    [Fact]
    public void A_changed_list_keeps_the_place()
    {
        var run = new QueueRun([1, 2, 3]);
        run.Move(1);
        run.Sync([0, 1, 2, 3, 9]);
        Assert.Equal(2, run.Current);
        run.Sync([1, 3]);   // the current one was removed: its place is taken by the next
        Assert.Equal(3, run.Current);
        Assert.DoesNotContain(2L, run.Visited);
        run.Sync([]);
        Assert.Equal(0, run.Count);
    }

    static bool Upto40(int n) => n is >= 1 and <= 40;

    [Fact]
    public void Two_digits_make_one_number()
    {
        var t = new TypedNumber();
        Assert.Equal(2, t.Type(2, 5000, Upto40));
        Assert.Equal(28, t.Type(8, 5300, Upto40));
    }

    [Fact]
    public void After_the_pause_a_new_number_starts()
    {
        var t = new TypedNumber();
        Assert.Equal(7, t.Type(7, 5000, Upto40));
        Assert.Equal(6, t.Type(6, 5000 + TypedNumber.ResetAfterMs + 1, Upto40));
    }

    [Fact]
    public void A_slow_jump_does_not_eat_the_time_for_the_next_digit()
    {
        var t = new TypedNumber();
        Assert.Equal(1, t.Type(1, 5000, Upto40));
        t.Finished(7000);   // selecting the verse took two seconds
        Assert.Equal(13, t.Type(3, 7100, Upto40));
    }

    [Fact]
    public void A_number_that_does_not_exist_starts_over()
    {
        var t = new TypedNumber();
        Assert.Equal(7, t.Type(7, 5000, Upto40));
        Assert.Equal(6, t.Type(6, 5100, Upto40));   // there is no verse 76
    }

    [Fact]
    public void Zero_alone_is_no_verse_but_ends_a_number()
    {
        var t = new TypedNumber();
        Assert.Null(t.Type(0, 5000, Upto40));
        Assert.Equal(1, t.Type(1, 5100, Upto40));
        Assert.Equal(10, t.Type(0, 5200, Upto40));
    }

    [Fact]
    public void Reset_forgets_the_digits()
    {
        var t = new TypedNumber();
        t.Type(1, 5000, Upto40);
        t.Reset();
        Assert.Equal(3, t.Type(3, 5100, Upto40));
    }
}
