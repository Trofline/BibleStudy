using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Reading paths: verses (marked or not) linked in the order in which they build on each other. In the text each step has its number in the
/// margin and an arrow leads to the next step of the same chapter (as the jumps beside the code of a disassembler); under the verse a small
/// bar leads to the step before and after, also in another chapter. Made from the verse menu or from marks chosen in the explorer.
/// </summary>
public partial class MainWindow
{
    static readonly string[] PathColors = ["#E07A3A", "#3A8FE0", "#9B5DE5", "#2BA84A", "#D63C7A", "#B8901A"];

    double _pathGutterWidth;
    List<ReadingPath> _chapterPaths = [];

    List<ReadingPath> ReadingPaths()
    {
        try { return _vault.GetReadingPaths(); } catch (Exception ex) when (ex is not OutOfMemoryException) { return []; }
    }

    static Color PathColor(ReadingPath p) => Color.TryParse(p.Color, out var c) ? c : Color.Parse(PathColors[(int)(Math.Abs(p.Id) % PathColors.Length)]);

    string StepLabel(StudyPassage s) => $"{BookName(s.Book)} {s.Chapter}:{s.From}" + (s.To > s.From ? "–" + s.To : "");

    /// <summary>The paths that have a step in this chapter (read before the verses are built: the bars under the verses need them).</summary>
    void LoadChapterPaths() => _chapterPaths = ReadingPaths().Where(p => p.Steps.Any(s => s.Covers(_curBook, _curChapter))).ToList();

    /// <summary>The circles and arrows in the margin (the margin is only as wide as they need).</summary>
    void UpdatePathMargin()
    {
        HookPathMargin();
        var badges = new List<PathGutter.Badge>();
        var raw = new List<(int From, int To, Color Color)>();
        if (_settings.PathArrows && !_independent)
            foreach (var p in _chapterPaths)
            {
                var color = PathColor(p);
                foreach (var (n, s) in p.StepsIn(_curBook, _curChapter))
                    if (_verseBoxes.ContainsKey(s.From)) badges.Add(new(s.From, n, color, $"{p.Name} · {n}/{p.Steps.Count}"));
                foreach (var (_, from, to) in p.ArrowsIn(_curBook, _curChapter))
                    if (_verseBoxes.ContainsKey(from) && _verseBoxes.ContainsKey(to)) raw.Add((from, to, color));
            }
        var lanes = PathArrows.Lanes([.. raw.Select(a => (a.From, a.To))]);
        var arrows = raw.Select((a, i) => new PathGutter.Arrow(a.From, a.To, lanes[i], a.Color)).ToList();
        var width = PathGutter.WidthFor(lanes.Length == 0 ? 0 : lanes.Max() + 1, badges.Count > 0);
        PathMargin.BoxOf = v => _verseBoxes.GetValueOrDefault(v);
        PathMargin.Show(badges, arrows);
        if (Math.Abs(width - _pathGutterWidth) > 0.5) { _pathGutterWidth = width; FitReader(); }
    }

    bool _pathLayoutHooked;

    /// <summary>The arrows follow the verses when the text is laid out anew (a wider window, a note that opens).</summary>
    void HookPathMargin()
    {
        if (_pathLayoutHooked) return;
        _pathLayoutHooked = true;
        ReaderPanel.LayoutUpdated += (_, _) => { if (PathMargin.HasContent) PathMargin.InvalidateVisual(); };
    }

    // ---- under the verse ------------------------------------------------------------------------------------------------

