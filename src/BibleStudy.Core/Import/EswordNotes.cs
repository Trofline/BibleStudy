using System.Text;
using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Import;

/// <summary>A note on a verse (or on verses up to <see cref="EndVerse"/>) from another program.</summary>
public sealed record ImportedNote(VerseRef Ref, int EndVerse, string Text);

/// <summary>A longer text from another program: a topic, or the notes on a book or a chapter.</summary>
public sealed record ImportedText(string Title, string Text);

public sealed record ImportedNotes(string Name, IReadOnlyList<ImportedNote> Notes, IReadOnlyList<ImportedText> Texts);

/// <summary>
/// The user's own files of e-Sword: notes and own commentaries (.notx, .cmtx) and topic notes (.topx). They are SQLite files whose tables
/// differ a little between versions, so the tables are recognised by their columns: book, chapter, verse (or the beginning and end of a
/// range) and the text, which e-Sword keeps as RTF or HTML. MySword commentaries (.mybible / .cmt.mybible) are read the same way.
/// </summary>
public static class EswordNotes
{
    public static readonly string[] Extensions = [".notx", ".cmtx", ".topx", ".mybible"];

    public static ImportedNotes Read(string path)
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        con.Open();
        var name = Path.GetFileNameWithoutExtension(path);
        var notes = new List<ImportedNote>();
        var texts = new List<ImportedText>();

