using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using BibleStudy.Core;
using BibleStudy.Core.Import;
using BibleStudy.Core.Live;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

/// <summary>The Bible study features: the room for the phones, the archive, the synopsis, the church fathers, roots and e-Sword notes.</summary>
public class BibleStudyGroupTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));

    public BibleStudyGroupTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    // ---- the room for the phones ------------------------------------------------------------------------------------------

    static LiveRoom Room() => new(() => "<html>page</html>");
    static string Body((int Status, string Type, byte[] Body) r) => Encoding.UTF8.GetString(r.Body);

    [Fact]
    public void Only_who_knows_the_code_gets_in()
    {
        var room = Room();
        Assert.Equal(404, room.Handle("GET", "/", "", "a").Status);
        Assert.Equal(404, room.Handle("GET", "/wrongcode", "", "a").Status);
        Assert.Equal(404, room.Handle("GET", "/" + room.Code + "x", "", "a").Status);
        var page = room.Handle("GET", "/" + room.Code, "", "a");
        Assert.Equal(200, page.Status);
        Assert.Equal("<html>page</html>", Body(page));
        Assert.Equal(1, room.Phones);
        Assert.Equal(404, room.Handle("GET", $"/{room.Code}/handout", "", "a").Status);   // no handout yet
    }

    [Fact]
    public void The_phones_see_what_is_shown_and_the_handout()
    {
        var room = Room();
        room.Show("Joh 3,16 · LUT", "Denn also hat Gott die Welt geliebt");
        room.SetHandout("<html>handout</html>");
        using var state = JsonDocument.Parse(Body(room.Handle("GET", $"/{room.Code}/state?voter=x", "", "a")));
        Assert.Equal("Joh 3,16 · LUT", state.RootElement.GetProperty("title").GetString());
        Assert.True(state.RootElement.GetProperty("handout").GetBoolean());
        Assert.Equal("<html>handout</html>", Body(room.Handle("GET", $"/{room.Code}/handout", "", "a")));
    }

    [Fact]
    public void Questions_come_in_but_not_too_many_from_one_phone()
    {
        var room = Room();
        var changed = 0;
        room.Changed += () => changed++;
        for (var i = 0; i < 5; i++) Assert.Equal(200, room.Handle("POST", $"/{room.Code}/ask", $"{{\"text\":\"Frage {i}\",\"name\":\"Anna\"}}", "phone1").Status);
        Assert.Equal(400, room.Handle("POST", $"/{room.Code}/ask", "{\"text\":\"noch eine\"}", "phone1").Status);   // six in a minute is too many
        Assert.Equal(200, room.Handle("POST", $"/{room.Code}/ask", "{\"text\":\"von einem anderen\"}", "phone2").Status);
        Assert.Equal(400, room.Handle("POST", $"/{room.Code}/ask", "{\"text\":\"   \"}", "phone3").Status);
        Assert.Equal(400, room.Handle("POST", $"/{room.Code}/ask", "kein json", "phone3").Status);
        Assert.Equal(6, room.Questions.Count);
        Assert.Equal(6, changed);
        Assert.Equal("Anna", room.Questions[0].Name);
        var long_ = room.Handle("POST", $"/{room.Code}/ask", JsonSerializer.Serialize(new { text = new string('x', 2000) }), "phone4");
        Assert.Equal(200, long_.Status);
        Assert.Equal(500, room.Questions[^1].Text.Length);
    }

    [Fact]
    public void Every_phone_has_one_vote_and_may_change_it()
    {
        var room = Room();
        var poll = room.StartPoll("Welcher Vers?", ["Vers 1", "Vers 2", "", "Vers 2"]);
        Assert.Equal(["Vers 1", "Vers 2"], poll.Options);   // empty and doubled answers go
        Assert.Equal(200, room.Handle("POST", $"/{room.Code}/vote", $"{{\"poll\":{poll.Id},\"option\":0,\"voter\":\"a\"}}", "x").Status);
        Assert.Equal(200, room.Handle("POST", $"/{room.Code}/vote", $"{{\"poll\":{poll.Id},\"option\":1,\"voter\":\"b\"}}", "x").Status);
        Assert.Equal(200, room.Handle("POST", $"/{room.Code}/vote", $"{{\"poll\":{poll.Id},\"option\":1,\"voter\":\"a\"}}", "x").Status);   // a changes the vote
        Assert.Equal(400, room.Handle("POST", $"/{room.Code}/vote", $"{{\"poll\":{poll.Id},\"option\":7,\"voter\":\"c\"}}", "x").Status);
        Assert.Equal(400, room.Handle("POST", $"/{room.Code}/vote", $"{{\"poll\":{poll.Id + 1},\"option\":0,\"voter\":\"c\"}}", "x").Status);
        Assert.Equal([0, 2], room.Results());

        // the results reach the phones only when the leader shows them
        using (var hidden = JsonDocument.Parse(room.StateJson("a")))
        {
            Assert.False(hidden.RootElement.GetProperty("poll").TryGetProperty("results", out _));
            Assert.Equal(1, hidden.RootElement.GetProperty("poll").GetProperty("voted").GetInt32());
        }
        room.ShowPollResults(true);
        using (var shown = JsonDocument.Parse(room.StateJson("z")))
            Assert.Equal(2, shown.RootElement.GetProperty("poll").GetProperty("results")[1].GetInt32());
        room.EndPoll();
        Assert.Null(room.Poll);
    }

    [Fact]
    public async Task The_room_answers_over_the_network()
    {
        using var room = Room();
        Assert.True(room.Start());
        room.Show("Ps 23,1", "Der Herr ist mein Hirte");
        using var http = new HttpClient();
        var state = await http.GetStringAsync($"http://127.0.0.1:{room.Port}/{room.Code}/state");
        Assert.Contains("Der Herr ist mein Hirte", state);
        var ask = await http.PostAsync($"http://127.0.0.1:{room.Port}/{room.Code}/ask", new StringContent("{\"text\":\"Was heißt Hirte?\"}", Encoding.UTF8, "application/json"));
        Assert.True(ask.IsSuccessStatusCode);
        Assert.Single(room.Questions);
        var wrong = await http.GetAsync($"http://127.0.0.1:{room.Port}/nope");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, wrong.StatusCode);
    }

    [Fact]
    public void The_page_for_the_phones_has_its_texts_and_no_placeholders_left()
    {
        var page = LivePage.Build(new Dictionary<string, string> { ["title"] = "Bibelstunde", ["now"] = "Jetzt <b>", ["waiting"] = "Gleich" }, "de");
        Assert.Contains("<html lang=\"de\">", page);
        Assert.Contains("Jetzt &lt;b&gt;", page);      // escaped in the HTML
        Assert.DoesNotContain("@T:", page);
        Assert.DoesNotContain("@JSON@", page);
        Assert.DoesNotContain("Jetzt <b>", page);      // the JSON in the script escapes < as well
    }

    // ---- the archive --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Passages_are_read_written_and_joined()
    {
        var list = StudyPassage.ParseList("JHN 10:1-18;PSA 23;bad;ROM 8:28");
        Assert.Equal([new StudyPassage("JHN", 10, 1, 18), new StudyPassage("PSA", 23), new StudyPassage("ROM", 8, 28, 28)], list);
        Assert.Equal("JHN 10:1-18;PSA 23;ROM 8:28", StudyPassage.Join(list));
        var joined = StudyPassage.Add([new StudyPassage("JHN", 10, 1, 6)], new StudyPassage("JHN", 10, 7, 18));
        Assert.Equal([new StudyPassage("JHN", 10, 1, 18)], joined);
        Assert.Equal([new StudyPassage("JHN", 10)], StudyPassage.Add(joined, new StudyPassage("JHN", 10)));
    }

    [Fact]
    public void The_archive_keeps_the_studies_and_knows_where_a_chapter_was_read()
    {
        var s = new VaultStore(Path.Combine(_dir, "a.db"), "t");
        var first = s.AddStudy(new DateOnly(2026, 10, 1), "Der gute Hirte", [new StudyPassage("JHN", 10, 1, 18)], "gut", "- Was heißt Hirte?");
        s.AddStudy(new DateOnly(2026, 11, 5), "Psalm 23", [new StudyPassage("PSA", 23), new StudyPassage("JHN", 10, 27, 30)]);
        Assert.Equal(["Psalm 23", "Der gute Hirte"], s.GetStudies().Select(x => x.Title));
        Assert.Equal(2, s.StudiesOf("JHN", 10).Count);
        Assert.Empty(s.StudiesOf("JHN", 11));
        var study = s.GetStudy(first)!;
        s.UpdateStudy(study with { Title = "Hirte", Notes = "sehr gut" });
        Assert.Equal("sehr gut", s.GetStudy(first)!.Notes);
        s.DeleteStudy(first);
        Assert.Single(s.GetStudies());
    }

    [Fact]
    public void Merging_vaults_brings_the_archive_along_once()
    {
        var a = new VaultStore(Path.Combine(_dir, "m1.db"), "a");
        var b = new VaultStore(Path.Combine(_dir, "m2.db"), "b");
        b.AddStudy(new DateOnly(2026, 11, 1), "Römer 8", [new StudyPassage("ROM", 8)]);
        a.MergeFrom(Path.Combine(_dir, "m2.db"), " (b)");
        a.MergeFrom(Path.Combine(_dir, "m2.db"), " (b)");
        Assert.Single(a.GetStudies());
    }

    // ---- the synopsis -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_line_of_the_synopsis_is_complete_and_its_places_can_be_read()
    {
        var all = Synopsis.All;
        Assert.True(all.Count > 100);
        Assert.All(all, s =>
        {
            Assert.Equal(5, s.Titles.Count);
            Assert.All(s.Titles, t => Assert.False(string.IsNullOrWhiteSpace(t)));
            Assert.NotEmpty(s.Places);
            Assert.All(s.Places.Values.SelectMany(r => r), r => Assert.True(r.ToChapter > r.Chapter || (r.ToChapter == r.Chapter && r.To >= r.From), s.Titles[0]));
        });
        var baptism = all.Single(s => s.Titles[0] == "Taufe Jesu");
        Assert.Equal(4, baptism.Places.Count);
        Assert.Equal("The baptism of Jesus", baptism.Title("en"));
        Assert.Equal("The baptism of Jesus", baptism.Title("xx"));   // unknown language: English
    }

    [Fact]
    public void A_verse_finds_its_section_and_ranges_cross_chapters()
    {
        Assert.Equal("Taufe Jesu", Synopsis.At("MRK", 1, 10)[0].Titles[0]);
        var range = GospelRange.Parse("8:31-9:1")!;
        Assert.True(range.Contains(9, 1));
        Assert.True(range.Contains(8, 38));
        Assert.False(range.Contains(9, 2));
        Assert.Equal("Erste Leidensankündigung", Synopsis.At("MRK", 9, 1)[0].Titles[0]);
        Assert.Null(GospelRange.Parse("x"));
    }

    [Fact]
    public void Own_words_are_those_no_other_gospel_has()
    {
        var own = Synopsis.OwnWords(["Jesus wurde getauft im Jordan", "Jesus wurde getauft, der Himmel öffnete sich", "Jesus betete"]);
        Assert.Contains("jordan", own[0]);
        Assert.Contains("himmel", own[1]);
        Assert.DoesNotContain("jesus", own[0]);
        Assert.DoesNotContain("getauft", own[0]);
        Assert.Contains("betete", own[2]);
        Assert.Equal("offnete", Synopsis.Fold("öffnete"));
    }

    // ---- the church fathers -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_fathers_on_a_verse_are_found_by_their_range_and_year()
    {
        var path = Path.Combine(_dir, "cache", "fathers", "commentaries.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var con = new SqliteConnection("Data Source=" + path))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE father_meta(name, default_year, wiki_url, father_category, condemned_by_council);
                CREATE TABLE commentary(id, father_name, file_name, append_to_author_name, ts INTEGER, book, location_start INTEGER, location_end INTEGER, txt, source_url, source_title);
                INSERT INTO father_meta VALUES('Augustine of Hippo', '430', '', 'Western', 0);
                INSERT INTO commentary VALUES('1','Augustine of Hippo','John 3_16.toml','',430,'john',3000016,3000016,'God so loved','https://x/1','Tractate 12');
                INSERT INTO commentary VALUES('2','John Chrysostom','John 3_14-17.toml','',407,'john',3000014,3000017,'Range','','');
                INSERT INTO commentary VALUES('3','Thomas Aquinas','John 3_16.toml','',1274,'john',3000016,3000016,'Later','','');
                INSERT INTO commentary VALUES('4','Origen','John 3_17.toml','',254,'john',3000017,3000017,'Other verse','','');
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var fathers = new ChurchFathers(new HttpClient(), Path.Combine(_dir, "cache"));
        Assert.True(fathers.IsLoaded);
        var early = fathers.For("JHN", 3, 16, 750);
        Assert.Equal(["John Chrysostom", "Augustine of Hippo"], early.Select(q => q.Father));   // the oldest first, the range 3:14-17 counts
        Assert.Equal("3,14–17", early[0].Range);
        Assert.Equal("Western", early[1].Category);
        Assert.Equal(3, fathers.For("JHN", 3, 16).Count);
        Assert.Empty(fathers.For("XYZ", 1, 1));
        Assert.Equal("songofsolomon", ChurchFathers.BookKey("SNG"));
    }

    // ---- Hebrew roots ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_root_family_runs_from_its_root_to_the_next_root()
    {
        const string lexicon = """
            <lexicon xmlns="http://openscriptures.github.com/morphhb/namespace"><part><section id="a.ac">
              <entry id="a.ac.aa" type="root"><w>אָבַד</w> <def>perish</def></entry>
              <entry id="a.ac.ab"><w>אֹבֵד</w> <def>destruction</def></entry>
              <entry id="a.ac.ac"><w>אֲבֵדָה</w> <def>a lost thing</def></entry>
              <entry id="a.ac.ad" type="root"><w>אָבַד</w> <def>II. other root</def></entry>
              <entry id="a.ac.ae"><w>x</w> <def>other</def></entry>
            </section></part></lexicon>
            """;
        const string index = """
            <index><entry><xref bdb="a.ac.aa" strong="6"/></entry><entry><xref bdb="a.ac.ab" strong="8"/></entry><entry><xref bdb="a.ac.ac" strong="9"/></entry>
            <entry><xref bdb="a.ac.ad" strong="7"/></entry><entry><xref bdb="a.ac.ae" strong="5"/></entry></index>
            """;
        var entries = BdbLexicon.ParseEntries(XDocument.Parse(lexicon));
        var strongsOf = BdbLexicon.ParseStrongsByEntry(XDocument.Parse(index));
        var family = BdbLexicon.Family("a.ac.ab", entries, strongsOf)!;
        Assert.Equal("אָבַד", family.Root);
        Assert.Equal(["H6", "H8", "H9"], family.Strongs);
        Assert.Equal("destruction", family.Members[1].Meaning);
        Assert.Equal(["H7", "H5"], BdbLexicon.Family("a.ac.ae", entries, strongsOf)!.Strongs);
    }

    // ---- e-Sword notes ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Notes_and_topics_of_esword_are_read_and_their_rtf_becomes_text()
    {
        var path = Path.Combine(_dir, "My.notx");
        using (var con = new SqliteConnection("Data Source=" + path))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE BookNotes(Book INT, Comments TEXT);
                CREATE TABLE ChapterNotes(Book INT, Chapter INT, Comments TEXT);
                CREATE TABLE VerseNotes(Book INT, ChapterBegin INT, VerseBegin INT, ChapterEnd INT, VerseEnd INT, Comments TEXT);
                CREATE TABLE Topics(Title TEXT, Comments TEXT);
                CREATE TABLE Details(Title TEXT, Abbreviation TEXT, Comments TEXT);
                INSERT INTO Details VALUES('My notes', 'MY', 'The description of the module');
                INSERT INTO BookNotes VALUES(43, 'Das Evangelium der Liebe');
                INSERT INTO ChapterNotes VALUES(43, 3, '<p>Nikodemus</p>');
                INSERT INTO VerseNotes VALUES(43, 3, 16, 3, 17, '{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0 Gott liebt die Welt\par Gr\u246?\''f6e}');
                INSERT INTO Topics VALUES('Gnade', 'Unverdient');
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var read = EswordNotes.Read(path);
        var note = Assert.Single(read.Notes);
        Assert.Equal(new VerseRef("JHN", 3, 16), note.Ref);
        Assert.Equal(17, note.EndVerse);
        Assert.Equal("Gott liebt die Welt\nGrööe", note.Text);   // \u246 and \'f6 are both ö
        Assert.DoesNotContain("Arial", note.Text);
        Assert.Contains(read.Texts, t => t.Title == "My: JHN 3" && t.Text == "Nikodemus");
        Assert.Contains(read.Texts, t => t.Title == "My: JHN" && t.Text == "Das Evangelium der Liebe");
        Assert.Contains(read.Texts, t => t.Title == "Gnade" && t.Text == "Unverdient");
        Assert.DoesNotContain(read.Texts, t => t.Text.Contains("description"));   // the module's own description is no note
    }

    [Fact]
    public void Rtf_keeps_the_text_and_its_special_characters()
    {
        Assert.Equal("Grüße\nzwei", EswordNotes.RtfToText(@"{\rtf1\ansi Gr\'fc\u223?e\par zwei}"));
        Assert.Equal("a{b}", EswordNotes.RtfToText(@"{\rtf1 a\{b\}}"));
        Assert.Equal("x", EswordNotes.RtfToText(@"{\rtf1{\*\generator Riched20;}x}"));
    }
}
