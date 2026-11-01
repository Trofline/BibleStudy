using System.Text;

namespace BibleStudy.Core.Import;

/// <summary>One word of a PDF page with what the layout reader needs: position, size and whether the font is bold or italic.</summary>
public sealed record PdfWord(string Text, double Left, double Bottom, double Size, bool Bold, bool Italic);

public sealed record PdfPageWords(double Width, IReadOnlyList<PdfWord> Words);

/// <summary>
/// Reads a printed Bible (two columns, verse numbers in a bold small font, headings in italics, parallel passages and footnotes in small
/// type, a running head with the book name) from the words of its pages. Everything that is not the Bible text itself (running head, page
/// numbers, headings, parallel passages, footnotes) is left out, hyphenated words are joined, and a verse number only counts when it
/// continues the count, so a number inside a sentence stays text.
/// Printed Bibles often have no chapter numbers in the text: a chapter begins where the verse count starts again (a "2" after a higher
/// verse), and its unnumbered first verse starts after the last heading before that "2".
/// </summary>
public static class PdfBibleLayout
{
    enum Kind { Text, Verse, Book, Heading }

    sealed record Token(Kind Kind, string Text, int Number = 0, string? Book = null, bool Ligature = false);

    static readonly Dictionary<char, string> Ligatures = new() { ['ﬁ'] = "fi", ['ﬂ'] = "fl", ['ﬀ'] = "ff", ['ﬃ'] = "ffi", ['ﬄ'] = "ffl" };

    static (string Text, bool Ligature) Unligate(string s)
    {
        if (!s.Any(Ligatures.ContainsKey)) return (s, false);
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(Ligatures.TryGetValue(c, out var l) ? l : c.ToString());
        return (sb.ToString(), true);
    }

