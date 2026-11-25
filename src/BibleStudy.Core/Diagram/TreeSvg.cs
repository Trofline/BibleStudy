using System.Globalization;
using System.Net;
using System.Text;

namespace BibleStudy.Core.Diagram;

/// <summary>One box of the syntax tree: where it stands, its role, the words and their meaning. Color is the frame of a role ("#4C9F70"), or null.</summary>
public sealed record TreeBox(double X, double Y, double Width, double Height, string Label, string Text, string Gloss, bool Clause, string? Color, bool RightToLeft);

/// <summary>Writes the syntax tree (boxes joined by curves from a parent to its children) as an SVG picture, sharp at any size.</summary>
public static class TreeSvg
{
    static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    static string E(string s) => WebUtility.HtmlEncode(s);

    /// <summary>Shortens a text to what fits into <paramref name="width"/> at <paramref name="fontSize"/> (roughly: half the font size per letter).</summary>
    public static string Fit(string text, double width, double fontSize)
    {
        var max = Math.Max(3, (int)(width / (fontSize * 0.55)));
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
    }

    public static string Write(IReadOnlyList<TreeBox> boxes, IEnumerable<(int Parent, int Child)> edges, double width, double height, bool dark = false, string title = "")
    {
        var bg = dark ? "#0F1216" : "#F6F1E6";
        var surface = dark ? "#161A20" : "#FFFCF6";
        var clauseFill = dark ? "#3A3120" : "#F0DFBF";
        var accent = dark ? "#E3B65F" : "#8A4F0F";
        var border = dark ? "#262D37" : "#E2D9C5";
        var ink = dark ? "#ECE7DA" : "#24201A";
        var muted = dark ? "#9AA2AD" : "#6B6354";
        var line = dark ? "#3A4350" : "#CFC4AE";

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(width)}\" height=\"{N(height)}\" viewBox=\"0 0 {N(width)} {N(height)}\">");
        if (title.Length > 0) sb.Append($"<title>{E(title)}</title>");
        sb.Append($"<rect width=\"100%\" height=\"100%\" fill=\"{bg}\"/>");
        sb.Append($"<g fill=\"none\" stroke=\"{line}\" stroke-width=\"1.6\">");
        foreach (var (p, c) in edges)
        {
            if (p < 0 || c < 0 || p >= boxes.Count || c >= boxes.Count) continue;
            var a = boxes[p]; var b = boxes[c];
            double ax = a.X + a.Width / 2, ay = a.Y + a.Height, bx = b.X + b.Width / 2, by = b.Y, mid = (ay + by) / 2;
            sb.Append($"<path d=\"M{N(ax)} {N(ay)} C{N(ax)} {N(mid)} {N(bx)} {N(mid)} {N(bx)} {N(by)}\"/>");
        }
        sb.Append("</g>");
        sb.Append("<g font-family=\"'Segoe UI',system-ui,sans-serif\" text-anchor=\"middle\">");
        foreach (var x in boxes)
        {
            var stroke = x.Clause ? accent : x.Color ?? border;
            var strokeWidth = x.Clause || x.Color is not null ? 2 : 1;
            sb.Append($"<rect x=\"{N(x.X)}\" y=\"{N(x.Y)}\" width=\"{N(x.Width)}\" height=\"{N(x.Height)}\" rx=\"10\" fill=\"{(x.Clause ? clauseFill : surface)}\" stroke=\"{stroke}\" stroke-width=\"{strokeWidth}\"/>");
            var rows = new List<(string Text, double Size, string Color, bool Bold, bool Script)>();
            if (x.Label.Length > 0) rows.Add((x.Label, 10.5, ink, true, false));
            if (x.Text.Length > 0) rows.Add((x.Text, x.RightToLeft ? 16 : 14, ink, false, true));
            if (x.Text.Length > 0 && x.Gloss.Length > 0) rows.Add((x.Gloss, 10, muted, false, false));
            var total = rows.Sum(r => r.Size * 1.25);
            var y = x.Y + (x.Height - total) / 2;
            var cx = x.X + x.Width / 2;
            foreach (var (text, size, color, bold, script) in rows)
            {
                y += size * 1.25;
                var family = script ? (x.RightToLeft ? " font-family=\"'SBL Hebrew','Ezra SIL','Times New Roman',serif\" direction=\"rtl\" unicode-bidi=\"embed\"" : " font-family=\"'Gentium Plus','Times New Roman',serif\"") : "";
                sb.Append($"<text x=\"{N(cx)}\" y=\"{N(y - size * 0.3)}\" font-size=\"{N(size)}\" fill=\"{color}\"{(bold ? " font-weight=\"600\"" : "")}{family}>{E(Fit(text, x.Width - 8, size))}</text>");
            }
        }
        sb.Append("</g></svg>");
        return sb.ToString();
    }
}
