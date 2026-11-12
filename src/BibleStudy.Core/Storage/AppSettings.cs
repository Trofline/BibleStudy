using System.Text.Json;

namespace BibleStudy.Core.Storage;

/// <summary>Position and size of a detached panel window (Tab = index of the side panel tab).</summary>
public sealed record WindowBounds(int X, int Y, int W, int H, bool Maximized) { public string Key { get; set; } = ""; }

/// <summary>A category for documents (stored on the document as the tag "kat:Name"). Color is an index into the program's palette.</summary>
public sealed class DocCategory
{
    public string Name { get; set; } = "";
    public int Color { get; set; }
}

/// <summary>A saved arrangement: side panel and detached windows.</summary>
/// <summary>A place in the history of the reader, kept over the restart after an update.</summary>
public sealed class ResumePlace
{
    public string Book { get; set; } = "";
    public int Chapter { get; set; }
    public int? Verse { get; set; }
    public double? OffsetX { get; set; }
    public double? OffsetY { get; set; }
}

/// <summary>Where the reader was when the program restarted itself for an update; used once by the next start.</summary>
public sealed class ResumeState
{
    public string Book { get; set; } = "";
    public int Chapter { get; set; }
    public int? Verse { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public bool Home { get; set; }
    public List<ResumePlace> History { get; set; } = [];
    public List<ResumePlace> Forward { get; set; } = [];
}

public sealed class LayoutPreset
{
    public bool SideOpen { get; set; } = true;
    public double SideWidth { get; set; } = 430;
    public int SideTab { get; set; }
    public List<WindowBounds> Windows { get; set; } = [];   // W <= 0: docked as a tab in the main window
    public string? ActiveWork { get; set; }                 // work tab shown in the main window ("group:Dokumente"), null = reading
}

/// <summary>User settings, stored as JSON next to the data (exportable by copying the file).</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Default";      // Default | Light | Dark
    public int FontSize { get; set; } = 22;
    public bool StartOnHome { get; set; } = true;
    public bool AutoMarkRead { get; set; }
    public string? TranslationId { get; set; }
    public string? BookId { get; set; }
    public int Chapter { get; set; } = 1;
    public string Vault { get; set; } = "Standard";
    public bool AutoTagBook { get; set; } = true;
    public string DefaultColor { get; set; } = "#FFD54F";
    /// <summary>Quick marking: while it is on, everything selected in the text is marked at once in <see cref="QuickMarkColor"/>.</summary>
    public bool QuickMark { get; set; }
    public string QuickMarkColor { get; set; } = "";
    /// <summary>A small bar with the colors appears over the selected text (no click on the mark button needed).</summary>
    public bool SelectionPopup { get; set; } = true;
    public bool MarksAllVaults { get; set; }   // the collection and the text show the marks of all vaults, not only of the active one
    /// <summary>The clock at the bottom right of the status bar.</summary>
    public bool ShowClock { get; set; } = true;
    /// <summary>The length of the timer chosen last, in minutes.</summary>
    public int TimerMinutes { get; set; } = 10;
    /// <summary>Split view: the work area shows up to four areas side by side (the Bible text and tabs), each as wide as the user drags it.</summary>
    public bool SplitView { get; set; }
    /// <summary>The areas of the split view from left to right: "reader" for the Bible text, otherwise the key of a tab (like "group:Dokumente").</summary>
    public List<string> SplitPanes { get; set; } = [];
    /// <summary>The widths of those areas as shares (they are scaled to the window).</summary>
    public List<double> SplitWidths { get; set; } = [];
    /// <summary>A maximized window leaves a line free at the edge of an automatically hidden taskbar, so the taskbar comes up when the mouse goes there (Windows).</summary>
    public bool TaskbarReveal { get; set; }
    /// <summary>The folder of the "Materials" tab (the documents of a Bible study someone sent you); "" = none chosen yet.</summary>
    public string MaterialsFolder { get; set; } = "";
    /// <summary>The materials tab also lists the files in the sub-folders.</summary>
    public bool MaterialsSubfolders { get; set; } = true;
    /// <summary>The files open in the materials tab (full paths), in their order.</summary>
    public List<string> MaterialsOpen { get; set; } = [];
    /// <summary>The folders of the materials tree that are closed (paths relative to the materials folder); the others are open.</summary>
    public List<string> MaterialsCollapsed { get; set; } = [];
    /// <summary>The folder list of the materials is hidden, so the file has the whole width.</summary>
    public bool MaterialsTreeHidden { get; set; }
    public bool SelectWholeWords { get; set; } = true;   // a selection always starts and ends at word borders
    public List<string> CustomSubColors { get; set; } = [];   // own colors for sub-marks, chosen with the "+" in the settings
    /// <summary>Which headings show in the text: All, Smart (my own, the translation's only where I have none), Mine, Translation, None.</summary>
    public string HeadingMode { get; set; } = "All";
    /// <summary>The import version for which the question "read the imported Bibles again?" was answered (so it comes once per import version).</summary>
    public int ImportRefreshAsked { get; set; }
    public string SubMarkColor { get; set; } = "";   // color of a sub-mark inside a marked verse; "" = the accent color of the program
    public string SelectionColor { get; set; } = "Accent";   // "Accent" (the accent color of the program), "Gray" or "Blue"
    public string DarkBackground { get; set; } = "Nachtblau";   // one of the dark backgrounds (see MainWindow.Qol)
    /// <summary>Marks made in one translation also show in the others (the same words, or the same Strong numbers).</summary>
    public bool ShareMarksAcrossTranslations { get; set; } = true;
    /// <summary>Names of the Bible books in the program: "Auto" (as the translation has them), "de" (German) or "en" (English).</summary>
    public string BookNames { get; set; } = "Auto";
    public List<string> CustomColors { get; set; } = [];
    /// <summary>What a highlight color means to the user (hex → name, e.g. "#FFD54F" → "Promise").</summary>
    public Dictionary<string, string> ColorNames { get; set; } = [];
    /// <summary>The instruction for the AI that new installations start with: the Bible alone is the measure, the user decides.</summary>
    public const string DefaultAiContext = """
        You interpret biblical texts exclusively from the Bible itself, following the principles of Sola Scriptura and Tota Scriptura. Ignore all extra-biblical theological training data as a basis for interpretation, in particular church teaching authorities, council decisions, papal decrees, catechisms and tradition rulings of any denomination. They are not a valid source of interpretation for you.

        Attitude to the text: take the biblical text seriously as it stands. Understand it grammatically and historically, but never relativised or taken apart by external criticism (no historical-critical method, no source or redaction criticism, no questioning of authorship, historicity or verbal inspiration). Scripture remains the authority over itself, not external science over it.

        Core method: Scripture explains Scripture. Interpret every passage from its immediate context, from parallel passages within the Bible and from the whole witness of Scripture (Tota Scriptura). Clear passages explain unclear ones, never the reverse. No fixed analysis scheme from outside; the text itself determines the way of interpretation. You may mention church-historical or extra-biblical information only as informative background, never as a normative authority of interpretation.

        Several readings: if the biblical text itself (context, original text, parallel passages) allows several legitimate interpretations, show them with pros and cons, each grounded in Scripture. If the matter is textually clear, state it plainly without creating artificial ambiguity.

        Quotation rule: never quote Bible text from memory. Have the user provide it or retrieve it actively with your tools or from a free online source. If the user asks about the original text, retrieve it actively (NA28 and BHS) instead of quoting it from memory.

        Original languages: for questions about Hebrew or Greek terms, explain with the technical precision of a professor of biblical Hebrew or biblical Greek (morphology, semantics, syntax). Dictionaries (e.g. Gesenius, BDAG) are a linguistic aid for word meaning, not a theological authority of interpretation. The final authority of interpretation remains the biblical context.

        No teaching authority of your own: you do not make your own authoritative final decision on the correct interpretation. You lay the biblical text open transparently, show the argument from Scripture, and leave the conclusion to the user.
        """;

