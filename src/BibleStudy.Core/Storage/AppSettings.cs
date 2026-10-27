using System.Text.Json;

namespace BibleStudy.Core.Storage;

/// <summary>User settings, stored as JSON next to the data (exportable by copying the file).</summary>
/// <summary>Position and size of a detached panel window (Tab = index of the side panel tab).</summary>
public sealed record WindowBounds(int X, int Y, int W, int H, bool Maximized) { public string Key { get; set; } = ""; }

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
    public string AiContext { get; set; } = "Standpunkt: adventistisch; die Bibel allein ist Maßstab (keine kirchliche oder päpstliche Autorität).";
    public string AiModel { get; set; } = "";
    public bool XrefInText { get; set; }
    public double SideWidth { get; set; } = 430;
    public bool SideOpen { get; set; } = true;
    public bool ThreadGraph { get; set; }
    public List<string> SearchHistory { get; set; } = [];
    public bool SearchExactWords { get; set; }
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
    public string Commentary { get; set; } = "matthew-henry";
    public Dictionary<string, string> KeyBindings { get; set; } = [];
    public Dictionary<string, WindowBounds> Windows { get; set; } = [];
    public Dictionary<string, LayoutPreset> Layouts { get; set; } = [];
    public LayoutPreset? LastLayout { get; set; }
    public string? ActiveLayout { get; set; }               // name of the layout the user loaded or saved last
    public bool Animations { get; set; } = true;
    public string Logo { get; set; } = "Book";            // Book (rounded square) | Circle
    public bool AutoUpdate { get; set; } = true;
    public string? LastUpdateCheck { get; set; }
    public string? SkippedVersion { get; set; }
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
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
