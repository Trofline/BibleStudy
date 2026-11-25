using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BibleStudy.Core;

#if !MOBILE
namespace BibleStudy.App;

/// <summary>
/// Links to Bible passages on the computer. Windows starts the program with the link as its argument: when the program already runs,
/// the link is handed to it over its local control port (the same one the AI tools use, with its secret) and the new process ends.
/// The scheme "biblestudy:" is registered for the current user, without administrator rights.
/// </summary>
static class LinkForwarder
{
    /// <summary>True when a running program took the link.</summary>
    public static bool SendToRunning(string link)
    {
        try
        {
            var info = Path.Combine(AppPaths.Root, "control.json");
            if (!File.Exists(info)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(info));
            var root = doc.RootElement;
            if (!root.TryGetProperty("port", out var port) || !root.TryGetProperty("token", out var token)) return false;
            if (root.TryGetProperty("pid", out var pid))
            {
                try { using var p = Process.GetProcessById(pid.GetInt32()); if (p.HasExited) return false; }
                catch (ArgumentException) { return false; }   // the program that wrote the file is gone
            }
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port.GetInt32()}/call")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { tool = "open_link", args = new { link } }), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Token", token.GetString());
            using var res = http.Send(req);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is IOException or JsonException or HttpRequestException or TaskCanceledException or InvalidOperationException or UnauthorizedAccessException or FormatException)
        {
            return false;   // then this process starts and opens the link itself
        }
    }

    const string Key = @"Software\Classes\biblestudy";

    /// <summary>
    /// Windows: "biblestudy:" opens this program. Set when it is missing or leads to a program file that no longer exists; a working entry of
    /// another copy (an installed program next to a portable one) is left alone, so a portable copy on a USB stick does not take the links over.
    /// </summary>
    public static void RegisterScheme()
    {
        if (!OperatingSystem.IsWindows() || AppPaths.IsStore || Environment.ProcessPath is not { } exe) return;
        try
        {
            using (var existing = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key + @"\shell\open\command"))
            {
                if (existing?.GetValue(null) is string cmd && ExeOf(cmd) is { } target && File.Exists(target)
                    && !(AppPaths.IsInstalled && !string.Equals(Path.GetFullPath(target), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.Combine(Path.GetDirectoryName(target)!, "installed.flag"))))
                    return;   // (an installed program takes over from a portable copy, never the other way round)
            }
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
            k.SetValue(null, "URL:BibleStudy");
            k.SetValue("URL Protocol", "");
            using (var icon = k.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exe}\",0");
            using var command = k.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"\"{exe}\" \"%1\"");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { /* links simply do not open the program */ }
    }

    /// <summary>Removes the entry when it leads into <paramref name="programDir"/> (the uninstaller).</summary>
    public static void UnregisterScheme(string programDir)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using (var existing = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key + @"\shell\open\command"))
            {
                if (existing?.GetValue(null) is not string cmd || ExeOf(cmd) is not { } target
                    || !Path.GetFullPath(target).StartsWith(Path.GetFullPath(programDir), StringComparison.OrdinalIgnoreCase)) return;
            }
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Key, false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or ArgumentException) { /* nothing to undo */ }
    }

    /// <summary>The program file of a command line ("C:\…\BibleStudy.exe" "%1").</summary>
    static string? ExeOf(string command)
    {
        var c = command.Trim();
        if (c.StartsWith('"')) { var end = c.IndexOf('"', 1); return end > 1 ? c[1..end] : null; }
        var space = c.IndexOf(' ');
        return space > 0 ? c[..space] : c;
    }
}
#endif
