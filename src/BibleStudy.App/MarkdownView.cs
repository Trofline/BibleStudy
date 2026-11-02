using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>
/// A small Markdown reader for the handbook: headings, paragraphs, lists, quotes, code blocks, tables, rules,
/// and **bold**, *italic*, `code` and [links](target) inside the text. Nothing else is needed, so no extra package.
/// </summary>
public sealed class MarkdownView
{
    /// <summary>The blocks in order, each with its plain text (for searching) and, for headings, its level.</summary>
    public sealed record Block(Control Control, string Text, int Heading);

    readonly Action<string> _openLink;
    readonly Func<string, IBrush?> _brush;

    public MarkdownView(Action<string> openLink, Func<string, IBrush?> brush) { _openLink = openLink; _brush = brush; }

    static readonly Regex Inline = new(@"\*\*(?<b>.+?)\*\*|`(?<c>[^`]+)`|\[(?<t>[^\]]+)\]\((?<u>[^)\s]+)\)|(?<![*\w])\*(?<i>[^*\s][^*]*?)\*(?![*\w])", RegexOptions.Compiled);

    public List<Block> Render(string markdown)
    {
        var blocks = new List<Block>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length;)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            if (line.StartsWith("```"))
            {
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].StartsWith("```"); i++) code.Add(lines[i]);
                i++;
                var text = string.Join("\n", code);
                blocks.Add(new(CodeBox(text), text, 0));
                continue;
            }
            var h = Regex.Match(line, @"^(#{1,4})\s+(.*)$");
            if (h.Success)
            {
                var level = h.Groups[1].Length;
                var tb = Paragraph(h.Groups[2].Value, level switch { 1 => 28, 2 => 21, 3 => 17, _ => 15 }, true);
                tb.Margin = new(0, level == 1 ? 4 : level == 2 ? 22 : 14, 0, 4);
                blocks.Add(new(tb, StripInline(h.Groups[2].Value), level));
                i++; continue;
            }
            if (Regex.IsMatch(line, @"^-{3,}\s*$")) { blocks.Add(new(new Border { Height = 1, Margin = new(0, 10), Background = _brush("BsMuted"), Opacity = 0.4 }, "", 0)); i++; continue; }
            if (line.TrimStart().StartsWith('|') && i + 1 < lines.Length && Regex.IsMatch(lines[i + 1], @"^\s*\|[\s:|-]+\|\s*$"))
            {
                var rows = new List<string[]>();
                rows.Add(Cells(line));
                for (i += 2; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++) rows.Add(Cells(lines[i]));
                blocks.Add(new(Table(rows), string.Join(" ", rows.SelectMany(r => r.Select(StripInline))), 0));
                continue;
            }
            if (line.StartsWith('>'))
            {
                var quote = new List<string>();
                for (; i < lines.Length && lines[i].StartsWith('>'); i++) quote.Add(lines[i].TrimStart('>').Trim());
                var q = string.Join(" ", quote);
                var inner = Paragraph(q, 14.5, false);
                blocks.Add(new(new Border { Child = inner, BorderBrush = _brush("BsAccent"), BorderThickness = new(3, 0, 0, 0), Padding = new(12, 6), Margin = new(0, 6), Background = new SolidColorBrush(Colors.Gray, 0.1) }, StripInline(q), 0));
                continue;
            }
            if (Regex.IsMatch(line, @"^\s*([-*]|\d+\.)\s+"))
            {
                var panel = new StackPanel { Spacing = 3, Margin = new(0, 4) };
                var plain = new List<string>();
                for (; i < lines.Length && Regex.IsMatch(lines[i], @"^\s*([-*]|\d+\.)\s+"); i++)
                {
                    var m = Regex.Match(lines[i], @"^(\s*)([-*]|\d+\.)\s+(.*)$");
                    var indent = m.Groups[1].Length / 2 * 18;
                    var marker = m.Groups[2].Value is "-" or "*" ? "•" : m.Groups[2].Value;
                    var row = new Grid { ColumnDefinitions = new("24,*"), Margin = new(indent, 0, 0, 0) };
                    row.Children.Add(new TextBlock { Text = marker, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 0, 8, 0) });
                    var body = Paragraph(m.Groups[3].Value, 14.5, false);
                    Grid.SetColumn(body, 1); row.Children.Add(body);
                    panel.Children.Add(row);
                    plain.Add(StripInline(m.Groups[3].Value));
                }
                blocks.Add(new(panel, string.Join(" ", plain), 0));
                continue;
            }
            var para = new List<string>();
            for (; i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !lines[i].StartsWith("```") && !Regex.IsMatch(lines[i], @"^(#{1,4}\s|>|\s*([-*]|\d+\.)\s+|\s*\|)"); i++) para.Add(lines[i].Trim());
            if (para.Count == 0) { i++; continue; }
            var joined = string.Join(" ", para);
            var p = Paragraph(joined, 14.5, false);
            p.Margin = new(0, 4);
            blocks.Add(new(p, StripInline(joined), 0));
        }
        return blocks;
    }

    static string[] Cells(string row) => row.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

    static string StripInline(string s) => Inline.Replace(s, m => m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["c"].Success ? m.Groups["c"].Value : m.Groups["t"].Success ? m.Groups["t"].Value : m.Groups["i"].Value);

    Control CodeBox(string text) => new Border
    {
        Background = new SolidColorBrush(Colors.Gray, 0.15), CornerRadius = new(6), Padding = new(12, 8), Margin = new(0, 6),
        Child = new SelectableTextBlock { Text = text, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 13, TextWrapping = TextWrapping.Wrap },
    };

    Control Table(List<string[]> rows)
    {
        var cols = rows.Max(r => r.Length);
        var grid = new Grid { Margin = new(0, 6) };
        for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(c == cols - 1 ? 2 : 1, GridUnitType.Star));
        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var c = 0; c < cols; c++)
            {
                var cell = Paragraph(c < rows[r].Length ? rows[r][c] : "", 13.5, r == 0);
                var box = new Border
                {
                    Child = cell, Padding = new(8, 4), BorderBrush = _brush("BsMuted"), BorderThickness = new(0, 0, 0, r == 0 ? 2 : 0.5),
                    Background = r == 0 ? new SolidColorBrush(Colors.Gray, 0.12) : null,
                };
                Grid.SetRow(box, r); Grid.SetColumn(box, c);
                grid.Children.Add(box);
            }
        }
        return grid;
    }

    TextBlock Paragraph(string text, double size, bool bold)
    {
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size, LineHeight = size * 1.5 };
        if (bold) tb.FontWeight = FontWeight.SemiBold;
        var pos = 0;
        foreach (Match m in Inline.Matches(text))
        {
            if (m.Index > pos) tb.Inlines!.Add(new Run(text[pos..m.Index]));
            if (m.Groups["b"].Success) tb.Inlines!.Add(new Run(m.Groups["b"].Value) { FontWeight = FontWeight.Bold });
            else if (m.Groups["i"].Success) tb.Inlines!.Add(new Run(m.Groups["i"].Value) { FontStyle = FontStyle.Italic });
            else if (m.Groups["c"].Success) tb.Inlines!.Add(new Run(m.Groups["c"].Value) { FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), Background = new SolidColorBrush(Colors.Gray, 0.18) });
            else
            {
                var target = m.Groups["u"].Value;
                var link = new Button { Classes = { "ghost" }, Padding = new(0), Background = Brushes.Transparent, BorderThickness = new(0), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Bottom,
                    Content = new TextBlock { Text = m.Groups["t"].Value, TextDecorations = TextDecorations.Underline, Foreground = _brush("BsAccent"), FontSize = size } };
                link.Click += (_, _) => _openLink(target);
                tb.Inlines!.Add(new InlineUIContainer(link));
            }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) tb.Inlines!.Add(new Run(text[pos..]));
        return tb;
    }
}
