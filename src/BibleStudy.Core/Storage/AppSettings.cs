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
    public bool ThreadGraph { get; set; }

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
