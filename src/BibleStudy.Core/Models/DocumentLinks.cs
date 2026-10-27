using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BibleStudy.Core.Import;

namespace BibleStudy.Core.Models;

/// <summary>Finds links inside document text: [[Title]] links to other documents and verse references like "Joh 3,16" or "1. Kor 13:4-7".</summary>
public static partial class DocumentLinks
{
    static readonly Lazy<Dictionary<string, string>> Names = new(Build);

    /// <summary>Titles linked with [[Title]] (also [[Title|text]] and [[Title#part]]), without duplicates.</summary>
    public static IReadOnlyList<string> WikiLinks(string body) =>
        Wiki().Matches(body).Select(m => m.Groups["t"].Value.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Verse references in the text, first verse of a range, in order of appearance without duplicates.</summary>
    public static IReadOnlyList<VerseRef> VerseRefs(string body)
    {
        var result = new List<VerseRef>();
        foreach (Match m in Verse().Matches(body))
        {
            if (!Names.Value.TryGetValue(Normalize(m.Groups["b"].Value), out var book)) continue;
            var r = new VerseRef(book, int.Parse(m.Groups["c"].Value), int.Parse(m.Groups["v"].Value));
            if (!result.Contains(r)) result.Add(r);
        }
        return result;
    }

    static string Normalize(string name)
    {
        var folded = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in folded)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>();
        for (var i = 0; i < BookData.UsfmIds.Length; i++)
        {
            var id = BookData.UsfmIds[i];
            foreach (var name in new[] { id, BookData.GermanNames[i], BookData.EnglishNames[i], BookData.GermanAbbreviations[i] }) map.TryAdd(Normalize(name), id);
        }
        // common alternative spellings
        foreach (var (alt, id) in new (string, string)[]
        {
            ("Gen", "GEN"), ("Ex", "EXO"), ("Lev", "LEV"), ("Num", "NUM"), ("Dtn", "DEU"), ("Deut", "DEU"), ("Jos", "JOS"), ("Ri", "JDG"), ("Rut", "RUT"), ("1Sam", "1SA"), ("2Sam", "2SA"),
            ("1Kön", "1KI"), ("2Kön", "2KI"), ("1Chr", "1CH"), ("2Chr", "2CH"), ("Ps", "PSA"), ("Spr", "PRO"), ("Pred", "ECC"), ("Hld", "SNG"), ("Jes", "ISA"), ("Jer", "JER"), ("Hes", "EZK"),
            ("Mt", "MAT"), ("Matt", "MAT"), ("Mk", "MRK"), ("Mar", "MRK"), ("Lk", "LUK"), ("Joh", "JHN"), ("Jn", "JHN"), ("Apg", "ACT"), ("Röm", "ROM"), ("Rom", "ROM"), ("1Kor", "1CO"), ("2Kor", "2CO"),
            ("Gal", "GAL"), ("Eph", "EPH"), ("Phil", "PHP"), ("Kol", "COL"), ("1Thess", "1TH"), ("2Thess", "2TH"), ("1Tim", "1TI"), ("2Tim", "2TI"), ("Tit", "TIT"), ("Heb", "HEB"),
            ("Jak", "JAS"), ("1Petr", "1PE"), ("2Petr", "2PE"), ("1Joh", "1JN"), ("2Joh", "2JN"), ("3Joh", "3JN"), ("Offb", "REV"), ("Apk", "REV"), ("Rev", "REV"),
        }) map.TryAdd(Normalize(alt), id);
        return map;
    }

    [GeneratedRegex(@"\[\[(?<t>[^\]|#]+)(?:[|#][^\]]*)?\]\]")] private static partial Regex Wiki();

    [GeneratedRegex(@"(?<![\p{L}\d])(?<b>(?:[1-3]\s?\.?\s?)?\p{L}{1,20}\.?)\s*(?<c>\d{1,3})\s*[,:]\s*(?<v>\d{1,3})(?:\s*[-–]\s*\d{1,3})?")]
    private static partial Regex Verse();
}
