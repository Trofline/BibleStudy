using BibleStudy.Core;
using BibleStudy.Core.Import;

namespace BibleStudy.Tests;

public class AudioBibleTests
{
    [Fact]
    public void Every_book_has_a_folder_and_a_chapter_count()
    {
        Assert.Equal(66, BookData.UsfmIds.Length);
        Assert.Equal(66, AudioBible.ChapterCounts.Length);
        Assert.All(BookData.UsfmIds, id => Assert.NotNull(AudioBible.Folder(id)));
        Assert.Equal(1189, AudioBible.ChapterCounts.Sum());   // the 1,189 chapters of the Bible
        Assert.Equal("43_John", AudioBible.Folder("JHN"));
        Assert.Equal(150, AudioBible.Chapters("PSA"));
        Assert.Null(AudioBible.Folder("TOB"));
        Assert.False(AudioBible.Has("XXA"));
    }

    [Theory]
    [InlineData("0997 John-Chapter One.mp3", 1)]
    [InlineData("1017 John-Chapter Twenty One.mp3", 21)]
    [InlineData("01 0973 Luke-Chapter One.mp3", 1)]
    [InlineData("01 0989 Luke-Chapter Seventeen (1).mp3", 17)]
    [InlineData("01_21_Genesis_Chapter_Twenty_One.mp3", 21)]
    [InlineData("01_50_Genesis_Chapter_Fifty.mp3", 50)]
    [InlineData("0597 Psalms-One Hundred Nineteen.mp3", 119)]
    [InlineData("0628 Psalms-One Hundred Fifty.mp3", 150)]
    [InlineData("0549 Psalms-Seventy One (1).mp3", 71)]
    [InlineData("0629 Proverbs-Chapter 001.mp3", 1)]
    [InlineData("0659 Proverbs-Chapter 031.mp3", 31)]
    public void The_chapter_is_read_from_the_name(string name, int chapter) => Assert.Equal(chapter, AudioBible.ChapterOf(name));

    [Fact]
    public void A_name_without_a_number_is_no_chapter() => Assert.Null(AudioBible.ChapterOf("readme.mp3"));

    [Fact]
    public void The_listing_is_taken_by_chapter_not_by_running_number()
    {
        // Isaiah 16 and 17 share the running number 0695 on the server; a copy "(1)" of Psalm 71 must not count twice
        var html = """
            <a href="01%200694%20Isaiah-Chapter%20Fifteen.mp3">x</a>
            <a href="01%200695%20Isaiah-Chapter%20Seventeen.mp3">x</a>
            <a href="01%200695%20Isaiah-Chapter%20Sixteen.mp3">x</a>
            <a href="01%200696%20Isaiah-Chapter%20Eighteen.mp3">x</a>
            <a href="01%200696%20Isaiah-Chapter%20Eighteen%20%281%29.mp3">x</a>
            <a href="../">up</a>
            """;
        var files = AudioBible.ParseFiles(html);
        Assert.Equal([15, 16, 17, 18], files.Keys);
        Assert.Equal("01%200695%20Isaiah-Chapter%20Sixteen.mp3", files[16]);
        Assert.Equal("01%200695%20Isaiah-Chapter%20Seventeen.mp3", files[17]);
    }

    [Fact]
    public void Next_and_previous_cross_book_borders()
    {
        Assert.Equal(("JHN", 4), AudioBible.Next("JHN", 3));
        Assert.Equal(("ACT", 1), AudioBible.Next("JHN", 21));
        Assert.Equal(("EXO", 1), AudioBible.Next("GEN", 50));
        Assert.Null(AudioBible.Next("REV", 22));
        Assert.Equal(("JHN", 20), AudioBible.Previous("JHN", 21));
        Assert.Equal(("JHN", 21), AudioBible.Previous("ACT", 1));
        Assert.Equal(("MAL", 4), AudioBible.Previous("MAT", 1));
        Assert.Null(AudioBible.Previous("GEN", 1));
    }

    [Fact]
    public void The_cache_drops_the_oldest_chapters_first_and_keeps_the_one_in_use()
    {
        var cache = Path.Combine(Path.GetTempPath(), "bs_audio_" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(cache, "audio", "web");
        Directory.CreateDirectory(dir);
        try
        {
            string Make(string name, int minutesAgo)
            {
                var p = Path.Combine(dir, name);
                File.WriteAllBytes(p, new byte[1000]);
                File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddMinutes(-minutesAgo));
                return p;
            }
            var old = Make("GEN-001.mp3", 30);
            var mid = Make("GEN-002.mp3", 20);
            var current = Make("GEN-003.mp3", 40);   // the oldest, but just downloaded / in use
            var removed = AudioBible.TrimCache(cache, 2000, keep: current);
            Assert.Equal(1, removed);
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(mid));
            Assert.True(File.Exists(current));
        }
        finally { Directory.Delete(cache, true); }
    }
}
