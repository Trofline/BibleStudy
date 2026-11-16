using System.Reflection;
using System.Text.Json;
using BibleStudy.Core.Storage;
using BibleStudy.Core.Sync;

namespace BibleStudy.App;

/// <summary>
/// The person's own data of the program that is not in a vault, for the device sync (see <see cref="AppData"/>): the verses to learn by heart,
/// the vocabulary, and the settings that are content rather than the look of one device.
/// </summary>
public partial class MainWindow
{
    /// <summary>Settings that belong to the person and go to every device. Window sizes, the font size, the theme and the like stay on each device.</summary>
    static readonly string[] SharedSettings =
    [
        nameof(AppSettings.Categories), nameof(AppSettings.ColorNames), nameof(AppSettings.CustomColors), nameof(AppSettings.CustomSubColors),
        nameof(AppSettings.DefaultColor), nameof(AppSettings.SavedSearches), nameof(AppSettings.ParallelPresets), nameof(AppSettings.TranslationAliases),
        nameof(AppSettings.CustomPlan), nameof(AppSettings.PlanId), nameof(AppSettings.PlanStart), nameof(AppSettings.PlanDoneDays),
        nameof(AppSettings.AiContext), nameof(AppSettings.HandoutGroup), nameof(AppSettings.TimerProgram),
    ];

    static PropertyInfo? SettingProperty(string name) => typeof(AppSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

    Dictionary<string, Dictionary<string, string>> CollectAppData()
    {
        var settings = new Dictionary<string, string>();
        foreach (var name in SharedSettings)
            if (SettingProperty(name) is { } p) settings[name] = JsonSerializer.Serialize(p.GetValue(_settings), p.PropertyType);
        return new()
        {
            ["settings"] = settings,
            ["memory"] = Memory.Export(),
            ["learn"] = Learn.Export(),
        };
    }

    /// <summary>Takes over what another device changed (three ways, as in the vault) and shows it.</summary>
    void MergeAppData(Dictionary<string, Dictionary<string, string>> theirs, Dictionary<string, Dictionary<string, string>>? bas)
    {
        var mine = CollectAppData();
        Dictionary<string, string> Merged(string set) =>
            AppData.Merge(mine[set], theirs.GetValueOrDefault(set) ?? [], bas?.GetValueOrDefault(set));

        var settings = Merged("settings");
        var changedSettings = false;
        foreach (var (name, json) in settings)
        {
            if (mine["settings"].GetValueOrDefault(name) == json || SettingProperty(name) is not { } p) continue;
            try { p.SetValue(_settings, JsonSerializer.Deserialize(json, p.PropertyType)); changedSettings = true; }
            catch (JsonException ex) { ErrorLog.Write(ex); }
        }
        if (changedSettings)
        {
            SaveSettings();
            try { BuildCategoryChips(); BuildColorNamesPanel(); BuildDefaultColorPanel(); BuildPlanUi(); }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException) { ErrorLog.Write(ex); }
        }

        var memory = Merged("memory");
        if (!SameSet(memory, mine["memory"])) { Memory.ReplaceAll(memory); if (!MemoryPanel.IsEffectivelyVisible) BuildMemoryUi(); }
        var learn = Merged("learn");
        if (!SameSet(learn, mine["learn"])) { Learn.ReplaceAll(learn); if (!LearnPanel.IsEffectivelyVisible) BuildLearnUi(); }
    }

    static bool SameSet(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(x => b.TryGetValue(x.Key, out var v) && v == x.Value);
}
