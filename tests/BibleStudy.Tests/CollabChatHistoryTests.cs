using BibleStudy.Core.Collab;
using Xunit;

namespace BibleStudy.Tests;

public class CollabChatHistoryTests
{
    static string TempFile() => Path.Combine(Path.GetTempPath(), "bs-chat-" + Guid.NewGuid().ToString("N"), "collab-chats.json");

    [Fact]
    public void The_chat_comes_back_only_for_the_same_person()
    {
        var path = TempFile();
        var h = CollabChatHistory.Load(path);
        h.Add(["ANNA1"], new CollabChatLine { Mid = "m1", When = new DateTime(2026, 11, 1, 10, 0, 0), Who = "Anna", Text = "Hallo" });
        h.Add(["ANNA1"], new CollabChatLine { Mid = "m2", When = new DateTime(2026, 11, 1, 10, 1, 0), Who = "Ich", Text = "Hi", Mine = true });

        var again = CollabChatHistory.Load(path);   // after a restart
        Assert.True(again.Has("ANNA1"));
        Assert.Equal(["Hallo", "Hi"], again.For(["ANNA1"]).Select(l => l.Text));
        Assert.False(again.Has("BEN22"));
        Assert.Empty(again.For(["BEN22"]));
    }

    [Fact]
    public void A_group_line_is_kept_with_each_person_and_shown_once()
    {
        var h = CollabChatHistory.Load(TempFile());
        h.Add(["ANNA1", "BEN22"], new CollabChatLine { Mid = "m1", When = DateTime.Now, Text = "an beide" });
        h.Add(["ANNA1", "BEN22"], new CollabChatLine { Mid = "m1", When = DateTime.Now, Text = "an beide" });   // the same message again
        Assert.Single(h.For(["ANNA1"]));
        Assert.Single(h.For(["BEN22"]));
        Assert.Single(h.For(["ANNA1", "BEN22"]));
        Assert.Equal(2, h.People);
    }

    [Fact]
    public void Clearing_forgets_everything_and_bad_ids_are_ignored()
    {
        var path = TempFile();
        var h = CollabChatHistory.Load(path);
        h.Add(["../evil", ""], new CollabChatLine { Text = "x" });
        Assert.Equal(0, h.Lines);
        h.Add(["ANNA1"], new CollabChatLine { Text = "x" });
        Assert.Equal(1, h.Lines);
        h.Clear();
        Assert.Equal(0, h.Lines);
        Assert.Equal(0, CollabChatHistory.Load(path).Lines);
    }

    [Fact]
    public void Only_the_newest_lines_are_kept()
    {
        var h = CollabChatHistory.Load(TempFile());
        for (var i = 0; i < CollabChatHistory.MaxPerPerson + 5; i++) h.Add(["ANNA1"], new CollabChatLine { Mid = "m" + i, When = DateTime.Now.AddSeconds(i), Text = i.ToString() });
        var lines = h.For(["ANNA1"]);
        Assert.Equal(CollabChatHistory.MaxPerPerson, lines.Count);
        Assert.Equal("5", lines[0].Text);
    }

    [Fact]
    public void A_damaged_file_is_an_empty_chat()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ nicht json");
        Assert.Equal(0, CollabChatHistory.Load(path).Lines);
    }

    [Fact]
    public void Two_friends_get_the_same_room_from_their_secret()
    {
        var a = RelayLink.FriendCode("ABCDEF0123456789ABCDEF0123456789");
        Assert.Equal(a, RelayLink.FriendCode("ABCDEF0123456789ABCDEF0123456789"));
        Assert.NotEqual(a, RelayLink.FriendCode("ABCDEF0123456789ABCDEF012345678A"));
        Assert.Equal(a, RelayLink.Normalize(a));   // a valid room code
    }
}
