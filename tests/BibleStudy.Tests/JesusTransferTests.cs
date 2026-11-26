using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.Tests;

/// <summary>Words of Jesus estimated from the KJV for a translation that does not mark them.</summary>
public class JesusTransferTests
{
    static Verse Kjv(int n, params (string Text, bool Red)[] parts) => new(n, parts.Select(p => new Segment(p.Text, p.Red)).ToList());
    static Verse Plain(int n, string text) => new(n, [new Segment(text)]);
    static string Red(Verse v) => string.Concat(v.Segments.Where(s => s.WordsOfJesus).Select(s => s.Text)).Trim();

    [Fact]
    public void A_verse_all_red_in_the_KJV_is_all_red()
    {
        var v = JesusTransfer.Transfer(Plain(1, "Selig sind, die da geistlich arm sind; denn das Himmelreich ist ihr."), Kjv(3, ("Blessed are the poor in spirit: for theirs is the kingdom of heaven.", true)));
        Assert.Equal("Selig sind, die da geistlich arm sind; denn das Himmelreich ist ihr.", Red(v));
    }

    [Fact]
    public void Quotation_marks_give_the_spoken_part()
    {
        var v = JesusTransfer.Transfer(Plain(6, "Jesus spricht zu ihm: „Ich bin der Weg und die Wahrheit und das Leben.“"),
            Kjv(6, ("Jesus saith unto him, ", false), ("I am the way, the truth, and the life: no man cometh unto the Father, but by me.", true)));
        Assert.Equal("Ich bin der Weg und die Wahrheit und das Leben.", Red(v));
        Assert.Equal("Jesus spricht zu ihm: „Ich bin der Weg und die Wahrheit und das Leben.“", new Verse(6, v.Segments).Text);
    }

    [Fact]
    public void A_lone_closing_mark_ends_a_speech_from_the_verse_before()
    {
        var v = JesusTransfer.Transfer(Plain(2, "so würdet ihr auch meinen Vater kennen.“ Da sprach Philippus zu ihm"),
            Kjv(2, ("ye should have known my Father also:", true), (" and Philip said", false)));
        Assert.Equal("so würdet ihr auch meinen Vater kennen.", Red(v));
    }

    [Fact]
    public void Without_quotation_marks_the_red_part_starts_after_the_colon()
    {
        var v = JesusTransfer.Transfer(Plain(19, "Und er spricht zu ihnen: Folgt mir nach, ich will euch zu Menschenfischern machen."),
            Kjv(19, ("And he saith unto them, ", false), ("Follow me, and I will make you fishers of men.", true)));
        Assert.Equal("Folgt mir nach, ich will euch zu Menschenfischern machen.", Red(v));
    }

    [Fact]
    public void A_verse_without_red_in_the_KJV_stays_as_it_is()
    {
        var plain = Plain(1, "Das Buch von der Geburt Jesu Christi.");
        Assert.Same(plain, JesusTransfer.Transfer(plain, Kjv(1, ("The book of the generation of Jesus Christ", false))));
    }

    [Fact]
    public void Footnotes_and_strongs_are_kept()
    {
        var target = new Verse(1, [new Segment("Er sprach: "), new Segment("Folgt", Strongs: ["G190"]), new Segment("", NoteId: 3), new Segment(" mir.")]);
        var v = JesusTransfer.Transfer(target, Kjv(1, ("He said, ", false), ("Follow me.", true)));
        Assert.Contains(v.Segments, s => s.NoteId == 3);
        Assert.Contains(v.Segments, s => s is { Text: "Folgt", WordsOfJesus: true } && s.Strongs is ["G190"]);
    }

    [Fact]
    public void Only_chapters_without_own_marks_in_books_with_red_letters_are_wanted()
    {
        Assert.True(JesusTransfer.Wanted(new Chapter("local:x", "JHN", 14, [Plain(1, "Euer Herz erschrecke nicht")])));
        Assert.False(JesusTransfer.Wanted(new Chapter("local:x", "GEN", 1, [Plain(1, "Im Anfang")])));
        Assert.False(JesusTransfer.Wanted(new Chapter("local:x", "JHN", 14, [Kjv(1, ("Euer Herz", true))])));
        Assert.False(JesusTransfer.Wanted(new Chapter("eng_kjv", "JHN", 14, [Plain(1, "Let not")])));
    }

    [Fact]
    public void Apply_works_verse_by_verse()
    {
        var target = new Chapter("local:x", "JHN", 14, [Plain(1, "Euer Herz erschrecke nicht!"), Plain(2, "Text ohne Gegenstück")]);
        var kjv = new Chapter("eng_kjv", "JHN", 14, [Kjv(1, ("Let not your heart be troubled", true))]);
        var result = JesusTransfer.Apply(target, kjv);
        Assert.Equal("Euer Herz erschrecke nicht!", Red(result.Verses[0]));
        Assert.Equal("", Red(result.Verses[1]));
    }
}