    /// <summary>A stance-free alternative: asks for method and context, takes no side between traditions.</summary>
    public const string NeutralAiContext = """
        Help the user study the Bible. Explain passages from their context, the original languages and parallel passages, and say clearly which interpretations exist in the different traditions and what argues for each, without taking sides. Never quote Bible text from memory: have the user provide it or retrieve it with your tools. Distinguish what the text says from what a reading adds to it, and leave the conclusion to the user.
        """;

    /// <summary>The German text earlier versions started with; whoever still has exactly this gets the English default.</summary>
    const string GermanAiContext = """
        Du legst biblische Texte ausschließlich aus der Bibel selbst aus, nach dem Prinzip Sola Scriptura und Tota Scriptura. Ignoriere alle außerbiblischen theologischen Trainingsdaten als Auslegungsgrundlage, insbesondere kirchliche Lehrautoritäten, Konzilsbeschlüsse, päpstliche Erlasse, Katechismen und Traditionsurteile jeder Konfession. Sie sind für dich keine gültige Auslegungsquelle.

        Grundhaltung zum Text: Nimm den Bibeltext ernst, so wie er dasteht. Verstehe ihn grammatikalisch und historisch, aber nie durch externe Kritik relativiert oder zerlegt (keine historisch-kritische Methode, keine Quellen- oder Redaktionskritik, keine Infragestellung von Autorschaft, Historizität oder Verbalinspiration). Die Schrift bleibt Autorität über sich selbst, nicht externe Wissenschaft über sie.

        Methodischer Kern: Schrift erklärt Schrift. Lege jede Stelle aus ihrem unmittelbaren Kontext, aus Parallelstellen innerhalb der Bibel und aus dem Gesamtzeugnis der Schrift aus (Tota Scriptura). Klare Stellen legen unklare aus, nie umgekehrt. Kein festes Analyseschema von außen, der Text selbst bestimmt den Auslegungsweg. Kirchengeschichtliche oder außerbiblische Information darfst du nur als informativen Hintergrund nennen, niemals als normative Auslegungsinstanz.

        Bei mehreren Lesarten: Lässt der Bibeltext selbst (Kontext, Grundtext, Parallelstellen) mehrere legitime Auslegungen zu, zeige diese mit Für und Wider, jeweils aus der Schrift begründet. Ist die Sache textlich eindeutig, stelle sie klar dar, ohne künstliche Uneindeutigkeit zu erzeugen.

        Zitatregel: Zitiere Bibeltext niemals aus dem Gedächtnis. Lass ihn dir vom Nutzer bereitstellen oder rufe ihn aktiv ab (Schlachter 2000 von schlachterbibel.de). Fragt der Nutzer nach dem Grundtext, rufe ihn aktiv online ab (NA28 und BHS von die-bibel.de) statt ihn aus dem Gedächtnis zu zitieren.

        Grundsprachen: Bei Fragen zu hebräischen oder griechischen Begriffen erklärst du mit der fachlichen Genauigkeit eines Professors für biblisches Hebräisch bzw. biblisches Griechisch (Morphologie, Semantik, Syntax). Wörterbücher (z.B. Gesenius, BDAG) sind sprachliches Hilfsmittel zur Wortbedeutung, keine theologische Auslegungsautorität. Letzte Auslegungsinstanz bleibt der biblische Kontext selbst.

        Keine eigene Lehrautorität: Du triffst keine eigene, autoritative Letztentscheidung über die richtige Auslegung. Du legst den Bibeltext transparent offen, zeigst die Argumentation aus der Schrift, und überlässt die Schlussfolgerung dem Nutzer.
        """;

