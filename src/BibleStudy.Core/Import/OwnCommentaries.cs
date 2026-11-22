using System.Text.RegularExpressions;
using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Core.Import;

/// <summary>One entry of a commentary: the verses From..To of a chapter (From 0: the chapter as a whole, an introduction; To 999: on into the next chapter).</summary>
public sealed record CommentaryEntry(string Book, int Chapter, int From, int To, string Text);

public sealed record ParsedCommentary(string Name, IReadOnlyList<CommentaryEntry> Entries);

/// <summary>
/// Commentaries the user brings along. BibleStudy ships none: what a commentary teaches is for the user to judge, so only files the user chooses are read:
/// e-Sword (.cmtx) and MySword (.cmt.mybible) modules, and Markdown or text files in which a line with a Bible reference ("## Joh 3,16-18") starts the
/// entry for these verses and a heading with a chapter ("## Johannes 3") the introduction of that chapter.
/// </summary>
public static partial class OwnCommentaries
{
    public static readonly string[] Extensions = [".cmtx", ".mybible", ".md", ".markdown", ".txt"];

    public static ParsedCommentary Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var fallback = NameOf(path);
        var parsed = ext is ".md" or ".markdown" or ".txt" ? ReadText(File.ReadAllText(path), fallback) : ReadModule(path, fallback);
        if (parsed.Entries.Count == 0)
            throw new InvalidDataException(Loc.L("In der Datei wurden keine Kommentare gefunden. Erwartet: ein e-Sword-Kommentar (.cmtx), ein MySword-Kommentar (.cmt.mybible) oder eine Markdown- oder Textdatei, in der jede Bibelstelle (zum Beispiel „## Joh 3,16“) eine eigene Zeile hat."));
        return parsed;
    }

    /// <summary>"Calvin.cmt.mybible" → "Calvin".</summary>
    static string NameOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.EndsWith(".cmt", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Trim().Length > 0 ? name.Trim() : "Kommentar";
    }

    // ---- e-Sword and MySword modules --------------------------------------------------------------------------------------

    static ParsedCommentary ReadModule(string path, string fallback)
    {
        var read = EswordNotes.Read(path);
        var entries = read.Notes.Select(n => new CommentaryEntry(n.Ref.Book, n.Ref.Chapter, n.Ref.Verse, Math.Max(n.Ref.Verse, n.EndVerse), n.Text)).ToList();
        // the text about a chapter or a book as a whole (the reader names it "<module>: <book> <chapter>")
        foreach (var t in read.Texts)
        {
            var m = ChapterTitle().Match(t.Title);
            if (m.Success) entries.Add(new CommentaryEntry(m.Groups["b"].Value, m.Groups["c"].Success ? int.Parse(m.Groups["c"].Value) : 1, 0, 0, t.Text));
        }
        return new ParsedCommentary(ModuleTitle(path) ?? fallback, entries);
    }

    /// <summary>The title the module gives itself (e-Sword: Details.Title, MySword: Details.Description); null when it has none.</summary>
    static string? ModuleTitle(string path)
    {
        try
        {
            using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            con.Open();
            foreach (var column in new[] { "Title", "Description" })
            {
                try
                {
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = $"SELECT {column} FROM Details LIMIT 1";
                    if (cmd.ExecuteScalar() is string s && s.Trim().Length > 0) return EswordNotes.ToText(s).Split('\n')[0].Trim();
                }
                catch (SqliteException) { /* the other column */ }
            }
        }
        catch (SqliteException) { /* no title: the file name serves */ }
        return null;
    }

    [GeneratedRegex(@": (?<b>[1-3A-Z]{3})(?: (?<c>\d+))?$")] private static partial Regex ChapterTitle();

    // ---- Markdown and text ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A line that is only a Bible reference (also as a heading, bold or with a colon) starts the entry for these verses; the lines below belong to it until the next
    /// such line. A heading with a book and a chapter ("# Johannes 3") starts the introduction of that chapter. A first heading that is no reference names the commentary.
    /// </summary>
    public static ParsedCommentary ReadText(string text, string fallbackName)
    {
        var lines = text.TrimStart('﻿').ReplaceLineEndings("\n").Split('\n').ToList();
        string? name = null;
        if (lines.Count > 0 && lines[0].Trim() == "---")
        {
            var end = lines.FindIndex(1, l => l.Trim() == "---");
            if (end > 0)
            {
                foreach (var l in lines.Take(end).Skip(1)) if (l.StartsWith("title:", StringComparison.OrdinalIgnoreCase)) name = l[6..].Trim().Trim('"', '\'');
                lines = lines.Skip(end + 1).ToList();
            }
        }
        var entries = new List<CommentaryEntry>();
        (string Book, int Chapter, int From, int To)? open = null;
        var body = new List<string>();
        void Close()
        {
            if (open is { } o)
            {
                var t = string.Join("\n", body.Select(b => b.Trim()).Where(b => b.Length > 0));
                if (t.Length > 0) entries.Add(new CommentaryEntry(o.Book, o.Chapter, o.From, o.To, t));
            }
            body.Clear();
        }
        foreach (var line in lines)
        {
            if (Header(line) is { } h) { Close(); open = h; continue; }
            if (open is null)
            {
                if (name is null && line.StartsWith("# ", StringComparison.Ordinal) && line[2..].Trim().Length > 0) name = line[2..].Trim();
                continue;
            }
            body.Add(line);
        }
        Close();
        return new ParsedCommentary(string.IsNullOrWhiteSpace(name) ? fallbackName : name!, entries);
    }

    /// <summary>The place a line stands for if the line is nothing else than that place; null otherwise.</summary>
    static (string Book, int Chapter, int From, int To)? Header(string line)
    {
        var heading = line.TrimStart().StartsWith('#');
        var t = line.Trim().TrimStart('#', '>', ' ').Trim().Trim('*', '_', ' ').TrimEnd(':', '.').Trim().Trim('*', '_', ' ');
        if (t.Length == 0 || t.Length > 70) return null;
        if (DocumentLinks.VerseSpans(t) is { Count: > 0 })
        {
            var span = DocumentLinks.Spans(t);
            if (span.Count == 1 && span[0].Index == 0 && span[0].Length >= t.Length - 1)
            {
                var s = span[0];
                return (s.Ref.Book, s.Ref.Chapter, s.Ref.Verse, s.EndChapter > s.Ref.Chapter ? 999 : Math.Max(s.Ref.Verse, s.EndVerse));
            }
            return null;
        }
        var m = ChapterLine().Match(t);
        if (m.Success && DocumentLinks.BookId(m.Groups["b"].Value.Trim()) is { } book && int.Parse(m.Groups["c"].Value) is > 0 and var chapter && (heading || m.Groups["rest"].Length == 0))
            return (book, chapter, 0, 0);
        return null;
    }

    [GeneratedRegex(@"^(?<b>[1-3]?\.?\s?\p{L}[\p{L}\. ]*?)\s+(?<c>\d{1,3})\s*(?<rest>[(\[:–—-].*)?$")] private static partial Regex ChapterLine();
}
