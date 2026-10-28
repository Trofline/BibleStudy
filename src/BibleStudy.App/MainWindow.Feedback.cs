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
        BugBtn.Click += (_, _) => OpenTicket("bug", L("Fehler: "), L("**Was ist passiert?**\n\n\n**Was hast du erwartet?**\n\n\n**Wie kann ich es nachstellen?**\n1. \n2. \n"));
        IdeaBtn.Click += (_, _) => OpenTicket("enhancement", L("Idee: "), L("**Meine Idee**\n\n\n**Wozu wäre sie gut?**\n"));
        FeedbackBtn.Click += (_, _) => OpenTicket("feedback", L("Feedback: "), L("**Das gefällt mir**\n\n\n**Das könnte besser sein**\n"));
        IdeasListBtn.Click += (_, _) => OpenUrl(IssuesUrl);
        StartMenuBtn.IsVisible = OperatingSystem.IsWindows();
        StartMenuBtn.Click += (_, _) => StartMenuInfo.Text = AddStartMenuEntry();
        if (OperatingSystem.IsWindows()) StartMenuInfo.Text = StartMenuLink() is { } p && File.Exists(p) ? L("Der Eintrag im Startmenü ist vorhanden.") : "";
    }

    void OpenTicket(string label, string title, string template)
    {
        var os = RuntimeInformation.OSDescription;
        var body = template + $"\n\n---\nBibleStudy {AppPaths.Version} · {os}";
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
