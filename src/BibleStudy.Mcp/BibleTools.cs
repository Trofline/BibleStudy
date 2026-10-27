using System.ComponentModel;
using ModelContextProtocol.Server;

/// <summary>
/// Tools exposed to MCP clients. Book arguments take a USFM id (GEN, JHN, 1CO …) or a book name of the translation;
/// call list_books to see them. Every tool returns JSON; failures are {"error": "..."}.
/// </summary>
[McpServerToolType]
public static class BibleTools
{
    const string Tr = "Translation id (e.g. deu_elbbk, eng_kjv) or short name; default: the translation shown in the app.";

    // ---- reading ----------------------------------------------------------------------------------------------------

    [McpServerTool(Name = "get_app_state"), Description("What the app currently shows: translation, book, chapter, selected verse, vault, open parallel translations.")]
    public static Task<string> GetAppState(AppClient app) => app.CallAsync("get_app_state");

    [McpServerTool(Name = "list_translations"), Description("Available Bible translations with language and license. Optionally filter by language name (e.g. German, English, Greek).")]
    public static Task<string> ListTranslations(AppClient app, [Description("Language name or code to filter by")] string? language = null)
        => app.CallAsync("list_translations", new { language });

    [McpServerTool(Name = "list_books"), Description("Books of a translation with id, name and number of chapters.")]
    public static Task<string> ListBooks(AppClient app, [Description(Tr)] string? translation = null) => app.CallAsync("list_books", new { translation });

    [McpServerTool(Name = "get_passage"), Description("Reads verses of one chapter. Omit verse_from/verse_to for the whole chapter. Includes footnotes. Always quote Scripture from this tool, not from memory.")]
    public static Task<string> GetPassage(AppClient app, [Description("Book id or name")] string book, int chapter,
        [Description("First verse (optional)")] int? verse_from = null, [Description("Last verse (optional)")] int? verse_to = null,
        [Description(Tr)] string? translation = null)
        => app.CallAsync("get_passage", new { book, chapter, verse_from, verse_to, translation });

    [McpServerTool(Name = "search_text"), Description("Full-text search in a translation (all words must occur; word prefixes match). The first search in a translation downloads and indexes it, which can take a few seconds.")]
    public static Task<string> SearchText(AppClient app, string query, [Description(Tr)] string? translation = null, [Description("Max results (default 30)")] int? limit = null)
        => app.CallAsync("search_text", new { query, translation, limit });

    [McpServerTool(Name = "get_cross_references"), Description("Cross references of a verse (OpenBible.info), best first, with the verse text.")]
    public static Task<string> GetCrossReferences(AppClient app, string book, int chapter, int verse, [Description("Max results (default 15)")] int? limit = null,
        [Description(Tr)] string? translation = null)
        => app.CallAsync("get_cross_references", new { book, chapter, verse, limit, translation });

    [McpServerTool(Name = "get_strongs"), Description("Strong's dictionary entry (Hebrew/Greek original word, transliteration, meaning, KJV usage). Id like G3056 or H1.")]
    public static Task<string> GetStrongs(AppClient app, [Description("Strong's number, e.g. G3056 or H7225")] string id) => app.CallAsync("get_strongs", new { id });

    [McpServerTool(Name = "get_word_occurrences"), Description("All places where a Strong's number occurs in a translation that has Strong's tags (e.g. eng_kjv). Needs the concordance of that translation (built once in the app).")]
    public static Task<string> GetWordOccurrences(AppClient app, string strongs_id, [Description(Tr)] string? translation = null, int? limit = null)
        => app.CallAsync("get_word_occurrences", new { strongs_id, translation, limit });

    [McpServerTool(Name = "get_interlinear"), Description("The original text of one verse word by word: Hebrew/Aramaic (Old Testament) or Greek (New Testament) with transliteration, English meaning, Strong's number and grammar. Use it to check how a translation renders the original.")]
    public static Task<string> GetInterlinear(AppClient app, string book, int chapter, int verse) => app.CallAsync("get_interlinear", new { book, chapter, verse });

    // ---- knowledge base ---------------------------------------------------------------------------------------------

    [McpServerTool(Name = "list_documents"), Description("Documents of the user's knowledge base (own notes and linked files) in the active vault; optional text query (title, body, tags) or tag.")]
    public static Task<string> ListDocuments(AppClient app, string? query = null, string? tag = null) => app.CallAsync("list_documents", new { query, tag });

    [McpServerTool(Name = "get_document"), Description("A document with its full text, the verses it mentions and the documents it links to ([[Title]]). Give id or title.")]
    public static Task<string> GetDocument(AppClient app, long? id = null, string? title = null) => app.CallAsync("get_document", new { id, title });

