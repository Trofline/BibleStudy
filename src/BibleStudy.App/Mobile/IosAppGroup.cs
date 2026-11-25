#if IOS
using Foundation;

namespace BibleStudy.App;

/// <summary>
/// The folder that the app shares with its widget extension (an app group). The group is called group.de.trofline.biblestudy in the entitlements,
/// but SideStore registers it under the Apple ID that signs the app and may give it another name. The installed app carries its provisioning profile
/// (embedded.mobileprovision) with the groups that really belong to it: the name is read from there, the widget does the same on its side.
/// </summary>
static class IosAppGroup
{
    public const string Wanted = "group.de.trofline.biblestudy";

    static string? _container;
    static bool _looked;

    /// <summary>The path of the shared folder, or null when the app has no group (installed without the widget, or signed without groups).</summary>
    public static string? Container()
    {
        if (_looked) return _container;
        _looked = true;
        foreach (var id in Candidates())
        {
            try
            {
                if (NSFileManager.DefaultManager.GetContainerUrl(id)?.Path is { Length: > 0 } path) { Directory.CreateDirectory(path); return _container = path; }
            }
            catch (Exception) { /* the next name */ }
        }
        return null;
    }

    static IEnumerable<string> Candidates()
    {
        var found = new List<string>();
        try
        {
            var file = Path.Combine(NSBundle.MainBundle.BundlePath, "embedded.mobileprovision");
            if (File.Exists(file))
            {
                // the profile is a signed envelope around a plain property list: the list is cut out and read
                var raw = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(file));
                int from = raw.IndexOf("<?xml", StringComparison.Ordinal), to = raw.IndexOf("</plist>", StringComparison.Ordinal);
                if (from >= 0 && to > from)
                {
                    var xml = raw[from..(to + "</plist>".Length)];
                    using var data = NSData.FromString(xml, NSStringEncoding.UTF8);
                    var format = NSPropertyListFormat.Xml;
                    if (NSPropertyListSerialization.PropertyListWithData(data, NSPropertyListReadOptions.Immutable, ref format, out _) is NSDictionary profile
                        && profile["Entitlements"] is NSDictionary ent && ent["com.apple.security.application-groups"] is NSArray groups)
                        for (nuint i = 0; i < groups.Count; i++)
                            if (groups.GetItem<NSString>(i)?.ToString() is { } g && g.Contains("de.trofline.biblestudy", StringComparison.Ordinal)) found.Add(g);
                }
            }
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
        found.Add(Wanted);
        return found.Distinct();
    }
}
#endif
