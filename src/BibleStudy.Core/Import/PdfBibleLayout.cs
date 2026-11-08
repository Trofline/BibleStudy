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
    enum Kind { Text, Verse, Book, Heading, Para }

    sealed record Token(Kind Kind, string Text, int Number = 0, string? Book = null, bool Ligature = false);

    static readonly Dictionary<char, string> Ligatures = new() { ['ﬁ'] = "fi", ['ﬂ'] = "fl", ['ﬀ'] = "ff", ['ﬃ'] = "ffi", ['ﬄ'] = "ffl" };

    /// <summary>One italic word alone on its line is a heading only when it looks like one (a capital letter, more than a short word like "Sie" or "ein").</summary>
    static bool IsHeadingWord(string word) => word.Length >= 5 && char.IsUpper(word[0]);

    /// <summary>A word set in small capitals (the "ERR" behind the large "H" of HERR): smaller than the body text, letters all capital, perhaps with a punctuation mark behind (ERRN,).</summary>
    public static bool IsSmallCaps(string text, double size, double bodySize)
    {
        if (size >= bodySize - 1.5 || size <= bodySize - 3.5 || text.Length > 8) return false;
        var letters = text.Where(char.IsLetter).ToList();
        return letters.Count >= 2 && letters.All(char.IsUpper) && text.All(c => char.IsLetter(c) || ".,;:!?)(][»«›‹\"”’-".Contains(c));
    }

    static (string Text, bool Ligature) Unligate(string s)
    {
        if (!s.Any(Ligatures.ContainsKey)) return (s, false);
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(Ligatures.TryGetValue(c, out var l) ? l : c.ToString());
        return (sb.ToString(), true);
    }

    /// <summary>The text in book / chapter / verse order, or null when the pages do not look like a printed Bible.</summary>
    public static List<(string Book, int Chapter, int Verse, string Text)>? Read(IEnumerable<PdfPageWords> pages, Func<string, string?> bookFromName) => ReadFull(pages, bookFromName)?.Verses;

    public sealed record Result(List<(string Book, int Chapter, int Verse, string Text)> Verses, List<(string Book, int Chapter, int Verse, string Text)> Headings);

    /// <summary>The verses and the headings (the italic lines between the verses) of a printed Bible, or null when the pages do not look like one.</summary>
    public static Result? ReadFull(IEnumerable<PdfPageWords> pages, Func<string, string?> bookFromName)
    {
        var tokens = new List<Token>();
        string? lastBook = null; var pendingTitle = -1;
        var seenPages = new HashSet<string>();
        foreach (var page in pages)
        {
            // a PDF that holds the same page twice (some copies repeat whole stretches of pages) is read once
            if (page.Words.Count >= 150 && !seenPages.Add(page.Words.Count + ":" + string.Join(' ', page.Words.Take(120).Select(w => w.Text)))) continue;
            var words = WithoutRepeats(page.Words.Where(w => w.Text.Length > 0 && w.Left >= 0 && w.Left < page.Width));
            if (words.Count < 20) continue;
            // the body size is the most common size of plain words (a page with none, e.g. all in italics, is no Bible text)
            var sizes = words.Where(w => !w.Bold && !w.Italic).GroupBy(w => Math.Round(w.Size * 2) / 2).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (sizes is null) continue;
            var bodySize = sizes.Key;
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
                var isNumber = w.Text.All(char.IsAsciiDigit) && w.Text.Length <= 3;
                if (w.Italic) { if (Near(w.Size, bodySize, 0.8)) body.Add(w); continue; }   // headings (italic, body size); footnote letters are smaller
                if (w.Bold) { if (isNumber && w.Size >= bodySize - 1.6 && w.Size <= bodySize + 0.9) body.Add(w); continue; }   // a few pages set the numbers a little larger than the text
                if (IsSmallCaps(w.Text, w.Size, bodySize)) { body.Add(w); continue; }   // "ERR", "ERRN," in "HERR", "HERRN,": small capitals, maybe with a punctuation mark behind
                if (Near(w.Size, bodySize)) body.Add(w);                        // parallel passages, footnotes and titles are other sizes
            }
            var split = Gutter(body, page.Width);
            // Italic words are a heading when they fill a line on their own; between the other words of a line they are words the translators added: part of the verse
            var headingLines = body.GroupBy(b => (b.Left < split ? 0 : 1, Math.Round(b.Bottom / 3)))
                .Where(g => g.All(x => x.Italic) && (g.Count() >= 2 || IsHeadingWord(g.First().Text))).Select(g => g.Key).ToHashSet();
            double lastRight = 0, lastBottom = 0;
            foreach (var col in new[] { 0, 1 })
            {
                var colWords = body.Where(b => (b.Left < split ? 0 : 1) == col).ToList();
                var firstOfLine = colWords.GroupBy(b => Math.Round(b.Bottom / 3)).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Left).First());
                var margin = firstOfLine.Count == 0 ? 0 : firstOfLine.Values.GroupBy(x => Math.Round(x.Left)).OrderByDescending(g => g.Count()).First().Key;
                // a paragraph begins on an indented line that follows a line which stops short of the right edge (the text is justified, so only the last line of a paragraph does)
                var lineEnd = colWords.GroupBy(b => Math.Round(b.Bottom / 3)).ToDictionary(g => g.Key, g => g.Max(x => x.Left + x.Text.Length * x.Size * 0.5));
                var edge = lineEnd.Count == 0 ? 0 : lineEnd.Values.OrderByDescending(v => v).Skip(lineEnd.Count / 10).First();
                var ordered = lineEnd.Keys.OrderByDescending(k => k).ToList();
                var indentedLines = new HashSet<double>();
                for (var li = 0; li < ordered.Count; li++)
                    if (firstOfLine[ordered[li]] is { Italic: false } f && f.Left - margin is >= 6 and <= 40 && (li == 0 || lineEnd[ordered[li - 1]] < edge - 12)) indentedLines.Add(ordered[li]);
                foreach (var w in colWords.OrderByDescending(b => Math.Round(b.Bottom / 3)).ThenBy(b => b.Left))
                {
                    if (indentedLines.Remove(Math.Round(w.Bottom / 3))) tokens.Add(new Token(Kind.Para, ""));
                    if (w.Italic && headingLines.Contains((col, Math.Round(w.Bottom / 3))))
                    {
                        var word = Unligate(w.Text).Text;
                        if (tokens.Count > 0 && tokens[^1].Kind == Kind.Heading)
                        {
                            var before = tokens[^1].Text;
                            // a word hyphenated at the end of a line is one word again
                            tokens[^1] = tokens[^1] with { Text = before.EndsWith('-') && before.Length > 1 && char.IsLetter(before[^2]) && word.Length > 0 && char.IsLower(word[0]) ? before[..^1] + word : (before + " " + word).Trim() };
                        }
                        else tokens.Add(new Token(Kind.Heading, word));
                        continue;
                    }
                    if (w.Size < bodySize - 1.5)
                    {
                        // small capitals ("ERR" behind the large "H"): they belong to the capital in front of them; anywhere else a small word is no part of the verse
                        var last = tokens.Count > 0 && tokens[^1].Kind == Kind.Text ? tokens[^1] : null;
                        if (last is { Text.Length: 1 } && char.IsUpper(last.Text[0]) && IsSmallCaps(w.Text, w.Size, bodySize)) tokens[^1] = last with { Text = last.Text + w.Text };   // "D" + "AS": a title that begins with a large capital
                        else if (last is not null && last.Text.EndsWith('H') && (last.Text.Length == 1 || !char.IsLetter(last.Text[^2]) || char.IsLower(last.Text[^2]) == false)) tokens[^1] = last with { Text = last.Text + w.Text };
                        else if (w.Text.StartsWith("ERR", StringComparison.Ordinal) && IsSmallCaps(w.Text, w.Size, bodySize)) tokens.Add(new Token(Kind.Text, "H" + w.Text));
                        continue;
                    }
                    if (w.Bold && !w.Italic && w.Text.All(char.IsAsciiDigit))
                    {
                        var number = w.Text;
                        // "8" in the regular font right in front of a bold "3" on the same line is the verse number 83
                        if (tokens.Count > 0 && tokens[^1] is { Kind: Kind.Text, Text.Length: 1 } digit && char.IsAsciiDigit(digit.Text[0]) && lastRight > 0 && Math.Abs(lastBottom - w.Bottom) < 1.5 && w.Left - lastRight < 2.5)
                        { number = digit.Text + number; tokens.RemoveAt(tokens.Count - 1); }
                        tokens.Add(new Token(Kind.Verse, number, int.Parse(number)));
                        lastRight = 0; continue;
                    }
                    var (plain, lig) = Unligate(w.Text);
                    tokens.Add(new Token(Kind.Text, plain, 0, null, lig));
                    lastRight = w.Left + w.Text.Length * w.Size * 0.5; lastBottom = w.Bottom;
                }
            }
        }

        // a word hyphenated at the end of a column goes on at the top of the next one: that line is no paragraph start
        for (var i = 1; i < tokens.Count; i++)
            if (tokens[i].Kind == Kind.Para && tokens[i - 1] is { Kind: Kind.Text } before && before.Text.EndsWith('-')) tokens.RemoveAt(i--);

        // words that were hyphenated at the end of a line, or split behind a ligature ("ﬂ äche")
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i] is not { Kind: Kind.Text } a || tokens[i + 1] is not { Kind: Kind.Text } b || b.Text.Length == 0) continue;
            // a name with a real hyphen ("Jegar-" + "Sahaduta"): the hyphen stays, only the space goes
            if (a.Text.Length > 2 && a.Text.EndsWith('-') && char.IsUpper(b.Text[0]) && char.IsUpper(a.Text[0])) { tokens[i] = a with { Text = a.Text + b.Text }; tokens.RemoveAt(i + 1); i--; continue; }
            // a stray full stop, comma etc. that the PDF has as a word of its own belongs to the word before it
            if (b.Text.Length == 1 && ".,;:!?".Contains(b.Text[0]) && a.Text.Length > 0 && char.IsLetterOrDigit(a.Text[^1])) { tokens[i] = a with { Text = a.Text + b.Text }; tokens.RemoveAt(i + 1); i--; continue; }
            // "d" + "aß": the first letter of a word that the PDF set apart
            if (a.Text == "d" && b.Text.StartsWith("aß", StringComparison.Ordinal)) { tokens[i] = a with { Text = "d" + b.Text }; tokens.RemoveAt(i + 1); i--; continue; }
            if (!char.IsLower(b.Text[0])) continue;
            if (a.Text.Length > 2 && a.Text.EndsWith('-') && char.IsLetter(a.Text[^2])) { tokens[i] = a with { Text = a.Text[..^1] + b.Text, Ligature = b.Ligature }; tokens.RemoveAt(i + 1); i--; }
            else if (a.Ligature && (a.Text.EndsWith("fl") || a.Text.EndsWith("fi") || a.Text.EndsWith("ff") || a.Text.EndsWith("ffi") || a.Text.EndsWith("ffl")) && (b.Text.Length > 1 || b.Text[0] is 'n' or 'e' or 's' or 'r')) { tokens[i] = a with { Text = a.Text + b.Text, Ligature = false }; tokens.RemoveAt(i + 1); i--; }
        }

        JoinSplitWords(tokens);
        TitlesInCapitals(tokens);

        var result = new List<(string, int, int, string)>();
        var headings = new List<(string, int, int, string)>(); var pending = new List<string>();
        string? bookId = null; var chapter = 0; var verse = 0; var text = new StringBuilder(); var headingAt = -1; var paras = new List<int>();
        // the headings seen since the last verse number stand in front of the verse that begins now
        void PlaceHeadings() { foreach (var h in pending) if (bookId is not null && chapter > 0 && verse > 0 && h.Length > 0) headings.Add((bookId, chapter, verse, h)); pending.Clear(); }
        void Flush() { if (bookId is not null && chapter > 0 && verse > 0 && text.Length > 0) result.Add((bookId, chapter, verse, text.ToString().Trim())); text.Clear(); headingAt = -1; paras.Clear(); }
        foreach (var t in tokens)
        {
            switch (t.Kind)
            {
                case Kind.Book: Flush(); bookId = t.Book; chapter = 0; verse = 0; pending.Clear(); break;
                case Kind.Para: if (bookId is not null && text.Length > 0) paras.Add(text.Length); break;
                case Kind.Heading: if (bookId is not null) { headingAt = text.Length; pending.Add(t.Text); } break;
                case Kind.Verse when bookId is not null && (t.Number == 2 && verse != 1 || t.Number == 3 && verse > 5 && headingAt >= 0):
                {
                    // the count starts again: a new chapter. Its first verse is what follows the last heading; everything before belongs to the old verse.
                    // (A "3" counts the same when the "2" is missing in the print: after a heading the numbers cannot go from 20 back to 3 within a chapter.)
                    var all = text.ToString();
                    // in a book's first chapter the text before verse 2 is verse 1 (the headings stand in front of it, also when the page puts them behind it)
                    var cut = chapter == 0 ? (headingAt >= 0 && headingAt < all.Length ? headingAt : (all.Length <= 600 ? 0 : -1)) : headingAt >= 0 && headingAt < all.Length - 2 ? headingAt : NewVerseOneStart(all, paras);   // a heading with no text behind it was set after its paragraph: the paragraph is found by its indent
                    var first = cut >= 0 && cut < all.Length ? all[cut..].Trim() : "";
                    text.Clear(); text.Append(cut >= 0 ? all[..cut] : "");
                    Flush();
                    chapter++; verse = 1; PlaceHeadings();
                    if (t.Number == 3 && SplitFirstSentence(first) is var (one, two)) { text.Append(one); Flush(); verse = 2; text.Append(two); Flush(); }
                    else { text.Append(first); Flush(); }
                    verse = t.Number;
                    break;
                }
                case Kind.Verse when bookId is not null && chapter > 0 && t.Number > verse && t.Number <= verse + 3: Flush(); verse = t.Number; PlaceHeadings(); break;
                case Kind.Text when bookId is not null: text.Append(text.Length > 0 ? " " : "").Append(t.Text); break;
            }
        }
        Flush();
        return result.Count >= 500 ? new Result(result, headings) : null;
    }


    /// <summary>Verse 1 and 2 of a chapter whose printed "2" is missing: the first sentence (up to a full stop or a colon followed by a capital) and the rest.</summary>
    static (string One, string Two)? SplitFirstSentence(string text)
    {
        for (var i = 15; i + 16 < text.Length; i++)
            if (text[i] == ' ' && ".!?:;".Contains(text[i - 1]) && (char.IsUpper(text[i + 1]) || text[i + 1] is '»' or '„' or '“')) return (text[..i].Trim(), text[(i + 1)..].Trim());
        return null;
    }

    static string Core(string w) => w.Trim('.', ',', ';', ':', '!', '?', '»', '«', '›', '‹', '"', '“', '”', '„', '(', ')', '[', ']', '—');

    /// <summary>
    /// Letters that the print set apart from their word ("gin g en", "de r", "An stoßes") are joined again when the joined word occurs elsewhere in the same Bible
    /// and the pieces do not (a real word is never changed: only fragments are put back).
    /// </summary>
    static void JoinSplitWords(List<Token> tokens)
    {
        var freq = new Dictionary<string, int>();
        foreach (var t in tokens) if (t.Kind == Kind.Text && Core(t.Text) is { Length: > 0 } c && c.All(char.IsLetter)) freq[c] = freq.GetValueOrDefault(c) + 1;
        int F(string w) => freq.GetValueOrDefault(w);
        bool Plain(int i, bool forward) => i >= 0 && i < tokens.Count && tokens[i].Kind == Kind.Text && tokens[i].Text.Length > 0 && (forward ? true : char.IsLetter(tokens[i].Text[^1]));
        for (var i = 1; i + 1 < tokens.Count; i++)
        {
            if (tokens[i].Kind != Kind.Text) continue;
            var t = tokens[i].Text;
            if (t.Length == 2 && char.IsLetter(t[0]) && t[1] == '-' && Plain(i - 1, false) && Plain(i + 1, true) && char.IsLower(tokens[i + 1].Text[0]) && Core(tokens[i - 1].Text) is { Length: > 2 } fragment && fragment.All(char.IsLetter) && F(fragment) <= 2)
            {
                tokens[i - 1] = tokens[i - 1] with { Text = tokens[i - 1].Text + t[0] + tokens[i + 1].Text }; tokens.RemoveRange(i, 2); i--; continue;
            }
            // "g" + "e-" + "stoßen": a lone letter before a letter with a hyphen is one word with what comes after the hyphen
            if (t.Length == 2 && char.IsLower(t[0]) && t[1] == '-' && tokens[i - 1] is { Kind: Kind.Text, Text.Length: 1 } lone && char.IsLower(lone.Text[0]) && Plain(i + 1, true) && char.IsLower(tokens[i + 1].Text[0]))
            {
                tokens[i - 1] = lone with { Text = lone.Text + t[0] + tokens[i + 1].Text }; tokens.RemoveRange(i, 2); i--; continue;
            }
            if (t.Length == 1 && char.IsLower(t[0]) && "bcdefghjklmnprstvwxz".Contains(t[0]) && Plain(i - 1, false) && Plain(i + 1, true))
            {
                var a = tokens[i - 1].Text; var b = tokens[i + 1].Text;
                if (F(Core(a + t + b)) >= 2 && Core(a).All(char.IsLetter) && F(Core(a)) < F(Core(a + t + b)) * 2) { tokens[i - 1] = tokens[i - 1] with { Text = a + t + b }; tokens.RemoveRange(i, 2); i--; continue; }
                if (F(Core(a + t)) >= 2 && a.Length >= 2 && char.IsLetter(a[^1])) { tokens[i - 1] = tokens[i - 1] with { Text = a + t }; tokens.RemoveAt(i); i--; continue; }
                if (F(Core(t + b)) >= 1 && char.IsLetter(b[0])) { tokens[i + 1] = tokens[i + 1] with { Text = t + b }; tokens.RemoveAt(i); i--; continue; }
            }
            else if (Plain(i - 1, false) && tokens[i - 1].Kind == Kind.Text && Core(tokens[i - 1].Text) is { Length: > 1 } a2 && a2.All(char.IsLetter) && tokens[i - 1].Text[^1] != '-' && Core(t) is { Length: > 1 } b2 && b2.All(char.IsLetter)
                     && !t.StartsWith('-') && (F(b2) <= 2 || F(a2) <= 2 && tokens[i - 1].Text == a2) && F(a2 + b2) >= 2 && char.IsLower(b2[0]) && (a2 + b2).Length >= 5 && t == b2 + t[b2.Length..])
            { tokens[i - 1] = tokens[i - 1] with { Text = tokens[i - 1].Text + t }; tokens.RemoveAt(i); i--; }
        }
    }

    static readonly HashSet<string> DivineNames = ["HERR", "HERRN", "GOTT", "GOTTES", "HERRN,", "HERR,", "JAH", "JHWH"];

    /// <summary>A line of capital letters in the text (a part title like "DIE URZEIT" or "DAS LEIDEN UND STERBEN JESU CHRISTI") is a heading, not part of a verse.</summary>
    static void TitlesInCapitals(List<Token> tokens)
    {
        bool Caps(Token t) => t.Kind == Kind.Text && Core(t.Text) is { Length: >= 3 } c && c.All(char.IsLetter) && c.All(char.IsUpper);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!Caps(tokens[i])) continue;
            var j = i; while (j + 1 < tokens.Count && Caps(tokens[j + 1])) j++;
            // also the small words in between, which are capitals too, but short ("UND")
            if (j > i && Enumerable.Range(i, j - i + 1).Any(k => !DivineNames.Contains(Core(tokens[k].Text))))
            {
                var title = string.Join(' ', Enumerable.Range(i, j - i + 1).Select(k => tokens[k].Text));
                tokens.RemoveRange(i, j - i + 1);
                tokens.Insert(i, new Token(Kind.Heading, title));
            }
            i = Math.Max(i, j - (j - i));
        }
    }

    /// <summary>Some pages carry their text twice, one layer over the other (the same word at the same place): it is read once.</summary>
    static List<PdfWord> WithoutRepeats(IEnumerable<PdfWord> words)
    {
        var kept = new List<PdfWord>(); var byText = new Dictionary<string, List<PdfWord>>();
        foreach (var w in words)
        {
            if (!byText.TryGetValue(w.Text, out var same)) byText[w.Text] = same = [];
            if (same.Any(o => Math.Abs(o.Left - w.Left) < 1.5 && Math.Abs(o.Bottom - w.Bottom) < 1.5)) continue;
            same.Add(w); kept.Add(w);
        }
        return kept;
    }

    /// <summary>
    /// Where the first verse of a new chapter begins in a text that holds the last verse of the old chapter and the unnumbered first verse of the new one, when no heading
    /// marks it: at the last paragraph start (the first verse of a chapter begins a paragraph), else at the last sentence end. all.Length when nothing is certain.
    /// </summary>
    static int NewVerseOneStart(string all, List<int> paragraphStarts)
    {
        var para = paragraphStarts.Where(p => p > 0 && p < all.Length - 8).DefaultIfEmpty(-1).Max();
        if (para > 0) return para;
        var cut = -1;
        for (var i = 1; i + 2 < all.Length; i++)
            if (all[i] == ' ' && (".!?".Contains(all[i - 1]) || "«»“”’›\"".Contains(all[i - 1]) && ".!?,".Contains(all[i - 2])) && i - 1 > 10 && (char.IsUpper(all[i + 1]) || all[i + 1] is '»' or '„' or '“') && all.Length - i > 25) cut = i + 1;
        return cut > 0 ? cut : all.Length;
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
        // "Die Offenbarung des Johannes", "Die Klagelieder des Jeremia": the name of the book comes before the name of the author, who is a book of his own
        foreach (var w in words[..^1]) if (w.Length > 4 && char.IsUpper(w[0]) && bookFromName(w) is { } named) return named;
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
