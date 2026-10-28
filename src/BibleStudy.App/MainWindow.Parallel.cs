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
    Border? _syncPill;

    Color ColumnColor(int i) => i == 0 ? (Res("BsAccent") as ISolidColorBrush)?.Color ?? Colors.Goldenrod : Color.Parse(ColumnColors[i % ColumnColors.Length]);

    Border ColumnHeader(TranslationInfo t, Chapter? c, int i)
    {
        var color = ColumnColor(i);
        var name = new TextBlock { Text = t.ShortName + (c is null ? L(" (Kapitel nicht verfügbar)") : ""), FontWeight = FontWeight.Bold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
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

    Border MakeVerseBox(Control content, int n)
    {
        var box = new Border { Child = content, Padding = new(10, 3), CornerRadius = new(8), BorderThickness = new(3, 0, 0, 0), BorderBrush = Brushes.Transparent, Classes = { "verse" } };
        box.AddHandler(PointerPressedEvent, (object? _, PointerPressedEventArgs e) => OnVersePressed(n, e), RoutingStrategies.Tunnel, handledEventsToo: true);
        box.AddHandler(PointerReleasedEvent, (object? _, PointerReleasedEventArgs e) => CaptureSelection(e), RoutingStrategies.Tunnel, handledEventsToo: true);
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
            foreach (var v in cols[i].C?.Verses ?? [])
            {
                var num = v.Number;
                var cell = VerseCell(v, num, cols[i].T, size, i == 0 && noted.Contains(num), notes[i],
                    _chapterMarks.Where(m => m.Ref.Verse == num && !m.WholeVerse && m.TranslationId == cols[i].T.Id).ToList());
                var box = MakeVerseBox(i == 0 && XrefChips(num, primary.Id) is { } chips ? new StackPanel { Children = { cell, chips } } : cell, num);
                boxes[num] = box; panel.Children.Add(box);
            }
            var scroll = new ScrollViewer { Content = panel };
            _colScrolls.Add(scroll);
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

    /// <summary>The bar above the text: sync switch plus one colored dot per column.</summary>
    void EnsureSyncPill()
    {
        if (_syncPill is not null) return;
        var check = new CheckBox { Content = L("Synchron scrollen"), IsChecked = _settings.ParallelSync, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(check, L("An: alle Spalten laufen Vers für Vers nebeneinander und scrollen gemeinsam. Aus: jede Spalte scrollt für sich."));
        check.IsCheckedChanged += async (_, _) =>
        {
            _settings.ParallelSync = check.IsChecked == true;
            await ShowChapterAsync(_selected);
        };
        _syncPill = new Border { Classes = { "pill" }, Margin = new(16, 8, 0, 8), Padding = new(12, 0), Child = check };
    }

    void RecolorPills()
    {
        for (var i = 0; i < _extras.Count; i++)
            if (_extras[i].Root is Border { Child: StackPanel { Children: [Border dot, ..] } })
                dot.Background = new SolidColorBrush(ColumnColor(i + 1));
    }
}
