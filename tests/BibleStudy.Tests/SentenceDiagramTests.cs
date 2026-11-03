using BibleStudy.Core.Diagram;
using Xunit;

namespace BibleStudy.Tests;

public class SentenceDiagramTests
{
    // John 1:1 and 1:2 in the shape of the MACULA syntax trees (shortened to the attributes that matter)
    const string Xml = """
        <book><sentence>
          <wg rule="Conj3CL" class="cl">
            <wg class="cl" rule="P-VC-S">
              <wg rule="PrepNp" class="pp" role="p">
                <w ref="JHN 1:1!1" class="prep" gloss="In [the]">Ἐν</w>
                <w ref="JHN 1:1!2" class="noun" gloss="beginning" case="dative">ἀρχῇ</w>
              </wg>
              <w role="vc" ref="JHN 1:1!3" class="verb" person="third" number="singular" mood="indicative" gloss="was">ἦν</w>
              <wg class="np" rule="DetNP" role="s">
                <w ref="JHN 1:1!4" class="det" gloss="the">ὁ</w>
                <w ref="JHN 1:1!5" class="noun" gloss="Word">Λόγος</w>
              </wg>
            </wg>
            <wg type="group">
              <w ref="JHN 1:1!6" class="conj" gloss="and">καὶ</w>
              <wg class="cl" rule="S-VC-P">
                <wg class="np" role="s">
                  <w ref="JHN 1:1!7" class="det" gloss="the">ὁ</w>
                  <w ref="JHN 1:1!8" class="noun" gloss="Word">Λόγος</w>
                </wg>
                <w role="vc" ref="JHN 1:1!9" class="verb" person="third" number="singular" mood="indicative" gloss="was">ἦν</w>
                <w role="p" ref="JHN 1:1!10" class="noun" gloss="God">Θεός</w>
              </wg>
            </wg>
          </wg>
        </sentence>
        <sentence>
          <wg class="cl" rule="S-V-O">
            <w role="s" ref="JHN 1:2!1" class="pron" gloss="This">Οὗτος</w>
            <w role="v" ref="JHN 1:2!2" class="verb" person="third" number="singular" mood="indicative" gloss="was">ἦν</w>
            <w role="o" ref="JHN 1:2!3" class="noun" gloss="God">Θεόν</w>
          </wg>
        </sentence>
        <sentence>
          <wg class="cl" rule="V">
            <w role="v" ref="JHN 1:3!1" class="verb" person="first" number="plural" mood="indicative" gloss="we saw">εἴδομεν</w>
            <wg class="cl" role="o" rule="that-VP">
              <w ref="JHN 1:3!2" class="conj" gloss="that">ὅτι</w>
              <w role="v" ref="JHN 1:3!3" class="verb" person="third" number="singular" mood="indicative" gloss="came">ἦλθεν</w>
            </wg>
          </wg>
        </sentence></book>
        """;

    static List<DSentence> Parse() => SentenceModel.ParseChapter(Xml, 1);

    [Fact]
    public void Sentences_get_their_verse_range()
    {
        var list = Parse();
        Assert.Equal(3, list.Count);
        Assert.Equal((1, 1), (list[0].FirstVerse, list[0].LastVerse));
        Assert.Equal(2, list[1].FirstVerse);
        Assert.Empty(SentenceModel.ParseChapter(Xml, 2));
    }

    [Fact]
    public void A_sentence_of_joined_clauses_becomes_a_group_with_the_conjunction()
    {
        var root = Parse()[0].Root;
        Assert.Equal(2, root.Items.Count);
        Assert.Equal("καὶ", root.Conjs[0]!.Text);
        Assert.Equal("ἦν", string.Join(" ", root.Items[0].Clause!.Verb.Select(v => v.Text)));
    }

    [Fact]
    public void Subject_verb_complement_and_the_article_under_the_subject()
    {
        var c = Parse()[0].Root.Items[1].Clause!;
        Assert.Equal("Λόγος", c.Subject!.Head[0].Text);
        Assert.Contains(c.Subject.Mods, m => m.Kind == DModKind.Word && m.Word!.Text == "ὁ");
        Assert.Equal("Θεός", c.Complement!.Head[0].Text);
        Assert.Null(c.Object);
    }

    [Fact]
    public void A_prepositional_phrase_hangs_under_the_verb_with_its_object_on_a_shelf()
    {
        var c = Parse()[0].Root.Items[0].Clause!;
        var mod = Assert.Single(c.Mods);
        Assert.Equal(DModKind.Prep, mod.Kind);
        Assert.Equal("Ἐν", mod.Word!.Text);
        Assert.Equal("ἀρχῇ", mod.Phrase!.Head[0].Text);
    }

    [Fact]
    public void A_missing_subject_is_taken_from_the_verb_ending()
    {
        var c = Parse()[2].Root.Clause!;
        Assert.Null(c.Subject);
        Assert.Equal("first/plural", c.ImpliedSubject);
    }

    [Fact]
    public void A_noun_clause_stands_on_a_stilt_as_the_object()
    {
        var c = Parse()[2].Root.Clause!;
        var stilt = Assert.Single(c.Object!.Mods);
        Assert.True(stilt.Stilt);
        Assert.Equal("ὅτι", stilt.Clause!.Conj!.Text);
        Assert.Equal("ἦλθεν", stilt.Clause.Verb[0].Text);
    }

    [Fact]
    public void The_layout_puts_every_text_inside_the_picture_and_mirrors_hebrew()
    {
        var options = new DiagramOptions(w => w.Text, w => w.Gloss, k => k);
        var layout = new DiagramLayout(options, (t, size) => t.Length * size * 0.5);
        foreach (var s in Parse())
        {
            var scene = layout.Layout(s);
            Assert.True(scene.Width > 20 && scene.Height > 20);
            Assert.NotEmpty(scene.Lines);
            foreach (var t in scene.Texts.Where(t => t.Angle == 0 && t.Anchor == DAnchor.Center))
                Assert.InRange(t.X, 0, scene.Width);
        }
        var plain = Parse()[1];
        var left = layout.Layout(plain).Texts.First(t => t.Kind == DTextKind.Subject).X;
        plain.Hebrew = true;
        var scene2 = layout.Layout(plain);
        var right = scene2.Texts.First(t => t.Kind == DTextKind.Subject).X;
        Assert.True(right > scene2.Width / 2 && left < scene2.Width / 2, "the subject is on the left in Greek and on the right in Hebrew");
    }

    [Fact]
    public void The_svg_contains_the_words_and_is_well_formed()
    {
        var layout = new DiagramLayout(new DiagramOptions(w => w.Text, w => null, k => k), (t, size) => t.Length * size * 0.5);
        var svg = DiagramSvg.Write(layout.Layout(Parse()[0]), false, "Joh 1,1");
        Assert.Contains("Λόγος", svg);
        System.Xml.Linq.XDocument.Parse(svg);
    }
}
