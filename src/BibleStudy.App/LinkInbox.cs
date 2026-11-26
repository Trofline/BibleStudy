#if !MOBILE
using System.IO.Pipes;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// Links "biblestudy://Joh3,16" on a computer. Windows starts the program with the link as its argument: when the program already runs, the new start
/// hands the link over to it (a local pipe of this user) and ends at once, so the passage opens in the open window instead of a second program.
/// </summary>
static class LinkInbox
{
    static string PipeName => "BibleStudy-links-" + Environment.UserName;

    /// <summary>The link among the arguments of this start, if any.</summary>
    public static string? FromArgs(IEnumerable<string> args) => args.FirstOrDefault(VerseLink.IsVerseLink);

    /// <summary>Gives the link to the program that already runs; true when it took it (this start then ends).</summary>
    public static bool SendToRunning(string link)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(500);
            using var w = new StreamWriter(pipe);
            w.WriteLine(link.ReplaceLineEndings(" "));
            w.Flush();
            return true;
        }
        catch (Exception) { return false; }   // none runs (or it does not answer): this start opens the link itself
    }

    static bool _listening;

    /// <summary>Takes the links of later starts, on a thread of its own; <paramref name="received"/> is called with each.</summary>
    public static void Listen(Action<string> received)
    {
        if (_listening) return;
        _listening = true;
        new Thread(() =>
        {
            while (true)
            {
                NamedPipeServerStream server;
                try { server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly); }
                catch (Exception) { return; }   // another copy of the program already takes the links
                try
                {
                    using (server)
                    {
                        server.WaitForConnection();
                        using var r = new StreamReader(server);
                        var line = r.ReadLine()?.Trim();
                        if (line is { Length: > 0 and < 2000 } && VerseLink.IsVerseLink(line)) received(line);
                    }
                }
                catch (Exception) { Thread.Sleep(200); }   // a start that broke off: wait for the next
            }
        }) { IsBackground = true, Name = "BibleStudy links" }.Start();
    }

    const string ClassesKey = @"Software\Classes\" + VerseLink.Scheme;

    /// <summary>
    /// Windows: the scheme "biblestudy:" is entered for this user (no administrator needed) and points to this program file. Done at every start, so it
    /// follows the program when it is moved or updated (installed, portable, Scoop, winget). Not when started through "dotnet run".
    /// </summary>
    public static void RegisterScheme()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } exe || exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var command = $"\"{exe}\" \"%1\"";
            using (var open = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ClassesKey + @"\shell\open\command"))
                if (open?.GetValue(null) as string == command) return;   // already so
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ClassesKey);
            k.SetValue(null, "URL:BibleStudy");
            k.SetValue("URL Protocol", "");
            using (var icon = k.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exe}\",0");
            using (var cmd = k.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, command);
        }
        catch (Exception ex) { ErrorLog.Write(ex); }   // links just do not open the program
    }

    /// <summary>Uninstalling: the scheme is taken out again.</summary>
    public static void UnregisterScheme()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(ClassesKey, false); } catch (Exception) { /* harmless leftover */ }
    }
}
#endif
