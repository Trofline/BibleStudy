using System.Text;
using System.Text.RegularExpressions;
using BibleStudy.Core.Models;

namespace BibleStudy.Core.Import;

/// <summary>
/// Words of Jesus ("red letters") that an imported file marks itself: OSIS &lt;q who="Jesus"&gt;, USFM \wj, USX &lt;char style="wj"&gt;,
/// red text in Zefania, e-Sword, MySword, web pages, EPUB and Word. The readers put <see cref="JesusOpen"/> … <see cref="JesusClose"/>
/// around such text; turning the verse into segments makes <see cref="Segment.WordsOfJesus"/> of it.
/// </summary>
public static partial class BibleImporters
{
    /// <summary>Marks red-letter text inside the raw text of a verse while it is read (private-use characters, never part of a Bible text).</summary>
    public const char JesusOpen = '', JesusClose = '';

    /// <summary>
    /// The raw text of a verse (with Jesus and footnote markers) as segments; <paramref name="note"/> gives the id of a footnote letter, or null to leave it out.
    /// <paramref name="jesus"/> says whether red-letter text is open when the verse begins (a quote may go on over several verses) and is open when it ends.
    /// </summary>
    static List<Segment> MarkedSegments(string raw, ref bool jesus, Func<string, int?>? note = null)
    {
        var segments = new List<Segment>();
        var on = jesus;
        var text = new StringBuilder();
        void Put() { if (text.Length > 0) segments.Add(new Segment(text.ToString(), on)); text.Clear(); }
        for (var i = 0; i < raw.Length; i++)
        {
            var ch = raw[i];
            if (ch == JesusOpen || ch == JesusClose) { if (on != (ch == JesusOpen)) { Put(); on = ch == JesusOpen; } continue; }
            if (ch == PdfBibleLayout.NoteOpen)
            {
                var close = raw.IndexOf(PdfBibleLayout.NoteClose, i + 1);
                if (close > i)
                {
                    Put();
                    if (note?.Invoke(raw[(i + 1)..close]) is { } id) segments.Add(new Segment("", NoteId: id));
                    i = close;
                    continue;
                }
            }
            text.Append(ch);
        }
        Put();
        jesus = on;
        return Normalize(segments);
    }

    /// <summary>True for a CSS or Word color that reads as red (red, #f00, #c00000, #e01b24, rgb(200,0,0) …), the color of red-letter editions.</summary>
    public static bool IsRedColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim().ToLowerInvariant();
        if (v is "red" or "darkred" or "crimson" or "firebrick" or "maroon") return true;
        int r, g, b;
        var rgb = RgbColor().Match(v);
        if (rgb.Success) { r = int.Parse(rgb.Groups[1].Value); g = int.Parse(rgb.Groups[2].Value); b = int.Parse(rgb.Groups[3].Value); }
        else
        {
            var hex = v.TrimStart('#');
            if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
            if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var n)) return false;
            r = n >> 16 & 0xFF; g = n >> 8 & 0xFF; b = n & 0xFF;
        }
        return r >= 0x99 && g <= r * 0.45 && b <= r * 0.45;
    }

    /// <summary>The color value in a style attribute ("color: #c00; font-weight: bold" → "#c00"); null when it sets none.</summary>
    static string? CssColor(string? style) => style is null ? null : CssColorProperty().Match(style) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>True for an HTML start tag that marks words of Jesus: a class such as "wj", "woj", "jesus", "red", "redletter", or a red color.</summary>
    static bool IsJesusTag(string tag)
    {
        var cls = HtmlAttribute("class", tag);
        if (cls is not null && cls.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(c => JesusClass().IsMatch(c))) return true;
        return IsRedColor(CssColor(HtmlAttribute("style", tag))) || IsRedColor(HtmlAttribute("color", tag));
    }

    static string? HtmlAttribute(string name, string tag)
    {
        var m = Regex.Match(tag, $@"\b{name}\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["v"].Value : null;
    }

    /// <summary>Puts the Jesus markers around the content of every span, font, q or i element that marks words of Jesus (nested elements are counted).</summary>
    public static string MarkJesusInHtml(string html)
    {
        if (!JesusHint().IsMatch(html)) return html;
        var sb = new StringBuilder(html.Length + 64);
        var stack = new Stack<(string Name, bool Jesus)>();
        var pos = 0;
        foreach (Match t in HtmlInlineTag().Matches(html))
        {
            sb.Append(html, pos, t.Index - pos);
            pos = t.Index + t.Length;
            var name = t.Groups["n"].Value.ToLowerInvariant();
            if (t.Groups["c"].Success)
            {
                // the end of the innermost open element of that name
                if (stack.Any(e => e.Name == name))
                {
                    while (stack.Count > 0)
                    {
                        var e = stack.Pop();
                        if (e.Jesus) sb.Append(JesusClose);
                        if (e.Name == name) break;
                    }
                }
                sb.Append(t.Value);
                continue;
            }
            sb.Append(t.Value);
            if (t.Value.EndsWith("/>", StringComparison.Ordinal)) continue;
            var jesus = IsJesusTag(t.Value);
            stack.Push((name, jesus));
            if (jesus) sb.Append(JesusOpen);
        }
        sb.Append(html, pos, html.Length - pos);
        return sb.ToString();
    }

    [GeneratedRegex(@"rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})")] private static partial Regex RgbColor();
    [GeneratedRegex(@"(?:^|;)\s*color\s*:\s*([^;""']+)", RegexOptions.IgnoreCase)] private static partial Regex CssColorProperty();
    [GeneratedRegex(@"^(?:wj|woj|jesus|words-?of-?jesus|words-?of-?christ|christ|red|redletter|red-letter|rl)$", RegexOptions.IgnoreCase)] private static partial Regex JesusClass();
    [GeneratedRegex(@"wj|woj|jesus|christ|red|color", RegexOptions.IgnoreCase)] private static partial Regex JesusHint();
    [GeneratedRegex(@"<(?<c>/)?(?<n>span|font|q|i|em|b|strong)\b[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex HtmlInlineTag();
}