    /// <summary>Under a step: the path, which step it is, and the step before and after (a click goes there).</summary>
    IEnumerable<Control> PathChips(int verse)
    {
        foreach (var p in _chapterPaths)
        {
            for (var i = 0; i < p.Steps.Count; i++)
            {
                var s = p.Steps[i];
                if (!s.Covers(_curBook, _curChapter) || s.From != verse) continue;
                var path = p; var index = i;
                var color = PathColor(p);
                var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new(0, 0, 8, 4) };
                Border Piece(string text, Action? click, string tip, bool strong = false)
                {
                    var label = new TextBlock { Text = text, FontSize = 12, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
                    var b = new Border { Child = label, Padding = new(8, 2), CornerRadius = new(10), BorderThickness = new(1), BorderBrush = new SolidColorBrush(color), Background = new SolidColorBrush(color, 0.12) };
                    if (click is not null) { b.Cursor = new Cursor(StandardCursorType.Hand); b.OnPress((_, e) => { if (e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) { e.Handled = true; click(); } }); }
                    ToolTip.SetTip(b, tip);
                    return b;
                }
                if (index > 0) bar.Children.Add(Piece("‹ " + (index) + " · " + StepLabel(p.Steps[index - 1]), () => GoToStep(path, index - 1), L("Zum Schritt davor")));
                bar.Children.Add(Piece($"{p.Name} · {index + 1}/{p.Steps.Count}", () => _ = ShowPathsAsync(path.Id), L("Lesepfad ansehen und ordnen"), true));
                if (index + 1 < p.Steps.Count) bar.Children.Add(Piece(StepLabel(p.Steps[index + 1]) + " · " + (index + 2) + " ›", () => GoToStep(path, index + 1), L("Zum nächsten Schritt")));
                yield return bar;
            }
        }
    }

    void GoToStep(ReadingPath p, int index)
    {
        if (index < 0 || index >= p.Steps.Count) return;
        var s = p.Steps[index];
        _ = GoToAsync(s.Book, s.Chapter, s.From);
    }

    // ---- the verse menu -------------------------------------------------------------------------------------------------

    /// <summary>The verses a step is made of: the chosen verses when this one is among them (each run of verses one step), else this verse.</summary>
    List<StudyPassage> StepsFor(int verse)
    {
        var chosen = _selVerses.Contains(verse) && _selVerses.Count > 1 ? _selVerses.ToHashSet() : [verse];
        return MarkLogic.Runs(chosen).Select(r => new StudyPassage(_curBook, _curChapter, r.From, r.To)).ToList();
    }

    MenuItem PathMenu(int verse, Control anchor)
    {
        var menu = new MenuItem { Header = SubHeader(L("Lesepfad")) };
        var steps = StepsFor(verse);
        var paths = ReadingPaths();
        foreach (var p in paths.Take(12))
        {
            var path = p;
            menu.Items.Add(MenuAction(L("An „{0}“ anhängen (Schritt {1})", Shorten(p.Name, 30), p.Steps.Count + 1), () => AddSteps(path, steps)));
        }
        menu.Items.Add(MenuAction(L("Neuer Lesepfad ab hier …"), () => AskPathName(anchor, "", name => NewPath(name, steps))));
        var inPaths = paths.SelectMany(p => p.Steps.Select((s, i) => (p, s, i))).Where(x => x.s.Covers(_curBook, _curChapter) && x.s.From == verse).ToList();
        if (inPaths.Count > 0 || paths.Count > 0) menu.Items.Add(new Separator());
        foreach (var (p, _, i) in inPaths)
        {
            var path = p; var index = i;
            menu.Items.Add(MenuAction(L("Aus „{0}“ nehmen (Schritt {1})", Shorten(p.Name, 30), i + 1), () => RemoveStep(path, index)));
        }
        if (paths.Count > 0) menu.Items.Add(MenuAction(L("Lesepfade ordnen …"), () => _ = ShowPathsAsync(inPaths.FirstOrDefault().p?.Id)));
        return menu;
    }

