using System.Xml.Linq;
using BibleStudy.Core.Diagram;
using BibleStudy.Core.Models;

namespace BibleStudy.Tests;

public class StarAndTreeTests
{
    static CrossRef X(string book, int chapter, int verse, int end = 0, int score = 1) => new(new VerseRef(book, chapter, verse), Math.Max(verse, end), score);

    [Fact]
    public void Neighbours_that_point_to_each_other_are_joined()
    {
        var ring = new[] { X("ROM", 5, 8), X("1JN", 4, 9, 10), X("PSA", 23, 1) };
        var refsOf = new Dictionary<int, IReadOnlyList<CrossRef>>
        {
            [0] = [X("1JN", 4, 10, score: 7), X("GEN", 1, 1)],   // lands inside 1 Joh 4,9-10
            [2] = [X("JHN", 10, 11)],
        };
        var links = StarLinks.Between(ring, i => refsOf.GetValueOrDefault(i));
        Assert.Equal((0, 1, 7), Assert.Single(links));
    }

    [Fact]
    public void Both_directions_count_once_with_the_stronger_score()
    {
        var ring = new[] { X("ROM", 5, 8), X("JHN", 3, 16) };
        var links = StarLinks.Between(ring, i => i == 0 ? [X("JHN", 3, 16, score: 2)] : [X("ROM", 5, 8, score: 9)]);
        Assert.Equal((0, 1, 9), Assert.Single(links));
    }

    [Fact]
    public void The_syntax_tree_is_valid_svg_with_its_words()
    {
        var boxes = new[]
        {
            new TreeBox(0, 0, 108, 62, "Satz", "", "", true, null, false),
            new TreeBox(0, 96, 108, 62, "Subjekt", "ὁ θεὸς", "Gott", false, "#4C9F70", false),
            new TreeBox(118, 96, 108, 62, "Verb", "ἠγάπησεν & <liebte>", "", false, null, false),
        };
        var svg = TreeSvg.Write(boxes, [(0, 1), (0, 2)], 240, 170, title: "Joh 3,16");
        var doc = XDocument.Parse(svg);   // well-formed, special characters escaped
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(2, doc.Descendants(ns + "path").Count());
        Assert.Equal(4, doc.Descendants(ns + "rect").Count());   // background and three boxes
        Assert.Contains(doc.Descendants(ns + "text"), t => t.Value == "ὁ θεὸς");
        Assert.Contains("#4C9F70", svg);
    }

    [Fact]
    public void Long_words_are_shortened_to_fit_their_box() =>
        Assert.EndsWith("…", TreeSvg.Fit(new string('a', 80), 100, 14));
}
