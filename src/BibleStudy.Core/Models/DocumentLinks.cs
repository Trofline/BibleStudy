using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BibleStudy.Core.Import;

namespace BibleStudy.Core.Models;

/// <summary>Finds links inside document text: [[Title]] links to other documents and verse references like "Joh 3,16" or "1. Kor 13:4-7".</summary>
public static partial class DocumentLinks
{
    static readonly Lazy<Dictionary<string, string>> Names = new(Build);

    /// <summary>
    /// One link in a text. Links carry the number of the document: [[#12:Title]] or [[#12:Title|own text]]. The number decides where a link leads, so a new
    /// title cannot break it; the title in it is only there to be readable in the text. [[Title]] without a number (typed by hand, or from older versions) leads by its title.
    /// </summary>
    public sealed record DocLink(long? Id, string Title, string Text, int Index, int Length)
    {
        /// <summary>What the link shows when it has no text of its own: <paramref name="currentTitle"/>, the title of the document now.</summary>
        public string Shown(string? currentTitle = null) => Text.Length > 0 ? Text : currentTitle ?? Title;
    }

    public static IReadOnlyList<DocLink> Links(string body) =>
        Wiki().Matches(body).Select(ToLink).Where(l => l.Title.Length > 0 || l.Id is not null).ToList();

    static DocLink ToLink(Match m) => new(
        m.Groups["id"].Success && long.TryParse(m.Groups["id"].Value, out var id) ? id : null,
        m.Groups["t"].Value.Trim(), m.Groups["a"].Value.Trim(), m.Index, m.Length);

    /// <summary>The link as it is written into a text.</summary>
    public static string LinkText(long id, string title, string shown = "")
    {
        var clean = title.Replace("[", "").Replace("]", "").Replace("|", "").Replace("#", "").Trim();
        var own = shown.Replace("[", "").Replace("]", "").Replace("|", "").Trim();
        return $"[[#{id}:{clean}{(own.Length > 0 && own != title.Trim() ? "|" + own : "")}]]";
    }

