using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace BibleStudy.App;

/// <summary>
/// One answer of the AI. While it streams in, it is plain text; when it is complete, the little Markdown the AI writes
/// (headings, lists, quotes, **bold**, *italic*, `code`) is shown formatted. It stays one selectable block, so the user can
/// select across the whole answer, and <see cref="Markdown"/> keeps the original text for saving and copying.
/// </summary>
public sealed class AiAnswer : SelectableTextBlock
{
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public string Markdown { get; private set; } = "";

    public void Append(string text) { Markdown += text; Text = Markdown; }

    public void SetText(string text) { Markdown = text; Text = text; }

    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    static readonly Regex Heading = new(@"^(#{1,4})\s+(.*)$", RegexOptions.Compiled);
    static readonly Regex ListItem = new(@"^(\s*)([-*•]|\d+[.)])\s+(.*)$", RegexOptions.Compiled);
    static readonly Regex Rule = new(@"^\s*(-{3,}|\*{3,}|_{3,})\s*$", RegexOptions.Compiled);

    /// <summary>Shows the finished answer formatted.</summary>
    public void Render()
    {
        var md = Markdown.Replace("\r\n", "\n").Trim('\n');
        if (md.Length == 0) return;
        var inlines = new InlineCollection();
        var lines = md.Split('\n');
        var inCode = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i > 0) inlines.Add(new LineBreak());
            if (line.TrimStart().StartsWith("```")) { inCode = !inCode; continue; }
            if (inCode) { inlines.Add(new Run(line) { FontFamily = Mono, FontSize = FontSize * 0.92 }); continue; }
            if (Heading.Match(line) is { Success: true } h)
            {
                AddInline(inlines, h.Groups[2].Value, FontWeight.Bold, h.Groups[1].Length <= 2 ? FontSize * 1.18 : FontSize * 1.06);
                continue;
            }
            if (Rule.IsMatch(line)) { inlines.Add(new Run("────────") { Foreground = Brushes.Gray }); continue; }
            if (ListItem.Match(line) is { Success: true } li)
            {
                var indent = new string(' ', li.Groups[1].Length / 2 * 4);
                var marker = li.Groups[2].Value is "-" or "*" or "•" ? "•" : li.Groups[2].Value;
                inlines.Add(new Run($"{indent}  {marker}  "));
                AddInline(inlines, li.Groups[3].Value, null, null);
                continue;
            }
            if (line.StartsWith('>'))
            {
                inlines.Add(new Run("▍ ") { Foreground = Brushes.Gray });
                var start = inlines.Count;
                AddInline(inlines, line.TrimStart('>').Trim(), null, null);
                for (var k = start; k < inlines.Count; k++) if (inlines[k] is Run r) r.FontStyle = FontStyle.Italic;
                continue;
            }
            AddInline(inlines, line, null, null);
        }
        Text = null;
        Inlines = inlines;
    }

    /// <summary>One line with **bold**, *italic* and `code` inside.</summary>
    void AddInline(InlineCollection inlines, string text, FontWeight? weight, double? size)
    {
        Run Plain(string s)
        {
            var r = new Run(s);
            if (weight is { } w) r.FontWeight = w;
            if (size is { } z) r.FontSize = z;
            return r;
        }
        var pos = 0;
        foreach (Match m in MarkdownView.Inline.Matches(text))
        {
            if (m.Index > pos) inlines.Add(Plain(text[pos..m.Index]));
            if (m.Groups["b"].Success) { var r = Plain(m.Groups["b"].Value); r.FontWeight = FontWeight.Bold; inlines.Add(r); }
            else if (m.Groups["i"].Success) { var r = Plain(m.Groups["i"].Value); r.FontStyle = FontStyle.Italic; inlines.Add(r); }
            else if (m.Groups["c"].Success) inlines.Add(new Run(m.Groups["c"].Value) { FontFamily = Mono, Background = new SolidColorBrush(Colors.Gray, 0.18) });
            else inlines.Add(Plain(m.Groups["t"].Value));   // a link: only its text, the AI's links lead nowhere in the app
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) inlines.Add(Plain(text[pos..]));
    }
}
