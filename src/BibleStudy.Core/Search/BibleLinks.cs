using System.Text.RegularExpressions;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Search;

/// <summary>
/// Links that open a Bible passage in BibleStudy. The passage is written in a form that does not depend on the language of the program
/// ("JHN.3.16", "PSA.23", "ROM.8.28-30", "JHN.3.16-4.2"; several passages with ";"), so a link sent from a German program opens on an English one.
/// <list type="bullet">
/// <item><c>biblestudy://open/JHN.3.16</c>: opens the program directly (the program registers the scheme).</item>
/// <item><c>https://trofline.github.io/BibleStudy/v/?JHN.3.16</c>: a web page that forwards to the program; messengers and e-mail programs
/// only make web addresses clickable.</item>
/// </list>
/// Links typed by hand are read too: <c>biblestudy://Joh 3,16</c>, <c>biblestudy://open/Röm%208,28</c>.
/// </summary>
public static partial class BibleLinks
{
    public const string Scheme = "biblestudy";
    public const string AppPrefix = "biblestudy://open/";
    public const string WebPrefix = "https://trofline.github.io/BibleStudy/v/?";

    /// <summary>The language-free form of one passage: "JHN.3.16", "JHN.3.16-18", "JHN.3.16-4.2", "PSA.23", "PSA.23-25".</summary>
    public static string Code(PassageRef p)
    {
        var s = $"{p.Book}.{p.Chapter}";
        if (p.FromVerse is int from)
        {
            s += "." + from;
            if (p.ToChapter is int c2 && c2 != p.Chapter && p.ToVerse is int v2) s += $"-{c2}.{v2}";
            else if (p.ToVerse is int to && to > from) s += "-" + to;
        }
        else if (p.ToChapter is int last && last > p.Chapter) s += "-" + last;
        return s;
    }

    public static string Code(IEnumerable<PassageRef> passages) => string.Join(";", passages.Select(Code));

    public static string AppLink(IEnumerable<PassageRef> passages) => AppPrefix + Code(passages);

    public static string WebLink(IEnumerable<PassageRef> passages) => WebPrefix + Code(passages);

    /// <summary>One verse or a run of verses of one chapter as a passage (<paramref name="last"/> ≤ <paramref name="verse"/>: the verse alone).</summary>
    public static PassageRef Passage(string book, int chapter, int verse, int last = 0) => new(book, chapter, verse, Math.Max(verse, last));

    /// <summary>True for an address this class can read (the app link or the web page); text that only contains a passage is not a link.</summary>
    public static bool IsLink(string text)
    {
        var t = text.Trim();
        return t.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase) || t.StartsWith(WebPrefix[..^1], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The passages of a link; null when it is not a link to a passage (a pairing link "biblestudy-sync:" is not one).</summary>
    public static IReadOnlyList<PassageRef>? Parse(string link)
    {
        var t = link.Trim();
        string rest;
        if (t.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase)) rest = t[(Scheme.Length + 3)..];
        else if (t.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase)) rest = t[(Scheme.Length + 1)..];
        else if (t.StartsWith(WebPrefix[..^1], StringComparison.OrdinalIgnoreCase))
        {
            // the page itself: …/v/?JHN.3.16, …/v/index.html?JHN.3.16, also …/v/?r=JHN.3.16 or …/v/#JHN.3.16
            var q = t.IndexOfAny(['?', '#']);
            if (q < 0) return null;
            rest = t[(q + 1)..];
            if (rest.StartsWith("r=", StringComparison.OrdinalIgnoreCase)) rest = rest[2..];
        }
        else return null;
        if (rest.StartsWith("open/", StringComparison.OrdinalIgnoreCase)) rest = rest[5..];
        else if (rest.StartsWith("open?r=", StringComparison.OrdinalIgnoreCase)) rest = rest[7..];
        var cut = rest.IndexOfAny(['&', '#']);
        if (cut >= 0) rest = rest[..cut];
        try { rest = Uri.UnescapeDataString(rest.Replace('+', ' ')); } catch (UriFormatException) { return null; }
        rest = rest.Trim().TrimEnd('/');
        return rest.Length == 0 ? null : ParseCodes(rest) ?? PassageParser.Parse(rest, out _);
    }

    /// <summary>"JHN.3.16;ROM.8.28-30" (upper or lower case); null when one of the parts is not such a code.</summary>
    public static IReadOnlyList<PassageRef>? ParseCodes(string text)
    {
        var result = new List<PassageRef>();
        foreach (var part in text.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = CodePattern().Match(part);
            if (!m.Success) return null;
            var book = m.Groups["b"].Value.ToUpperInvariant();
            if (Array.IndexOf(BookData.UsfmIds, book) < 0) return null;
            var c = int.Parse(m.Groups["c"].Value);
            if (c < 1) return null;
            if (m.Groups["v"].Success)
            {
                var v = int.Parse(m.Groups["v"].Value);
                if (m.Groups["c2"].Success) result.Add(new(book, c, v, int.Parse(m.Groups["v2"].Value), int.Parse(m.Groups["c2"].Value)));
                else result.Add(new(book, c, v, m.Groups["v2"].Success ? Math.Max(v, int.Parse(m.Groups["v2"].Value)) : v));
            }
            else if (m.Groups["v2"].Success) result.Add(new(book, c, null, null, Math.Max(c, int.Parse(m.Groups["v2"].Value))));
            else result.Add(new(book, c, null, null));
        }
        return result.Count > 0 ? result : null;
    }

    [GeneratedRegex(@"^(?<b>[1-3]?[A-Za-z]{2,3})\.(?<c>[0-9]{1,3})(?:\.(?<v>[0-9]{1,3}))?(?:-(?:(?<c2>[0-9]{1,3})\.)?(?<v2>[0-9]{1,3}))?$")]
    private static partial Regex CodePattern();

    /// <summary>
    /// What was shared with the program (another app's "Share", a pasted text): first a link in it, else the passages named in it
    /// ("Joh 3,16 – Denn so sehr …", a list of passages). Empty when nothing was found.
    /// </summary>
    public static IReadOnlyList<PassageRef> FromSharedText(string text)
    {
        foreach (Match m in LinkInText().Matches(text))
            if (Parse(m.Value) is { Count: > 0 } found) return found;
        return PassageParser.Extract(text, out _);
    }

    [GeneratedRegex(@"(?:biblestudy:(?://)?|https://trofline\.github\.io/BibleStudy/v/[^\s?#]*[?#])[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkInText();

    /// <summary>The first verse to show for a passage (a whole chapter starts at its top: null).</summary>
    public static VerseRef Start(PassageRef p) => new(p.Book, p.Chapter, p.FromVerse ?? 1);
}
