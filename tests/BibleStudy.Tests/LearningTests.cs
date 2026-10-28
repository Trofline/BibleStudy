using BibleStudy.Core;

namespace BibleStudy.Tests;

public class LearningTests
{
    [Fact]
    public void Vocabulary_ids_are_valid_and_unique()
    {
        foreach (var greek in new[] { true, false })
        {
            var ids = Learning.Vocabulary(greek);
            Assert.True(ids.Count > 60);
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, i => Assert.Matches(greek ? @"^G\d{1,4}$" : @"^H\d{1,4}$", i));
        }
    }

    [Fact]
    public void Alphabets_are_complete()
    {
        Assert.Equal(24, Learning.Greek.Length);
        Assert.Equal(22, Learning.Hebrew.Length);
    }

    [Fact]
    public void Right_answers_wait_longer_and_wrong_answers_come_back_at_once()
    {
        var path = Path.Combine(Path.GetTempPath(), "learn-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new LearnStore(path);
            var now = new DateTime(2026, 1, 10, 12, 0, 0);
            store.Answer("G26", true, now);
            Assert.False(store.IsDue("G26", now));
            Assert.True(store.IsDue("G26", now.AddDays(2)));
            store.Answer("G26", true, now.AddDays(2));
            Assert.False(store.IsDue("G26", now.AddDays(3)));
            store.Answer("G26", false, now.AddDays(10));
            Assert.Equal(0, store.Get("G26")!.Box);
            Assert.True(store.IsDue("G26", now.AddDays(10)));
            Assert.Equal(new LearnStore(path).Seen, 1);   // saved to disk
        }
        finally { File.Delete(path); }
    }
}
