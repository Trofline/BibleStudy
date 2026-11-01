using System.Xml.Linq;

namespace BibleStudy.Core;

/// <summary>A folder that a sync program (Syncthing) already keeps in step between the user's devices.</summary>
public sealed record SyncFolder(string Label, string Path);

/// <summary>
/// Finds the folders of a locally installed Syncthing by reading its configuration file (nothing is started, nothing is sent).
/// A backup placed in such a folder is copied to the user's other devices by Syncthing. Only the backup folder is meant for this:
/// the live data folder should never be synced while the program runs (half-written files would be copied).
/// </summary>
public static class SyncFolders
{
    public static IEnumerable<string> ConfigCandidates()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(local, "Syncthing", "config.xml");                                   // Windows
        yield return Path.Combine(home, "Library", "Application Support", "Syncthing", "config.xml");   // macOS
        var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        yield return Path.Combine(string.IsNullOrEmpty(state) ? Path.Combine(home, ".local", "state") : state, "syncthing", "config.xml");   // Linux, newer
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        yield return Path.Combine(string.IsNullOrEmpty(config) ? Path.Combine(home, ".config") : config, "syncthing", "config.xml");          // Linux, older
    }

    public static IReadOnlyList<SyncFolder> Find(IEnumerable<string>? candidates = null)
    {
        foreach (var file in candidates ?? ConfigCandidates())
        {
            try
            {
                if (!File.Exists(file)) continue;
                return Parse(XDocument.Load(file));
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException) { /* an unreadable file: the next candidate */ }
        }
        return [];
    }

    public static IReadOnlyList<SyncFolder> Parse(XDocument config)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = new List<SyncFolder>();
        foreach (var f in config.Root?.Elements("folder") ?? [])
        {
            var path = (string?)f.Attribute("path");
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (path.StartsWith('~')) path = Path.Combine(home, path.TrimStart('~', '/', '\\'));
            var label = (string?)f.Attribute("label");
            result.Add(new SyncFolder(string.IsNullOrWhiteSpace(label) ? Path.GetFileName(path.TrimEnd('/', '\\')) : label, path));
        }
        return result;
    }
}
