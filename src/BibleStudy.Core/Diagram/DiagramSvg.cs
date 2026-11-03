using System.Globalization;
using System.Net;
using System.Text;

namespace BibleStudy.Core.Diagram;

/// <summary>Writes a laid-out sentence diagram as an SVG picture (a vector graphic that stays sharp at any size, for slides and handouts).</summary>
public static class DiagramSvg
{
    static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    public static string ColorOf(DTextKind kind, bool dark) => kind switch
    {
        DTextKind.Subject => dark ? "#6fcf97" : "#1f7a45",
        DTextKind.Verb => dark ? "#ff8a80" : "#b3261e",
        DTextKind.Object => dark ? "#7fb2ff" : "#1d5fb4",
        DTextKind.Complement => dark ? "#c9a3ee" : "#7b3fa8",
        DTextKind.Prep => dark ? "#f0b45a" : "#a85f00",
        DTextKind.Conj => dark ? "#a9a9a9" : "#6f6f6f",
        DTextKind.Implied or DTextKind.Gloss => dark ? "#9a9a9a" : "#7c7c7c",
        _ => dark ? "#e4e4e4" : "#2b2b2b",
    };

    public static string Write(DScene scene, bool dark = false, string title = "")
    {
        var ink = dark ? "#d6d6d6" : "#2b2b2b";
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(scene.Width)}\" height=\"{N(scene.Height)}\" viewBox=\"0 0 {N(scene.Width)} {N(scene.Height)}\">");
        if (title.Length > 0) sb.Append($"<title>{WebUtility.HtmlEncode(title)}</title>");
        sb.Append($"<rect width=\"100%\" height=\"100%\" fill=\"{(dark ? "#171a20" : "#ffffff")}\"/>");
        sb.Append($"<g stroke=\"{ink}\" stroke-linecap=\"round\" fill=\"none\">");
        foreach (var l in scene.Lines)
        {
            var style = l.Style switch
            {
                DLineStyle.Baseline or DLineStyle.Divider => "stroke-width=\"2.2\"",
                DLineStyle.Stilt => "stroke-width=\"2\"",
                DLineStyle.Dashed => "stroke-width=\"1.4\" stroke-dasharray=\"5 4\"",
                _ => "stroke-width=\"1.7\"",
            };
            sb.Append($"<line x1=\"{N(l.X1)}\" y1=\"{N(l.Y1)}\" x2=\"{N(l.X2)}\" y2=\"{N(l.Y2)}\" {style}/>");
        }
        sb.Append("</g><g font-family=\"'Gentium Plus','Gentium Basic','SBL Greek','SBL Hebrew','Ezra SIL','Cardo','Times New Roman',serif\">");
        foreach (var t in scene.Texts)
        {
            var anchor = t.Anchor switch { DAnchor.Left => "start", DAnchor.Right => "end", _ => "middle" };
            var rotate = t.Angle != 0 ? $" transform=\"rotate({N(t.Angle)} {N(t.X)} {N(t.Y)})\"" : "";
            var weight = t.Kind is DTextKind.Verb or DTextKind.Subject ? " font-weight=\"600\"" : "";
            var style = t.Kind is DTextKind.Gloss or DTextKind.Implied ? " font-style=\"italic\"" : "";
            sb.Append($"<text x=\"{N(t.X)}\" y=\"{N(t.Y)}\" font-size=\"{N(t.Size)}\" text-anchor=\"{anchor}\" fill=\"{ColorOf(t.Kind, dark)}\"{weight}{style}{rotate}>{WebUtility.HtmlEncode(t.Text)}</text>");
        }
        sb.Append("</g></svg>");
        return sb.ToString();
    }
}
