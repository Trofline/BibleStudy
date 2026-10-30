using System.Text.Json;

namespace BibleStudy.Core.Storage;

/// <summary>User settings, stored as JSON next to the data (exportable by copying the file).</summary>
/// <summary>Position and size of a detached panel window (Tab = index of the side panel tab).</summary>
public sealed record WindowBounds(int X, int Y, int W, int H, bool Maximized) { public string Key { get; set; } = ""; }

/// <summary>A category for documents (stored on the document as the tag "kat:Name"). Color is an index into the program's palette.</summary>
public sealed class DocCategory
{
    public string Name { get; set; } = "";
    public int Color { get; set; }
}

/// <summary>A saved arrangement: side panel and detached windows.</summary>
public sealed class LayoutPreset
{
    public bool SideOpen { get; set; } = true;
    public double SideWidth { get; set; } = 430;
    public int SideTab { get; set; }
    public List<WindowBounds> Windows { get; set; } = [];   // W <= 0: docked as a tab in the main window
    public string? ActiveWork { get; set; }                 // work tab shown in the main window ("group:Dokumente"), null = reading
}

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
    public List<string> CustomColors { get; set; } = [];
    /// <summary>The instruction for the AI that new installations start with: the Bible alone is the measure, the user decides.</summary>
    public const string DefaultAiContext = """
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
    public bool CollabSendLocation { get; set; } = true;
    public List<SavedSearch> SavedSearches { get; set; } = [];
    /// <summary>Version and date of the consent to the terms of studying together ("1|2026-10-28"); empty = not agreed.</summary>
    public string CollabConsent { get; set; } = "";
    /// <summary>Terms of use accepted at the first start ("1|2026-10-28"); empty = not yet.</summary>
    public string TermsAccepted { get; set; } = "";
    public string ReaderFont { get; set; } = "Crimson Text";
    public double LineSpacing { get; set; } = 1.7;
    public int ReaderWidth { get; set; } = 760;
    public string VerseNumbers { get; set; } = "Small";   // Small | Normal | Hidden
    public bool JesusRed { get; set; } = true;
    public bool Justify { get; set; }
    public bool ScrollMarksRead { get; set; }
    public bool ChapterHeading { get; set; } = true;
    public string Accent { get; set; } = "Messing";
    public int SpeakRate { get; set; }
    public int SideTab { get; set; }
    public bool FocusMode { get; set; }
    /// <summary>Parallel columns scroll together verse by verse (true) or each column on its own (false).</summary>
    public bool ParallelSync { get; set; } = true;
    /// <summary>With independent columns: the translations whose columns still scroll together (verse by verse). Empty = none.</summary>
    public List<string> ParallelSyncIds { get; set; } = [];
    /// <summary>Commentaries are human writings (Calvin, Henry …), not Scripture: hidden unless switched on.</summary>
    public bool ShowCommentaries { get; set; }
    /// <summary>Translate the English word data (glosses, dictionary entries) into the program language with the local Claude (cached).</summary>
    public bool TranslateMeanings { get; set; } = true;
    public List<DocCategory> Categories { get; set; } = [];
    public bool CategoriesSeeded { get; set; }
    public string Commentary { get; set; } = "matthew-henry";
    public Dictionary<string, string> KeyBindings { get; set; } = [];
    /// <summary>Plugins the user has allowed: plugin id → fingerprint of its plugin.json at the time (a changed plugin must be allowed again).</summary>
    public Dictionary<string, string> AllowedPlugins { get; set; } = [];
    /// <summary>Order of the work tabs at the top (keys of the tabs), as the user dragged them.</summary>
    public List<string> TabOrder { get; set; } = [];
    /// <summary>The one-time note about where the data comes from has been read (version of the note).</summary>
    public string SourcesNoticeSeen { get; set; } = "";
    /// <summary>Show the STEPBible brief lexicon under the Strong entries.</summary>
    public bool ShowStepLexicon { get; set; } = true;
    public Dictionary<string, WindowBounds> Windows { get; set; } = [];
    public Dictionary<string, LayoutPreset> Layouts { get; set; } = [];
    public LayoutPreset? LastLayout { get; set; }
    public string? ActiveLayout { get; set; }               // name of the layout the user loaded or saved last
    public bool Animations { get; set; } = true;
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
    public string? PlanId { get; set; }
    public string? PlanStart { get; set; }
    public List<string> PlanDoneDays { get; set; } = [];
    public WindowBounds? WindowBounds { get; set; }
    public string XrefDirection { get; set; } = "Out";   // Out | In | Both
    public bool Tray { get; set; }
    public bool CloseToTray { get; set; }
    public bool RememberWindow { get; set; } = true;
    public bool RestoreWindows { get; set; } = true;
    public string StartMode { get; set; } = "Normal";   // Normal | Maximized | Minimized

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        try
        {
            var s = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new() : new();
            if (s.AiContext == OldDefaultAiContext) s.AiContext = DefaultAiContext;   // never changed by the user: the new default replaces it
            return s;
        }
        catch (JsonException) { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // written next to it first and then swapped in: a crash or full disk while writing must not leave half a file
        // (an unreadable file would mean all settings start from scratch)
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
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