    void AskPathName(Control anchor, string start, Action<string> done)
    {
        var box = new TextBox { Text = start, Width = 240, PlaceholderText = L("Name des Lesepfads, etwa „Rechtfertigung“") };
        var ok = new Button { Content = L("OK"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var fly = new Flyout { Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = L("Lesepfad"), FontWeight = FontWeight.SemiBold }, box, ok } } };
        void Apply()
        {
            var n = (box.Text ?? "").Trim();
            if (n.Length == 0) return;
            fly.Hide();
            done(n);
        }
        ok.Click += (_, _) => Apply();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(); };
        fly.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        fly.ShowAt(anchor);
    }

    long NewPath(string name, IReadOnlyList<StudyPassage> steps)
    {
        var used = ReadingPaths().Select(p => p.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var color = PathColors.FirstOrDefault(c => !used.Contains(c)) ?? PathColors[used.Count % PathColors.Length];
        var id = _vault.AddReadingPath(name, color, steps);
        AfterPathChange(L("Lesepfad „{0}“ angelegt. Hänge weitere Verse über das Vers-Menü an.", name.Trim()));
        return id;
    }

    void AddSteps(ReadingPath p, IReadOnlyList<StudyPassage> steps)
    {
        _vault.UpdateReadingPath(p with { Steps = [.. p.Steps, .. steps] });
        AfterPathChange(L("An „{0}“ angehängt: jetzt {1} Schritte.", p.Name, p.Steps.Count + steps.Count));
    }

    void RemoveStep(ReadingPath p, int index)
    {
        var list = p.Steps.ToList(); list.RemoveAt(index);
        _vault.UpdateReadingPath(p with { Steps = list });
        AfterPathChange(L("Schritt {0} aus „{1}“ genommen.", index + 1, p.Name));
    }

    void AfterPathChange(string? note = null)
    {
        if (note is not null) StatusText.Text = note;
        if (ReaderHost.IsVisible && _curBook != "") _ = RedrawChapterAsync();
    }

    // ---- ordering the paths ---------------------------------------------------------------------------------------------

    /// <summary>All paths: rename, recolor, delete, and the steps in their order (up, down, out, go there).</summary>
    async Task ShowPathsAsync(long? select = null)
    {
        var win = new Window { Title = L("Lesepfade"), Width = 560, Height = 620, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var pick = new ComboBox { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Stretch };
        var steps = new StackPanel { Spacing = 2 };
        var empty = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Text = L("Noch kein Lesepfad. Klicke mit der rechten Maustaste auf einen Vers (am Handy lange drücken) und wähle „Lesepfad“ › „Neuer Lesepfad ab hier“. Weitere Verse hängst du ebenso an; die Reihenfolge ist die, in der sie gelesen werden.") };
        var rename = new Button { Content = L("Umbenennen …") };
        var recolor = new Button { Content = L("Farbe …"), Margin = new(6, 0, 0, 0) };
        var delete = new Button { Content = L("Löschen"), Margin = new(6, 0, 0, 0) };
        var arrows = new CheckBox { Content = L("Pfeile und Nummern am Rand des Textes zeigen"), IsChecked = _settings.PathArrows, Margin = new(0, 6, 0, 0) };
        arrows.IsCheckedChanged += (_, _) => { _settings.PathArrows = arrows.IsChecked == true; SaveSettings(); };
        var changed = false;
        List<ReadingPath> all = [];
        ReadingPath? Current() => pick.SelectedItem is ComboBoxItem { Tag: long id } ? all.FirstOrDefault(p => p.Id == id) : null;

        void Load(long? keep)
        {
            all = ReadingPaths();
            pick.Items.Clear();
            foreach (var p in all)
                pick.Items.Add(new ComboBoxItem { Tag = p.Id, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new Border { Width = 12, Height = 12, CornerRadius = new(6), Background = new SolidColorBrush(PathColor(p)), VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = $"{p.Name}  ({p.Steps.Count})" } } } });
            pick.SelectedItem = pick.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is long id && id == keep) ?? pick.Items.OfType<ComboBoxItem>().FirstOrDefault();
            empty.IsVisible = all.Count == 0;
            rename.IsEnabled = recolor.IsEnabled = delete.IsEnabled = pick.IsEnabled = all.Count > 0;
            FillSteps();
        }

        void Save(ReadingPath p) { _vault.UpdateReadingPath(p); changed = true; Load(p.Id); }

        void FillSteps()
        {
            steps.Children.Clear();
            if (Current() is not { } p) return;
            if (p.Steps.Count == 0) steps.Children.Add(new TextBlock { Classes = { "caption" }, Text = L("Dieser Lesepfad hat noch keine Schritte.") });
            for (var i = 0; i < p.Steps.Count; i++)
            {
                var index = i; var s = p.Steps[i];
                Button Tool(string text, string tip, Action act, bool enabled = true)
                {
                    var b = new Button { Content = text, Classes = { "tool" }, Width = 30, Height = 30, IsEnabled = enabled, Padding = new(0), HorizontalContentAlignment = HorizontalAlignment.Center };
                    ToolTip.SetTip(b, tip); b.Click += (_, _) => act();
                    return b;
                }
                void Move(int by) { var list = p.Steps.ToList(); (list[index], list[index + by]) = (list[index + by], list[index]); Save(p with { Steps = list }); }
                var number = new Border { Width = 24, Height = 24, CornerRadius = new(12), Background = new SolidColorBrush(PathColor(p)), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White } };
                var place = new Button { Content = StepLabel(s), Classes = { "ghost" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
                ToolTip.SetTip(place, L("Hingehen"));
                place.Click += (_, _) => { win.Close(); GoToStep(p, index); };
                var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children =
                {
                    Tool("↑", L("Früher lesen"), () => Move(-1), index > 0),
                    Tool("↓", L("Später lesen"), () => Move(1), index + 1 < p.Steps.Count),
                    Tool("✕", L("Aus dem Lesepfad nehmen"), () => { var list = p.Steps.ToList(); list.RemoveAt(index); Save(p with { Steps = list }); }),
                } };
                DockPanel.SetDock(number, Dock.Left); DockPanel.SetDock(tools, Dock.Right);
                steps.Children.Add(new DockPanel { Margin = new(0, 2), Children = { number, tools, place } });
            }
        }

        pick.SelectionChanged += (_, _) => FillSteps();
        rename.Click += (_, _) => { if (Current() is { } p) AskPathName(rename, p.Name, n => Save(p with { Name = n })); };
        recolor.Click += (_, _) =>
        {
            if (Current() is not { } p) return;
            var fly = new Flyout();
            var wrap = new WrapPanel { MaxWidth = 240 };
            foreach (var hex in PathColors.Concat(PresetColors)) wrap.Children.Add(Swatch(hex, h => { fly.Hide(); Save(p with { Color = h }); }, string.Equals(hex, p.Color, StringComparison.OrdinalIgnoreCase)));
            fly.Content = wrap; fly.ShowAt(recolor);
        };
        delete.Click += (_, _) =>
        {
            if (Current() is not { } p) return;
            _vault.DeleteReadingPath(p.Id); changed = true; Load(null);
            StatusText.Text = L("Lesepfad „{0}“ gelöscht.", p.Name);
        };
        var close = new Button { Content = L("Schließen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        close.Click += (_, _) => win.Close();
        var head = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = L("Lesepfade"), FontSize = 20, FontWeight = FontWeight.SemiBold },
            new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Text = L("Verse in der Reihenfolge, in der sie aufeinander aufbauen. Im Text zeigt eine Nummer am Rand den Schritt, ein Pfeil führt zum nächsten Schritt im selben Kapitel; unter dem Vers geht es zum Schritt davor und danach.") },
            empty, pick,
            new WrapPanel { Children = { rename, recolor, delete } },
            arrows,
        } };
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(close, Dock.Bottom);
        win.Content = new DockPanel { Margin = new(22), Children = { head, close, new ScrollViewer { Margin = new(0, 12, 0, 0), Content = steps } } };
        Load(select);
        var arrowsBefore = _settings.PathArrows;
        await ShowModalAsync(win);
        if (changed || arrowsBefore != _settings.PathArrows) AfterPathChange();
    }

    /// <summary>The marks (or notes) chosen in the explorer become a reading path, in the order in which they were chosen.</summary>
    void PathFromExplorer(List<ExItem> items, Control anchor)
    {
        var marks = _vault.GetMarks().ToDictionary(m => m.Id);
        var steps = new List<StudyPassage>();
        foreach (var i in items)
        {
            if (i.Kind == ExKind.Mark && marks.TryGetValue(i.Id, out var m)) steps.Add(new StudyPassage(m.Ref.Book, m.Ref.Chapter, m.Ref.Verse, Math.Max(m.Ref.Verse, m.LastVerse)));
            else if (i.Kind == ExKind.Note && i.Ref is { } r) steps.Add(new StudyPassage(r.Book, r.Chapter, r.Verse, r.Verse));
        }
        if (steps.Count == 0) { ExNote(L("Wähle Markierungen oder Notizen; ein Lesepfad führt durch Verse."), false); return; }
        AskPathName(anchor, "", name =>
        {
            NewPath(name, steps);
            ExNote(L("Lesepfad „{0}“ mit {1} Schritten angelegt, in der Reihenfolge, in der du gewählt hast.", name, steps.Count), false);
        });
    }
}