    [McpServerTool(Name = "create_document"), Description("Creates a note in the knowledge base. In the text, verse references like \"Joh 3,16\" become links and [[Other title]] links to another document.")]
    public static Task<string> CreateDocument(AppClient app, string title, string body, string[]? tags = null, string? path = null) => app.CallAsync("create_document", new { title, body, tags, path });

    [McpServerTool(Name = "update_document"), Description("Changes title, text or tags of a document (id from list_documents). Only given fields change.")]
    public static Task<string> UpdateDocument(AppClient app, long id, string? title = null, string? body = null, string[]? tags = null) => app.CallAsync("update_document", new { id, title, body, tags });

    // ---- controlling the app ----------------------------------------------------------------------------------------

    [McpServerTool(Name = "navigate"), Description("Shows a chapter (and highlights a verse) in the app.")]
    public static Task<string> Navigate(AppClient app, string book, int chapter, int? verse = null) => app.CallAsync("navigate", new { book, chapter, verse });

    [McpServerTool(Name = "set_translation"), Description("Switches the translation shown in the app.")]
    public static Task<string> SetTranslation(AppClient app, [Description(Tr)] string translation) => app.CallAsync("set_translation", new { translation });

    // ---- personal data (the active vault) ---------------------------------------------------------------------------

    [McpServerTool(Name = "list_marks"), Description("Highlights and saved verses of the active vault, optionally filtered by folder name or tag.")]
    public static Task<string> ListMarks(AppClient app, string? folder = null, string? tag = null) => app.CallAsync("list_marks", new { folder, tag });

    [McpServerTool(Name = "add_mark"), Description("Highlights a verse, or only the words in `text` (must occur in the verse), with a color, optional note, tags and folder. The book name is added as a tag when the user enabled that.")]
    public static Task<string> AddMark(AppClient app, string book, int chapter, int verse,
        [Description("Words inside the verse to highlight; omit for the whole verse")] string? text = null,
        [Description("Color as #RRGGBB; default: the user's default color")] string? color = null,
        string? note = null, [Description("Tags")] string[]? tags = null, [Description("Folder name; created if missing")] string? folder = null)
        => app.CallAsync("add_mark", new { book, chapter, verse, text, color, note, tags, folder });

    [McpServerTool(Name = "update_mark"), Description("Changes color, note, tags or folder of a mark (id from list_marks). Only given fields change.")]
    public static Task<string> UpdateMark(AppClient app, long id, string? color = null, string? note = null, string[]? tags = null, string? folder = null)
        => app.CallAsync("update_mark", new { id, color, note, tags, folder });

    [McpServerTool(Name = "delete_mark"), Description("Deletes a mark (id from list_marks).")]
    public static Task<string> DeleteMark(AppClient app, long id) => app.CallAsync("delete_mark", new { id });

    [McpServerTool(Name = "list_folders"), Description("Folders of the active vault.")]
    public static Task<string> ListFolders(AppClient app) => app.CallAsync("list_folders");

    [McpServerTool(Name = "create_folder"), Description("Creates a folder in the active vault.")]
    public static Task<string> CreateFolder(AppClient app, string name) => app.CallAsync("create_folder", new { name });

    [McpServerTool(Name = "get_notes"), Description("All verse notes of the active vault.")]
    public static Task<string> GetNotes(AppClient app) => app.CallAsync("get_notes");

    [McpServerTool(Name = "set_note"), Description("Sets the note of a verse; empty text deletes it.")]
    public static Task<string> SetNote(AppClient app, string book, int chapter, int verse, string text) => app.CallAsync("set_note", new { book, chapter, verse, text });

    [McpServerTool(Name = "queue_add"), Description("Adds verses to the user's queue (to walk through one after another). References look like \"JHN 3:16\" or \"ROM 5:8\".")]
    public static Task<string> QueueAdd(AppClient app, string[] references) => app.CallAsync("queue_add", new { references });

    [McpServerTool(Name = "queue_list"), Description("The user's queue.")]
    public static Task<string> QueueList(AppClient app) => app.CallAsync("queue_list");

    [McpServerTool(Name = "todo_add"), Description("Adds a to-do, optionally linked to a verse.")]
    public static Task<string> TodoAdd(AppClient app, string text, string? book = null, int? chapter = null, int? verse = null)
        => app.CallAsync("todo_add", new { text, book, chapter, verse });

    [McpServerTool(Name = "todo_list"), Description("The user's to-dos.")]
    public static Task<string> TodoList(AppClient app) => app.CallAsync("todo_list");

    [McpServerTool(Name = "mark_chapter_read"), Description("Marks a chapter as read (or unread).")]
    public static Task<string> MarkChapterRead(AppClient app, string book, int chapter, bool read = true) => app.CallAsync("mark_chapter_read", new { book, chapter, read });
}
