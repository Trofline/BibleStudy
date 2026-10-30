using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// Parallel columns: every column has its own color (header, tint, dot in the bar) so the translations are easy to tell apart.
/// Scrolling is either synchronous (verse by verse in one list, headers stay on top) or independent (one scroller per column).
/// </summary>
public partial class MainWindow
{
    static readonly string[] ColumnColors = ["", "#14B8A6", "#A855F7", "#F43F5E"];

    bool _independent;
    Grid? _indepGrid;
    readonly List<Dictionary<int, Border>> _colBoxes = [];
    readonly List<ScrollViewer> _colScrolls = [];
    /// <summary>Per column: the passage text when it differs from the main text (shown in the header), and whether it does.</summary>
    List<string?> _colPassage = [];
    readonly List<bool> _colForeign = [];
    bool IsForeign(int col) => col > 0 && col < _colForeign.Count && _colForeign[col];
    Border? _syncPill;
    bool _settingSyncCheck;

    Color ColumnColor(int i) => i == 0 ? (Res("BsAccent") as ISolidColorBrush)?.Color ?? Colors.Goldenrod : Color.Parse(ColumnColors[i % ColumnColors.Length]);

    Border ColumnHeader(TranslationInfo t, Chapter? c, int i)
    {
        var color = ColumnColor(i);
        var name = new TextBlock { Text = t.ShortName + (i < _colPassage.Count && _colPassage[i] is { } passage ? " · " + passage : "") + (c is null ? L(" (Kapitel nicht verfügbar)") : ""), FontWeight = FontWeight.Bold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
        var sub = new TextBlock { Text = t.LanguageName, FontSize = 11.5, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
        return new Border
        {
            Background = new SolidColorBrush(color, 0.16), BorderBrush = new SolidColorBrush(color), BorderThickness = new(0, 0, 0, 3),
            CornerRadius = new(8, 8, 0, 0), Padding = new(10, 5), Margin = new(0, 0, 6, 0),
            Child = new StackPanel { Children = { name, sub } },
        };
    }

    /// <summary>One verse cell of a column inside the synchronous list: colored bar and light tint.</summary>
    Control ColumnCell(Control cell, int i, int count)
    {
        if (count < 2) return cell;
        var color = ColumnColor(i);
        return new Border { Child = cell, Background = new SolidColorBrush(color, 0.06), BorderBrush = new SolidColorBrush(color, 0.7), BorderThickness = new(3, 0, 0, 0), Padding = new(10, 2, 0, 2), Margin = new(0, 0, 6, 0) };
    }

    void BuildStickyHeader(List<(TranslationInfo T, Chapter? C)> cols)
    {
        ParallelHeader.ColumnDefinitions.Clear(); ParallelHeader.Children.Clear();
        for (var i = 0; i < cols.Count; i++)
        {
            ParallelHeader.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var h = ColumnHeader(cols[i].T, cols[i].C, i);
            Grid.SetColumn(h, i);
            ParallelHeader.Children.Add(h);
        }
        ParallelHeader.IsVisible = true;
    }

    Border MakeVerseBox(Control content, int n, bool foreign = false)
    {
        var box = new Border { Child = content, Padding = new(10, 3), CornerRadius = new(8), BorderThickness = new(3, 0, 0, 0), BorderBrush = Brushes.Transparent, Classes = { "verse" } };
        box.AddHandler(PointerPressedEvent, (object? _, PointerPressedEventArgs e) => OnVersePressed(n, e, foreign), RoutingStrategies.Tunnel, handledEventsToo: true);
        if (!foreign) box.AddHandler(PointerReleasedEvent, (object? _, PointerReleasedEventArgs e) => CaptureSelection(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        return box;
    }

    /// <summary>Each column in its own scroller, verses of the same number are not forced onto one line.</summary>
    void BuildIndependent(List<(TranslationInfo T, Chapter? C)> cols, int size, HashSet<int> noted, List<Dictionary<int, (string Letter, string Text)>?> notes, TranslationInfo primary)
    {
        var grid = new Grid { Margin = new(8, 4, 8, 0) };
        for (var i = 0; i < cols.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var panel = new StackPanel { Spacing = 2, Margin = new(4, 6, 8, 60) };
            var boxes = i == 0 ? _verseBoxes : new Dictionary<int, Border>();
            if (i > 0) _colBoxes.Add(boxes);
            var color = ColumnColor(i);
            var foreign = i > 0 && i < _colPassage.Count && _colPassage[i] is not null;   // another book or chapter than the main text: read-only reference
            _colForeign.Add(foreign);
            foreach (var v in cols[i].C?.Verses ?? [])
            {
                var num = v.Number;
                var cell = VerseCell(v, num, cols[i].T, size, i == 0 && noted.Contains(num), notes[i],
                    foreign ? [] : _chapterMarks.Where(m => m.Ref.Verse == num && !m.WholeVerse && m.TranslationId == cols[i].T.Id).ToList());
                var box = MakeVerseBox(i == 0 && XrefChips(num, primary.Id) is { } chips ? new StackPanel { Children = { cell, chips } } : cell, num, foreign);
                boxes[num] = box; panel.Children.Add(box);
            }
            var scroll = new ScrollViewer { Content = panel };
            _colScrolls.Add(scroll);
            _colIds.Add(cols[i].T.Id);
            var col = i;
            scroll.ScrollChanged += (_, _) => MirrorScroll(col);
            var column = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            var header = ColumnHeader(cols[i].T, cols[i].C, i);
            header.Margin = new(0);
            column.Children.Add(header);
            Grid.SetRow(scroll, 1); column.Children.Add(scroll);
            var frame = new Border
            {
                Child = column, Margin = new(i == 0 ? 0 : 8, 0, 0, 0), CornerRadius = new(8), ClipToBounds = true,
                Background = new SolidColorBrush(color, 0.05), BorderBrush = new SolidColorBrush(color, 0.45), BorderThickness = new(1),
            };
            Grid.SetColumn(frame, i); grid.Children.Add(frame);
        }
        _indepGrid = grid;
        ReaderPanel.Children.Add(grid);
        FitReader();
    }

    // ---- selective sync: columns on their own, but the chosen ones follow each other verse by verse -------------------------

    readonly List<string> _colIds = [];
    readonly Dictionary<int, double> _mirrorExpect = [];

    Dictionary<int, Border> BoxesOf(int col) => col == 0 ? _verseBoxes : col - 1 < _colBoxes.Count ? _colBoxes[col - 1] : [];

    void MirrorScroll(int src)
    {
        if (!_independent || src >= _colIds.Count || IsForeign(src) || !_settings.ParallelSyncIds.Contains(_colIds[src])) return;
        var scroll = _colScrolls[src];
        if (_mirrorExpect.TryGetValue(src, out var expected) && Math.Abs(scroll.Offset.Y - expected) < 2) { _mirrorExpect.Remove(src); return; }
        if (scroll.Content is not Control content) return;
        // the verse at the top of this column and how far into it we are
        int? verse = null; double top = 0;
        foreach (var (n, box) in BoxesOf(src).OrderBy(b => b.Key))
            if (box.TranslatePoint(new Point(0, 0), content) is { } p && p.Y + box.Bounds.Height > scroll.Offset.Y + 2) { verse = n; top = p.Y; break; }
        if (verse is not int v) return;
        var delta = scroll.Offset.Y - top;
        for (var j = 0; j < _colScrolls.Count; j++)
        {
            if (j == src || IsForeign(j) || !_settings.ParallelSyncIds.Contains(_colIds[j]) || _colScrolls[j].Content is not Control other) continue;
            var boxes = BoxesOf(j);
            var target = boxes.Where(b => b.Key >= v).OrderBy(b => b.Key).Select(b => b.Value).FirstOrDefault();
            if (target?.TranslatePoint(new Point(0, 0), other) is not { } q) continue;
            var y = Math.Max(0, q.Y + delta);
            if (Math.Abs(_colScrolls[j].Offset.Y - y) < 2) continue;
            _mirrorExpect[j] = y;
            _colScrolls[j].Offset = new Vector(0, y);
        }
    }

    /// <summary>The bar above the text: sync switch (all columns together), a choice of which columns scroll together, one colored dot per column.</summary>
    void EnsureSyncPill()
    {
        if (_syncPill is not null) return;
        var check = new CheckBox { Content = L("Synchron scrollen"), IsChecked = _settings.ParallelSync, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(check, L("An: alle Spalten laufen Vers für Vers nebeneinander und scrollen gemeinsam. Aus: jede Spalte scrollt für sich."));
        check.IsCheckedChanged += async (_, _) =>
        {
            if (_settingSyncCheck) return;
            _settings.ParallelSync = check.IsChecked == true;
            if (_settings.ParallelSync) _settings.ParallelSyncIds.Clear();
            await ShowChapterAsync(_selected);
        };
        var pick = new Button { Classes = { "tool" }, Width = 28, Height = 28, Content = new Icon { Data = Geometry.Parse("M4 6h16 M7 12h10 M10 18h4"), IconSize = 14 }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(pick, L("Auswählen, welche Spalten zusammen scrollen"));
        pick.Click += (_, _) => ShowSyncChoice(pick, check);
        _syncPill = new Border { Classes = { "pill" }, Margin = new(16, 8, 0, 8), Padding = new(12, 0, 4, 0), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { check, pick } } };
    }

    void ShowSyncChoice(Control anchor, CheckBox all)
    {
        var cols = new List<TranslationInfo>();
        if (TranslationBox.SelectedItem is TranslationInfo primary) cols.Add(primary);
        cols.AddRange(_extras.Select(e => e.Translation).OfType<TranslationInfo>());
        var chosen = _settings.ParallelSync ? cols.Select(c => c.Id).ToHashSet() : _settings.ParallelSyncIds.ToHashSet();
        var panel = new StackPanel { Spacing = 4, Width = 260 };
        panel.Children.Add(new TextBlock { Text = L("Diese Spalten scrollen zusammen, die anderen für sich:"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        for (var i = 0; i < cols.Count; i++)
        {
            var id = cols[i].Id;
            var box = new CheckBox { Content = cols[i].ShortName, IsChecked = chosen.Contains(id), Foreground = new SolidColorBrush(ColumnColor(i)) };
            box.IsCheckedChanged += (_, _) => { if (box.IsChecked == true) chosen.Add(id); else chosen.Remove(id); };
            panel.Children.Add(box);
        }
        var apply = new Button { Content = L("Übernehmen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 6, 0, 0) };
        var fly = new Flyout { Content = panel };
        apply.Click += async (_, _) =>
        {
            fly.Hide();
            var allOn = chosen.Count == cols.Count;
            _settings.ParallelSync = allOn;
            _settings.ParallelSyncIds = allOn ? [] : chosen.Where(c => cols.Any(x => x.Id == c)).ToList();
            _settingSyncCheck = true;   // the switch only shows the new state; drawing happens once, below
            all.IsChecked = allOn;
            _settingSyncCheck = false;
            await ShowChapterAsync(_selected);
        };
        panel.Children.Add(apply);
        fly.ShowAt(anchor);
    }

    // ---- a column with a passage of its own ----------------------------------------------------------------------------

    void UpdatePassageButton(ExtraColumn col) =>
        col.PassageBtn.Content = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Text = col.Own is { } o ? $"{BookName(o.Book)} {o.Chapter}" : L("Wie links"), FontWeight = col.Own is null ? FontWeight.Normal : FontWeight.SemiBold, Opacity = col.Own is null ? 0.7 : 1 };

    /// <summary>Book, then chapter, for one column; the main text keeps its place.</summary>
    void ChooseColumnPassage(ExtraColumn col)
    {
        async Task Apply((string Book, int Chapter)? own) { col.Own = own; UpdatePassageButton(col); await ShowChapterAsync(_selected); }
        ShowBookPicker(col.PassageBtn, col.Own?.Book ?? (BookBox.SelectedItem as BookInfo)?.Id, book =>
        {
            if (book.NumberOfChapters > 1) ShowChapterPicker(col.PassageBtn, book, col.Own is { } o && o.Book == book.Id ? o.Chapter : 1, c => _ = Apply((book.Id, c)));
            else _ = Apply((book.Id, 1));
        }, (L("Wie die Hauptspalte (folgt dem Text links)"), () => _ = Apply(null)));
    }

    void RecolorPills()
    {
        for (var i = 0; i < _extras.Count; i++)
            if (_extras[i].Root is Border { Child: StackPanel { Children: [Border dot, ..] } })
                dot.Background = new SolidColorBrush(ColumnColor(i + 1));
    }
}
