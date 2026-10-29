using System.Diagnostics;
using System.Runtime.InteropServices;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// Bug reports, ideas and feedback: they open a prefilled ticket on the project's GitHub page (nothing is sent before the user submits it there).
/// Also the Start menu entry, so that the Windows search finds a portable or development copy.
/// </summary>
public partial class MainWindow
{
    const string IssuesUrl = "https://github.com/Trofline/BibleStudy/issues";

    void InitFeedback()
    {
        ErrorLog.Show = msg => { StatusText.Text = msg; StatusBar.Opacity = 1; };
        BugBtn.Click += (_, _) => OpenTicket("bug", L("Fehler: "), L("**Was ist passiert?**\n\n\n**Was hast du erwartet?**\n\n\n**Wie kann ich es nachstellen?**\n1. \n2. \n"));
        IdeaBtn.Click += (_, _) => OpenTicket("enhancement", L("Idee: "), L("**Meine Idee**\n\n\n**Wozu wäre sie gut?**\n"));
        FeedbackBtn.Click += (_, _) => OpenTicket("feedback", L("Feedback: "), L("**Das gefällt mir**\n\n\n**Das könnte besser sein**\n"));
        IdeasListBtn.Click += (_, _) => OpenUrl(IssuesUrl);
        // the installer makes the entry itself; a portable or development copy must not bend it to another program file
        StartMenuBtn.IsVisible = OperatingSystem.IsWindows() && !AppPaths.IsInstalled;
        StartMenuBtn.Click += (_, _) => StartMenuInfo.Text = AddStartMenuEntry();
        if (OperatingSystem.IsWindows())
            StartMenuInfo.Text = AppPaths.IsInstalled ? L("Der Installer hat BibleStudy im Startmenü eingetragen.")
                : StartMenuLinkTarget() is not { } t ? ""
                : SamePath(t, Environment.ProcessPath) ? L("Der Eintrag im Startmenü ist vorhanden.")
                : L("Im Startmenü steht schon ein BibleStudy-Eintrag für ein anderes Programm ({0}). Eintragen ersetzt ihn.", t);
    }

    static bool SamePath(string a, string? b) => b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The program the Start menu entry points to, or null when there is none.</summary>
    static string? StartMenuLinkTarget()
    {
        if (StartMenuLink() is not { } path || !File.Exists(path)) return null;
        try
        {
            dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            string target = sh.CreateShortcut(path).TargetPath;
            return target.Length > 0 ? target : null;
        }
        catch { return null; }
    }

    /// <summary>A portable copy that is removed takes its own Start menu entry with it (an entry of another copy stays).</summary>
    static void RemoveOwnStartMenuEntry(string programDir)
    {
        try
        {
            if (StartMenuLinkTarget() is { } t && Path.GetFullPath(t).StartsWith(Path.GetFullPath(programDir), StringComparison.OrdinalIgnoreCase))
                File.Delete(StartMenuLink()!);
        }
        catch { /* a leftover link is harmless */ }
    }

    void OpenTicket(string label, string title, string template)
    {
        var os = RuntimeInformation.OSDescription;
        var body = template + $"\n\n---\nBibleStudy {AppPaths.Version} · {os}";
        // the last recorded error helps to find a bug; it is only sent if the user submits the form (and can delete it there)
        if (label == "bug" && ErrorLog.Last() is { } err) body += "\n\n" + L("Letzter Fehler im Protokoll:") + "\n```\n" + err + "\n```";
        var url = $"{IssuesUrl}/new?labels={Uri.EscapeDataString(label)}&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
        OpenUrl(url);
        StatusText.Text = L("Die Seite auf GitHub wurde im Browser geöffnet. Abgeschickt ist noch nichts.");
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* no browser */ }
    }

    static string? StartMenuLink() => OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BibleStudy.lnk") : null;

    string AddStartMenuEntry()
    {
        var exe = Environment.ProcessPath;
        if (!OperatingSystem.IsWindows() || exe is null || StartMenuLink() is not { } path) return "";
        try
        {
            var shell = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell");
            dynamic sh = Activator.CreateInstance(shell)!;
            dynamic link = sh.CreateShortcut(path);
            link.TargetPath = exe; link.WorkingDirectory = Path.GetDirectoryName(exe); link.Description = "BibleStudy"; link.IconLocation = exe + ",0";
            link.Save();
            return L("Eingetragen. Windows findet BibleStudy jetzt über die Suche (nach einigen Sekunden).");
        }
        catch (Exception ex) { return L("Das ging nicht: {0}", ex.Message); }
    }
}
