using System.Text;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

/// <summary>The own .txt export: whatever the importer recognized comes back exactly when the file is imported again.</summary>
public class BibleTextFileTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_txt_" + Guid.NewGuid().ToString("N"));
    public BibleTextFileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>Everything a translation holds, as one text: two translations are the same when these are equal.</summary>
    static string Canon(ParsedBible b)
    {
        var sb = new StringBuilder();
        sb.Append($"{b.Name}|{b.ShortName}|{b.LanguageCode}\n");
        foreach (var v in b.Verses)
        {
            sb.Append($"{v.Book} {v.Chapter}:{v.Verse}");
            foreach (var s in v.Segments)
                sb.Append($" «{s.Text}»j={s.WordsOfJesus},n={s.NoteId?.ToString() ?? "-"},s={(s.Strongs is null ? "null" : "[" + string.Join(",", s.Strongs) + "]")}");
            sb.Append('\n');
        }
        foreach (var (book, chapter, note) in b.Footnotes) sb.Append($"N {book} {chapter} {note.NoteId} {note.Verse} «{note.Text}»\n");
        foreach (var h in b.Headings ?? []) sb.Append($"H {h.Book} {h.Chapter} {h.Verse} «{h.Text}»\n");
        return sb.ToString();
    }

    /// <summary>A translation with every kind of thing an import can bring, and the signs the format itself uses inside the text.</summary>
    static ParsedBible Rich()
    {
        var verses = new List<ParsedVerse>
        {
            new("GEN", 1, 1, [new Segment("Im Anfang", false, ["H7225"]), new Segment(" "), new Segment("schuf", false, ["H1254", "H8804"]), new Segment(" Gott"), new Segment("", false, null, 0), new Segment(" die Himmel.")]),
            new("GEN", 1, 2, [new Segment("Und die Erde war [wüst] | leer < > \\ Ende"), new Segment(" zweites Stück direkt danach"), new Segment("", NoteId: 1)]),
            new("GEN", 1, 3, [new Segment("Tab\tim\nText\r und Leerzeichen am Ende ")]),
            new("PSA", 3, 0, [new Segment("Ein Psalm Davids.")]),
            new("MAT", 5, 3, [new Segment("Und er sprach: "), new Segment("Glückselig", true, ["G3107"]), new Segment(" die Armen", true), new Segment("", true, null, 0), new Segment(" im Geist.", true)]),
            new("MAT", 5, 4, [new Segment("Leere Liste", false, []), new Segment(""), new Segment("Nummer mit Leerzeichen", false, ["G1 x"])]),
            new("JHN", 3, 16, [new Segment("Denn also hat Gott die Welt geliebt", true)]),
            new("XYZ", 1, 1, [new Segment("Ein Buch, das die Bibel sonst nicht kennt.")]),
        };
        var notes = new List<(string, int, Footnote)>
        {
            ("GEN", 1, new Footnote(0, 1, "Wörtlich: \"Die Himmel\"; vgl. Joh 1,1.")),
            ("GEN", 1, new Footnote(1, 2, "Mit\tTab und\nZeilenumbruch \\ und Rückstrich ")),
            ("MAT", 5, new Footnote(0, 3, "Fußnote in roter Schrift")),
            ("MAT", 6, new Footnote(0, 1, "Fußnote eines Kapitels ohne Verse")),
        };
        var headings = new List<(string, int, int, string)>
        {
            ("GEN", 1, 1, "Die Erschaffung der Welt"),
            ("GEN", 1, 1, "Zweite Überschrift am selben Vers"),
            ("MAT", 5, 3, "Die Seligpreisungen"),
            ("MAT", 5, 99, "Überschrift hinter dem letzten Vers"),
        };
        return new ParsedBible("Meine Bibel | mit [Zeichen]", "MB", "de", verses, notes, headings);
    }

    [Fact]
    public void Written_and_read_again_it_is_the_same_translation()
    {
        var bible = Rich();
        var text = BibleTextFile.Write(bible);
        var back = BibleTextFile.Read(text.Split('\n'), "egal", "en");
        Assert.Equal(Canon(bible), Canon(back));
        Assert.Equal(text, BibleTextFile.Write(back));
    }

    [Fact]
    public void No_line_ends_with_a_space_and_no_text_breaks_a_line()
    {
        var text = BibleTextFile.Write(Rich());
        var lines = text.Split('\n');
        Assert.All(lines, l => Assert.False(l.EndsWith(' ') || l.Contains('\r')));
        Assert.StartsWith(BibleTextFile.Magic, lines[0]);
        Assert.Contains("GEN 1:1\t[Im Anfang|H7225] [schuf|H1254 H8804] Gott<F0> die Himmel.", lines);
        Assert.Contains("MAT 5:3\tUnd er sprach: <J>[Glückselig|G3107] die Armen<F0> im Geist.</J>", lines);
    }

    [Fact]
    public async Task Import_export_import_through_the_store_and_the_importer_is_identical()
    {
        var first = new LocalBibleStore(Path.Combine(_dir, "a.db"));
        var id = first.Save(Rich());
        var stored = first.Load(id)!;

        var file = Path.Combine(_dir, "export.txt");
        File.WriteAllText(file, BibleTextFile.Write(stored), new UTF8Encoding(true));   // also with a byte order mark
        var read = BibleImporters.Read(file, "en");
        Assert.Equal(Canon(stored), Canon(read));

        var second = new LocalBibleStore(Path.Combine(_dir, "b.db"));
        var id2 = second.Save(read, file);
        var again = second.Load(id2)!;
        Assert.Equal(Canon(stored), Canon(again));
        Assert.Equal(BibleTextFile.Write(stored), BibleTextFile.Write(again));

        // what the reader shows is the same chapter, footnotes and headings included
        foreach (var (book, chapter) in new[] { ("GEN", 1), ("MAT", 5), ("PSA", 3) })
        {
            var a = await first.GetChapterAsync(id, book, chapter);
            var b = await second.GetChapterAsync(id2, book, chapter);
            Assert.Equal(string.Join("\n", a.Verses.Select(v => v.Number + " " + v.Text)), string.Join("\n", b.Verses.Select(v => v.Number + " " + v.Text)));
            Assert.Equal(string.Join("\n", (a.Footnotes ?? []).Select(n => $"{n.NoteId} {n.Verse} {n.Text}")), string.Join("\n", (b.Footnotes ?? []).Select(n => $"{n.NoteId} {n.Verse} {n.Text}")));
            Assert.Equal(string.Join("\n", (a.Headings ?? []).Select(h => $"{h.BeforeVerse} {h.Text}")), string.Join("\n", (b.Headings ?? []).Select(h => $"{h.BeforeVerse} {h.Text}")));
        }
    }

    [Fact]
    public void An_imported_usfm_file_survives_export_and_import()
    {
        var usfm = Path.Combine(_dir, "bibel.usfm");
        var sb = new StringBuilder("\\id JHN\n\\c 3\n\\s1 Jesus und Nikodemus\n");
        for (var v = 1; v <= 12; v++) sb.Append($"\\v {v} Vers {v} mit \\wj Worten Jesu\\wj* und Text.\n");
        File.WriteAllText(usfm, sb.ToString());
        var store = new LocalBibleStore(Path.Combine(_dir, "u.db"));
        var id = store.Save(BibleImporters.Read(usfm, "de"));
        var stored = store.Load(id)!;
        var file = Path.Combine(_dir, "u.txt");
        File.WriteAllText(file, BibleTextFile.Write(stored));
        Assert.Equal(Canon(stored), Canon(BibleImporters.Read(file, "de")));
        Assert.Contains(stored.Verses[0].Segments, s => s.WordsOfJesus);
        Assert.NotNull(stored.Headings);
    }

    [Fact]
    public void A_name_the_importer_knows_and_hand_edited_lines_are_read()
    {
        var text = "﻿\n#BIBLESTUDY-TXT 1\n# ein Kommentar\n#NAME\tHandarbeit\n1. Mose 1:1\tIm Anfang\r\n#UNBEKANNT\tkommt aus einer späteren Version\nJoh 3:16\tDenn also\n";
        var b = BibleTextFile.Read(text.Split('\n'), "x", "de");
        Assert.Equal("Handarbeit", b.Name);
        Assert.Equal("de", b.LanguageCode);
        Assert.Equal(new[] { "GEN", "JHN" }, b.Verses.Select(v => v.Book).ToArray());
        Assert.Equal("Im Anfang", b.Verses[0].Segments.Single().Text);
    }

    [Fact]
    public void A_broken_line_names_its_number()
    {
        var e = Assert.Throws<InvalidDataException>(() => BibleTextFile.Read(["#BIBLESTUDY-TXT 1", "GEN 1:1\tgut", "GEN 1:2\t[kaputt"], "x", "de"));
        Assert.Contains("3", e.Message);
    }

    [Fact]
    public void A_plain_text_file_without_the_first_line_is_read_as_before()
    {
        var file = Path.Combine(_dir, "plain.txt");
        File.WriteAllLines(file, Enumerable.Range(1, 12).Select(v => $"Gen 1:{v} Vers {v} [mit Klammern]"));
        var b = BibleImporters.Read(file, "de");
        Assert.Equal(12, b.Verses.Count);
        Assert.Equal("Vers 1 [mit Klammern]", b.Verses[0].Segments.Single().Text);
    }
}
