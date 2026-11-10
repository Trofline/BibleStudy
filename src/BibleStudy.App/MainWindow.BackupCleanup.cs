using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>Clean up old backups: a list of the backup ZIPs with a check box each and the space they take; the checked ones are deleted.</summary>
public partial class MainWindow
{
    static string SizeText(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    /// <summary>The kind of a backup from its name: Auto, Manuell, vor-Update, vor-Deinstallation …</summary>
    static string BackupKind(string name)
    {
        var n = Path.GetFileNameWithoutExtension(name);
        if (n.StartsWith("BibleStudy-Daten-Auto-")) return L("Automatisch");
        if (n.StartsWith("BibleStudy-Daten-Manuell-")) return L("Von Hand");
        var up = System.Text.RegularExpressions.Regex.Match(n, @"vor-Update-(\d+(\.\d+)*)");
        if (up.Success) return L("Vor dem Update auf {0}", up.Groups[1].Value);
        if (n.Contains("vor-dem-Loeschen")) return L("Vor dem Löschen eines Vaults");
        if (n.Contains("Deinstallation", StringComparison.OrdinalIgnoreCase)) return L("Vor dem Deinstallieren");
        return L("Sicherung");
    }

    async Task ShowBackupCleanupAsync()
    {
        var folders = new[] { BackupFolder, AppPaths.BackupDir }.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var files = folders.SelectMany(f => new DirectoryInfo(f).EnumerateFiles("BibleStudy-Daten-*.zip")).OrderByDescending(f => f.LastWriteTime).ToList();
        if (files.Count == 0) { await AskAsync(L("Sicherungen aufräumen"), L("Es gibt keine Sicherungen in {0}.", BackupFolder), L("OK")); return; }

        var boxes = new List<(CheckBox Box, FileInfo File)>();
        var list = new StackPanel { Spacing = 2 };
        var total = new TextBlock { FontWeight = FontWeight.SemiBold };
        var del = new Button { Content = L("Ausgewählte löschen …"), Classes = { "primary" }, IsEnabled = false };
        void Update()
        {
            var chosen = boxes.Where(b => b.Box.IsChecked == true).ToList();
            total.Text = L("{0} von {1} ausgewählt ({2} von {3})", chosen.Count, boxes.Count, SizeText(chosen.Sum(c => c.File.Length)), SizeText(boxes.Sum(c => c.File.Length)));
            del.IsEnabled = chosen.Count > 0;
        }
        foreach (var f in files)
        {
            var box = new CheckBox { Content = $"{f.LastWriteTime.ToString("g", Loc.Culture)}   ·   {BackupKind(f.Name)}   ·   {SizeText(f.Length)}", Margin = new(0, 2) };
            ToolTip.SetTip(box, f.FullName);
            box.IsCheckedChanged += (_, _) => Update();
            boxes.Add((box, f)); list.Children.Add(box);
        }
        var win = new Window { Title = L("Sicherungen aufräumen"), Width = 560, Height = 560, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var oldBtn = new Button { Content = L("Alle bis auf die 3 neuesten") };
        oldBtn.Click += (_, _) => { for (var i = 0; i < boxes.Count; i++) boxes[i].Box.IsChecked = i >= 3; };
        var noneBtn = new Button { Content = L("Keine") };
        noneBtn.Click += (_, _) => boxes.ForEach(b => b.Box.IsChecked = false);
        var close = new Button { Content = L("Schließen") };
        close.Click += (_, _) => win.Close();
        del.Click += async (_, _) =>
        {
            var chosen = boxes.Where(b => b.Box.IsChecked == true).ToList();
            var size = SizeText(chosen.Sum(c => c.File.Length));
            if (await AskAsync(L("Sicherungen löschen?"), L("{0} Sicherungen ({1}) werden endgültig gelöscht und lassen sich nicht zurückholen.", chosen.Count, size), L("Löschen"), L("Abbrechen")) != 0) return;
            var failed = 0;
            foreach (var (box, file) in chosen)
            {
                try { file.Delete(); list.Children.Remove(box); boxes.Remove((box, file)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed++; }
            }
            Update();
            StatusText.Text = failed == 0 ? L("{0} Sicherungen gelöscht ({1} frei).", chosen.Count, size) : L("{0} Sicherungen konnten nicht gelöscht werden.", failed);
            if (boxes.Count == 0) win.Close();
        };
        var tools = new WrapPanel { Margin = new(0, 0, 0, 8), Children = { oldBtn, noneBtn } };
        foreach (var b in tools.Children.OfType<Button>()) b.Margin = new(0, 0, 8, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0), Children = { del, close } };
        var head = new StackPanel { Children = { new TextBlock { Text = L("Alte Sicherungen belegen Speicher. Hake an, was weg kann."), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) }, tools } };
        var foot = new StackPanel { Children = { total, buttons } };
        var dock = new DockPanel { Margin = new(18) };
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(head); dock.Children.Add(foot); dock.Children.Add(new ScrollViewer { Content = list });
        win.Content = dock;
        Update();
        await ShowModalAsync(win);
    }
}
