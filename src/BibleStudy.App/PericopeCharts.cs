using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Diagram;

namespace BibleStudy.App;

/// <summary>The small pictures of the pericope analysis: tiles, ring, bars, verse rhythm, word traces, verb grid and sentence bars. All drawn with plain shapes.</summary>
public static class PericopeCharts
{
    public static readonly string[] Palette = ["#4C78A8", "#F58518", "#54A24B", "#E45756", "#72B7B2", "#B279A2", "#EECA3B", "#9D755D", "#BAB0AC", "#FF9DA6"];

    static readonly Dictionary<string, string> ClassColors = new()
    {
        ["noun"] = "#4C78A8", ["verb"] = "#F58518", ["det"] = "#BAB0AC", ["prep"] = "#72B7B2", ["conj"] = "#54A24B", ["pron"] = "#B279A2",
        ["adj"] = "#EECA3B", ["adv"] = "#E45756", ["ptcl"] = "#9D755D", ["num"] = "#FF9DA6", ["intj"] = "#8F8F8F", ["om"] = "#7F7F7F", ["rel"] = "#C9A0DC", ["other"] = "#8F8F8F",
    };

    public static Color ColorOfClass(string cls) => Color.Parse(ClassColors.GetValueOrDefault(cls, "#8F8F8F"));

    static IBrush Muted => new SolidColorBrush(Colors.Gray, 0.85);
    static IBrush Track => new SolidColorBrush(Colors.Gray, 0.16);

