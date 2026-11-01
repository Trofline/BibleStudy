using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>
/// Status bar (version number, state of the updates), the GitHub button and the patch notes: the notes of the releases are read from
/// GitHub (kept in the data folder for offline reading) and shown once after an update; afterwards they can be opened any time.
/// </summary>
public partial class MainWindow
{
    const string ProjectUrl = "https://github.com/Trofline/BibleStudy";

    Window? _patchNotes;

    void InitPatchNotes()
    {
        VersionChipText.Text = "v" + AppPaths.Version;
        VersionChip.Click += async (_, _) => await ShowPatchNotesAsync();
        PatchNotesBtn.Click += async (_, _) => await ShowPatchNotesAsync();
        GitHubBtn.Click += (_, _) => OpenUrl(ProjectUrl);
        UpdateChip.Click += async (_, _) => await CheckForUpdateAsync(manual: true);
        // the state of the updates also in the status bar: the same text as on the settings page, short when everything is fine
        UpdateStatus.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBlock.TextProperty) return;
            var text = UpdateStatus.Text ?? "";
            // everything fine: the version number carries it ("v1.0.20 · ✓ Aktuell"); anything else (new version, progress, error) gets its own field
            var upToDate = text == L("Du hast die neueste Version ({0}).", AppPaths.Version);
            VersionChipText.Text = "v" + AppPaths.Version + (upToDate ? "  ·  ✓ " + L("Aktuell") : "");
            UpdateChip.IsVisible = text.Length > 0 && !upToDate;
            UpdateChipText.Text = text;
            ToolTip.SetTip(UpdateChip, text.Length > 0 ? text : L("Stand der Updates. Klicken: jetzt nach Updates suchen."));
        };
    }

    /// <summary>After an update (or the first start with this feature) the notes of the running version appear once.</summary>
    async Task ShowPatchNotesIfNewAsync()
    {
        if (_settings.LastSeenVersion == AppPaths.Version) return;
        try
        {
            _updater ??= new Updater(_http);
            var list = await _updater.ListAsync(AppPaths.Version);
            _settings.LastSeenVersion = AppPaths.Version; SaveSettings();
            if (list.Any(r => r.Version == AppPaths.Version) && IsVisible && OwnedWindows.Count == 0) ShowPatchNotes(list);
        }
        catch { /* offline: next start */ }
    }

    async Task ShowPatchNotesAsync()
    {
        if (_patchNotes is not null) { _patchNotes.Activate(); return; }
        _updater ??= new Updater(_http);
        try { ShowPatchNotes(await _updater.ListAsync(AppPaths.Version)); }
        catch (Exception ex) { StatusText.Text = L("Die Patch Notes konnten nicht geladen werden: {0}", ex.Message); }
    }

    void ShowPatchNotes(List<ReleaseNote> list)
    {
        if (_patchNotes is not null) { _patchNotes.Activate(); return; }
        var current = Updater.Parse(AppPaths.Version);
        var stack = new StackPanel { Spacing = 8, Margin = new(0, 0, 8, 0) };
        var first = true;
        foreach (var r in list)
        {
            var v = Updater.Parse(r.Version);
            var tag = v is null || current is null ? "" : v == current ? "  ·  " + L("installiert") : v > current ? "  ·  " + L("neu") : "";
            var head = L("Version {0}", r.Version) + (r.Date is { } d ? "  ·  " + d.ToString("d", Loc.Culture) : "") + tag;
            var body = new StackPanel { Spacing = 4, Margin = new(4, 6, 4, 8) };
            AddMarkdown(body, Updater.NotesFor(r.Notes, Loc.Code));
            var link = new Button { Content = L("Auf GitHub ansehen"), Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
            var url = r.PageUrl; link.Click += (_, _) => OpenUrl(url);
            var row = new WrapPanel { Children = { link } };
            // back to an older version: only after a question, with a backup of the data first (same way as an update)
            if (v is not null && current is not null && v < current && OperatingSystem.IsWindows() && (AppPaths.IsInstalled || AppPaths.IsPortable) && (AppPaths.IsPortable ? r.Portable : r.Setup) is { } package)
            {
                var back = new Button { Content = L("Auf diese Version zurück"), Classes = { "ghost" }, Margin = new(8, 0, 0, 0) };
                back.Click += async (_, _) =>
                {
                    var answer = await AskAsync(L("Zurück auf Version {0}", package.Version),
                        L("Möchtest du auf Version {0} zurückgehen (du hast {1})?\n\nVorher wird eine Sicherung deiner Daten angelegt, danach startet das Programm neu. Ältere Versionen kennen neuere Funktionen und Daten eventuell nicht. Ohne deine Zustimmung geschieht nichts.", package.Version, AppPaths.Version),
                        L("Zurückgehen"), L("Abbrechen"));
                    if (answer != 0) return;
                    _patchNotes?.Close();
                    await InstallUpdateAsync(package);
                };
                row.Children.Add(back);
            }
            body.Children.Add(row);
            stack.Children.Add(new Expander { Header = head, Content = body, IsExpanded = first || (v is not null && v == current), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            first = false;
        }
        if (list.Count == 0) stack.Children.Add(new TextBlock { Text = L("Es gibt noch keine veröffentlichten Versionen."), TextWrapping = TextWrapping.Wrap });

        var close = new Button { Content = L("Schließen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var win = new Window { Title = L("Patch Notes"), Width = 640, Height = 560, MinWidth = 380, MinHeight = 300, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var dock = new DockPanel { Margin = new(20) };
        DockPanel.SetDock(close, Dock.Bottom);
        dock.Children.Add(close);
        var title = new TextBlock { Text = L("Patch Notes"), FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(title, Dock.Top);
        dock.Children.Add(title);
        dock.Children.Add(new ScrollViewer { Content = stack });
        win.Content = dock;
        close.Click += (_, _) => win.Close();
        win.Closed += (_, _) => _patchNotes = null;
        _patchNotes = win;
        win.Show(this);
    }

    /// <summary>Headings, "- " lists and **bold** / `code` of a release text, as plain controls.</summary>
    static void AddMarkdown(Panel panel, string text)
    {
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;
            var bullet = line.StartsWith("- ") || line.StartsWith("* ");
            var heading = line.StartsWith('#');
            var content = bullet ? line[2..] : heading ? line.TrimStart('#').Trim() : line;
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(bullet ? 12 : 0, heading ? 6 : 0, 0, 0), FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal };
            if (bullet) block.Inlines!.Add(new Run("•  "));
            var bold = false;
            foreach (var part in content.Split("**"))
            {
                foreach (var (piece, i) in part.Split('`').Select((p, i) => (p, i)))
                {
                    if (piece.Length == 0) continue;
                    var run = new Run(piece);
                    if (bold) run.FontWeight = FontWeight.SemiBold;
                    if (i % 2 == 1) run.FontFamily = new FontFamily("Consolas, Menlo, monospace");
                    block.Inlines!.Add(run);
                }
                bold = !bold;
            }
            panel.Children.Add(block);
        }
    }
}
