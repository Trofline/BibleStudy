using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BibleStudy.Core.Plugins;

/// <summary>plugin.json in the plugin's folder.</summary>
public sealed class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Where the plugin gets its texts from and under which rights it may do so – shown before the user allows the plugin.</summary>
    public string ContentNotice { get; set; } = "";
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = [];
    /// <summary>What the plugin offers: "bibles" (translations) and/or "commands". Empty = translations, as in the first plugins.</summary>
    public List<string> Provides { get; set; } = [];
    /// <summary>Commands that appear in the search box (Ctrl+K); running one sends the plugin the request "command".</summary>
    public List<PluginCommand> Commands { get; set; } = [];

    public bool ProvidesBibles => Provides.Count == 0 ? Commands.Count == 0 : Provides.Contains("bibles", StringComparer.OrdinalIgnoreCase);
}

public sealed class PluginCommand
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
}

/// <summary>A plugin folder as found on disk. <paramref name="Hash"/> is the fingerprint of its plugin.json: a changed file must be allowed again.</summary>
public sealed record PluginInfo(PluginManifest Manifest, string Dir, string Hash, string? Problem);

/// <summary>
/// Finds the plugins in the plugin folder and runs the ones the user has allowed. A plugin is a program that runs with the rights of the
/// user, so nothing starts before the user has allowed that plugin (by its fingerprint); the project checks no plugin and no content.
/// </summary>
public sealed partial class PluginManager(string pluginsDir, Func<IReadOnlyDictionary<string, string>> approved, Func<IReadOnlyDictionary<string, string>>? environment = null) : IPluginSource, IDisposable
{
    public IEnumerable<PluginInfo> Allowed() => Discover().Where(IsAllowed);

    public string Dir { get; } = pluginsDir;

    readonly Dictionary<string, PluginClient> _clients = [];
    readonly object _lock = new();

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")] private static partial Regex IdPattern();

    public IReadOnlyList<PluginInfo> Discover()
    {
        var list = new List<PluginInfo>();
        if (!Directory.Exists(Dir)) return list;
        var seen = new HashSet<string>();
        foreach (var dir in Directory.GetDirectories(Dir).Order(StringComparer.OrdinalIgnoreCase))
        {
            var file = Path.Combine(dir, "plugin.json");
            if (!File.Exists(file)) continue;
            string? problem = null;
            PluginManifest m;
            string hash = "";
            try
            {
                var bytes = File.ReadAllBytes(file);
                hash = Convert.ToHexString(SHA256.HashData(bytes));
                m = JsonSerializer.Deserialize<PluginManifest>(bytes, Json) ?? new();
            }
            catch (Exception ex) when (ex is JsonException or IOException) { m = new() { Name = Path.GetFileName(dir) }; problem = L("plugin.json ist nicht lesbar: {0}", ex.Message); }
            if (problem is null)
            {
                if (!IdPattern().IsMatch(m.Id)) problem = L("Die Plugin-Kennung (id) fehlt oder ist ungültig (erlaubt: a–z, 0–9 und Bindestrich).");
                else if (m.Command.Trim().Length == 0) problem = L("Es fehlt der Startbefehl (command).");
                else if (!seen.Add(m.Id)) problem = L("Es gibt schon ein Plugin mit dieser Kennung.");
            }
            if (m.Name.Length == 0) m.Name = m.Id.Length > 0 ? m.Id : Path.GetFileName(dir);
            list.Add(new PluginInfo(m, dir, hash, problem));
        }
        return list;
    }

    public bool IsAllowed(PluginInfo p) => p.Problem is null && approved().TryGetValue(p.Manifest.Id, out var h) && h == p.Hash;

    /// <summary>The running connection to an allowed plugin, started on first use. Null when there is no such allowed plugin.</summary>
    public PluginClient? ClientFor(string pluginId)
    {
        var info = Discover().FirstOrDefault(p => p.Manifest.Id == pluginId && IsAllowed(p));
        if (info is null) { Forget(pluginId); return null; }
        var key = pluginId + "|" + info.Hash;
        lock (_lock)
        {
            if (_clients.TryGetValue(key, out var existing)) return existing;
            Forget(pluginId);   // an older version of the same plugin is stopped
            return _clients[key] = new PluginClient(new ProcessTransport(info.Manifest, info.Dir, environment));
        }
    }

    void Forget(string pluginId)
    {
        lock (_lock)
            foreach (var key in _clients.Keys.Where(k => k.StartsWith(pluginId + "|")).ToList())
            { _clients[key].Dispose(); _clients.Remove(key); }
    }

    /// <summary>The commands of all allowed plugins (for the search box).</summary>
    public IEnumerable<(PluginInfo Plugin, PluginCommand Command)> Commands() =>
        Allowed().SelectMany(p => p.Manifest.Commands.Where(c => c.Id.Length > 0 && c.Title.Length > 0).Select(c => (p, c)));

    /// <summary>Runs a command of a plugin. The plugin may answer with {"message": "…"}; that text is returned for the status bar.</summary>
    public async Task<string?> RunCommandAsync(string pluginId, string commandId, CancellationToken ct = default)
    {
        var client = ClientFor(pluginId) ?? throw new PluginException(L("Das Plugin „{0}“ ist nicht erlaubt oder nicht vorhanden.", pluginId));
        var result = await client.CallAsync("command", new { id = commandId }, ct);
        return result.ValueKind == JsonValueKind.Object && result.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
    }

    /// <summary>Stops a plugin (e.g. when it is no longer allowed).</summary>
    public void Stop(string pluginId) => Forget(pluginId);

    public void Dispose()
    {
        lock (_lock) { foreach (var c in _clients.Values) c.Dispose(); _clients.Clear(); }
    }
}
