using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace BibleStudy.Setup;

static class Program
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BibleStudy";

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var opt = new Options(args);
        var existing = ExistingInstall();
        opt.Dir ??= existing ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BibleStudy");

        if (opt.Silent)
        {
            try { Installer.Run(opt, _ => { }); return 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "BibleStudy-Setup-error.txt"), ex.ToString()); return 1; }
        }
        Application.Run(new SetupForm(opt, existing is not null));
        return 0;
    }

    static string? ExistingInstall()
    {
        using var k = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return k?.GetValue("InstallLocation") as string is { Length: > 0 } p && Directory.Exists(p) ? p : null;
    }
}

sealed class Options
{
    public bool Silent, Update, NoLaunch, DesktopShortcut;
    public string? Dir;
    public int WaitPid;

    public Options(string[] a)
    {
        for (var i = 0; i < a.Length; i++)
            switch (a[i])
            {
                case "--silent": Silent = true; break;
                case "--update": Update = true; break;
                case "--no-launch": NoLaunch = true; break;
                case "--desktop-shortcut": DesktopShortcut = true; break;
                case "--dir" when i + 1 < a.Length: Dir = a[++i]; break;
                case "--wait-pid" when i + 1 < a.Length: int.TryParse(a[++i], out WaitPid); break;
            }
    }
}

static class Installer
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BibleStudy";

    public static string Version => Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";

    public static void Run(Options o, Action<string> status)
    {
        var dir = Path.GetFullPath(o.Dir!);
        var exe = Path.Combine(dir, "BibleStudy.App.exe");

        // never unpack into a folder that already holds other things
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() && !File.Exists(exe))
            throw new InvalidOperationException(Tr.T("folderNotEmpty", dir));

        status(Tr.T("waiting"));
        WaitForExit(o.WaitPid, dir);

        status(Tr.T("unpacking"));
        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip") ?? throw new InvalidOperationException(Tr.T("noPayload"));
        using var zip = new ZipArchive(payload);
        Directory.CreateDirectory(dir);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            var target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
            if (!target.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;   // no path tricks
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
        File.WriteAllText(Path.Combine(dir, "installed.flag"), Version);

        status(Tr.T("shortcuts"));
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BibleStudy.lnk");
        Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BibleStudy.lnk"), exe, dir);
        if (o.DesktopShortcut || File.Exists(desktop)) Shortcut(desktop, exe, dir);

        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", "BibleStudy");
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "Trofline");
            k.SetValue("InstallLocation", dir);
            k.SetValue("DisplayIcon", exe);
            k.SetValue("UninstallString", $"\"{exe}\" --uninstall");
            k.SetValue("URLInfoAbout", "https://github.com/trofline/BibleStudy");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024), RegistryValueKind.DWord);
        }

        status(Tr.T("done"));
        if (!o.NoLaunch && (o.Silent || o.Update)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir });
    }

    static void WaitForExit(int pid, string dir)
    {
        var until = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < until)
        {
            var running = Process.GetProcessesByName("BibleStudy.App").Any(p => p.Id != Environment.ProcessId && (pid == 0 ? SafePath(p).StartsWith(dir, StringComparison.OrdinalIgnoreCase) : p.Id == pid));
            if (!running) return;
            Thread.Sleep(400);
        }
        throw new InvalidOperationException(Tr.T("stillRunning"));
    }

    static string SafePath(Process p) { try { return p.MainModule?.FileName ?? ""; } catch { return ""; } }

    static void Shortcut(string path, string target, string workDir)
    {
        var shell = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException(Tr.T("noShortcuts"));
        dynamic sh = Activator.CreateInstance(shell)!;
        dynamic link = sh.CreateShortcut(path);
        link.TargetPath = target; link.WorkingDirectory = workDir; link.Description = Tr.T("description"); link.IconLocation = target + ",0";
        link.Save();
    }
}

sealed class SetupForm : Form
{
    readonly Options _o;
    readonly TextBox _dir = new() { Width = 360 };
    readonly CheckBox _desktop = new() { Text = Tr.T("desktopShortcut"), Checked = true, AutoSize = true };
    readonly ProgressBar _bar = new() { Style = ProgressBarStyle.Marquee, Visible = false, Width = 460 };
    readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(470, 0) };
    readonly Button _go = new() { Text = Tr.T("install"), Width = 130, Height = 32 };
    bool _done;

    public SetupForm(Options o, bool update)
    {
        _o = o;
        Text = "BibleStudy – Setup"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 330); Font = new Font("Segoe UI", 9.5f);
        _dir.Text = o.Dir!;
        var title = new Label { Text = update ? Tr.T("titleUpdate") : Tr.T("titleInstall"), Font = new Font("Segoe UI Semibold", 15f), AutoSize = true, Location = new(20, 16) };
        var info = new Label
        {
            Text = Tr.T("info1") + "\n\n" + Tr.T("info2"),
            Location = new(20, 56), Size = new(460, 100),
        };
        var where = new Label { Text = Tr.T("folder"), AutoSize = true, Location = new(20, 166) };
        _dir.Location = new(20, 188);
        var browse = new Button { Text = Tr.T("change"), Location = new(390, 186), Width = 90 };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = _dir.Text };
            if (d.ShowDialog() == DialogResult.OK) _dir.Text = Path.GetFileName(d.SelectedPath).Equals("BibleStudy", StringComparison.OrdinalIgnoreCase) ? d.SelectedPath : Path.Combine(d.SelectedPath, "BibleStudy");
        };
        _desktop.Location = new(20, 224);
        _bar.Location = new(20, 254); _status.Location = new(20, 254);
        _go.Location = new(350, 282);
        var close = new Button { Text = Tr.T("close"), Location = new(250, 282), Width = 90, Height = 32 };
        close.Click += (_, _) => Close();
        _go.Click += async (_, _) => await Go();
        Controls.AddRange([title, info, where, _dir, browse, _desktop, _bar, _status, _go, close]);
        AcceptButton = _go;
    }

    async Task Go()
    {
        if (_done)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(_o.Dir!, "BibleStudy.App.exe")) { UseShellExecute = true, WorkingDirectory = _o.Dir });
            Close();
            return;
        }
        _o.Dir = _dir.Text.Trim(); _o.DesktopShortcut = _desktop.Checked;
        _go.Enabled = false; _dir.Enabled = false; _bar.Visible = true; _status.Top = _bar.Bottom + 4;
        try
        {
            await Task.Run(() => Installer.Run(_o, s => BeginInvoke(() => _status.Text = s)));
            _bar.Visible = false; _status.Top = 254; _status.Text = Tr.T("installed");
            _done = true; _go.Text = Tr.T("start"); _go.Enabled = true;
        }
        catch (Exception ex)
        {
            _bar.Visible = false; _status.Top = 254; _status.Text = Tr.T("error") + ex.Message;
            _go.Enabled = true; _dir.Enabled = true;
        }
    }
}