    /// <summary>Titles linked in the text, without duplicates. Links with a number name the title too; the store keeps it up to date.</summary>
    public static IReadOnlyList<string> WikiLinks(string body) =>
        Links(body).Select(l => l.Title).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>True when the text links to this document: by its number, or by title for a link without one.</summary>
    public static bool LinksTo(string body, long id, string title) =>
        Links(body).Any(l => l.Id is long n ? n == id : string.Equals(l.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every link gets the number of its document and that document's current title. <paramref name="titleOf"/> gives the title of a number; <paramref name="idOf"/> the number
    /// of a title (null when there is no such document or several). A link that finds nothing stays as it is. Text in HTML comments is left alone.
    /// </summary>
    public static string NumberLinks(string body, Func<long, string?> titleOf, Func<string, long?> idOf)
    {
        if (!body.Contains("[[", StringComparison.Ordinal)) return body;
        var comments = Comment().Matches(body);
        return Wiki().Replace(body, m =>
        {
            if (comments.Any(c => m.Index >= c.Index && m.Index < c.Index + c.Length)) return m.Value;
            var l = ToLink(m);
            if (l.Id is long id && titleOf(id) is { } title) return LinkText(id, title, l.Text);
            return idOf(l.Title) is long found && titleOf(found) is { } t ? LinkText(found, t, l.Text) : m.Value;
        });
    }

    /// <summary>The text with plain [[Title|text]] links, for places where the numbers mean nothing: another person's vault, another program.</summary>
    public static string WithoutNumbers(string body) => Wiki().Replace(body, m =>
    {
        var l = ToLink(m);
        return l.Id is null ? m.Value : $"[[{l.Title}{(l.Text.Length > 0 ? "|" + l.Text : "")}]]";
    });

    /// <summary>The text with links as what they show, for short previews.</summary>
    public static string WithLinkTexts(string body) => Wiki().Replace(body, m => ToLink(m).Shown());

    /// <summary>The text with [[Old]], [[Old|text]] and [[Old#part]] (links without a number) pointing to the new title.</summary>
    public static string RenameLink(string body, string oldTitle, string newTitle) =>
        Wiki().Replace(body, m => !m.Groups["id"].Success && string.Equals(m.Groups["t"].Value.Trim(), oldTitle.Trim(), StringComparison.OrdinalIgnoreCase)
            ? "[[" + newTitle.Trim() + m.Value[(m.Groups["t"].Index - m.Index + m.Groups["t"].Length)..]
            : m.Value);

    /// <summary>Each verse reference outside of a [[link]] goes through <paramref name="replace"/> (null keeps it as it is).</summary>
    public static string ReplaceVerses(string text, Func<string, string?> replace)
    {
        var links = Wiki().Matches(text);
        var result = text;
        foreach (var (index, length, _) in VerseSpans(text).Reverse())
        {
            if (links.Any(l => index < l.Index + l.Length && index + length > l.Index)) continue;
            if (replace(text.Substring(index, length)) is { } instead) result = result[..index] + instead + result[(index + length)..];
        }
        return result;
    }

    /// <summary>
    /// The text for other Markdown programs: a link becomes [text](file) when <paramref name="target"/> knows the file of its document (with the document's current title),
    /// otherwise only its text; verse references become links when <paramref name="verseHref"/> knows a place for them. In an export names no longer change, so files are named after titles.
    /// </summary>
    public static string ToMarkdownLinks(string body, Func<DocLink, (string Title, string File)?> target, Func<string, string?>? verseHref = null)
    {
        var text = verseHref is null ? body : ReplaceVerses(body, span => verseHref(span) is { } href ? $"[{span}]({href})" : null);
        return Wiki().Replace(text, m =>
        {
            var l = ToLink(m);
            return target(l) is { } t ? $"[{l.Shown(t.Title)}]({Uri.EscapeDataString(t.File)})" : l.Shown();
        });
    }

    /// <summary>The text without a first heading that only repeats the title (the export writes the title itself).</summary>
    public static string WithoutTitleHeading(string body, string title)
    {
        var text = body.TrimStart();
        var end = text.IndexOf('\n');
        var first = (end < 0 ? text : text[..end]).Trim();
        return first.StartsWith('#') && string.Equals(first.TrimStart('#').Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase)
            ? (end < 0 ? "" : text[(end + 1)..].TrimStart('\r', '\n'))
            : body;
    }

    /// <summary>Verse references in the text, first verse of a range, in order of appearance without duplicates (each verse of a list "4,13.20" counts).</summary>
    public static IReadOnlyList<VerseRef> VerseRefs(string body)
    {
        var result = new List<VerseRef>();
        foreach (var s in Spans(body))
        {
            if (!result.Contains(s.Ref)) result.Add(s.Ref);
            foreach (var v in s.MoreVerses) if (new VerseRef(s.Ref.Book, s.Ref.Chapter, v) is var r && !result.Contains(r)) result.Add(r);
        }
        return result;
    }

    /// <summary>
    /// Verse references with their last verse ("Joh 3,14-18" → 3,14 to 18; a single verse ends where it starts). A range into a later chapter
    /// ("Jes 44,27–45,3") runs to the end of the first chapter here (999).
    /// </summary>
    public static IReadOnlyList<(VerseRef First, int EndVerse)> VerseRanges(string body) => Spans(body).Select(s => (s.Ref, s.EndInChapter)).ToList();

    /// <summary>Where verse references stand in the text: position, length and the (first) verse, for making them clickable.</summary>
    public static IReadOnlyList<(int Index, int Length, VerseRef Ref)> VerseSpans(string text) => Spans(text).Select(s => (s.Index, s.Length, s.Ref)).ToList();

    /// <summary>A reference in a text: where it stands, its first verse, where it ends (chapter and verse) and the further verses of a list ("4,13.20.22" → 20, 22).</summary>
    public sealed record VerseSpan(int Index, int Length, VerseRef Ref, int EndChapter, int EndVerse, IReadOnlyList<int> MoreVerses)
    {
        /// <summary>The last verse in the first chapter (999: the range goes on into a later chapter).</summary>
        public int EndInChapter => EndChapter > Ref.Chapter ? 999 : Math.Max(Ref.Verse, EndVerse);

        /// <summary>True when the reference takes in this verse: in its range (also one into a later chapter) or in its list.</summary>
        public bool Covers(VerseRef verse)
        {
            if (verse.Book != Ref.Book || verse.Chapter < Ref.Chapter || verse.Chapter > Math.Max(Ref.Chapter, EndChapter)) return false;
            if (verse.Chapter == Ref.Chapter) return verse.Verse >= Ref.Verse && verse.Verse <= EndInChapter || MoreVerses.Contains(verse.Verse);
            return verse.Chapter < EndChapter || verse.Verse <= EndVerse;
        }
    }

    /// <summary>
    /// All the references of a text, in the ways they are written: "Joh 3,16", "1Kö 8,44–48", "Jes 44,27–45,3", "Dan 2,4b–7,28", "Dan 4,13.20.22",
    /// and those that leave out the book because it was just named: "Jer 50,38; 51,30–32" and "(4,13.20)" after "Dan 4,10" in the same line.
    /// A number in front that does not belong to the book ("Seite 4 Joh 3,16") is left out.
    /// </summary>
    public static IReadOnlyList<VerseSpan> Spans(string text)
    {
        var result = new List<VerseSpan>();
        foreach (Match m in Verse().Matches(text))
        {
            var lead = 0;
            if (!TryName(m.Groups["b"].Value, out var book))
            {
                if (Regex.Match(m.Groups["b"].Value, @"^[1-5]\s?\.?\s?") is { Success: true, Length: > 0 } l && TryName(m.Groups["b"].Value[l.Length..], out book)) lead = l.Length;
                else continue;
            }
            if (NoPlace(m)) continue;   // "Joh 0,0": there is no chapter or verse 0
            result.Add(SpanOf(m, m.Index + lead, m.Length - lead, book));
        }
        // places without a book take the book of the reference just before them in the same line
        foreach (Match m in BareVerse().Matches(text))
        {
            var start = m.Index + m.Groups["sep"].Length;
            var end = m.Index + m.Length;
            if (result.Any(r => start < r.Index + r.Length && end > r.Index)) continue;
            // the reference nearest before it (the list is not in order of the text here: places found by this loop come after all named ones)
            if (result.Where(r => r.Index + r.Length <= start).MaxBy(r => r.Index) is not { } before || text.AsSpan(before.Index, start - before.Index).IndexOfAny('\n', '\r') >= 0) continue;
            // "(3,5 Millionen)" is no reference: one in brackets has to end there (or go on with ";" or ","); after ";" or "vgl." anything goes
            var next = end < text.Length ? text[end] : ')';
            var closed = next is ')' or ';' or ',' or ']' or '\n' or '\r' || (next is '.' or ':' && (end + 1 >= text.Length || !char.IsDigit(text[end + 1])));
            if (!closed && m.Groups["sep"].Value.TrimStart().StartsWith('(')) continue;
            if (NoPlace(m)) continue;
            result.Add(SpanOf(m, start, end - start, before.Ref.Book));
        }
        result.Sort((a, b) => a.Index.CompareTo(b.Index));
        return result;
    }

    static bool NoPlace(Match m) => int.Parse(m.Groups["c"].Value) == 0 || int.Parse(m.Groups["v"].Value) == 0;

    static VerseSpan SpanOf(Match m, int index, int length, string book)
    {
        var c = int.Parse(m.Groups["c"].Value); var v = int.Parse(m.Groups["v"].Value);
        var endChapter = m.Groups["ec"].Success ? Math.Max(c, int.Parse(m.Groups["ec"].Value)) : c;
        var endVerse = m.Groups["e"].Success ? int.Parse(m.Groups["e"].Value) : v;
        return new VerseSpan(index, length, new VerseRef(book, c, v), endChapter, endVerse, m.Groups["more"].Captures.Select(x => int.Parse(x.Value)).ToList());
    }

    static bool TryName(string raw, out string book)
    {
        var name = raw.Trim();
        book = "";
        if (name.Length == 0) return false;
        book = Names.Value.TryGetValue(Normalize(name), out var id) ? id : "";
        if (book == "") return false;
        return !(char.IsLower(name[0]) && EverydayWords.Contains(Normalize(name)));
    }

    static readonly HashSet<string> EverydayWords = ["mal", "hab", "am", "ex", "job", "mark", "mi", "hi"];

    /// <summary>USFM id for a book name or abbreviation (German or English), or null.</summary>
    public static string? BookId(string name) => Names.Value.TryGetValue(Normalize(name), out var id) ? id : null;

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
            ("Hebr", "HEB"), ("Koh", "ECC"), ("Hi", "JOB"), ("Esr", "EZR"), ("Obd", "OBA"), ("Zef", "ZEP"), ("Am", "AMO"), ("Mi", "MIC"),   // more of the usual German short forms (Loccum list)
            // and the other ways people write them: "1Kö", "1Kg", "1Mo", "1Chron", English short forms …
            ("1Kö", "1KI"), ("2Kö", "2KI"), ("1Kg", "1KI"), ("2Kg", "2KI"), ("1Kgs", "1KI"), ("2Kgs", "2KI"), ("1Ki", "1KI"), ("2Ki", "2KI"), ("1Kon", "1KI"), ("2Kon", "2KI"),
            ("1Mo", "GEN"), ("2Mo", "EXO"), ("3Mo", "LEV"), ("4Mo", "NUM"), ("5Mo", "DEU"), ("1Mos", "GEN"), ("2Mos", "EXO"), ("3Mos", "LEV"), ("4Mos", "NUM"), ("5Mos", "DEU"),
            ("1Sa", "1SA"), ("2Sa", "2SA"), ("1Ch", "1CH"), ("2Ch", "2CH"), ("1Chron", "1CH"), ("2Chron", "2CH"), ("Esth", "EST"), ("Ijob", "JOB"),
            ("Psa", "PSA"), ("Psalmen", "PSA"), ("Prov", "PRO"), ("Ecc", "ECC"), ("Eccl", "ECC"), ("Song", "SNG"), ("Isa", "ISA"), ("Lam", "LAM"), ("Ez", "EZK"), ("Ezek", "EZK"),
            ("Jon", "JON"), ("Mich", "MIC"), ("Zeph", "ZEP"), ("Sach", "ZEC"), ("Zech", "ZEC"), ("Mat", "MAT"), ("Mrk", "MRK"), ("Luk", "LUK"),
            ("Act", "ACT"), ("Rö", "ROM"), ("1Ko", "1CO"), ("2Ko", "2CO"), ("1Cor", "1CO"), ("2Cor", "2CO"), ("Phl", "PHP"), ("Php", "PHP"), ("Col", "COL"),
            ("1Th", "1TH"), ("2Th", "2TH"), ("1Thes", "1TH"), ("2Thes", "2TH"), ("1Ti", "1TI"), ("2Ti", "2TI"), ("Phm", "PHM"), ("Philem", "PHM"), ("Jas", "JAS"),
            ("1Pe", "1PE"), ("2Pe", "2PE"), ("1Pet", "1PE"), ("2Pet", "2PE"), ("1Jo", "1JN"), ("2Jo", "2JN"), ("3Jo", "3JN"), ("1Jn", "1JN"), ("2Jn", "2JN"), ("3Jn", "3JN"),
            ("Jude", "JUD"), ("Offbg", "REV"), ("Offenb", "REV"),
        }) map.TryAdd(Normalize(alt), id);
        return map;
    }

    [GeneratedRegex(@"\[\[(?:#(?<id>\d+):)?(?<t>[^\]|#]*)(?:[|#](?<a>[^\]]*))?\]\]")] private static partial Regex Wiki();
    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)] private static partial Regex Comment();

    // chapter,verse with an optional letter (4b), a range in the chapter or into another one (44,27–45,3), a list of more verses (.20.22-23) and "f."/"ff."
    const string Place = @"(?<c>\d{1,3})\s*[,:]\s*(?<v>\d{1,3})(?:[a-e](?!\p{L}))?(?:\s*[-–—]\s*(?:(?<ec>\d{1,3})\s*[,:]\s*)?(?<e>\d{1,3})(?:[a-e](?!\p{L}))?(?![,:]\s*\d))?(?:\.(?<more>\d{1,3})(?:[a-e](?!\p{L}))?(?:[-–]\d{1,3})?(?![\d,:]))*(?:\s?ff?\.(?!\p{L}))?";

    // a number before the book name goes up to 5: "4. Mose" and "5. Mose" are books of their own
    [GeneratedRegex(@"(?<![\p{L}\d])(?<b>(?:[1-5]\s?\.?\s?)?\p{L}{1,20}\.?)\s*" + Place)]
    private static partial Regex Verse();

    // a place without a book right after "(", ";", "vgl.", "und" … (its book is that of the reference before it)
    [GeneratedRegex(@"(?<sep>(?:[(;]|(?<![\p{L}])(?:vgl|siehe|s|und|sowie|bzw|auch|cf|see|and)\.?)\s*)(?<![\d,.:])" + Place)]
    private static partial Regex BareVerse();
}