        var tables = new List<string>();
        using (var t = con.CreateCommand())
        {
            t.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var r = t.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }
        foreach (var table in tables)
        {
            // the description of the module, its settings and the internal tables of SQLite are no notes
            if (table.Equals("Details", StringComparison.OrdinalIgnoreCase) || table.Equals("info", StringComparison.OrdinalIgnoreCase) || table.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)) continue;
            var columns = new List<string>();
            using (var c = con.CreateCommand())
            {
                c.CommandText = $"SELECT name FROM pragma_table_info('{table.Replace("'", "''")}')";
                using var r = c.ExecuteReader();
                while (r.Read()) columns.Add(r.GetString(0));
            }
            string? Col(params string[] names) => columns.FirstOrDefault(c => names.Any(n => c.Equals(n, StringComparison.OrdinalIgnoreCase)));
            var text = Col("Comments", "Comment", "Notes", "Note", "Text", "Content", "Data");
            if (text is null) continue;
            var book = Col("Book", "BookNumber", "book_number");
            var chapter = Col("Chapter", "ChapterBegin", "ChapterStart", "chapter_number_from");
            var verse = Col("Verse", "VerseBegin", "VerseStart", "verse_number_from");
            var chapterEnd = Col("ChapterEnd", "chapter_number_to");
            var verseEnd = Col("VerseEnd", "verse_number_to");
            var title = Col("Title", "Topic", "Name", "Subject");

            using var q = con.CreateCommand();
            if (book is not null)
            {
                q.CommandText = $"SELECT \"{book}\", {(chapter is null ? "NULL" : $"\"{chapter}\"")}, {(verse is null ? "NULL" : $"\"{verse}\"")}, " +
                                $"{(chapterEnd is null ? "NULL" : $"\"{chapterEnd}\"")}, {(verseEnd is null ? "NULL" : $"\"{verseEnd}\"")}, \"{text}\" FROM \"{table}\"";
                using var r = q.ExecuteReader();
                var mySword = book.Equals("book_number", StringComparison.OrdinalIgnoreCase);
                while (r.Read())
                {
                    if (r.IsDBNull(0) || r.IsDBNull(5)) continue;
                    // a row with a number that is no number is left out, the others of the file still come in
                    if (Number(r, 0) is not { } bookNumber || BookId(bookNumber, mySword) is not { } b) continue;
                    var ch = Number(r, 1) ?? 0;
                    var v = Number(r, 2) ?? 0;
                    var ce = Number(r, 3) ?? ch;
                    var ve = Number(r, 4) ?? v;
                    var body = ToText(Convert.ToString(r.GetValue(5)) ?? "");
                    if (body.Length == 0) continue;
                    if (ch > 0 && v > 0) notes.Add(new ImportedNote(new VerseRef(b, ch, v), ce == ch && ve > v ? ve : v, body));
                    else if (ch > 0) texts.Add(new ImportedText($"{name}: {b} {ch}", body));
                    else texts.Add(new ImportedText($"{name}: {b}", body));
                }
            }
            else if (title is not null)
            {
                q.CommandText = $"SELECT \"{title}\", \"{text}\" FROM \"{table}\"";
                using var r = q.ExecuteReader();
                while (r.Read())
                {
                    if (r.IsDBNull(1)) continue;
                    var body = ToText(Convert.ToString(r.GetValue(1)) ?? "");
                    if (body.Length == 0) continue;
                    texts.Add(new ImportedText(r.IsDBNull(0) ? name : (Convert.ToString(r.GetValue(0)) ?? name).Trim(), body));
                }
            }
        }
        return new ImportedNotes(name, notes, texts);
    }

    /// <summary>A whole number from a column (stored as a number or as text); null when it is empty or no number.</summary>
    static int? Number(SqliteDataReader r, int column)
    {
        if (r.IsDBNull(column)) return null;
        var value = r.GetValue(column);
        return value switch
        {
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            double d when d == Math.Floor(d) && Math.Abs(d) < int.MaxValue => (int)d,
            string s when int.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i) => i,
            _ => null,
        };
    }

    /// <summary>e-Sword counts the books 1–66; MySword commentaries use the numbers of MyBible (10, 20 … 730).</summary>
    static string? BookId(int number, bool myBible)
    {
        if (myBible)
        {
            var i = Array.IndexOf(MyBibleNumbers, number);
            return i >= 0 ? BookData.UsfmIds[i] : null;
        }
        return number >= 1 && number <= 66 ? BookData.UsfmIds[number - 1] : null;
    }

    static readonly int[] MyBibleNumbers =
    [
        10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 190, 220, 230, 240, 250, 260, 290, 300, 310, 330, 340, 350, 360, 370, 380,
        390, 400, 410, 420, 430, 440, 450, 460, 470, 480, 490, 500, 510, 520, 530, 540, 550, 560, 570, 580, 590, 600, 610, 620, 630, 640, 650, 660, 670,
        680, 690, 700, 710, 720, 730,
    ];

    /// <summary>The text of an e-Sword note: RTF or HTML becomes plain text with its paragraphs.</summary>
    public static string ToText(string s)
    {
        s = s.Trim();
        if (s.StartsWith(@"{\rtf", StringComparison.Ordinal)) s = RtfToText(s);
        else if (s.Contains('<') && s.Contains('>')) s = HtmlToText(s);
        var lines = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd());
        var sb = new StringBuilder();
        var blank = 0;
        foreach (var l in lines)
        {
            if (l.Trim().Length == 0) { blank++; continue; }
            if (sb.Length > 0) sb.Append(blank > 0 ? "\n\n" : "\n");
            sb.Append(l.Trim());
            blank = 0;
        }
        return sb.ToString();
    }

    static string HtmlToText(string html)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(html, @"<\s*(br|/p|/div|/li|/h\d)\s*/?\s*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(s);
    }

    /// <summary>Plain text from RTF: groups that hold no text (fonts, colours, pictures …) are skipped, \par is a line break, \'hh and \uN are characters.</summary>
    public static string RtfToText(string rtf)
    {
        var sb = new StringBuilder();
        var skip = new Stack<bool>();
        var skipping = false;
        var uc = 1; var pendingSkip = 0;
        var cp1252 = CodePages();
        for (var i = 0; i < rtf.Length; i++)
        {
            var c = rtf[i];
            switch (c)
            {
                case '{': skip.Push(skipping); break;
                case '}': skipping = skip.Count > 0 && skip.Pop(); break;
                case '\\':
                {
                    if (i + 1 >= rtf.Length) break;
                    var n = rtf[i + 1];
                    if (n is '\\' or '{' or '}') { if (!skipping && pendingSkip == 0) sb.Append(n); else if (pendingSkip > 0) pendingSkip--; i++; break; }
                    if (n == '\'' && i + 3 < rtf.Length)
                    {
                        var hex = rtf.Substring(i + 2, 2);
                        i += 3;
                        if (pendingSkip > 0) { pendingSkip--; break; }
                        if (!skipping && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code)) sb.Append(cp1252?.GetString([(byte)code]) ?? ((char)code).ToString());
                        break;
                    }
                    if (n == '*') { skipping = true; i++; break; }
                    // a control word: letters, then perhaps a number, then perhaps one space
                    var j = i + 1;
                    while (j < rtf.Length && char.IsAsciiLetter(rtf[j])) j++;
                    var word = rtf[(i + 1)..j];
                    var k = j;
                    if (k < rtf.Length && (rtf[k] == '-' || char.IsAsciiDigit(rtf[k]))) { k++; while (k < rtf.Length && char.IsAsciiDigit(rtf[k])) k++; }
                    var arg = rtf[j..k];
                    if (k < rtf.Length && rtf[k] == ' ') k++;
                    i = k - 1;
                    if (word.Length == 0) { i = j; break; }
                    switch (word)
                    {
                        case "fonttbl" or "colortbl" or "stylesheet" or "info" or "pict" or "header" or "footer" or "listtable" or "listoverridetable" or "rsidtbl" or "generator" or "xmlnstbl":
                            skipping = true; break;
                        case "par" or "line" or "sect" or "page": if (!skipping) sb.Append('\n'); break;
                        case "tab": if (!skipping) sb.Append('\t'); break;
                        case "uc": uc = int.TryParse(arg, out var u) ? u : 1; break;
                        case "u":
                            if (!skipping && int.TryParse(arg, out var ch)) sb.Append((char)(ch < 0 ? ch + 65536 : ch));
                            pendingSkip = uc;
                            break;
                    }
                    break;
                }
                case '\r' or '\n': break;
                default:
                    if (pendingSkip > 0) { pendingSkip--; break; }
                    if (!skipping) sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    static Encoding? CodePages()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(1252); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return null; }
    }
}
