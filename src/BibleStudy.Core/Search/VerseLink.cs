using System.Text.RegularExpressions;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Search;

/// <summary>
/// Links that open a Bible passage in the app from anywhere (WhatsApp, e-mail, Obsidian, a web page): "biblestudy://Joh3,16".
/// Also read: "biblestudy://Joh%203,16-18", "biblestudy:Röm 8,28", "biblestudy://open?ref=Joh+3,16", "biblestudy://John.3.16" and several passages
/// separated by semicolons. (The pairing links "biblestudy-sync://…" are another scheme and are not read here.)
/// </summary>
public static partial class VerseLink
{
    public const string Scheme = "biblestudy";

    /// <summary>True for an address of this scheme (not the pairing scheme "biblestudy-sync").</summary>
    public static bool IsVerseLink(string? url) => url is not null && url.TrimStart().StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>The passage a link names, readable as "Joh 3,16" (and by <see cref="PassageParser"/>); null when the link names none.</summary>
    public static string? Passage(string? url)
    {
        if (!IsVerseLink(url)) return null;
        var s = url!.Trim()[(Scheme.Length + 1)..].TrimStart('/');

        // the query form: biblestudy://open?ref=Joh+3,16
        var q = s.IndexOf('?');
        if (q >= 0)
        {
            string? found = null;
            foreach (var part in s[(q + 1)..].Split('&'))
            {
                var eq = part.IndexOf('=');
                if (eq > 0 && part[..eq].ToLowerInvariant() is "ref" or "q" or "passage" or "stelle") { found = part[(eq + 1)..].Replace('+', ' '); break; }
            }
            s = found ?? s[..q];
        }
        s = s.Split('#')[0];
        try { s = Uri.UnescapeDataString(s); } catch (UriFormatException) { /* left as it is */ }
        s = s.Replace('_', ' ').Replace(' ', ' ').Trim().TrimEnd('/').Trim();
        if (s.StartsWith("open/", StringComparison.OrdinalIgnoreCase)) s = s[5..].Trim();
        if (s.Length == 0 || s.Length > 200) return null;

        // OSIS style: John.3.16, Joh.3.16-18
        if (Dotted().Match(s) is { Success: true } d)
            s = $"{d.Groups["b"].Value} {d.Groups["c"].Value}" + (d.Groups["v"].Success ? "," + d.Groups["v"].Value : "") + (d.Groups["v2"].Success ? "-" + d.Groups["v2"].Value : "");

        // "Joh3,16" reads as "Joh 3,16"
        s = BookThenNumber().Replace(s, "$1 ");
        return PassageParser.Parse(s, out _) is { Count: > 0 } ? s : null;
    }

    /// <summary>The link for a passage: "biblestudy://Joh3,16", "biblestudy://Joh3,16-18", "biblestudy://Ps23". Only plain letters, so every app shows it as one link.</summary>
    public static string ToUrl(string bookId, int chapter, int? fromVerse = null, int? toVerse = null)
    {
        var s = $"{Scheme}://{BookToken(bookId)}{chapter}";
        if (fromVerse is int f)
        {
            s += "," + f;
            if (toVerse is int t && t > f) s += "-" + t;
        }
        return s;
    }

    /// <summary>A short ASCII name of the book that reads back as the same book: the German short form ("Joh", "1Kor"), else the English name ("1Kings"), else its id.</summary>
    static string BookToken(string bookId)
    {
        var i = Array.IndexOf(BookData.UsfmIds, bookId);
        var candidates = i < 0 ? new[] { bookId } : new[] { BookData.GermanAbbreviations[i], BookData.EnglishNames[i], bookId };
        foreach (var c in candidates)
        {
            var token = new string(c.Where(char.IsLetterOrDigit).ToArray());
            if (token.Length > 0 && token.All(char.IsAscii) && char.IsLetter(token[^1]) && DocumentLinks.BookId(token) == bookId) return token;
        }
        return bookId;
    }

    [GeneratedRegex(@"^(?<b>(?:[1-5]\s?)?\p{L}{1,20})\.(?<c>[0-9]{1,3})(?:\.(?<v>[0-9]{1,3})(?:-(?<v2>[0-9]{1,3}))?)?$")]
    private static partial Regex Dotted();

    [GeneratedRegex(@"(\p{L})\.?(?=[0-9])")]
    private static partial Regex BookThenNumber();
}
