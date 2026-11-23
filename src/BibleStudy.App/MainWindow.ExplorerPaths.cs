using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Reading paths in the explorer: a path can belong to a folder. Opened, the folder shows the path above the list (with a button that starts
/// reading it) and arrows in the margin from entry to entry in the order of the path, as in the text. A new order of the steps, of the list or
/// a moved entry draws them anew (the list is filled anew each time).
/// </summary>
public partial class MainWindow
{
    PathGutter _exGutter = null!;
    StackPanel _exPathBar = null!;

    List<ReadingPath> PathsOfFolder(long folder)
    {
        try { return _vault.GetReadingPathsIn(folder); } catch (Exception ex) when (ex is not OutOfMemoryException) { return []; }
    }

    /// <summary>The row of the list a step stands on: the entry with just these verses, else the one that starts there, else one that holds it.</summary>
    static int? RowOfStep(List<ExItem> rows, StudyPassage s)
    {
        bool Here(ExItem i) => i.Kind is ExKind.Mark or ExKind.Note && i.Id >= 0 && i.Ref is { } r && r.Book == s.Book && r.Chapter == s.Chapter;
        int? Find(Func<ExItem, bool> fits)
        {
            for (var i = 0; i < rows.Count; i++) if (Here(rows[i]) && fits(rows[i])) return i;
            return null;
        }
        static int V(ExItem i) => i.Ref!.Value.Verse;
        return Find(i => V(i) == s.From && Math.Max(V(i), i.Last) == s.To)
            ?? Find(i => V(i) == s.From)
            ?? Find(i => V(i) <= s.From && s.From <= Math.Max(V(i), i.Last));
    }

    /// <summary>The paths of the open folder: the bar above the list and the arrows beside it (nothing in the trash, in "Alles zeigen" or at the top).</summary>
    void UpdateExplorerPaths(List<ExItem> rows)
    {
        var paths = !_exFlat && !_exInTrash && _exFolder is long f ? PathsOfFolder(f) : [];
        FillExplorerPathBar(paths, rows);
        var badges = new List<PathGutter.Badge>();
        var raw = new List<(int From, int To, Color Color)>();
        if (_settings.PathArrows)
            foreach (var p in paths)
            {
                var color = PathColor(p);
                int? before = null;
                for (var i = 0; i < p.Steps.Count; i++)
                {
                    if (RowOfStep(rows, p.Steps[i]) is not int row) continue;   // a step that is not in this folder: the arrow goes on to the next one that is
                    badges.Add(new(row, i + 1, color, $"{p.Name} · {i + 1}/{p.Steps.Count}"));
                    if (before is int b && b != row) raw.Add((b, row, color));
                    before = row;
                }
            }
        var lanes = PathArrows.Lanes([.. raw.Select(a => (a.From, a.To))]);
        var width = PathGutter.WidthFor(lanes.Length == 0 ? 0 : lanes.Max() + 1, badges.Count > 0);
        _exGutter.BoxOf = i => i >= 0 && i < _exList.Children.Count ? _exList.Children[i] : null;
        _exGutter.Show(badges, [.. raw.Select((a, i) => new PathGutter.Arrow(a.From, a.To, lanes[i], a.Color))]);
        _exList.Margin = new(6 + width, 0, 6, 12);
        _exHead.Margin = new(8 + width, 6, 12, 4);
    }

    /// <summary>Above the list: each path of the folder with its number of steps, "Lesen" (to the first step) and "Ordnen".</summary>
    void FillExplorerPathBar(List<ReadingPath> paths, List<ExItem> rows)
    {
        _exPathBar.Children.Clear();
        _exPathBar.IsVisible = paths.Count > 0;
        foreach (var p in paths)
        {
            var path = p;
            var color = PathColor(p);
            var missing = p.Steps.Count(s => RowOfStep(rows, s) is null);
            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Text = L("Lesepfad „{0}“ · {1} Schritte", p.Name, p.Steps.Count) + (missing > 0 ? "  ·  " + L("{0} nicht in diesem Ordner", missing) : ""),
            };
            ToolTip.SetTip(text, string.Join("\n", p.Steps.Select((s, i) => $"{i + 1}. {StepLabel(s)}")));
            var read = new Button { Content = L("▶ Lesen"), Classes = { "primary" }, Padding = new(10, 2), Height = 28, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = p.Steps.Count > 0 };
            ToolTip.SetTip(read, L("Zum ersten Schritt; unter jedem Vers geht es zum nächsten."));
            read.Click += (_, _) => GoToStep(path, 0);
            var order = new Button { Content = L("Ordnen …"), Padding = new(10, 2), Height = 28, Margin = new(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
            order.Click += (_, _) => _ = ShowPathsAsync(path.Id);
            var dot = new Border { Width = 12, Height = 12, CornerRadius = new(6), Background = new SolidColorBrush(color), Margin = new(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(8, 0, 0, 0), Children = { read, order } };
            DockPanel.SetDock(dot, Dock.Left); DockPanel.SetDock(buttons, Dock.Right);
            var line = new Border
            {
                Padding = new(10, 5), CornerRadius = new(10), BorderThickness = new(1), BorderBrush = new SolidColorBrush(color, 0.7), Background = new SolidColorBrush(color, 0.10),
                Child = new DockPanel { Children = { dot, buttons, text } },
            };
            _exPathBar.Children.Add(line);
        }
    }

    /// <summary>A submenu that is filled only when it opens (the menus are made for every row of the list).</summary>
    MenuItem LazyMenu(string header, Action<MenuItem> fill)
    {
        var menu = new MenuItem { Header = header };
        menu.Items.Add(new MenuItem { Header = "…" });   // without an item it does not open
        menu.SubmenuOpened += (_, _) => { menu.Items.Clear(); fill(menu); };
        return menu;
    }

    /// <summary>On a folder: which paths belong to it (a click changes it), a new path of its entries, and ordering the paths.</summary>
    MenuItem FolderPathsMenu(long folder, Control anchor) => LazyMenu(L("Lesepfade"), menu =>
    {
        var key = _vault.FolderKey(folder);
        foreach (var p in ReadingPaths().Where(p => !LookedAt(p)))
        {
            var path = p; var on = p.Folder == key && key.Length > 0;
            menu.Items.Add(MenuAction((on ? "✓  " : "     ") + p.Name + (p.Folder.Length > 0 && !on ? "  (" + FolderLabel(p.Folder) + ")" : ""), () =>
            {
                _vault.SetReadingPathFolder(path.Id, on ? null : folder);
                StatusText.Text = on ? L("„{0}“ gehört zu keinem Ordner mehr.", path.Name) : L("„{0}“ gehört jetzt zum Ordner „{1}“; im Explorer zeigen Pfeile den Weg.", path.Name, FolderById(folder)?.Name ?? "");
                AfterPathChange();
            }));
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction(L("Neuer Lesepfad aus diesem Ordner …"), () =>
        {
            // the entries of the folder in the order of the Bible (the order is changed afterwards in "Ordnen")
            var steps = ExAllItems().Where(i => i.FolderId == folder).OrderBy(i => i.Ref is { } r ? BookOrder(r.Book) : int.MaxValue).ThenBy(i => i.Ref?.Chapter).ThenBy(i => i.Ref?.Verse)
                .Select(ExStep).OfType<StudyPassage>().ToList();
            if (steps.Count == 0) { ExNote(L("In diesem Ordner liegen keine Markierungen oder Notizen."), false); return; }
            AskPathName(anchor, FolderById(folder)?.Name ?? "", name =>
            {
                NewPath(name, steps, folder);
                ExNote(L("Lesepfad „{0}“ mit {1} Schritten angelegt, in der Reihenfolge der Bibel. Die Reihenfolge änderst du unter „Ordnen“.", name, steps.Count), false);
            });
        }));
        menu.Items.Add(MenuAction(L("Lesepfade ordnen …"), () => _ = ShowPathsAsync(PathsOfFolder(folder).FirstOrDefault()?.Id)));
    });

    /// <summary>On a mark or note: onto the path used last, earlier or later in a path of the folder, out of it, onto another path, or a new path.</summary>
    MenuItem ItemPathMenu(ExItem item, List<ExItem> targets, Control anchor) => LazyMenu(L("Lesepfad"), menu => FillItemPathMenu(menu.Items, item, targets, anchor));

    /// <summary>The choices of <see cref="ItemPathMenu"/> (also under "Lesepfad" in the bar of the chosen entries, where a long press makes no menu).</summary>
    void FillItemPathMenu(ItemCollection menu, ExItem? item, List<ExItem> targets, Control anchor)
    {
        var steps = targets.Select(ExStep).OfType<StudyPassage>().ToList();
        if (steps.Count == 0) { menu.Add(MenuAction(L("Wähle Markierungen oder Notizen; ein Lesepfad führt durch Verse."), () => { }, enabled: false)); return; }
        var paths = ReadingPaths();
        if (LastPath() is { } last)
            menu.Add(MenuAction(L("＋ An „{0}“ anhängen (Schritt {1})", Shorten(last.Name, 30), last.Steps.Count + 1), () => AddSteps(last, steps)));
        // a step of a path of this folder: earlier, later, out
        var key = !_exFlat && _exFolder is long f ? _vault.FolderKey(f) : "";
        if (targets.Count == 1 && item is not null && ExStep(item) is { } mine)
            foreach (var p in paths.Where(p => key.Length > 0 && p.Folder == key))
            {
                var index = p.Steps.ToList().FindIndex(s => s.Book == mine.Book && s.Chapter == mine.Chapter && s.From == mine.From);
                if (index < 0) continue;
                var path = p;
                void Move(int by) { var list = path.Steps.ToList(); (list[index], list[index + by]) = (list[index + by], list[index]); _vault.UpdateReadingPath(path with { Steps = list }); AfterPathChange(L("„{0}“: jetzt Schritt {1}.", path.Name, index + by + 1)); }
                menu.Add(new Separator());
                menu.Add(MenuAction(L("„{0}“, Schritt {1}: früher lesen", Shorten(p.Name, 24), index + 1), () => Move(-1), index > 0));
                menu.Add(MenuAction(L("„{0}“, Schritt {1}: später lesen", Shorten(p.Name, 24), index + 1), () => Move(1), index + 1 < p.Steps.Count));
                menu.Add(MenuAction(L("Aus „{0}“ nehmen (Schritt {1})", Shorten(p.Name, 30), index + 1), () => RemoveStep(path, index)));
            }
        menu.Add(new Separator());
        foreach (var p in paths.Where(p => p.Id != LastPath()?.Id).Take(10))
        {
            var path = p;
            menu.Add(MenuAction(L("An „{0}“ anhängen (Schritt {1})", Shorten(p.Name, 30), p.Steps.Count + 1), () => AddSteps(path, steps)));
        }
        menu.Add(MenuAction(L("Neuer Lesepfad aus der Auswahl …"), () => PathFromExplorer(targets, anchor)));
        if (paths.Count > 0) menu.Add(MenuAction(L("Lesepfade ordnen …"), () => _ = ShowPathsAsync()));
    }
}