    /// <summary>The text in book / chapter / verse order, or null when the pages do not look like a printed Bible.</summary>
    public static List<(string Book, int Chapter, int Verse, string Text)>? Read(IEnumerable<PdfPageWords> pages, Func<string, string?> bookFromName)
    {
        var tokens = new List<Token>();
        string? lastBook = null; var pendingTitle = -1;
        foreach (var page in pages)
        {
            var words = page.Words.Where(w => w.Text.Length > 0 && w.Left >= 0 && w.Left < page.Width).ToList();
            if (words.Count < 20) continue;
            // the body size is the most common size of plain words
            var bodySize = words.Where(w => !w.Bold && !w.Italic).GroupBy(w => Math.Round(w.Size * 2) / 2).OrderByDescending(g => g.Count()).First().Key;
            bool Near(double s, double target, double tol = 0.6) => Math.Abs(s - target) <= tol;

            // running head: bigger than the body, but not a book title (much bigger)
            var head = words.Where(w => !w.Italic && !w.Bold && w.Size >= bodySize + 1.2 && w.Size <= bodySize + 3.2).ToList();
            var headY = head.Count > 0 ? head.Max(w => w.Bottom) : double.MaxValue;
            if (head.Count > 0)
            {
                var name = new StringBuilder();
                foreach (var w in head.OrderBy(w => w.Left))
                {
                    if (w.Text.Any(char.IsDigit) && !(w.Text.Length <= 3 && w.Text.EndsWith('.') && w.Text[..^1].All(char.IsDigit))) break;
                    name.Append(name.Length > 0 ? " " : "").Append(w.Text);
                }
                var book = name.Length > 0 ? bookFromName(name.ToString()) : null;
                if (name.Length > 0 && book != lastBook)
                {
                    // the page with the book's title has no running head: the book is known from the next page, but it began on the title page
                    if (pendingTitle >= 0 && book is not null) { tokens[pendingTitle] = new Token(Kind.Book, name.ToString(), 0, book); pendingTitle = -1; }
                    else tokens.Add(new Token(Kind.Book, name.ToString(), 0, book));
                    lastBook = book;
                }
            }
            else if (words.Any(w => w.Bold && !w.Italic && w.Size >= bodySize + 4))
            {
                var titleWords = words.Where(w => w.Bold && !w.Italic && w.Size >= bodySize + 4).OrderByDescending(w => Math.Round(w.Bottom / 3)).ThenBy(w => w.Left).Select(w => w.Text);
                var guess = TitleBook(string.Join(' ', titleWords), bookFromName);
                if (guess is not null) { tokens.Add(new Token(Kind.Book, "", 0, guess)); lastBook = guess; }
                else { pendingTitle = tokens.Count; tokens.Add(new Token(Kind.Book, "", 0, null)); lastBook = "\0title"; }
            }

            var body = new List<PdfWord>();
            foreach (var w in words)
            {
                if (w.Bottom >= headY - 2) continue;                           // running head and page number
                var isNumber = w.Text.All(char.IsDigit) && w.Text.Length <= 3;
                if (w.Italic) { if (Near(w.Size, bodySize, 0.8)) body.Add(w); continue; }   // headings (italic, body size); footnote letters are smaller
                if (w.Bold) { if (isNumber && w.Size >= bodySize - 1.6 && w.Size <= bodySize) body.Add(w); continue; }
                if (w.Size < bodySize - 1.5 && w.Size > bodySize - 3.5 && w.Text.Length <= 4 && w.Text.All(char.IsUpper)) { body.Add(w); continue; }   // "ERR" in "HERR"
                if (Near(w.Size, bodySize)) body.Add(w);                        // parallel passages, footnotes and titles are other sizes
            }
            var split = Gutter(body, page.Width);
            foreach (var col in new[] { 0, 1 })
                foreach (var w in body.Where(b => (b.Left < split ? 0 : 1) == col).OrderByDescending(b => Math.Round(b.Bottom / 3)).ThenBy(b => b.Left))
                {
                    if (w.Italic) { if (tokens.Count == 0 || tokens[^1].Kind != Kind.Heading) tokens.Add(new Token(Kind.Heading, "")); continue; }
                    if (w.Size < bodySize - 1.5) { if (tokens.Count > 0 && tokens[^1].Kind == Kind.Text) tokens[^1] = tokens[^1] with { Text = tokens[^1].Text + w.Text }; continue; }
                    if (w.Bold) { tokens.Add(new Token(Kind.Verse, w.Text, int.Parse(w.Text))); continue; }
                    var (plain, lig) = Unligate(w.Text);
                    tokens.Add(new Token(Kind.Text, plain, 0, null, lig));
                }
        }

        // words that were hyphenated at the end of a line, or split behind a ligature ("ﬂ äche")
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i] is not { Kind: Kind.Text } a || tokens[i + 1] is not { Kind: Kind.Text } b || b.Text.Length == 0) continue;
            // a name with a real hyphen ("Jegar-" + "Sahaduta"): the hyphen stays, only the space goes
            if (a.Text.Length > 2 && a.Text.EndsWith('-') && char.IsUpper(b.Text[0]) && char.IsUpper(a.Text[0])) { tokens[i] = a with { Text = a.Text + b.Text }; tokens.RemoveAt(i + 1); i--; continue; }
            if (!char.IsLower(b.Text[0])) continue;
            if (a.Text.Length > 2 && a.Text.EndsWith('-') && char.IsLetter(a.Text[^2])) { tokens[i] = a with { Text = a.Text[..^1] + b.Text, Ligature = b.Ligature }; tokens.RemoveAt(i + 1); i--; }
            else if (a.Ligature && (a.Text.EndsWith("fl") || a.Text.EndsWith("fi") || a.Text.EndsWith("ff") || a.Text.EndsWith("ffi") || a.Text.EndsWith("ffl")) && b.Text.Length > 1) { tokens[i] = a with { Text = a.Text + b.Text, Ligature = false }; tokens.RemoveAt(i + 1); i--; }
        }

        var result = new List<(string, int, int, string)>();
        string? bookId = null; var chapter = 0; var verse = 0; var text = new StringBuilder(); var headingAt = -1;
        void Flush() { if (bookId is not null && chapter > 0 && verse > 0 && text.Length > 0) result.Add((bookId, chapter, verse, text.ToString().Trim())); text.Clear(); headingAt = -1; }
        foreach (var t in tokens)
        {
            switch (t.Kind)
            {
                case Kind.Book: Flush(); bookId = t.Book; chapter = 0; verse = 0; break;
                case Kind.Heading: if (bookId is not null) headingAt = text.Length; break;
                case Kind.Verse when bookId is not null && t.Number == 2 && verse != 1:
                {
                    // the count starts again: a new chapter. Its first verse is what follows the last heading; everything before belongs to the old verse
                    var all = text.ToString();
                    var cut = headingAt >= 0 ? headingAt : (chapter == 0 ? (all.Length <= 600 ? 0 : -1) : all.Length);   // no heading in a book's first chapter: a short text before verse 2 is verse 1
                    var first = cut >= 0 && cut < all.Length ? all[cut..].Trim() : "";
                    text.Clear(); text.Append(cut >= 0 ? all[..cut] : "");
                    Flush();
                    chapter++; verse = 1; text.Append(first); Flush(); verse = 2;
                    break;
                }
                case Kind.Verse when bookId is not null && chapter > 0 && t.Number > verse && t.Number <= verse + 3: Flush(); verse = t.Number; break;
                case Kind.Text when bookId is not null: text.Append(text.Length > 0 ? " " : "").Append(t.Text); break;
            }
        }
        Flush();
        return result.Count >= 500 ? result : null;
    }


    static readonly Dictionary<string, string> Ordinals = new(StringComparer.OrdinalIgnoreCase) { ["erste"] = "1", ["zweite"] = "2", ["dritte"] = "3", ["erster"] = "1", ["zweiter"] = "2", ["dritter"] = "3", ["1."] = "1", ["2."] = "2", ["3."] = "3" };

    /// <summary>The book from the title of its first page ("Der zweite Brief des Johannes", "Das erste Buch Mose (Genesis)", "Der Brief des Paulus an Philemon").</summary>
    static string? TitleBook(string title, Func<string, string?> bookFromName)
    {
        var open = title.IndexOf('(');
        if (open >= 0 && title.IndexOf(')', open) is > 0 and var close && bookFromName(title[(open + 1)..close]) is { } inParens) return inParens;
        var words = title.Replace("(", " ").Replace(")", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;
        var number = words.Select(w => Ordinals.GetValueOrDefault(w)).FirstOrDefault(n => n is not null);
        var last = words[^1];
        if (number is not null && bookFromName($"{number}. {last}") is { } numbered) return numbered;
        if (bookFromName(title) is { } whole) return whole;
        for (var take = Math.Min(3, words.Length); take >= 1; take--)
            if (bookFromName(string.Join(' ', words[^take..])) is { } suffix) return suffix;
        return null;
    }
    /// <summary>
    /// The empty strip between the two columns (the columns are not always the same width): the x where the fewest words reach across.
    /// A plateau of equally empty positions is taken at its middle.
    /// </summary>
    static double Gutter(IReadOnlyList<PdfWord> words, double pageWidth)
    {
        var lo = pageWidth * 0.25; var hi = pageWidth * 0.75;
        double best = int.MaxValue, from = pageWidth / 2, to = pageWidth / 2;
        for (var x = lo; x <= hi; x += 1)
        {
            var across = words.Count(w => w.Left < x && w.Left + w.Text.Length * w.Size * 0.5 > x);
            if (across < best - 0.5) { best = across; from = to = x; }
            else if (Math.Abs(across - best) < 0.5 && Math.Abs(x - to - 1) < 0.01) to = x;
        }
        return (from + to) / 2;
    }
}
