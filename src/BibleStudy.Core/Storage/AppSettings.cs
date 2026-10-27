using System.Text.Json;

namespace BibleStudy.Core.Storage;

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