    static TextBlock Caption(string text, double size = 11.5, bool muted = true, TextAlignment align = TextAlignment.Left)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, TextAlignment = align };
        if (muted) t.Foreground = Muted;
        return t;
    }

    // ---- tiles ---------------------------------------------------------------------------------------------------------------

    public static Control Tiles(IEnumerable<(string Value, string Caption)> items)
    {
        var wrap = new WrapPanel();
        foreach (var (value, caption) in items)
        {
            var tile = new Border
            {
                Margin = new(0, 0, 10, 10), Padding = new(14, 8), CornerRadius = new(10), Background = new SolidColorBrush(Colors.Gray, 0.12), MinWidth = 92,
                Child = new StackPanel { Children = { new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeight.Bold }, Caption(caption, 11) } },
            };
            wrap.Children.Add(tile);
        }
        return wrap;
    }

    // ---- ring ----------------------------------------------------------------------------------------------------------------

    /// <summary>A ring chart with its legend on the right. <paramref name="center"/> is written in the hole.</summary>
    public static Control Donut(IReadOnlyList<(string Label, double Value, Color Color)> items, string center)
    {
        const double size = 190, outer = 90, inner = 56, c = size / 2;
        var total = items.Sum(i => i.Value);
        var canvas = new Canvas { Width = size, Height = size };
        double angle = -Math.PI / 2;
        foreach (var (label, value, color) in items)
        {
            if (value <= 0 || total <= 0) continue;
            var sweep = Math.Min(2 * Math.PI * value / total, 2 * Math.PI - 0.0001);
            var a0 = angle; var a1 = angle + sweep; angle = a1;
            Point P(double a, double r) => new(c + r * Math.Cos(a), c + r * Math.Sin(a));
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                var large = sweep > Math.PI;
                ctx.BeginFigure(P(a0, outer), true);
                ctx.ArcTo(P(a1, outer), new Size(outer, outer), 0, large, SweepDirection.Clockwise);
                ctx.LineTo(P(a1, inner));
                ctx.ArcTo(P(a0, inner), new Size(inner, inner), 0, large, SweepDirection.CounterClockwise);
                ctx.EndFigure(true);
            }
            var slice = new Avalonia.Controls.Shapes.Path { Data = geo, Fill = new SolidColorBrush(color), Stroke = Brushes.Transparent, StrokeThickness = 1 };
            ToolTip.SetTip(slice, $"{label}: {value:0} ({100 * value / total:0.#} %)");
            canvas.Children.Add(slice);
        }
        var mid = new TextBlock { Text = center, FontSize = 22, FontWeight = FontWeight.Bold, TextAlignment = TextAlignment.Center, Width = inner * 2 };
        Canvas.SetLeft(mid, c - inner); Canvas.SetTop(mid, c - 15);
        canvas.Children.Add(mid);

        var legend = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new(16, 0, 0, 0) };
        foreach (var (label, value, color) in items.Where(i => i.Value > 0))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            row.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new(3), Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = $"{label}  {value:0}  ({100 * value / Math.Max(1, total):0} %)", FontSize = 12.5 });
            legend.Children.Add(row);
        }
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { canvas, legend } };
    }

    // ---- bars ----------------------------------------------------------------------------------------------------------------

    /// <summary>Horizontal bars with a label on the left and the number on the right; a click on a row calls <paramref name="click"/>.</summary>
    public static Control HBars(IReadOnlyList<(string Label, string? Sub, double Value, Color Color, Action? Click)> rows)
    {
        var max = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(r => r.Value));
        var panel = new StackPanel { Spacing = 5 };
        foreach (var (label, sub, value, color, click) in rows)
        {
            var bar = new Grid { ColumnDefinitions = new($"{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}*,{Math.Max(0.0001, max - value).ToString(System.Globalization.CultureInfo.InvariantCulture)}*"), Height = 14, VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(new Border { Background = new SolidColorBrush(color), CornerRadius = new(3) });
            var track = new Border { Background = Track, CornerRadius = new(3), Child = bar, Height = 14, VerticalAlignment = VerticalAlignment.Center };
            var head = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = label, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis } } };
            if (sub is { Length: > 0 }) head.Children.Add(Caption(sub, 10.5));
            var row = new Grid { ColumnDefinitions = new("150,*,38"), MinHeight = 24 };
            row.Children.Add(head);
            Grid.SetColumn(track, 1); row.Children.Add(track);
            var count = new TextBlock { Text = value.ToString("0"), FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(count, 2); row.Children.Add(count);
            if (click is not null)
            {
                row.Cursor = new Cursor(StandardCursorType.Hand); row.Background = Brushes.Transparent;
                row.PointerPressed += (_, e) => { if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) click(); };
            }
            panel.Children.Add(row);
        }
        return panel;
    }

    // ---- rhythm of the verses ------------------------------------------------------------------------------------------------

    /// <summary>One column per verse, stacked from nouns (blue), verbs (orange) and the rest (gray); a green dot above for each connecting word.</summary>
    public static Control VerseRhythm(IReadOnlyList<VerseRow> rows, Action<int>? click)
    {
        if (rows.Count == 0) return new TextBlock();
        var colW = Math.Clamp(520.0 / rows.Count, 18, 44);
        const double h = 130, top = 16, bottom = 20;
        var max = Math.Max(1, rows.Max(r => r.Words));
        var canvas = new Canvas { Width = colW * rows.Count + 4, Height = h + top + bottom };
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var x = i * colW + colW * 0.12;
            var w = colW * 0.76;
            double y = top + h;
            void Seg(int n, Color color)
            {
                if (n <= 0) return;
                var sh = h * n / max;
                var rect = new Border { Width = w, Height = sh, Background = new SolidColorBrush(color), CornerRadius = new(1.5) };
                ToolTip.SetTip(rect, $"{r.Verse}: {n}");
                Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y - sh);
                canvas.Children.Add(rect); y -= sh;
            }
            Seg(r.Nouns, ColorOfClass("noun"));
            Seg(r.Verbs, ColorOfClass("verb"));
            Seg(r.Others, Color.Parse("#BAB0AC"));
            for (var k = 0; k < Math.Min(r.Connectors, 4); k++)
            {
                var dot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(ColorOfClass("conj")) };
                Canvas.SetLeft(dot, x + w / 2 - 3); Canvas.SetTop(dot, y - 9 - k * 7);
                canvas.Children.Add(dot);
            }
            var label = Caption(r.Verse.ToString(), 10.5, true, TextAlignment.Center); label.Width = colW;
            Canvas.SetLeft(label, i * colW); Canvas.SetTop(label, top + h + 3);
            canvas.Children.Add(label);
            if (click is not null)
            {
                var hit = new Border { Width = colW, Height = h + top + bottom, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
                var v = r.Verse;
                hit.PointerPressed += (_, e) => { if (e.GetCurrentPoint(hit).Properties.IsLeftButtonPressed) click(v); };
                Canvas.SetLeft(hit, i * colW); Canvas.SetTop(hit, 0);
                canvas.Children.Add(hit);
            }
        }
        var maxLabel = Caption(max.ToString(), 10.5); Canvas.SetLeft(maxLabel, 0); Canvas.SetTop(maxLabel, 0);
        canvas.Children.Add(maxLabel);
        return new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    // ---- where the key words occur -------------------------------------------------------------------------------------------

    /// <summary>One row per key word, one column per verse; a dot where the word occurs. Shows repetition, frames and turning points.</summary>
    public static Control Traces(IReadOnlyList<KeywordRow> keywords, int from, int to, Func<KeywordRow, string> label, Action<KeywordRow>? click)
    {
        var verses = Math.Max(1, to - from + 1);
        const double labelW = 150, rowH = 24, pad = 6;
        var colW = Math.Clamp(480.0 / verses, 10, 34);
        var canvas = new Canvas { Width = labelW + colW * verses + 8, Height = keywords.Count * rowH + 24 };
        for (var i = 0; i < keywords.Count; i++)
        {
            var k = keywords[i];
            var color = Color.Parse(Palette[i % Palette.Length]);
            var band = new Border { Width = colW * verses, Height = rowH - 4, Background = i % 2 == 0 ? new SolidColorBrush(Colors.Gray, 0.1) : Brushes.Transparent, CornerRadius = new(4) };
            Canvas.SetLeft(band, labelW); Canvas.SetTop(band, i * rowH + 2);
            canvas.Children.Add(band);
            var name = new TextBlock { Text = label(k), FontSize = 12.5, Width = labelW - 8, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            if (click is not null) { name.Cursor = new Cursor(StandardCursorType.Hand); name.PointerPressed += (_, e) => { if (e.GetCurrentPoint(name).Properties.IsLeftButtonPressed) click(k); }; }
            Canvas.SetLeft(name, 0); Canvas.SetTop(name, i * rowH + 4);
            canvas.Children.Add(name);
            foreach (var v in k.Verses)
            {
                var dot = new Ellipse { Width = 11, Height = 11, Fill = new SolidColorBrush(color) };
                ToolTip.SetTip(dot, $"{k.Lemma} · {v}");
                Canvas.SetLeft(dot, labelW + (v - from) * colW + colW / 2 - 5.5); Canvas.SetTop(dot, i * rowH + (rowH - 11) / 2);
                canvas.Children.Add(dot);
            }
        }
        // the verse numbers below (not every one when there are many)
        var step = verses > 28 ? 5 : verses > 14 ? 2 : 1;
        for (var v = from; v <= to; v++)
        {
            if ((v - from) % step != 0 && v != to) continue;
            var t = Caption(v.ToString(), 10.5, true, TextAlignment.Center); t.Width = colW * step;
            Canvas.SetLeft(t, labelW + (v - from) * colW + colW / 2 - colW * step / 2); Canvas.SetTop(t, keywords.Count * rowH + 4);
            canvas.Children.Add(t);
        }
        _ = pad;
        return new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    // ---- verb grid -----------------------------------------------------------------------------------------------------------

    /// <summary>A grid of rows × columns whose cells are the darker the more often the combination occurs.</summary>
    public static Control Heatmap(IReadOnlyList<(string Row, string Col, int Count)> cells, IReadOnlyList<string> rowOrder, IReadOnlyList<string> colOrder, Func<string, string> name)
    {
        var rows = rowOrder.Where(r => cells.Any(c => c.Row == r)).Concat(cells.Select(c => c.Row).Distinct().Except(rowOrder).OrderBy(x => x)).ToList();
        var cols = colOrder.Where(cc => cells.Any(c => c.Col == cc)).Concat(cells.Select(c => c.Col).Distinct().Except(colOrder).OrderBy(x => x)).ToList();
        if (rows.Count == 0 || cols.Count == 0) return new TextBlock();
        var max = Math.Max(1, cells.Max(c => c.Count));
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        foreach (var _ in cols) grid.ColumnDefinitions.Add(new ColumnDefinition(64, GridUnitType.Pixel));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var _ in rows) grid.RowDefinitions.Add(new RowDefinition(30, GridUnitType.Pixel));
        for (var j = 0; j < cols.Count; j++)
        {
            var h = Caption(name(cols[j]), 11, true, TextAlignment.Center); h.TextWrapping = TextWrapping.NoWrap; h.TextTrimming = TextTrimming.CharacterEllipsis; h.Margin = new(0, 0, 0, 4);
            Grid.SetColumn(h, j + 1); grid.Children.Add(h);
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var rl = new TextBlock { Text = name(rows[i]), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0) };
            Grid.SetRow(rl, i + 1); grid.Children.Add(rl);
            for (var j = 0; j < cols.Count; j++)
            {
                var n = cells.Where(c => c.Row == rows[i] && c.Col == cols[j]).Sum(c => c.Count);
                var cell = new Border { Margin = new(2), CornerRadius = new(6), Background = n == 0 ? new SolidColorBrush(Colors.Gray, 0.07) : new SolidColorBrush(Color.Parse("#F58518"), 0.18 + 0.82 * n / max) };
                if (n > 0) cell.Child = new TextBlock { Text = n.ToString(), FontSize = 12.5, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = n * 2 > max ? Brushes.White : null };
                ToolTip.SetTip(cell, $"{name(rows[i])} · {name(cols[j])}: {n}");
                Grid.SetRow(cell, i + 1); Grid.SetColumn(cell, j + 1); grid.Children.Add(cell);
            }
        }
        return new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    // ---- sentences -----------------------------------------------------------------------------------------------------------

    /// <summary>One column per sentence: the height is its length in words; the number above is its count of clauses. A click opens the sentence diagram.</summary>
    public static Control SentenceBars(IReadOnlyList<SentenceRow> rows, Func<SentenceRow, string> verses, Action<SentenceRow>? click)
    {
        if (rows.Count == 0) return new TextBlock();
        var colW = Math.Clamp(520.0 / rows.Count, 26, 52);
        const double h = 120, top = 18, bottom = 34;
        var max = Math.Max(1, rows.Max(r => r.Words));
        var canvas = new Canvas { Width = colW * rows.Count + 4, Height = h + top + bottom };
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var sh = Math.Max(3, h * r.Words / max);
            var bar = new Border { Width = colW * 0.72, Height = sh, CornerRadius = new(3), Background = new SolidColorBrush(Color.Parse(r.SubClauses > 0 ? "#4C78A8" : "#72B7B2")) };
            ToolTip.SetTip(bar, $"{verses(r)} · {r.Words} / {r.Clauses}");
            Canvas.SetLeft(bar, i * colW + colW * 0.14); Canvas.SetTop(bar, top + h - sh);
            if (click is not null) { bar.Cursor = new Cursor(StandardCursorType.Hand); bar.PointerPressed += (_, e) => { if (e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) click(r); }; }
            canvas.Children.Add(bar);
            var clauses = Caption($"{r.Clauses}", 10.5, false, TextAlignment.Center); clauses.Width = colW;
            Canvas.SetLeft(clauses, i * colW); Canvas.SetTop(clauses, top + h - sh - 17);
            canvas.Children.Add(clauses);
            var label = Caption(verses(r), 10, true, TextAlignment.Center); label.Width = colW; label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(label, i * colW); Canvas.SetTop(label, top + h + 4);
            canvas.Children.Add(label);
        }
        return new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
}
