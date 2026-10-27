using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>The tools behind the MCP server: read the Bible, search, and read/change the user's vault and the app state.</summary>
public partial class MainWindow
{
    ControlServer? _control;

    void StartControl()
    {
        _control = new ControlServer(RunToolAsync, Path.Combine(_root, "control.json"));
        if (!_control.Start()) StatusText.Text = "Die KI-Schnittstelle konnte nicht gestartet werden (kein freier lokaler Port).";
        Closing += (_, _) => _control?.Dispose();
    }

    // ---- argument helpers -------------------------------------------------------------------------------------------

    static JsonElement Prop(JsonElement a, string name) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : default;

    static string? ArgStr(JsonElement a, string n) => Prop(a, n) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    static int? ArgInt(JsonElement a, string n) => Prop(a, n) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var i) ? i : null;
    static long? ArgLong(JsonElement a, string n) => Prop(a, n) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var i) ? i : null;
    static bool? ArgBool(JsonElement a, string n) => Prop(a, n) is { ValueKind: JsonValueKind.True or JsonValueKind.False } v ? v.GetBoolean() : null;
    static string[]? ArgStrings(JsonElement a, string n) =>
        Prop(a, n) is { ValueKind: JsonValueKind.Array } v ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : null;

    static object Err(string message) => new { error = message };

    TranslationInfo? FindTranslation(string? idOrShort) =>
        idOrShort is null ? TranslationBox.SelectedItem as TranslationInfo
            : _all.FirstOrDefault(t => string.Equals(t.Id, idOrShort, StringComparison.OrdinalIgnoreCase) || string.Equals(t.ShortName, idOrShort, StringComparison.OrdinalIgnoreCase));

    static BookInfo? FindBook(IReadOnlyList<BookInfo> books, string name) =>
        books.FirstOrDefault(b => string.Equals(b.Id, name, StringComparison.OrdinalIgnoreCase))
        ?? books.FirstOrDefault(b => string.Equals(b.CommonName, name, StringComparison.OrdinalIgnoreCase) || string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? books.FirstOrDefault(b => b.CommonName.StartsWith(name, StringComparison.OrdinalIgnoreCase) || b.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));

    async Task<IReadOnlyList<BookInfo>> BooksOfAsync(TranslationInfo t) =>
        t.Id == (TranslationBox.SelectedItem as TranslationInfo)?.Id ? _books : await _provider.GetBooksAsync(t.Id);

    /// <summary>Resolves translation and book from tool arguments; on failure returns the error object to send back.</summary>
    async Task<(TranslationInfo? T, BookInfo? Book, object? Error)> ResolveAsync(JsonElement a, string bookKey = "book")
    {
        var t = FindTranslation(ArgStr(a, "translation"));
        if (t is null) return (null, null, Err("Unknown translation. Use list_translations."));
        var name = ArgStr(a, bookKey);
        if (name is null) return (t, null, null);
        var book = FindBook(await BooksOfAsync(t), name);
        return book is null ? (t, null, Err($"Unknown book '{name}' in {t.Id}. Use list_books.")) : (t, book, null);
    }

    static string Ref(VerseRef r) => $"{r.Book} {r.Chapter}:{r.Verse}";

    // ---- dispatcher -------------------------------------------------------------------------------------------------

    async Task<object?> RunToolAsync(string tool, JsonElement a)
    {
        switch (tool)
        {
            case "get_app_state":
                return new
                {
                    translation = (TranslationBox.SelectedItem as TranslationInfo)?.Id, language = LanguageBox.SelectedItem,
                    book = _curBook, bookName = BookName(_curBook), chapter = _curChapter, selectedVerse = _selected,
                    parallelTranslations = _extras.Select(e => e.Translation?.Id).OfType<string>(),
                    vault = _vault.Name, showingStartPage = Home.IsVisible,
                };

            case "list_translations": return ListTranslations(ArgStr(a, "language"));
            case "list_books":
            {
                var (t, _, err) = await ResolveAsync(a, "none");
                if (err is not null) return err;
                return new { translation = t!.Id, books = (await BooksOfAsync(t)).Select(b => new { id = b.Id, name = b.CommonName, chapters = b.NumberOfChapters }) };
            }
            case "get_passage": return await GetPassageAsync(a);
            case "search_text": return await SearchTextAsync(a);
            case "get_cross_references": return await CrossReferencesAsync(a);
            case "get_strongs":
            {
                var id = ArgStr(a, "id") ?? "";
                var e = await _strongs.LookupAsync(id);
                return e is null ? Err($"No Strong's entry for '{id}'.") : new { id = e.Id, lemma = e.Lemma, transliteration = e.Translit, pronunciation = e.Pronunciation, derivation = e.Derivation, meaning = e.Definition, kjv = e.KjvUsage, source = StrongsDictionary.Attribution };
            }
            case "get_word_occurrences": return GetOccurrences(a);
            case "import_bible":
                return ArgStr(a, "path") is { } p && File.Exists(p) ? new { result = await ImportBibleFromAsync(p, ArgStr(a, "language") ?? "de") } : Err("path of an existing file is required");
            case "list_documents":
            {
                var docs = _vault.GetDocuments(ArgStr(a, "query"), ArgStr(a, "tag"));
                return new { count = docs.Count, documents = docs.Select(d => new { id = d.Id, title = d.Title, tags = d.Tags, template = d.IsTemplate, file = d.Path, preview = Shorten(d.Body.ReplaceLineEndings(" "), 120) }) };
            }
            case "get_document":
            {
                var d = ArgLong(a, "id") is long id ? _vault.GetDocuments().FirstOrDefault(x => x.Id == id) : ArgStr(a, "title") is { } t ? _vault.FindDocument(t) : null;
                if (d is null) return Err("Document not found. Use list_documents.");
                return new { id = d.Id, title = d.Title, body = d.Body, tags = d.Tags, file = d.Path, template = d.IsTemplate,
                    verses = DocumentLinks.VerseRefs(d.Body).Select(Ref), linksTo = DocumentLinks.WikiLinks(d.Body) };
            }
            case "create_document":
            {
                var title = ArgStr(a, "title")?.Trim();
                if (string.IsNullOrEmpty(title)) return Err("title is required");
                if (_vault.FindDocument(title) is not null) return Err($"A document titled '{title}' exists. Use update_document.");
                var id = _vault.AddDocument(title, ArgStr(a, "body") ?? "", ArgStr(a, "path"), false, ArgStrings(a, "tags") ?? [], FolderId(ArgStr(a, "folder"), create: true));
                RefreshCollection();
                RefreshDocs();
                return new { ok = true, id };
            }
            case "update_document":
            {
                if (ArgLong(a, "id") is not long id || _vault.GetDocuments().FirstOrDefault(x => x.Id == id) is not { } d) return Err("Unknown document id. Use list_documents.");
                _vault.UpdateDocument(id, ArgStr(a, "title") ?? d.Title, ArgStr(a, "body") ?? d.Body, ArgStr(a, "path") ?? d.Path, d.IsTemplate, ArgStrings(a, "tags") ?? d.Tags, ArgStr(a, "folder") is { } fn ? FolderId(fn, create: true) : d.FolderId);
                RefreshCollection();
                RefreshDocs();
                return new { ok = true };
            }
            case "get_interlinear":
            {
                var (_, book, err) = await ResolveAsync(a);
                if (err is not null || book is null) return err ?? Err("book is required");
                var chapter = ArgInt(a, "chapter") ?? 1; var verse = ArgInt(a, "verse") ?? 1;
                var words = (await _hello.GetInterlinearAsync(book.Id, chapter)).GetValueOrDefault(verse);
                if (words is null || words.Count == 0) return Err($"No original-text data for {book.Id} {chapter}:{verse}.");
                return new
                {
                    reference = $"{book.Id} {chapter}:{verse}", language = words[0].IsHebrew ? "Hebrew/Aramaic (right to left)" : "Greek",
                    words = words.Select(w => new { original = w.Text, transliteration = w.Translit, meaning = w.Gloss, strongs = w.Strongs, grammar = w.ParsingFull }),
                    source = HelloAoProvider.InterlinearAttribution,
                };
            }

            case "navigate":
            {
                var (_, book, err) = await ResolveAsync(a);
                if (err is not null || book is null) return err ?? Err("book is required");
                await GoToAsync(book.Id, ArgInt(a, "chapter") ?? 1, ArgInt(a, "verse"));
                return new { ok = true, book = book.Id, chapter = _curChapter, verse = _selected };
            }
            case "set_translation": return SetTranslation(ArgStr(a, "translation"));

            case "list_marks": return ListMarks(a);
            case "add_mark": return await AddMarkAsync(a);
            case "update_mark": return await UpdateMarkAsync(a);
            case "delete_mark":
            {
                if (ArgLong(a, "id") is not long id) return Err("id is required");
                _vault.DeleteMark(id); CloseEditor(); await RefreshAfterMarkChangeAsync();
                return new { ok = true };
            }
            case "list_folders": return new { folders = _vault.GetFolders().Select(f => new { id = f.Id, name = f.Name }) };
            case "create_folder":
            {
                var name = ArgStr(a, "name")?.Trim();
                if (string.IsNullOrEmpty(name)) return Err("name is required");
                var existing = _vault.GetFolders().FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                var id = existing?.Id ?? _vault.AddFolder(name);
                RefreshCollection();
                return new { id, name };
            }
            case "get_notes": return new { notes = _vault.GetNotes().Select(n => new { reference = Ref(n.Ref), text = n.Text }) };
            case "set_note":
            {
                var (_, book, err) = await ResolveAsync(a);
                if (err is not null || book is null) return err ?? Err("book is required");
                _vault.SetNote(new VerseRef(book.Id, ArgInt(a, "chapter") ?? 1, ArgInt(a, "verse") ?? 1), ArgStr(a, "text") ?? "");
                RefreshLists();
                if (book.Id == _curBook) await ShowChapterAsync(_selected);
                return new { ok = true };
            }
            case "queue_add": return await QueueAddAsync(a);
            case "queue_list": return new { queue = _vault.GetQueue().Select(q => new { id = q.Id, reference = Ref(q.Ref), label = q.Label }) };
            case "todo_add":
            {
                var text = ArgStr(a, "text")?.Trim();
                if (string.IsNullOrEmpty(text)) return Err("text is required");
                VerseRef? r = null;
                if (ArgStr(a, "book") is { } bk)
                {
                    var (_, book, err) = await ResolveAsync(a);
                    if (err is not null) return err;
                    r = new VerseRef(book!.Id, ArgInt(a, "chapter") ?? 1, ArgInt(a, "verse") ?? 1);
                }
                _vault.AddTodo(text, r); RefreshTodos();
                return new { ok = true };
            }
            case "todo_list": return new { todos = _vault.GetTodos().Select(t => new { id = t.Id, text = t.Text, done = t.Done, reference = t.Ref is { } r ? Ref(r) : null }) };
            case "mark_chapter_read":
            {
                var (_, book, err) = await ResolveAsync(a);
                if (err is not null || book is null) return err ?? Err("book is required");
                SetRead(book.Id, [ArgInt(a, "chapter") ?? 1], ArgBool(a, "read") ?? true);
                return new { ok = true };
            }
            default: return Err($"Unknown tool '{tool}'.");
        }
    }

    // ---- reading tools ----------------------------------------------------------------------------------------------

    object ListTranslations(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return new
            {
                hint = "Pass a language to list its translations.",
                languages = _all.GroupBy(t => t.LanguageName).Where(g => g.Key != "").OrderBy(g => g.Key).Select(g => new { language = g.Key, code = g.First().Language, translations = g.Count() }),
            };
        var list = _all.Where(t => string.Equals(t.LanguageName, language, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Language, language, StringComparison.OrdinalIgnoreCase)
                                   || t.LanguageName.StartsWith(language, StringComparison.OrdinalIgnoreCase)).Take(120).ToList();
        return new { translations = list.Select(t => new { id = t.Id, shortName = t.ShortName, name = t.Name, license = t.License.PublicDomain ? "public domain" : t.LicenseUrl, hasStrongs = (bool?)null }) };
    }

    async Task<object> GetPassageAsync(JsonElement a)
    {
        var (t, book, err) = await ResolveAsync(a);
        if (err is not null || t is null || book is null) return err ?? Err("book is required");
        var chapter = ArgInt(a, "chapter") ?? 1;
        var ch = await _provider.GetChapterAsync(t.Id, book.Id, chapter);
        var from = ArgInt(a, "verse_from");
        var to = ArgInt(a, "verse_to") ?? from ?? int.MaxValue;
        var verses = ch.Verses.Where(v => v.Number >= (from ?? 1) && v.Number <= to).ToList();
        return new
        {
            translation = t.Id, license = t.License.PublicDomain ? "public domain" : t.LicenseUrl, book = book.Id, name = book.CommonName, chapter,
            verses = verses.Select(v => new { n = v.Number, text = v.Text }),
            footnotes = ch.Footnotes?.Where(f => f.Verse >= (from ?? 1) && f.Verse <= to).Select(f => f.Text),
        };
    }

    async Task<object> SearchTextAsync(JsonElement a)
    {
        var t = FindTranslation(ArgStr(a, "translation"));
        var query = ArgStr(a, "query");
        if (t is null || string.IsNullOrWhiteSpace(query)) return Err("translation and query are required");
        if (!_store.IsIndexed(t.Id))
        {
            var verses = await _provider.GetAllVersesAsync(t.Id);
            await Task.Run(() => _store.Index(t.Id, verses));
        }
        var limit = Math.Clamp(ArgInt(a, "limit") ?? 30, 1, 100);
        var hits = _store.Search(t.Id, query, limit);
        return new { translation = t.Id, count = hits.Count, results = hits.Select(h => new { reference = Ref(h.Ref), text = h.Snippet }) };
    }

    async Task<object> CrossReferencesAsync(JsonElement a)
    {
        var (t, book, err) = await ResolveAsync(a);
        if (err is not null || t is null || book is null) return err ?? Err("book is required");
        var verse = ArgInt(a, "verse") ?? 1;
        var refs = (await _hello.GetCrossReferencesAsync(book.Id, ArgInt(a, "chapter") ?? 1, verse)).Take(Math.Clamp(ArgInt(a, "limit") ?? 15, 1, 50)).ToList();
        var texts = await Task.WhenAll(refs.Select(x => VerseTextAsync(t.Id, x)));
        return new
        {
            source = HelloAoProvider.CrossRefAttribution,
            references = refs.Zip(texts, (x, text) => new { reference = Ref(x.Target) + (x.EndVerse > x.Target.Verse ? "-" + x.EndVerse : ""), score = x.Score, text }),
        };
    }

    object GetOccurrences(JsonElement a)
    {
        var t = FindTranslation(ArgStr(a, "translation"));
        var id = ArgStr(a, "strongs_id") ?? "";
        if (t is null || id.Length < 2 || !int.TryParse(id.AsSpan(1), out var n)) return Err("translation and a Strong's id like G3056 are required");
        if (!_store.IsStrongsIndexed(t.Id)) return Err($"The Strong's concordance of {t.Id} is not built yet. The user can build it in the app (Word study > All occurrences) for a translation with Strong's tags, e.g. eng_kjv.");
        var occ = _store.GetOccurrences(t.Id, $"{char.ToUpperInvariant(id[0])}{n:D4}", 3000);
        var limit = Math.Clamp(ArgInt(a, "limit") ?? 100, 1, 500);
        return new { translation = t.Id, strongs = id, count = occ.Count, results = occ.Take(limit).Select(o => new { reference = Ref(o.Ref), word = o.Word }) };
    }

    // ---- app control ------------------------------------------------------------------------------------------------

    object SetTranslation(string? id)
    {
        var t = FindTranslation(id);
        if (t is null || id is null) return Err("Unknown translation. Use list_translations.");
        _pendingTranslation = t.Id;
        if ((string?)LanguageBox.SelectedItem == t.LanguageName)
        {
            if (TranslationBox.Items.OfType<TranslationInfo>().FirstOrDefault(x => x.Id == t.Id) is { } same) TranslationBox.SelectedItem = same;
            _pendingTranslation = null;
        }
        else LanguageBox.SelectedItem = t.LanguageName;
        return new { ok = true, translation = t.Id };
    }

    // ---- personal data tools ----------------------------------------------------------------------------------------

    string? FolderName(long? id) => id is null ? null : _vault.GetFolders().FirstOrDefault(f => f.Id == id)?.Name;

    long? FolderId(string? name, bool create)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var existing = _vault.GetFolders().FirstOrDefault(f => string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        return existing?.Id ?? (create ? _vault.AddFolder(name) : null);
    }

    object ListMarks(JsonElement a)
    {
        long? folder = null;
        if (ArgStr(a, "folder") is { } fn) { folder = FolderId(fn, false); if (folder is null) return new { marks = Array.Empty<object>() }; }
        var marks = _vault.GetMarks(folderId: folder, tag: ArgStr(a, "tag"));
        return new
        {
            count = marks.Count,
            marks = marks.Select(m => new
            {
                id = m.Id, reference = Ref(m.Ref), wholeVerse = m.WholeVerse, text = m.Text, color = m.Color, note = m.Note, tags = m.Tags, folder = FolderName(m.FolderId),
            }),
        };
    }

    async Task RefreshAfterMarkChangeAsync()
    {
        RefreshCollection();
        LoadChapterMarks();
        await ShowChapterAsync(_selected);
    }

    async Task<object> AddMarkAsync(JsonElement a)
    {
        var (t, book, err) = await ResolveAsync(a);
        if (err is not null || t is null || book is null) return err ?? Err("book is required");
        var chapter = ArgInt(a, "chapter") ?? 1; var verseNo = ArgInt(a, "verse") ?? 1;
        var ch = await _provider.GetChapterAsync(t.Id, book.Id, chapter);
        if (ch.Verses.FirstOrDefault(v => v.Number == verseNo) is not { } verse) return Err($"{book.Id} {chapter}:{verseNo} does not exist in {t.Id}.");
        var color = NormalizeHex(ArgStr(a, "color")) ?? _settings.DefaultColor;
        var r = new VerseRef(book.Id, chapter, verseNo);
        var tags = ArgStrings(a, "tags") ?? AutoTags(r).ToArray();
        var folder = FolderId(ArgStr(a, "folder"), create: true);
        var note = ArgStr(a, "note") ?? "";
        var existing = _vault.GetMarks(book.Id, chapter);
        long id;
        if (ArgStr(a, "text") is { Length: > 0 } words)
        {
            var raw = string.Concat(verse.Segments.Where(s => s.NoteId is null).Select(s => s.Text));
            var start = raw.IndexOf(words, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return Err($"The text was not found in the verse. Verse text: {raw}");
            var end = start + words.Length;
            foreach (var m in existing.Where(m => !m.WholeVerse && m.Ref.Verse == verseNo && m.TranslationId == t.Id && m.Start < end && m.End > start)) _vault.DeleteMark(m.Id);
            id = _vault.AddMark(t.Id, r, start, end, raw[start..end], color, note, folder, tags);
        }
        else
        {
            foreach (var m in existing.Where(m => m.WholeVerse && m.Ref.Verse == verseNo)) _vault.DeleteMark(m.Id);
            id = _vault.AddMark(Mark.AnyTranslation, r, -1, -1, verse.Text, color, note, folder, tags);
        }
        await RefreshAfterMarkChangeAsync();
        return new { ok = true, id, reference = Ref(r), color };
    }

    async Task<object> UpdateMarkAsync(JsonElement a)
    {
        if (ArgLong(a, "id") is not long id || _vault.GetMarks().FirstOrDefault(m => m.Id == id) is not { } m) return Err("Unknown mark id. Use list_marks.");
        var folder = ArgStr(a, "folder") is { } fn ? FolderId(fn, create: true) : m.FolderId;
        _vault.UpdateMark(id, NormalizeHex(ArgStr(a, "color")) ?? m.Color, ArgStr(a, "note") ?? m.Note, folder, ArgStrings(a, "tags") ?? m.Tags);
        await RefreshAfterMarkChangeAsync();
        return new { ok = true };
    }

    async Task<object> QueueAddAsync(JsonElement a)
    {
        var books = (TranslationBox.SelectedItem is TranslationInfo cur) ? await BooksOfAsync(cur) : _books;
        var added = new List<VerseRef>();
        foreach (var s in ArgStrings(a, "references") ?? [])
        {
            var m = Regex.Match(s, @"^\s*(?<b>[1-3]?\s?[\p{L}]+)\.?\s+(?<c>\d+)\s*[:.]\s*(?<v>\d+)");
            if (!m.Success) continue;
            var name = m.Groups["b"].Value;
            var book = FindBook(books, name.Replace(" ", "")) ?? FindBook(books, name);
            if (book is not null) added.Add(new VerseRef(book.Id, int.Parse(m.Groups["c"].Value), int.Parse(m.Groups["v"].Value)));
        }
        if (added.Count == 0) return Err("No valid references. Use the form \"JHN 3:16\".");
        AddToQueue(added);
        return new { ok = true, added = added.Count };
    }
}
