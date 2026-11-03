using System.Text.Json;
using BibleStudy.Core;
using BibleStudy.Core.Diagram;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>More tools for the AI window (MCP): the analyses of the app (word statistics, sentence diagram, pericope), opening tabs, and the reading history.</summary>
public partial class MainWindow
{
    async Task<object?> RunMoreToolAsync(string tool, JsonElement a)
    {
        switch (tool)
        {
            case "list_tabs":
                return new { workspaces = NavGroups.Select(g => new { workspace = g.Name, tabs = g.Tabs.Select(t => t.Label) }) };
            case "open_workspace":
            {
                var name = ArgStr(a, "name") ?? "";
                if (FindGroupByName(name) is not { } g) return Err("Unknown workspace. Use list_tabs.");
                OpenWork(g);
                return new { ok = true, workspace = g.Name };
            }
            case "show_tab":
            {
                var name = ArgStr(a, "name") ?? "";
                var found = FindTabByName(name);
                if (found is null) return Err("Unknown tab. Use list_tabs.");
                if (ArgStr(a, "book") is not null)
                {
                    var (_, book, err) = await ResolveAsync(a);
                    if (err is not null || book is null) return err ?? Err("Unknown book.");
                    await GoToAsync(book.Id, ArgInt(a, "chapter") ?? 1, ArgInt(a, "verse"));
                }
                OpenWork(found.Value.Group, found.Value.Tab.Index);
                return new { ok = true, workspace = found.Value.Group.Name, tab = found.Value.Tab.Label };
            }
            case "show_word_chart":
            {
                var id = WordAnalyzer.Normalize(ArgStr(a, "strongs_id") ?? "");
                if (id is null) return Err("strongs_id like G3056 or H4325 is required");
                await ShowWordChartAsync(id, "");
                return new { ok = true };
            }
            case "get_word_stats": return await WordStatsAsync(a);
            case "get_sentence_diagram": return await SentenceDiagramAsync(a);
            case "get_pericope_analysis": return await PericopeAnalysisAsync(a);
            case "get_study_history": return StudyHistory();
            default: return Err($"Unknown tool '{tool}'.");
        }
    }