    /// <summary>The text that earlier versions started with; whoever still has exactly this gets the new default.</summary>
    const string OldDefaultAiContext = "Standpunkt: adventistisch; die Bibel allein ist Maßstab (keine kirchliche oder päpstliche Autorität).";

    public string AiContext { get; set; } = DefaultAiContext;
    public string AiModel { get; set; } = "";
    /// <summary>"claude" (Claude Code, with the Bible tools) or "ollama" (a local model, nothing leaves the computer).</summary>
    public string AiEngine { get; set; } = "claude";
    public string OllamaModel { get; set; } = "";
    /// <summary>Start Ollama in the background when it is needed (and end it with the program).</summary>
    public bool OllamaAutoStart { get; set; } = true;
    /// <summary>Translate the word meanings with Ollama instead of Claude Code.</summary>
    public bool OllamaTranslate { get; set; }
    /// <summary>"small" (fast, simple) or "large" (most exact, needs a strong computer) model for translating.</summary>
    public string OllamaTranslateSize { get; set; } = "small";
    /// <summary>Easy mode (only the essentials) or Advanced (everything).</summary>
    public bool EasyMode { get; set; }
    /// <summary>The setup of the first start was finished or skipped; an update or the end of the program in the middle of it does not count.</summary>
    public bool SetupDone { get; set; }
    public bool XrefInText { get; set; }
    /// <summary>Cross references with fewer votes are not shown in the text and in the graph beside it (0 = all). The list in the side panel stays complete.</summary>
    public int XrefMinVotes { get; set; }
    public double SideWidth { get; set; } = 430;
    public bool SideOpen { get; set; } = true;
    public bool ThreadGraph { get; set; }
    public List<string> SearchHistory { get; set; } = [];
    public bool SearchExactWords { get; set; }
    /// <summary>0 word beginning, 1 whole word, 2 word ending, 3 part of a word.</summary>
    public int SearchMatch { get; set; }
    public bool SearchOriginal { get; set; }
    public bool SearchCase { get; set; }
    /// <summary>Index of the "all words must occur …" choice: verse, 2 verses, 5 verses, chapter.</summary>
    public int SearchRange { get; set; }
    /// <summary>Name shown to the other person when studying together.</summary>
    public string CollabName { get; set; } = "";
    /// <summary>Optional own relay server (TURN) for studying together when no direct connection is possible.</summary>
    public string RelayUrl { get; set; } = "";
    public string RelayUser { get; set; } = "";
    /// <summary>The password itself; in the file it stands locked to this Windows user (see <see cref="RelayPasswordStored"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string RelayPassword { get; set; } = "";

