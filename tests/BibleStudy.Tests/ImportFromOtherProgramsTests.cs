using System.IO.Compression;
using System.Text;
using BibleStudy.Core.Import;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

/// <summary>Commentaries and Bibles from other programs: MySword, MyBible, theWord, SWORD, OSIS and the exports of Logos.</summary>
public class ImportFromOtherProgramsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_other_" + Guid.NewGuid().ToString("N"));
    public ImportFromOtherProgramsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    string Db(string name, string sql)
    {
        var path = Path.Combine(_dir, name);
        using (var con = new SqliteConnection($"Data Source={path}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        return path;
    }

    string Text(string name, string content) { var p = Path.Combine(_dir, name); File.WriteAllText(p, content, new UTF8Encoding(false)); return p; }

    // ---- modules of Bible programs ----------------------------------------------------------------------------------------

    [Fact]
    public void A_MySword_commentary_keeps_its_verse_ranges()
    {
        var path = Db("Henry.cmt.mybible", "CREATE TABLE Details(Description TEXT); INSERT INTO Details VALUES('Matthew Henry');"
            + "CREATE TABLE commentary(id INTEGER PRIMARY KEY, book INT, chapter INT, fromverse INT, toverse INT, data TEXT);"
            + "INSERT INTO commentary(book,chapter,fromverse,toverse,data) VALUES(43,3,16,18,'<p>Gott liebt die Welt.</p>');");
        var c = OwnCommentaries.Read(path);
        Assert.Equal("Matthew Henry", c.Name);
        var e = Assert.Single(c.Entries);
        Assert.Equal(("JHN", 3, 16, 18, "Gott liebt die Welt."), (e.Book, e.Chapter, e.From, e.To, e.Text));
    }

    [Fact]
    public void A_MyBible_commentary_uses_its_book_numbers_and_its_description()
    {
        var path = Db("Barnes.commentaries.SQLite3", "CREATE TABLE info(name TEXT, value TEXT); INSERT INTO info VALUES('description','Barnes Notes');"
            + "CREATE TABLE commentaries(book_number NUMERIC, chapter_number_from NUMERIC, verse_number_from NUMERIC, chapter_number_to NUMERIC, verse_number_to NUMERIC, marker TEXT, text TEXT);"
            + "INSERT INTO commentaries VALUES(500,3,16,3,17,'','Zu Johannes.');");
        var c = OwnCommentaries.Read(path);
        Assert.Equal("Barnes Notes", c.Name);
        Assert.Equal(("JHN", 3, 16, 17), (c.Entries[0].Book, c.Entries[0].Chapter, c.Entries[0].From, c.Entries[0].To));
    }

    [Fact]
    public void A_theWord_commentary_joins_its_texts_with_their_places()
    {
        var path = Db("Gill.cmt.twm", "CREATE TABLE config(name TEXT, value TEXT); INSERT INTO config VALUES('title','John Gill');"
            + "CREATE TABLE topics(id INTEGER PRIMARY KEY, pid INT, subject TEXT, rel_order INT, content_type TEXT);"
            + "CREATE TABLE content(topic_id INTEGER PRIMARY KEY, data TEXT, data2 BLOB);"
            + "CREATE TABLE bible_refs(id INTEGER PRIMARY KEY, topic_id INT, bi INT, ci INT, vi INT, eci INT, evi INT);"
            + "INSERT INTO topics VALUES(1,0,'Joh 3,16',1,'rtf'); INSERT INTO content VALUES(1,'{\\rtf1 Die Liebe Gottes.\\par}',NULL);"
            + "INSERT INTO bible_refs VALUES(1,1,43,3,16,3,18);");
        var c = OwnCommentaries.Read(path);
        Assert.Equal("John Gill", c.Name);
        var e = Assert.Single(c.Entries);
        Assert.Equal(("JHN", 3, 16, 18, "Die Liebe Gottes."), (e.Book, e.Chapter, e.From, e.To, e.Text));
    }

    [Fact]
    public void A_theWord_module_with_only_packed_texts_is_refused_as_protected()
    {
        var path = Db("Locked.cmt.twm", "CREATE TABLE content(topic_id INTEGER PRIMARY KEY, data BLOB);"
            + "CREATE TABLE bible_refs(id INTEGER PRIMARY KEY, topic_id INT, bi INT, ci INT, vi INT);"
            + "INSERT INTO content VALUES(1, x'00ff10a2b3'); INSERT INTO bible_refs VALUES(1,1,43,3,16);");
        Assert.Contains("kopiergeschützt", Assert.Throws<InvalidDataException>(() => OwnCommentaries.Read(path)).Message);
    }

    [Fact]
    public void A_file_that_is_no_database_is_explained()
    {
        var path = Path.Combine(_dir, "Verschluesselt.cmti");
        File.WriteAllBytes(path, Enumerable.Range(0, 4096).Select(i => (byte)(i * 37 % 251)).ToArray());
        Assert.Contains("verschlüsselt", Assert.Throws<InvalidDataException>(() => OwnCommentaries.Read(path)).Message);
    }

    // ---- SWORD -------------------------------------------------------------------------------------------------------------

    /// <summary>The place of a verse in a SWORD index (KJV): 0 module, 1 testament, then per book its introduction and per chapter its introduction and verses.</summary>
    const int Matt1_1 = 4, Matt1_2 = 5, Matt1_3 = 6;

    /// <summary>A zText/zCom module: all texts in one zlib block.</summary>
    static (byte[] Blocks, byte[] Index, byte[] Data) Zipped(Dictionary<int, string> texts)
    {
        var plain = new MemoryStream();
        var index = new byte[(texts.Keys.Max() + 1) * 10];
        foreach (var (i, t) in texts.OrderBy(p => p.Key))
        {
            var bytes = Encoding.UTF8.GetBytes(t);
            BitConverter.GetBytes(0u).CopyTo(index, i * 10);
            BitConverter.GetBytes((uint)plain.Length).CopyTo(index, i * 10 + 4);
            BitConverter.GetBytes((ushort)bytes.Length).CopyTo(index, i * 10 + 8);
            plain.Write(bytes);
        }
        var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(plain.ToArray());
        var blocks = new byte[12];
        BitConverter.GetBytes(0u).CopyTo(blocks, 0);
        BitConverter.GetBytes((uint)packed.Length).CopyTo(blocks, 4);
        BitConverter.GetBytes((uint)plain.Length).CopyTo(blocks, 8);
        return (blocks, index, packed.ToArray());
    }

    string SwordZip(string name, string conf, string dataPath, string ext, Dictionary<int, string> nt)
    {
        var path = Path.Combine(_dir, name + ".zip");
        var (blocks, index, data) = Zipped(nt);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string entry, byte[] bytes) { using var s = zip.CreateEntry(entry).Open(); s.Write(bytes); }
        Put("mods.d/" + name.ToLowerInvariant() + ".conf", Encoding.UTF8.GetBytes(conf));
        Put(dataPath + "nt." + ext + "s", blocks);
        Put(dataPath + "nt." + ext + "v", index);
        Put(dataPath + "nt." + ext + "z", data);
        return path;
    }

    [Fact]
    public void A_SWORD_commentary_is_read_from_the_zip_and_linked_verses_become_one_entry()
    {
        var conf = "[MHC]\nDataPath=./modules/comments/zcom/mhc/\nModDrv=zCom\nSourceType=ThML\nEncoding=UTF-8\nBlockType=BOOK\nDescription=Matthew Henry Concise\nLang=en\n";
        var path = SwordZip("MHC", conf, "modules/comments/zcom/mhc/", "bz", new()
        {
            [3] = "Zum ersten Kapitel.",
            [Matt1_1] = "<p>Das Geschlechtsregister.</p>",
            [Matt1_2] = "<p>Das Geschlechtsregister.</p>",
            [Matt1_3] = "Juda und <i>Tamar</i>.",
        });
        var c = OwnCommentaries.Read(path);
        Assert.Equal("Matthew Henry Concise", c.Name);
        Assert.Equal(
            [("MAT", 1, 0, 0, "Zum ersten Kapitel."), ("MAT", 1, 1, 2, "Das Geschlechtsregister."), ("MAT", 1, 3, 3, "Juda und Tamar.")],
            c.Entries.Select(e => (e.Book, e.Chapter, e.From, e.To, e.Text)).ToArray());
    }

    [Fact]
    public void A_SWORD_Bible_is_read_with_headings_and_words_of_Jesus_and_a_commentary_zip_is_no_Bible()
    {
        var conf = "[TestBible]\nDataPath=./modules/texts/ztext/testbible/\nModDrv=zText\nSourceType=OSIS\nEncoding=UTF-8\nDescription=Test-Bibel\nLang=de\n";
        // the importer wants at least ten verses: Matthew 1,1–12
        var texts = Enumerable.Range(3, 10).ToDictionary(v => Matt1_1 + v - 1, v => $"Vers {v}.");
        texts[Matt1_1] = "<title type=\"section\">Der Stammbaum</title>Dies ist das Buch<note>Fußnote</note> vom Ursprung.";
        texts[Matt1_2] = "Er sprach: <q who=\"Jesus\">Folgt mir nach.</q>";
        var path = SwordZip("TestBible", conf, "modules/texts/ztext/testbible/", "bz", texts);
        var b = BibleImporters.Read(path, "de");
        Assert.Equal("Test-Bibel", b.Name);
        Assert.Equal(Enumerable.Range(1, 12).ToArray(), b.Verses.Select(v => v.Verse).ToArray());
        Assert.Equal("Vers 12.", string.Concat(b.Verses[11].Segments.Select(s => s.Text)));
        Assert.Equal("Dies ist das Buch vom Ursprung.", string.Concat(b.Verses[0].Segments.Select(s => s.Text)));
        Assert.Contains(b.Verses[1].Segments, s => s.WordsOfJesus && s.Text.Contains("Folgt mir nach."));
        Assert.Contains(b.Headings!, h => h.Book == "MAT" && h.Chapter == 1 && h.Verse == 1 && h.Text == "Der Stammbaum");

        Assert.Throws<InvalidDataException>(() => OwnCommentaries.Read(path));   // a Bible is no commentary
    }

    [Fact]
    public void A_locked_SWORD_module_is_not_read()
    {
        var conf = "[Locked]\nDataPath=./modules/comments/zcom/locked/\nModDrv=zCom\nCipherKey=\nDescription=Gekauft\n";
        var path = SwordZip("Locked", conf, "modules/comments/zcom/locked/", "bz", new() { [Matt1_1] = "x" });
        Assert.Contains("kopiergeschützt", Assert.Throws<InvalidDataException>(() => OwnCommentaries.Read(path)).Message);
    }

    [Fact]
    public void An_installed_SWORD_module_is_read_from_its_conf()
    {
        var conf = "[Raw]\nDataPath=./modules/comments/rawcom/raw/\nModDrv=RawCom\nDescription=Roher Kommentar\n";
        var root = Path.Combine(_dir, "sword");
        Directory.CreateDirectory(Path.Combine(root, "mods.d"));
        var data = Path.Combine(root, "modules", "comments", "rawcom", "raw");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(root, "mods.d", "raw.conf"), conf);
        var text = Encoding.Latin1.GetBytes("Über den Anfang.");
        var vss = new byte[(Matt1_1 + 1) * 6];
        BitConverter.GetBytes(0u).CopyTo(vss, Matt1_1 * 6);
        BitConverter.GetBytes((ushort)text.Length).CopyTo(vss, Matt1_1 * 6 + 4);
        File.WriteAllBytes(Path.Combine(data, "nt.vss"), vss);
        File.WriteAllBytes(Path.Combine(data, "nt"), text);
        var c = OwnCommentaries.Read(Path.Combine(root, "mods.d", "raw.conf"));
        Assert.Equal(("MAT", 1, 1, "Über den Anfang."), (c.Entries[0].Book, c.Entries[0].Chapter, c.Entries[0].From, c.Entries[0].Text));
    }

    [Fact]
    public void The_SWORD_text_format_imp_is_read()
    {
        var c = OwnCommentaries.Read(Text("mhc.imp", "$$$Matthew 1:1\nDas <b>Buch</b>.\n$$$Matthew 1:2\nAbraham.\n"));
        Assert.Equal([("MAT", 1, 1, "Das Buch."), ("MAT", 1, 2, "Abraham.")], c.Entries.Select(e => (e.Book, e.Chapter, e.From, e.Text)).ToArray());
    }

    // ---- OSIS --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_OSIS_commentary_reads_annotated_divs_verses_and_chapter_introductions()
    {
        var osis = "<osis><osisText osisIDWork=\"K\"><header><work><title>Mein OSIS-Kommentar</title></work></header>"
            + "<div type=\"book\" osisID=\"John\"><chapter osisID=\"John.3\"><p>Über das Kapitel.</p>"
            + "<div type=\"section\" annotateType=\"commentary\" annotateRef=\"John.3.16-John.3.18\"><p>Gott liebt.</p><p>Zweiter Absatz.</p></div>"
            + "<verse osisID=\"John.3.19\">Das Licht.</verse></chapter></div></osisText></osis>";
        var c = OwnCommentaries.Read(Text("k.xml", osis));
        Assert.Equal("Mein OSIS-Kommentar", c.Name);
        Assert.Contains(c.Entries, e => e is { Book: "JHN", Chapter: 3, From: 16, To: 18 } && e.Text == "Gott liebt.\n\nZweiter Absatz.");
        Assert.Contains(c.Entries, e => e is { Book: "JHN", Chapter: 3, From: 19, To: 19, Text: "Das Licht." });
        Assert.Contains(c.Entries, e => e is { Book: "JHN", Chapter: 3, From: 0, Text: "Über das Kapitel." });
    }

    // ---- exports of Logos --------------------------------------------------------------------------------------------------

    const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    string Docx(string name, string body)
    {
        var path = Path.Combine(_dir, name);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        using var s = new StreamWriter(z.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
        s.Write($"<w:document xmlns:w=\"{W}\"><w:body>{body}</w:body></w:document>");
        return path;
    }

    static string Heading(string text, int level = 1) => $"<w:p><w:pPr><w:pStyle w:val=\"Heading{level}\"/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";
    static string Para(string text) => $"<w:p><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";
    static string BoldLead(string bold, string rest) => $"<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>{bold}</w:t></w:r><w:r><w:t xml:space=\"preserve\"> {rest}</w:t></w:r></w:p>";
    static string Sup(int n) => $"<w:r><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr><w:t>{n}</w:t></w:r>";

    [Fact]
    public void A_commentary_exported_from_Logos_as_Word_is_read_by_its_headings_and_bold_references()
    {
        var path = Docx("Kommentar.docx", Heading("Word Biblical Commentary") + Heading("John 3", 2) + Para("Nikodemus kommt bei Nacht.")
            + Heading("3:16–18", 3) + Para("Gott liebt die Welt.") + BoldLead("19", "Das Licht ist gekommen.") + BoldLead("Joh 4,1", "Jesus geht nach Galiläa.")
            + BoldLead("Wichtig", "kein Vers."));
        var c = OwnCommentaries.Read(path);
        Assert.Equal("Word Biblical Commentary", c.Name);
        Assert.Equal(
            [("JHN", 3, 0, 0), ("JHN", 3, 16, 18), ("JHN", 3, 19, 19), ("JHN", 4, 1, 1)],
            c.Entries.Select(e => (e.Book, e.Chapter, e.From, e.To)).ToArray());
        Assert.Equal("Das Licht ist gekommen.", c.Entries[2].Text);
        Assert.Contains("Wichtig", c.Entries[3].Text);
    }

    [Fact]
    public void Short_references_after_a_book_heading_start_entries_in_Markdown()
    {
        var c = OwnCommentaries.ReadText("# Römer\n\nEinleitung.\n\n## Kapitel 8\n\nZum Kapitel.\n\n### Vers 28\n\nAlles dient.\n\n### 8:31-39\n\nNichts trennt.\n\n## Song of Solomon 2:1\n\nRose.\n", "x");
        Assert.Equal(
            [("ROM", 1, 0, 0), ("ROM", 8, 0, 0), ("ROM", 8, 28, 28), ("ROM", 8, 31, 39), ("SNG", 2, 1, 1)],
            c.Entries.Select(e => (e.Book, e.Chapter, e.From, e.To)).ToArray());
    }

    [Fact]
    public void A_Bible_exported_from_Logos_with_big_chapter_numbers_is_read()
    {
        // a chapter of a Logos export: the big chapter number, verse 1 without a number, then the verses 2–6 in superscript
        static string Chapter(int n, string first) => $"<w:p><w:r><w:rPr><w:b/><w:sz w:val=\"48\"/></w:rPr><w:t>{n}</w:t></w:r><w:r><w:t xml:space=\"preserve\"> {first} </w:t></w:r>"
            + string.Concat(Enumerable.Range(2, 5).Select(v => $"{Sup(v)}<w:r><w:t xml:space=\"preserve\"> Kapitel {n} Vers {v}. </w:t></w:r>")) + "</w:p>";
        var path = Docx("Bibel.docx", Heading("Genesis") + Chapter(1, "Im Anfang schuf Gott.") + Chapter(2, "So wurden vollendet Himmel und Erde."));
        var b = BibleImporters.Read(path, "de");
        Assert.Equal(Enumerable.Range(1, 2).SelectMany(c => Enumerable.Range(1, 6).Select(v => ("GEN", c, v))).ToArray(), b.Verses.Select(v => (v.Book, v.Chapter, v.Verse)).ToArray());
        Assert.Equal("So wurden vollendet Himmel und Erde.", string.Concat(b.Verses[6].Segments.Select(s => s.Text)));
        Assert.Equal("Kapitel 2 Vers 6.", string.Concat(b.Verses[11].Segments.Select(s => s.Text)));
    }

    [Fact]
    public void Verses_copied_from_Logos_with_its_abbreviations_or_without_the_book_are_read()
    {
        // the importer wants at least ten verses
        var txt = string.Concat(Enumerable.Range(1, 10).Select(v => $"Ge 1:{v} Vers {v}.\n")) + "Ac 2:1 Pfingsten.\nRu 1:1 Rut.\n";
        var b = BibleImporters.Read(Text("kopiert.txt", txt), "de");
        Assert.Equal(Enumerable.Range(1, 10).Select(v => ("GEN", 1, v)).Append(("ACT", 2, 1)).Append(("RUT", 1, 1)).ToArray(), b.Verses.Select(v => (v.Book, v.Chapter, v.Verse)).ToArray());
        var md = BibleImporters.Read(Text("kopiert.md", "# Johannes\n\n" + string.Concat(Enumerable.Range(16, 10).Select(v => $"3:{v} Vers {v}.\n"))), "de");
        Assert.Equal(Enumerable.Range(16, 10).Select(v => ("JHN", 3, v)).ToArray(), md.Verses.Select(v => (v.Book, v.Chapter, v.Verse)).ToArray());
    }

    [Fact]
    public void Logos_resource_files_are_refused_with_the_way_to_export()
    {
        foreach (var ext in new[] { ".lbxlls", ".logos4" })
            Assert.Contains("Word", Assert.Throws<NotSupportedException>(() => BibleImporters.Read("x" + ext, "de")).Message);
        Assert.Contains("Word", Assert.Throws<InvalidDataException>(() => OwnCommentaries.Read("x.lbxlls")).Message);
    }
}