    NavGroup? FindGroupByName(string name) =>
        NavGroups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(L(g.Name), name, StringComparison.OrdinalIgnoreCase));

    (NavGroup Group, NavTab Tab)? FindTabByName(string name)
    {
        foreach (var g in NavGroups)
            if (g.Tabs.FirstOrDefault(t => string.Equals(t.Label, name, StringComparison.OrdinalIgnoreCase) || string.Equals(L(t.Label), name, StringComparison.OrdinalIgnoreCase)) is { } t)
                return (g, t);
        return null;
    }

    // ---- word statistics (what the "Vorkommen" picture shows) --------------------------------------------------------

    async Task<object> WordStatsAsync(JsonElement a)
    {
        var id = WordAnalyzer.Normalize(ArgStr(a, "strongs_id") ?? "");
        if (id is null) return Err("strongs_id like G3056 or H4325 is required");
        if (_analyzer is null) await LoadAnalyzerAsync(_ => { });
        if (_analyzer is null) return Err("The concordance could not be built (it needs the Strong's data of the KJV, loaded once).");
        var analyzer = _analyzer;
        StrongsEntry? entry = null;
        try { entry = await _strongs.LookupAsync(id); } catch { /* the numbers do not need the dictionary */ }
        var counts = analyzer.BookCounts(id);
        return new
        {
            id, lemma = entry?.Lemma, transliteration = entry?.Translit, meaning = entry?.Definition,
            testament = id[0] == 'H' ? "Old Testament (Hebrew/Aramaic)" : "New Testament (Greek)",
            occurrences = analyzer.Count(id), verses = analyzer.Occurrences(id).Distinct().Count(),
            perBook = _books.Where(b => counts.ContainsKey(b.Id)).OrderBy(b => b.Order).Select(b => new { book = b.Id, name = b.CommonName, count = counts[b.Id] }),
            renderings = analyzer.Renderings(id).Select(r => new { word = r.Word, count = r.Count }),
            note = "Counted by the Strong's numbers of the KJV; other translations can differ.",
        };
    }

    // ---- sentence diagram ----------------------------------------------------------------------------------------------

    static string W(DWord w) => w.Gloss.Length > 0 ? $"{w.Text} ({w.Gloss})" : w.Text;
    static object? PhraseObj(DPhrase? p) => p is null ? null : new { head = p.Head.Select(W), modifiers = p.Mods.Select(ModObj) };
    static object ModObj(DMod m) => new { kind = m.Kind.ToString(), word = m.Word is { } w ? W(w) : null, phrase = PhraseObj(m.Phrase), clause = ClauseObj(m.Clause) };
    static object? ClauseObj(DClause? c) => c is null ? null : new
    {
        conjunction = c.Conj is { } cj ? W(cj) : null, subject = PhraseObj(c.Subject), impliedSubject = c.ImpliedSubject, verb = c.Verb.Select(W),
        @object = PhraseObj(c.Object), complement = PhraseObj(c.Complement), modifiers = c.Mods.Select(ModObj), fragment = c.Fragment ? true : (bool?)null,
    };
    static object? UnitObj(DUnit u) => u.Clause is { } c ? ClauseObj(c) : new { joinedBy = u.Conjs.Select(x => x is null ? null : W(x)), items = u.Items.Select(UnitObj) };

    async Task<object> SentenceDiagramAsync(JsonElement a)
    {
        var (_, book, err) = await ResolveAsync(a);
        if (err is not null || book is null) return err ?? Err("book is required");
        var chapter = ArgInt(a, "chapter") ?? 1;
        if (!SyntaxProvider.IsAvailable(book.Id)) return Err("There is no original-text syntax data for this book.");
        List<DSentence> list;
        try { list = await SentencesOfAsync(book.Id, chapter); }
        catch (Exception ex) when (ex is HttpRequestException or IOException) { return Err("The syntax data could not be loaded: " + ex.Message); }
        var verse = ArgInt(a, "verse");
        var hits = (verse is int v ? list.Where(s => v >= s.FirstVerse && v <= s.LastVerse) : list).Take(12).ToList();
        if (hits.Count == 0) return Err($"No sentence found for {book.Id} {chapter}" + (verse is null ? "." : $":{verse}."));
        return new
        {
            reference = $"{book.Id} {chapter}" + (verse is null ? "" : $":{verse}"),
            sentences = hits.Select(s => new
            {
                index = s.Index, verses = $"{s.FirstVerse}-{s.LastVerse}", language = s.Hebrew ? "Hebrew" : "Greek",
                words = s.Words.Select(w => new { text = w.Text, meaning = w.Gloss, strongs = w.Strong, lemma = w.Lemma, wordClass = w.Class, person = w.Person, number = w.Number, mood = w.Class == "verb" ? w.Mood : null }),
                structure = UnitObj(s.Root),
            }),
            note = "Structure: subject / verb / object / complement on the line, modifiers below. Source: MACULA syntax trees (Clear Bible, CC BY 4.0).",
        };
    }

    // ---- pericope analysis ---------------------------------------------------------------------------------------------

    async Task<object> PericopeAnalysisAsync(JsonElement a)
    {
        var (_, book, err) = await ResolveAsync(a);
        if (err is not null || book is null) return err ?? Err("book is required");
        var chapter = ArgInt(a, "chapter") ?? 1;
        if (!SyntaxProvider.IsAvailable(book.Id)) return Err("There is no original-text grammar data for this book.");
        List<GrammarWord> words; List<DSentence> sentences;
        try { words = await _syntax.LoadChapterWordsAsync(book.Id, chapter); sentences = await SentencesOfAsync(book.Id, chapter); }
        catch (Exception ex) when (ex is HttpRequestException or IOException) { return Err("The grammar data could not be loaded: " + ex.Message); }
        if (words.Count == 0) return Err($"No grammar data for {book.Id} {chapter}.");
        var from = ArgInt(a, "verse_from") ?? 1;
        var to = ArgInt(a, "verse_to") ?? words.Max(w => w.Verse);
        if (to < from) (from, to) = (to, from);
        var hebrew = sentences.Count > 0 ? sentences[0].Hebrew : OriginalIsHebrew(book.Id);
        var r = PericopeStats.Compute(words, from, to, sentences, hebrew);
        return new
        {
            reference = $"{book.Id} {chapter}:{from}-{to}", language = hebrew ? "Hebrew (WLC)" : "Greek (Nestle 1904)",
            words = r.Words, differentWords = r.Lemmas, verses = r.Verses, sentences = r.Sentences, clauses = r.Clauses, subClauses = r.SubClauses, verbs = r.Verbs, imperatives = r.Imperatives,
            wordClasses = r.Classes.Select(c => new { wordClass = c.Class, count = c.Count }),
            verbForms = r.VerbGrid.Select(g => new { row = g.Row, column = g.Col, count = g.Count }),
            keywords = r.Keywords.Take(25).Select(k => new { lemma = k.Lemma, strongs = k.Strong, meaning = k.Gloss, text = k.Text, count = k.Count, verses = k.Verses }),
            perVerse = r.VerseRows.Select(v => new { verse = v.Verse, words = v.Words, nouns = v.Nouns, verbs = v.Verbs, others = v.Others, connectors = v.Connectors }),
            perSentence = r.SentenceRows.Select(s => new { index = s.Index, verses = $"{s.FirstVerse}-{s.LastVerse}", words = s.Words, clauses = s.Clauses, subClauses = s.SubClauses }),
            connectors = r.Connectors.Select(c => new { relation = c.Relation, count = c.Count, words = c.Words }),
            source = "MACULA (Clear Bible, CC BY 4.0)",
        };
    }

    // ---- reading history -----------------------------------------------------------------------------------------------

    object StudyHistory()
    {
        var dates = _vault.GetReadDates();
        var times = dates.Select(d => d.At).ToList();
        return new
        {
            chaptersRead = _vault.GetReadChapters().Count,
            longestStreakDays = ReadingStats.LongestStreak(times),
            chaptersPerDayLast14Days = ReadingStats.PerDay(times, DateTime.Today, 14),
            chaptersPerWeekLast8Weeks = ReadingStats.PerWeek(times, DateTime.Today, 8),
            recentlyRead = dates.OrderByDescending(d => d.At).Take(20).Select(d => new { reference = $"{d.Book} {d.Chapter}", at = d.At.ToString("yyyy-MM-dd") }),
            counts = new { marks = _vault.GetMarks().Count, verseNotes = _vault.GetNotes().Count, documents = _vault.GetDocuments().Count },
            showingNow = new { book = _curBook, chapter = _curChapter, verse = _selected },
        };
    }
}