    /// <summary>
    /// What settings.json holds: on Windows the password locked with the Windows account (DPAPI), so it is not readable in the file or in a
    /// backup; elsewhere as it is. A file of an older version holds it as plain text and is read as before.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("RelayPassword")]
    public string RelayPasswordStored
    {
        get => RelayPassword.Length == 0 || !OperatingSystem.IsWindows() ? RelayPassword
            : "dpapi:" + Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(RelayPassword), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
        set
        {
            if (value is null || !value.StartsWith("dpapi:", StringComparison.Ordinal)) { RelayPassword = value ?? ""; return; }
            try { RelayPassword = OperatingSystem.IsWindows() ? System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(value[6..]), null, System.Security.Cryptography.DataProtectionScope.CurrentUser)) : ""; }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException) { RelayPassword = ""; }   // another computer or user: it has to be typed again
        }
    }
    public bool CollabSendLocation { get; set; } = true;
    public List<SavedSearch> SavedSearches { get; set; } = [];
    /// <summary>How the hits are shown: 0 verses, 1 aligned, 2 grid, 3 comparison, 4 analysis.</summary>
    public int SearchView { get; set; }
    /// <summary>Translations that are searched alongside the current one in the grid and the comparison.</summary>
    public List<string> SearchCompare { get; set; } = [];
    /// <summary>Version and date of the consent to the terms of studying together ("1|2026-10-28"); empty = not agreed.</summary>
    public string CollabConsent { get; set; } = "";
    /// <summary>Terms of use accepted at the first start ("1|2026-10-28"); empty = not yet.</summary>
    public string TermsAccepted { get; set; } = "";
    public string ReaderFont { get; set; } = "Crimson Text";
    public double LineSpacing { get; set; } = 1.7;
    public int ReaderWidth { get; set; } = 760;
    public string VerseNumbers { get; set; } = "Small";   // Small | Normal | Hidden
    public bool JesusRed { get; set; } = true;
    /// <summary>Own color of the words of Jesus as #RRGGBB; empty = the program default.</summary>
    public string JesusColor { get; set; } = "";
    public List<string> CustomJesusColors { get; set; } = [];   // own colors for the words of Jesus, chosen with the "+" in the settings
    /// <summary>Reading aids: stronger contrast, a plain wide-spaced typeface (easier with dyslexia), Hebrew without vowel signs / accents.</summary>
    public bool HighContrast { get; set; }
    public bool ReadableText { get; set; }
    public bool HebrewVowels { get; set; } = true;
    public bool HebrewAccents { get; set; } = true;
    public bool Justify { get; set; }
    public bool ScrollMarksRead { get; set; }
    public bool ChapterHeading { get; set; } = true;
    public string Accent { get; set; } = "Messing";   // a ready-made name or a colour of the user ("#RRGGBB")
    public List<string> CustomAccents { get; set; } = [];
    public int SpeakRate { get; set; }
    public double AudioSpeed { get; set; } = 1;
    public string SpeakVoice { get; set; } = "windows";   // windows | natural (Piper)
    public double NaturalSpeed { get; set; } = 1;
    public Dictionary<string, string> SpeakVoiceIds { get; set; } = [];   // language (two letters) -> Piper voice
    public int SideTab { get; set; }
    public bool FocusMode { get; set; }
    /// <summary>Parallel columns scroll together verse by verse (true) or each column on its own (false).</summary>
    public bool ParallelSync { get; set; } = true;
    /// <summary>With independent columns: the translations whose columns still scroll together (verse by verse). Empty = none.</summary>
    public List<string> ParallelSyncIds { get; set; } = [];
    /// <summary>Own color per parallel column position ("#RRGGBB"); empty or missing = the standard color.</summary>
    public List<string> ParallelColors { get; set; } = [];

    /// <summary>Which words light up when a word is hovered or pinned: same Strong number and word, same number only, same word only.</summary>
    public bool MarkBoth { get; set; } = true;
    public bool MarkStrong { get; set; } = true;
    public bool MarkWord { get; set; }

    /// <summary>Names the user gave to translations (imported or ready-made), by translation id. Empty fields keep the original name.</summary>
    public Dictionary<string, TranslationAlias> TranslationAliases { get; set; } = [];
    public List<ParallelPreset> ParallelPresets { get; set; } = [];
    /// <summary>Commentaries are human writings (Calvin, Henry …), not Scripture: hidden unless switched on.</summary>
    public bool ShowCommentaries { get; set; }
    /// <summary>The compact look: everything in the side panel, no extra shortcuts on the rail and in the reading bar (applies after a restart).</summary>
    public bool CompactLook { get; set; }
    /// <summary>Translate the English word data (glosses, dictionary entries) into the program language with the local Claude (cached).</summary>
    public bool TranslateMeanings { get; set; } = true;
    public List<DocCategory> Categories { get; set; } = [];
    public bool CategoriesSeeded { get; set; }
    /// <summary>Documents save themselves every few seconds while they are open (the toggle above the editor).</summary>
    public bool AutoSaveDocs { get; set; } = true;
    public string Commentary { get; set; } = "matthew-henry";
    public Dictionary<string, string> KeyBindings { get; set; } = [];
    /// <summary>Plugins the user has allowed: plugin id → fingerprint of its plugin.json at the time (a changed plugin must be allowed again).</summary>
    public Dictionary<string, string> AllowedPlugins { get; set; } = [];
    /// <summary>Order of the work tabs at the top (keys of the tabs), as the user dragged them.</summary>
    public List<string> TabOrder { get; set; } = [];
    public string? PinnedTab { get; set; }                  // the tab the Tab key switches to from the reading tab (null = the last one)
    /// <summary>The one-time note about where the data comes from has been read (version of the note).</summary>
    public string SourcesNoticeSeen { get; set; } = "";
    /// <summary>Show the STEPBible brief lexicon under the Strong entries.</summary>
    public bool ShowStepLexicon { get; set; } = true;
    public Dictionary<string, WindowBounds> Windows { get; set; } = [];
    public Dictionary<string, LayoutPreset> Layouts { get; set; } = [];
    public LayoutPreset? LastLayout { get; set; }
    public string? ActiveLayout { get; set; }               // name of the layout the user loaded or saved last
    public bool Animations { get; set; } = true;
    /// <summary>How big the animations are when they are on: 0 = subtle, 1 = big (default).</summary>
    public int AnimationLevel { get; set; } = 1;
    public string CloseAnimation { get; set; } = "";   // "" (by the strength of the animations) | soft | blur | fade | tube | none
    public string? Language { get; set; }                 // UI language code (en, de, es, fr …); null = default (en)
    public string Logo { get; set; } = "Book";            // Book (rounded square) | Circle
    public bool AutoUpdate { get; set; } = true;
    public string? LastUpdateCheck { get; set; }
    public string BackupSchedule { get; set; } = "off";   // off | daily | weekly | monthly
    public string? BackupFolder { get; set; }              // null = Documents\BibleStudy-Backup
    public string? LastAutoBackup { get; set; }
    public string? SkippedVersion { get; set; }
    /// <summary>A downloaded update that is installed automatically at the next start or exit.</summary>
    public string? PendingUpdateFile { get; set; }
    public string? PendingUpdateVersion { get; set; }
    /// <summary>The user's own reading plan: a stretch of books spread over a number of days (plan id "custom").</summary>
    public CustomPlan? CustomPlan { get; set; }
    /// <summary>The tour through the program is still to be shown: true from the first start until it was seen or skipped (never for installations that existed before the tour).</summary>
    public bool TourPending { get; set; }

    /// <summary>The version whose patch notes were shown last (after an update they are shown once).</summary>
    public string? LastSeenVersion { get; set; }
    /// <summary>Whether the question about a weekly backup was asked already (it is asked once, when backups are off).</summary>
    public bool BackupAsked { get; set; }
    /// <summary>The last "new shortcuts" hint that was shown (so each one appears only once).</summary>
    public string? TipShown { get; set; }
    public string? PlanId { get; set; }
    public string? PlanStart { get; set; }
    public List<string> PlanDoneDays { get; set; } = [];
    public WindowBounds? WindowBounds { get; set; }
    public ResumeState? Resume { get; set; }   // set just before the restart after an update
    public string XrefDirection { get; set; } = "Out";   // Out | In | Both
    public bool XrefClickOpensTab { get; set; }   // a click on a cross reference opens it in context in a tab instead of going there
    public bool Tray { get; set; }
    public bool CloseToTray { get; set; }
    public bool RememberWindow { get; set; } = true;
    public bool RestoreWindows { get; set; } = true;
    public string StartMode { get; set; } = "Normal";   // Normal | Maximized | Minimized

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        var existed = File.Exists(path);
        string? json = null;
        // a virus scanner or a sync program may hold the file for a moment: try again instead of starting with empty settings
        // (they would be saved over the real ones soon after)
        for (var attempt = 0; existed && json is null; attempt++)
        {
            try { json = File.ReadAllText(path); }
            catch (IOException) when (attempt < 25) { Thread.Sleep(200); }
        }
        AppSettings s;
        try { s = json is null ? new() : JsonSerializer.Deserialize<AppSettings>(json) ?? new(); }
        catch (JsonException)
        {
            // an unreadable file is kept next to it, so nothing is lost when the defaults are saved over it
            try { File.Copy(path, path + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true); } catch (IOException) { }
            s = new();
        }
        if (!existed) { s.TourPending = true; s.BackupSchedule = "weekly"; s.BackupAsked = true; }   // new installations back up weekly from the start
        if (s.AiContext == OldDefaultAiContext || s.AiContext == GermanAiContext) s.AiContext = DefaultAiContext;   // never changed by the user: the new default replaces it
        return s;
    }

    /// <summary>Written next to it first and then swapped in: a crash or full disk while writing must not leave half a file.</summary>
    public void Save(string path) => SafeFile.WriteAllText(path, JsonSerializer.Serialize(this, Json));
}

/// <summary>A reading plan the user made: from one book to another (in the order of the books), spread over <see cref="Days"/> days.</summary>
public sealed class CustomPlan
{
    public string Name { get; set; } = "";
    public string FromBook { get; set; } = "";
    public string ToBook { get; set; } = "";
    public int Days { get; set; } = 30;
}

/// <summary>A search kept under a name: the text and the options it was made with.</summary>
public sealed class SavedSearch
{
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";
    public int Scope { get; set; }
    public int Match { get; set; }
    public int Range { get; set; }
    public bool Original { get; set; }
    public bool CaseSensitive { get; set; }
}

/// <summary>One saved arrangement of the parallel columns: which translations (and own passages) next to the main text, in this order.</summary>
public sealed class ParallelPreset
{
    public string Name { get; set; } = "";
    public List<ParallelPresetColumn> Columns { get; set; } = [];
}

public sealed class ParallelPresetColumn
{
    public string TranslationId { get; set; } = "";
    public string? Book { get; set; }
    public int Chapter { get; set; }
}

public sealed class TranslationAlias
{
    public string Name { get; set; } = "";
    public string Short { get; set; } = "";
}
