using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// A document printed as a handout for a Bible study: a designed head (group, title, date), clean A4 pages with page numbers, small numbers
/// at the Bible references that lead to the verses at the end (on paper a link cannot be clicked), the verses as cards (in two columns if
/// wished), questions for the talk set apart, and lined space for notes. Three looks: classic, modern and plain (saves ink).
/// It is a web page the browser prints or saves as PDF; the same page saved as HTML keeps its links.
/// </summary>
public partial class MainWindow
{
    static readonly string[] HandoutStyles = ["classic", "modern", "plain", "elegant", "community", "newspaper", "large", "nature"];

    /// <summary>The whole page of the handout. With <paramref name="print"/> the print dialog comes up at once.</summary>
    async Task<string> HandoutHtmlAsync(string title, string body, string tags, long selfId, List<Document> linked, bool print)
    {
        var s = _settings;
        var verses = s.HandoutAppendix || s.HandoutNumbers ? await ExportVersesAsync(linked.Select(d => d.Body).Prepend(body)) : [];
        var pages = linked.Prepend(new Document(selfId, title, body, null, false, DateTime.Now, [])).ToList();
        string? Href(long? number, string t) => VaultStore.Resolve(pages, new DocumentLinks.DocLink(number, t, "", 0, 0)) is { } d ? $"#doc-{pages.IndexOf(d)}" : null;
        var byVerse = verses.ToDictionary(v => v.Span, v => "#" + v.Anchor);
        string? VerseHref(string span) => byVerse.TryGetValue(span, out var h) ? h : null;
        string Part(Document d) => PolishHandout(MarkdownToHtml(DocumentLinks.WithoutTitleHeading(d.Body, d.Title), Href, VerseHref));

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"").Append(Loc.Code).Append("\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\">")
          .Append("<title>").Append(H(title.Length > 0 ? title : L("Dokument"))).Append("</title><style>").Append(HandoutCss(title)).Append("</style>");
        // on the screen: a button to print, and what to set in the print dialog; nothing of it is printed
        sb.Append("<div class=\"noprint bar\"><button onclick=\"print()\">").Append(H(L("Drucken / Als PDF speichern"))).Append("</button><span>")
          .Append(H(L("Tipp: Im Druckdialog „Kopf- und Fußzeilen“ ausschalten; dann stehen nur die Seitenzahlen unten."))).Append("</span></div>");
        sb.Append("<article class=\"page\">");
        // the head
        sb.Append("<header class=\"head\">");
        if (s.HandoutGroup.Trim().Length > 0) sb.Append("<div class=\"group\">").Append(H(s.HandoutGroup.Trim())).Append("</div>");
        sb.Append("<h1 class=\"title\" id=\"doc-0\">").Append(H(title)).Append("</h1>");
        var sub = string.Join("  ·  ", new[] { DateTime.Now.ToString("D", Loc.Culture), HandoutTags(tags) }.Where(x => x.Length > 0));
        sb.Append("<div class=\"sub\">").Append(H(sub)).Append("</div><div class=\"rule\"><span></span></div></header>");
        sb.Append("<main>").Append(Part(pages[0])).Append("</main>");
        for (var i = 0; i < linked.Count; i++)
            sb.Append("<div class=\"divider\"><span></span></div><section class=\"linked\" id=\"doc-").Append(i + 1).Append("\"><h2 class=\"doc\">").Append(H(linked[i].Title)).Append("</h2>")
              .Append(Part(linked[i])).Append("</section>");
        if (s.HandoutAppendix && verses.Count > 0)
        {
            sb.Append("<section class=\"appendix\"><h2 class=\"apx\">").Append(H(L("Bibelstellen"))).Append("</h2><div class=\"cards").Append(s.HandoutTwoColumns ? " two" : "").Append("\">");
            for (var i = 0; i < verses.Count; i++) sb.Append(VerseCard(verses[i], i + 1));
            sb.Append("</div></section>");
        }
        if (s.HandoutNoteLines > 0)
        {
            sb.Append("<section class=\"notes\"><h2>").Append(H(L("Notizen"))).Append("</h2>");
            for (var i = 0; i < s.HandoutNoteLines; i++) sb.Append("<div class=\"ln\"></div>");
            sb.Append("</section>");
        }
        sb.Append("</article>");
        if (print) sb.Append("<script>addEventListener('load',()=>setTimeout(()=>print(),400))</script>");
        return sb.Append("</html>").ToString();
    }

    /// <summary>The tags of the document as they are meant ("kat:Predigt" → "Predigt").</summary>
    static string HandoutTags(string tags) =>
        string.Join(", ", tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t => t.StartsWith("kat:", StringComparison.OrdinalIgnoreCase) ? t[4..] : t.TrimStart('#')));

    /// <summary>One verse of the appendix as a card: its number, the reference, the translation, the text.</summary>
    static string VerseCard(ExportVerse v, int n)
    {
        var texts = new List<string>(); var translation = "";
        foreach (var raw in v.Quote.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimStart('>', ' ').TrimEnd();
            if (line.Length == 0) continue;
            if (line.StartsWith("— ") || line.StartsWith("- "))
            {
                var m = Regex.Match(line, @"\(([^()]*)\)\s*$");
                if (m.Success && translation.Length == 0) translation = m.Groups[1].Value;
                continue;
            }
            texts.Add(line);
        }
        return $"<div class=\"card\" id=\"{H(v.Anchor)}\"><div class=\"card-head\"><span class=\"num\">{n}</span><span class=\"ref\">{H(v.Span)}</span>"
             + (translation.Length > 0 ? $"<span class=\"tr\">{H(translation)}</span>" : "") + "</div>"
             + string.Concat(texts.Select(t => $"<p>{H(t)}</p>")) + "</div>";
    }

    [GeneratedRegex(@"<a href=""#vers-(\d+)"">(.*?)</a>")]
    private static partial Regex VerseAnchor();

    [GeneratedRegex(@"(<h([23])>[^<]*?(?:frage|question|pregunta|вопрос)[^<]*</h\2>)(.*?)(?=<h[123][ >]|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex QuestionSection();

    [GeneratedRegex(@"<p>([^<]{3,300}\?)</p>")]
    private static partial Regex QuestionParagraph();

    /// <summary>The finishing touches on the text: numbers at the Bible references (or plain references without the appendix), questions set apart.</summary>
    string PolishHandout(string html)
    {
        var s = _settings;
        html = VerseAnchor().Replace(html, m => s.HandoutAppendix
            ? $"<a class=\"vref\" href=\"#vers-{m.Groups[1].Value}\">{m.Groups[2].Value}{(s.HandoutNumbers ? $"<sup>{m.Groups[1].Value}</sup>" : "")}</a>"
            : $"<span class=\"vref\">{m.Groups[2].Value}</span>");
        if (s.HandoutQuestions)
        {
            // a section "Questions" (or "Fragen zum Gespräch" …) becomes a box with numbered questions
            html = QuestionSection().Replace(html, m => "<div class=\"questions\">" + m.Groups[1].Value + m.Groups[3].Value.Replace("<ul>", "<ol>").Replace("</ul>", "</ol>") + "</div>");
            // a single line that asks something stands out as well
            html = QuestionParagraph().Replace(html, "<p class=\"question\">$1</p>");
        }
        return html;
    }

    /// <summary>The look: the same layout in three styles; A4 pages, page numbers, no lonely lines at a page break.</summary>
    string HandoutCss(string title)
    {
        var s = _settings;
        var style = HandoutStyles.Contains(s.HandoutStyle) ? s.HandoutStyle : "classic";
        // large print is larger in every size (for older eyes and dim rooms)
        var size = style == "large" ? s.HandoutFontSize switch { 0 => "13.5pt", 2 => "16.5pt", _ => "15pt" } : s.HandoutFontSize switch { 0 => "10.5pt", 2 => "13pt", _ => "11.5pt" };
        static string CssText(string t) => "\"" + t.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";
        var footer = s.HandoutGroup.Trim().Length > 0 ? s.HandoutGroup.Trim() + " · " + title : title;
        var vars = style switch
        {
            "modern" => "--body:'Segoe UI','Helvetica Neue',Arial,sans-serif;--head:'Segoe UI','Helvetica Neue',Arial,sans-serif;--accent:#2b7a8c;--dark:#1b5361;--soft:#eef6f8;--line:#cfe0e5;--ink:#1d2326;--muted:#6b777c",
            "plain" => "--body:Georgia,'Palatino Linotype','Book Antiqua',serif;--head:Georgia,'Palatino Linotype',serif;--accent:#555;--dark:#111;--soft:transparent;--line:#aaa;--ink:#111;--muted:#666",
            "elegant" => "--body:'Palatino Linotype','Book Antiqua',Georgia,serif;--head:'Palatino Linotype','Book Antiqua',Georgia,serif;--accent:#8c2f39;--dark:#5e1a22;--soft:#fbf4f4;--line:#e8d0d3;--ink:#241b1c;--muted:#7a6467",
            "community" => "--body:'Segoe UI','Helvetica Neue',Arial,sans-serif;--head:'Trebuchet MS','Segoe UI',Arial,sans-serif;--accent:#d9772b;--dark:#8a4210;--soft:#fff3e8;--line:#f2d4bb;--ink:#2a221c;--muted:#7b6a5c",
            "newspaper" => "--body:Georgia,'Times New Roman',serif;--head:Georgia,'Times New Roman',serif;--accent:#b3261e;--dark:#111;--soft:#f6f4f0;--line:#bbb;--ink:#141414;--muted:#555",
            "large" => "--body:Verdana,'Segoe UI',Tahoma,sans-serif;--head:Verdana,'Segoe UI',Tahoma,sans-serif;--accent:#1f4e9a;--dark:#123468;--soft:#eef3fb;--line:#9fb4d6;--ink:#000;--muted:#333",
            "nature" => "--body:Georgia,'Palatino Linotype',serif;--head:Georgia,'Palatino Linotype',serif;--accent:#5b8a3c;--dark:#2f5420;--soft:#f1f7ec;--line:#cfe0c2;--ink:#1f261b;--muted:#66705f",
            _ => "--body:Georgia,'Palatino Linotype','Book Antiqua',serif;--head:Georgia,'Palatino Linotype','Book Antiqua',serif;--accent:#b08a2e;--dark:#7a5c16;--soft:#fbf7ec;--line:#e6dcc2;--ink:#221f1a;--muted:#77705f",
        };
        var css = new StringBuilder();
        css.Append(":root{").Append(vars).Append(";--sans:'Segoe UI','Helvetica Neue',Arial,sans-serif;--size:").Append(size).Append('}');
        css.Append("@page{size:A4;margin:18mm 18mm 20mm;@bottom-center{content:counter(page) ' / ' counter(pages);font:8.5pt var(--sans);color:#999}")
           .Append("@bottom-left{content:").Append(CssText(footer)).Append(";font:8pt var(--sans);color:#aaa}}");
        css.Append("*{box-sizing:border-box}html{background:#e8e5de;-webkit-print-color-adjust:exact;print-color-adjust:exact}")
           .Append("body{margin:0;padding:24px 12px 60px;font-family:var(--body);font-size:var(--size);line-height:1.58;color:var(--ink)}")
           .Append(".page{max-width:210mm;margin:0 auto;background:#fff;padding:18mm 18mm 22mm;box-shadow:0 2px 18px #0002;border-radius:2px;hyphens:auto;overflow-wrap:break-word}")
           .Append(".bar{max-width:210mm;margin:0 auto 14px;display:flex;gap:14px;align-items:center;font:13px var(--sans);color:#555}")
           .Append(".bar button{font:600 14px var(--sans);padding:9px 16px;border:0;border-radius:8px;background:var(--dark);color:#fff;cursor:pointer}")
           .Append("@media screen and (max-width:720px){body{padding:10px 6px 40px}.page{padding:7mm 6mm 10mm}.cards.two{columns:1}.bar{flex-wrap:wrap}}")
           .Append("@media print{html,body{background:none;padding:0}.page{max-width:none;margin:0;padding:0;box-shadow:none}.noprint{display:none}}");
        // text
        css.Append("p{margin:0 0 .7em;orphans:3;widows:3}ul,ol{margin:.2em 0 .9em;padding-left:1.4em}li{margin:.25em 0;break-inside:avoid}")
           .Append("h1,h2,h3{font-family:var(--head);line-height:1.25;break-after:avoid;color:var(--ink)}")
           .Append("main h1,main h2,.linked h2,.linked h3,main h3{break-after:avoid}")
           .Append("h2{font-size:1.32em;margin:1.5em 0 .5em}h3{font-size:1.1em;margin:1.2em 0 .35em}")
           .Append("a{color:inherit;text-decoration:none}b{font-weight:700}")
           .Append(".vref{color:var(--dark);font-weight:600;white-space:nowrap}")
           .Append("sup{font:700 .62em var(--sans);color:var(--accent);margin-left:.15em;vertical-align:.55em}")
           .Append("blockquote{position:relative;margin:1.1em 0;padding:.3em 1em .3em 2.3em;font-style:italic;color:#3a362e;break-inside:avoid}")
           .Append("blockquote::before{content:'\\201C';position:absolute;left:.05em;top:-.2em;font:3.2em/1 Georgia,serif;color:var(--accent);opacity:.55}");
        // head
        css.Append(".head{margin-bottom:9mm}.group{font:600 .78em var(--sans);letter-spacing:.18em;text-transform:uppercase;color:var(--accent)}")
           .Append(".title{font-size:2.15em;margin:.15em 0 .1em;line-height:1.15}.sub{color:var(--muted);font-size:.92em}");
        // questions, appendix, notes
        css.Append(".questions{background:var(--soft);border:1px solid var(--line);border-radius:8px;padding:.5em 1.1em .7em;margin:1.3em 0;break-inside:avoid}")
           .Append(".questions h2,.questions h3{margin:.4em 0 .5em;color:var(--dark)}")
           .Append(".questions ol,.questions ul{list-style:none;padding-left:0;counter-reset:q;margin:0}")
           .Append(".questions li{counter-increment:q;position:relative;padding-left:2.1em;margin:.55em 0}")
           .Append(".questions li::before{content:counter(q);position:absolute;left:0;top:.2em;width:2em;height:2em;border-radius:50%;background:var(--accent);color:#fff;font:700 .72em/2em var(--sans);text-align:center}")
           .Append("main h1{font-size:1.55em;margin:1.4em 0 .5em}")
           .Append("p.question{position:relative;padding:.35em .8em .35em 2.2em;background:var(--soft);border-left:3px solid var(--accent);border-radius:0 6px 6px 0;break-inside:avoid}")
           .Append("p.question::before{content:'?';position:absolute;left:.75em;top:.3em;font:700 1em var(--sans);color:var(--accent)}")
           .Append(".appendix{margin-top:10mm}.apx{border-bottom:1px solid var(--line);padding-bottom:.25em;margin-bottom:.8em}")
           .Append(".cards.two{columns:2;column-gap:6mm}")
           .Append(".card{display:inline-block;width:100%;break-inside:avoid;border:1px solid var(--line);border-radius:7px;padding:.55em .85em .5em;margin:0 0 4mm;background:#fff}")
           .Append(".card-head{display:flex;align-items:center;gap:.5em;margin-bottom:.3em}")
           .Append(".num{flex:none;width:1.6em;height:1.6em;border-radius:50%;background:var(--accent);color:#fff;font:700 .72em/1.6em var(--sans);text-align:center}")
           .Append(".ref{font-variant:small-caps;letter-spacing:.04em;font-weight:700;color:var(--dark)}")
           .Append(".tr{margin-left:auto;font:.7em var(--sans);color:var(--muted);letter-spacing:.05em}")
           .Append(".card p{margin:0 0 .25em;font-size:.93em;line-height:1.48}")
           .Append(".notes{margin-top:10mm;break-inside:avoid}.ln{height:2.1em;border-bottom:1px solid var(--line)}")
           .Append(".divider{margin:9mm 0 6mm;display:flex;align-items:center;gap:1em}.divider::before,.divider::after{content:'';flex:1;border-top:1px solid var(--line)}");
        // the three looks
        switch (style)
        {
            case "modern":
                css.Append(".head{border-left:6px solid var(--accent);padding:.2em 0 .3em 1em}.title{font-weight:700;letter-spacing:-.01em}.rule{display:none}")
                   .Append("h2{font-weight:700;color:var(--dark)}h2::before{content:'';display:inline-block;width:.5em;height:.5em;background:var(--accent);border-radius:2px;margin-right:.5em;vertical-align:.12em}")
                   .Append(".apx::before,.questions h2::before,.notes h2::before{display:none}.card{border-left:4px solid var(--accent)}.divider span::before{content:'';display:block;width:.5em;height:.5em;background:var(--accent);border-radius:2px}");
                break;
            case "plain":
                css.Append(".head{border-bottom:1.5px solid #222;padding-bottom:.5em}.rule{display:none}.title{font-weight:700}")
                   .Append(".num,.questions li::before{background:none;color:#111;border:1px solid #555}.questions,p.question{background:none}.card{background:none}")
                   .Append("blockquote::before{color:#888}.divider span::before{content:'*  *  *';color:#777}");
                break;
            case "elegant":
                // small capitals, a large first letter, double rules; the verses without boxes, only a line at their side
                css.Append(".head{text-align:center;border-bottom:3px double var(--accent);padding-bottom:.7em}.rule{display:none}")
                   .Append(".title{font-variant:small-caps;letter-spacing:.06em;font-weight:400;font-size:2.3em}.sub{font-style:italic;letter-spacing:.03em}.group{font-family:var(--body);font-variant:small-caps;text-transform:none;letter-spacing:.25em;font-size:.95em}")
                   .Append("h2{font-variant:small-caps;letter-spacing:.08em;font-weight:400;text-align:center;color:var(--dark)}h3{font-style:italic;font-weight:400;color:var(--dark)}")
                   .Append("main>p:first-of-type::first-letter{float:left;font-size:3.6em;line-height:.82;margin:.06em .1em 0 0;color:var(--accent)}")
                   .Append(".card{border:0;border-left:2px solid var(--accent);border-radius:0;padding:.2em 0 .2em .9em;background:none}.num{background:none;color:var(--accent);border:1px solid var(--accent)}")
                   .Append(".questions{background:none;border:0;border-top:3px double var(--accent);border-bottom:3px double var(--accent);border-radius:0}.questions li::before{background:none;color:var(--accent);border:1px solid var(--accent)}")
                   .Append(".apx{text-align:center;border:0}p.question{background:none}.divider span::before{content:'\\2767';color:var(--accent)}");
                break;
            case "community":
                // a colored head across the page, round shapes, friendly
                css.Append(".head{background:var(--accent);color:#fff;border-radius:14px;padding:1.1em 1.4em 1.2em}.head .group,.head .sub{color:#fffd;opacity:.95}.title{color:#fff;font-weight:700}.rule{display:none}")
                   .Append("h2{display:inline-block;background:var(--soft);color:var(--dark);padding:.2em .8em;border-radius:999px;font-size:1.15em;font-weight:700}")
                   .Append(".card{border:0;border-radius:14px;background:var(--soft)}.num{border-radius:6px}.questions{border:2px dashed var(--accent);border-radius:14px}.questions li::before{border-radius:6px}")
                   .Append("blockquote{background:var(--soft);border-radius:12px}.apx{border:0}.divider span::before{content:'\\2665';color:var(--accent)}");
                break;
            case "newspaper":
                // the text in two columns like an article, a big headline between thick rules, red as the only color
                css.Append(".head{border-top:4px solid #111;border-bottom:1px solid #111;padding:.4em 0 .6em}.rule{display:none}")
                   .Append(".title{font-weight:700;font-size:2.6em;letter-spacing:-.02em;line-height:1.05}.group{color:var(--accent)}.sub{font:italic .9em var(--body)}")
                   .Append("main{columns:2;column-gap:7mm;column-rule:1px solid #ccc}main>p:first-of-type{font-size:1.1em;line-height:1.5;color:#222}main>p:first-of-type::first-line{font-weight:700}")
                   .Append("h2{font:700 .82em var(--sans);text-transform:uppercase;letter-spacing:.12em;color:var(--accent);border-top:2px solid #111;padding-top:.35em;margin-top:1.1em}h3{font-size:1em}")
                   .Append(".card{border:0;border-top:2px solid #111;border-radius:0;padding:.4em 0 .2em}.num{border-radius:0;background:#111}.questions{border-radius:0;background:var(--soft);border:0;border-left:4px solid var(--accent)}.questions li::before{border-radius:0;background:#111}")
                   .Append(".apx{font:700 .82em var(--sans);text-transform:uppercase;letter-spacing:.12em;border-bottom:2px solid #111}.divider span::before{content:'\\25A0';color:var(--accent)}");
                break;
            case "large":
                // large, clear and calm: strong contrast, wide lines, everything in one column
                css.Append("body{line-height:1.7}.head{border-bottom:3px solid var(--accent);padding-bottom:.5em}.rule{display:none}.title{font-weight:700;font-size:2em}")
                   .Append("h2{font-weight:700;color:var(--dark);font-size:1.25em}.cards.two{columns:1}.card{border:2px solid var(--line)}.card p{font-size:1em}")
                   .Append("sup{font-size:.78em}.num{width:1.9em;height:1.9em;line-height:1.9em;font-size:.8em}.ln{height:2.6em}.vref{text-decoration:underline;text-decoration-thickness:1px}")
                   .Append("blockquote{font-style:normal}.divider span::before{content:'\\25CF';color:var(--accent)}");
                break;
            case "nature":
                // soft greens, a leaf before the headings, rounded boxes with a green edge
                css.Append(".head{text-align:center}.title{font-weight:400}.sub{font-style:italic}")
                   .Append(".rule{display:flex;align-items:center;gap:.8em;width:55%;margin:.9em auto 0;color:var(--accent)}.rule::before,.rule::after{content:'';flex:1;border-top:1px solid var(--accent);opacity:.5}.rule span::before{content:'\\2767'}")
                   .Append("h2{font-weight:400;color:var(--dark)}h2::before{content:'\\2767  ';color:var(--accent)}.questions h2::before,.notes h2::before,.apx::before{content:''}")
                   .Append(".card{border:0;border-left:4px solid var(--accent);border-radius:10px;background:var(--soft)}.questions{border-radius:14px}.apx{text-align:center;border:0}")
                   .Append(".divider span::before{content:'\\2767';color:var(--accent)}");
                break;
            default:
                css.Append(".head{text-align:center}.title{font-weight:400;letter-spacing:.01em}.sub{font-style:italic}")
                   .Append(".rule{display:flex;align-items:center;gap:.8em;width:60%;margin:.9em auto 0;color:var(--accent)}.rule::before,.rule::after{content:'';flex:1;border-top:1px solid var(--accent);opacity:.6}.rule span::before{content:'\\2766'}")
                   .Append("h2{font-weight:400;color:var(--dark)}h3{font-style:italic;font-weight:400}.apx{text-align:center;border:0}.apx::after{content:'';display:block;width:3em;margin:.35em auto 0;border-top:1px solid var(--accent)}")
                   .Append(".divider span::before{content:'\\2766';color:var(--accent)}");
                break;
        }
        return css.ToString();
    }

    // ---- the export window ------------------------------------------------------------------------------------------------

    enum ExportFormat { Print, Html, Markdown }

    /// <summary>
    /// The window for exporting a document: the format at the top (print / PDF, HTML, Markdown), below what fits it. For print and HTML the
    /// look of the sheet and what goes on it, with a preview in the browser; for Markdown how the verses and linked documents come along,
    /// with the text of the file to see before saving.
    /// </summary>
    Task ShowHandoutDialogAsync() => ShowExportDialogAsync(ExportFormat.Print);

    async Task ShowExportDialogAsync(ExportFormat format)
    {
        var (title, body, tags, id, linked) = CurrentDocumentForExport();
        if (title.Length == 0 && body.Trim().Length == 0) { DocInfo.Text = L("Nichts zu exportieren."); return; }
        var s = _settings;
        var win = new Window { Title = L("Exportieren"), Width = 780, SizeToContent = SizeToContent.Height, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };

        // ---- the look of the sheet (print and HTML)
        var looks = new UniformGrid { Columns = IsPhone ? 2 : 4, Margin = new(0, 0, 0, 6) };
        var cards = new List<(string Key, Border Card)>();
        void Mark() { foreach (var (k, c) in cards) { c.BorderThickness = new(k == s.HandoutStyle ? 2.5 : 1); c.BorderBrush = k == s.HandoutStyle ? Res("BsAccent") : Res("BsBorder"); } }
        foreach (var (key, name, about) in HandoutLooks())
        {
            var k = key;
            var card = new Border
            {
                CornerRadius = new(10), Padding = new(8), Margin = new(0, 0, 8, 8), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { Spacing = 4, Children = { HandoutSketch(k), new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = about, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center } } },
            };
            card.PointerPressed += (_, _) => { s.HandoutStyle = k; SaveSettings(); Mark(); };
            cards.Add((k, card));
            looks.Children.Add(card);
        }
        Mark();

        var group = new TextBox { Text = s.HandoutGroup, PlaceholderText = L("z. B. Hauskreis Am Markt") };
        group.TextChanged += (_, _) => { s.HandoutGroup = group.Text ?? ""; SaveSettings(); };

        Control Choice(string[] names, Func<int> get, Action<int> set)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var buttons = new List<Button>();
            for (var i = 0; i < names.Length; i++)
            {
                var at = i;
                var b = new Button { Classes = { "seg" }, Content = names[i] };
                b.Click += (_, _) => { set(at); SaveSettings(); Paint(); };
                buttons.Add(b);
            }
            void Paint() { for (var i = 0; i < buttons.Count; i++) buttons[i].Classes.Set("active", i == get()); }
            Paint();
            foreach (var b in buttons) row.Children.Add(b);
            return row;
        }
        var lineCounts = new[] { 0, 6, 12, 26 };
        var fontChoice = Choice([L("Klein"), L("Normal"), L("Groß")], () => s.HandoutFontSize, i => s.HandoutFontSize = i);
        var notesChoice = Choice([L("Keine"), L("6 Zeilen"), L("12 Zeilen"), L("Eine Seite")], () => Math.Max(0, Array.IndexOf(lineCounts, s.HandoutNoteLines)), i => s.HandoutNoteLines = lineCounts[i]);

        CheckBox Check(string text, bool on, Action<bool> set) { var c = new CheckBox { Content = text, IsChecked = on }; c.IsCheckedChanged += (_, _) => { set(c.IsChecked == true); SaveSettings(); }; return c; }
        var appendix = Check(L("Bibelstellen als Anhang am Ende"), s.HandoutAppendix, v => s.HandoutAppendix = v);
        var two = Check(L("Anhang zweispaltig (spart Papier)"), s.HandoutTwoColumns, v => s.HandoutTwoColumns = v);
        var numbers = Check(L("Kleine Nummern an den Bibelstellen im Text"), s.HandoutNumbers, v => s.HandoutNumbers = v);
        var questions = Check(L("Fragen hervorheben (Abschnitt „Fragen“ und Zeilen mit „?“)"), s.HandoutQuestions, v => s.HandoutQuestions = v);
        appendix.IsCheckedChanged += (_, _) => { two.IsEnabled = numbers.IsEnabled = appendix.IsChecked == true; };
        two.IsEnabled = numbers.IsEnabled = s.HandoutAppendix;

        TextBlock Label_(string t) => new() { Text = t, FontWeight = FontWeight.SemiBold, Margin = new(0, 10, 0, 4) };
        var sheetPanel = new StackPanel
        {
            Children =
            {
                Label_(L("Aussehen")), looks,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,20,*"),
                    Children =
                    {
                        new StackPanel { Children = { Label_(L("Gemeinde oder Gruppe (steht oben auf dem Blatt)")), group, Label_(L("Schriftgröße")), fontChoice, Label_(L("Platz für Notizen am Ende")), notesChoice } },
                        new StackPanel { [Grid.ColumnProperty] = 2, Children = { Label_(L("Inhalt")), new StackPanel { Spacing = 4, Children = { appendix, new Border { Margin = new(24, 0, 0, 0), Child = new StackPanel { Spacing = 4, Children = { two, numbers } } }, questions } } } },
                    },
                },
            },
        };

        // ---- Markdown: how the verses and the linked documents come along, and the text to see first
        var mdPreview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 300, FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"), FontSize = 12.5 };
        var mdFiles = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) };
        var mdVersion = 0;
        async Task FillMarkdown()
        {
            var v = ++mdVersion;
            mdPreview.Text = L("Wird vorbereitet …");
            var files = await MarkdownFilesAsync(title, body, tags, id, linked);
            if (v != mdVersion) return;
            mdFiles.Text = L("Dateien im Ordner „{0}“: {1}", SafeName(title, L("Dokument")), string.Join(", ", files.Select(f => f.File)));
            mdPreview.Text = files.Count > 0 ? files[0].Text : "";
        }
        var verseModes = new[] { "file", "inline", "none" };
        var mdVerses = Choice([L("Eigene Datei mit Links"), L("Als Zitat unter dem Text"), L("Nicht mitnehmen")], () => Math.Max(0, Array.IndexOf(verseModes, s.MarkdownVerses)), i => { s.MarkdownVerses = verseModes[i]; _ = FillMarkdown(); });
        var mdLinked = Check(L("Verlinkte Dokumente als eigene Dateien mitnehmen"), s.MarkdownLinked, v => { s.MarkdownLinked = v; _ = FillMarkdown(); });
        var mdHead = Check(L("Kopf mit Titel, Datum und Schlagworten (YAML, wie Obsidian ihn liest)"), s.MarkdownFrontMatter, v => { s.MarkdownFrontMatter = v; _ = FillMarkdown(); });
        var mdPanel = new StackPanel
        {
            Children =
            {
                Label_(L("Bibelstellen")), mdVerses,
                new StackPanel { Spacing = 4, Margin = new(0, 10, 0, 0), Children = { mdLinked, mdHead } },
                Label_(L("Vorschau")), mdFiles, mdPreview,
            },
        };

        // ---- the format, the buttons
        var hint = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) };
        var preview = new Button { Content = L("Vorschau") };
        ToolTip.SetTip(preview, L("Zeigt das Blatt im Browser, ohne zu drucken."));
        var go = new Button { Classes = { "primary" } };
        var close = new Button { Content = L("Schließen") };
        var formats = new[] { L("Drucken / PDF"), L("HTML-Datei"), L("Markdown") };
        void Show()
        {
            sheetPanel.IsVisible = format != ExportFormat.Markdown;
            mdPanel.IsVisible = format == ExportFormat.Markdown;
            preview.IsVisible = format != ExportFormat.Markdown;
            go.Content = format switch { ExportFormat.Html => L("Als HTML speichern …"), ExportFormat.Markdown => L("Als Markdown speichern …"), _ => L("Drucken / PDF …") };
            hint.Text = format switch
            {
                ExportFormat.Html => L("Eine Webseite zum Weitergeben: dasselbe Aussehen wie gedruckt, die Bibelstellen und verlinkten Dokumente lassen sich anklicken."),
                ExportFormat.Markdown => L("Reiner Text für Obsidian und andere Markdown-Programme; jede Datei kommt in einen eigenen Ordner."),
                _ => L("Das Blatt öffnet sich im Browser; dort druckst du es oder speicherst es als PDF („Als PDF speichern“)."),
            };
            if (format == ExportFormat.Markdown && mdPreview.Text is null or "") _ = FillMarkdown();
        }
        var formatChoice = Choice(formats, () => (int)format, i => { format = (ExportFormat)i; Show(); });
        async Task MakeSheet(bool print)
        {
            var html = await HandoutHtmlAsync(title, body, tags, id, linked, print);
            OpenExported(SafeName(title, L("Dokument")) + ".html", html, print ? L("Zum Drucken gespeichert: {0}", "") : L("Vorschau gespeichert: {0}", ""));
        }
        preview.Click += async (_, _) => await MakeSheet(false);
        go.Click += async (_, _) =>
        {
            switch (format)
            {
                case ExportFormat.Html: win.Close(); await SaveDocumentAsHtmlAsync(); break;
                case ExportFormat.Markdown: win.Close(); await SaveDocumentAsMarkdownAsync(); break;
                default: win.Close(); await MakeSheet(true); break;
            }
        };
        close.Click += (_, _) => win.Close();
        Show();

        win.Content = new ScrollViewer
        {
            MaxHeight = 900,
            Content = new StackPanel
            {
                Margin = new(22),
                Children =
                {
                    new TextBlock { Text = L("Exportieren"), FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 8) },
                    formatChoice, hint,
                    sheetPanel, mdPanel,
                    new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0), ItemSpacing = 8, Children = { preview, go, close } },
                },
            },
        };
        await ShowModalAsync(win);
    }

    /// <summary>The looks of the sheet with their name and what makes them different.</summary>
    IEnumerable<(string Key, string Name, string About)> HandoutLooks() =>
    [
        ("classic", L("Klassisch"), L("Serifenschrift und Gold, feierlich")),
        ("modern", L("Modern"), L("Klar und luftig, mit Farbakzent")),
        ("plain", L("Schlicht"), L("Spart Tinte, ohne Farbflächen")),
        ("elegant", L("Elegant"), L("Weinrot, Kapitälchen und große Initiale")),
        ("community", L("Gemeindebrief"), L("Farbiger Kopf, rund und freundlich")),
        ("newspaper", L("Zeitung"), L("Text in zwei Spalten wie ein Artikel")),
        ("large", L("Großdruck"), L("Große, klare Schrift, gut lesbar")),
        ("nature", L("Natur"), L("Grüntöne mit Blattornament")),
    ];

    /// <summary>A tiny sheet in a look: head, lines of text, a verse card; so the looks can be told apart at a glance.</summary>
    static Control HandoutSketch(string style)
    {
        Color C(string hex) => Color.Parse(hex);
        var accent = style switch
        {
            "modern" => C("#2b7a8c"), "plain" => C("#444444"), "elegant" => C("#8c2f39"), "community" => C("#d9772b"),
            "newspaper" => C("#b3261e"), "large" => C("#1f4e9a"), "nature" => C("#5b8a3c"), _ => C("#b08a2e"),
        };
        var soft = style switch
        {
            "modern" => C("#eef6f8"), "community" => C("#fff3e8"), "nature" => C("#f1f7ec"), "classic" => C("#fbf7ec"), "large" => C("#eef3fb"), _ => Colors.Transparent,
        };
        var ink = new SolidColorBrush(C("#2a2722"));
        var grey = new SolidColorBrush(C("#d3cfc5"));
        var acc = new SolidColorBrush(accent);
        var centered = style is "classic" or "elegant" or "nature";
        var align = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        var big = style == "large";
        Border Bar(double w, double h, IBrush b, HorizontalAlignment a = HorizontalAlignment.Left, double gap = 3) => new() { Width = w, Height = h, Background = b, CornerRadius = new(1.5), HorizontalAlignment = a, Margin = new(0, 0, 0, gap) };

        // the head
        Control head = style switch
        {
            "community" => new Border { Background = acc, CornerRadius = new(6), Padding = new(6, 5), Child = new StackPanel { Children = { Bar(26, 2, Brushes.White), Bar(68, 6, Brushes.White, gap: 2), Bar(40, 2, Brushes.White, gap: 0) } } },
            "modern" => new Border { BorderBrush = acc, BorderThickness = new(3, 0, 0, 0), Padding = new(5, 0, 0, 0), Child = new StackPanel { Children = { Bar(26, 2, acc), Bar(70, 6, ink), Bar(42, 2, grey, gap: 0) } } },
            "newspaper" => new Border { BorderBrush = ink, BorderThickness = new(0, 3, 0, 1), Padding = new(0, 3, 0, 3), Child = new StackPanel { Children = { Bar(26, 2, acc), Bar(88, 9, ink), Bar(46, 2, grey, gap: 0) } } },
            "plain" => new Border { BorderBrush = ink, BorderThickness = new(0, 0, 0, 1.5), Padding = new(0, 0, 0, 3), Child = new StackPanel { Children = { Bar(26, 2, grey), Bar(70, 6, ink), Bar(42, 2, grey, gap: 0) } } },
            "large" => new Border { BorderBrush = acc, BorderThickness = new(0, 0, 0, 2.5), Padding = new(0, 0, 0, 3), Child = new StackPanel { Children = { Bar(78, 9, ink), Bar(50, 3, grey, gap: 0) } } },
            "elegant" => new Border { BorderBrush = acc, BorderThickness = new(0, 0, 0, 2), Padding = new(0, 0, 0, 3), Child = new StackPanel { Children = { Bar(30, 2, acc, align), Bar(74, 6, ink, align), Bar(46, 2, grey, align, 0) } } },
            _ => new StackPanel { Children = { Bar(26, 2, acc, align), Bar(74, 6, ink, align), Bar(46, 2, grey, align), new TextBlock { Text = style == "nature" ? "❧" : "❦", Foreground = acc, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center } } },
        };

        // the text: one or two columns, a large first letter in the elegant look, thick lines for large print
        var line = big ? 3.5 : 2.5;
        StackPanel Lines(double w, int n) { var p = new StackPanel(); for (var i = 0; i < n; i++) p.Children.Add(Bar(i == n - 1 ? w * 0.6 : w, line, grey, gap: big ? 4 : 3)); return p; }
        Control text = style switch
        {
            "newspaper" => new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*"), Children = { Lines(44, 5), new Border { [Grid.ColumnProperty] = 1, Width = 1, Background = grey, HorizontalAlignment = HorizontalAlignment.Center }, new Border { [Grid.ColumnProperty] = 2, Child = Lines(44, 5) } } },
            "elegant" => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new TextBlock { Text = "R", FontFamily = new FontFamily("Georgia"), FontSize = 17, Foreground = acc, Margin = new(0, -4, 0, 0) }, Lines(80, 3) } },
            "large" => Lines(96, 3),
            _ => Lines(98, 4),
        };
        var headingBar = style switch
        {
            "community" => (Control)new Border { Background = new SolidColorBrush(soft), CornerRadius = new(5), Padding = new(4, 1), HorizontalAlignment = HorizontalAlignment.Left, Child = Bar(30, 3, acc, gap: 0) },
            "newspaper" => new Border { BorderBrush = ink, BorderThickness = new(0, 1.5, 0, 0), Padding = new(0, 2, 0, 0), Child = Bar(30, 2.5, acc, gap: 0) },
            "nature" => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Children = { new TextBlock { Text = "❧", Foreground = acc, FontSize = 7 }, Bar(30, 3, ink, gap: 0) } },
            _ => Bar(36, big ? 4 : 3, style is "plain" or "large" ? ink : acc, centered && style != "classic" ? HorizontalAlignment.Center : HorizontalAlignment.Left),
        };

        // a verse card
        var dot = new Border { Width = 7, Height = 7, CornerRadius = new(style is "community" or "newspaper" ? 1.5 : 3.5), Background = style is "plain" or "elegant" ? Brushes.Transparent : style == "newspaper" ? ink : acc, BorderBrush = acc, BorderThickness = new(style is "plain" or "elegant" ? 1 : 0) };
        var cardInner = new StackPanel { Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { dot, Bar(30, 3, acc, gap: 0) } }, Bar(78, 2.5, grey, gap: 2), Bar(60, 2.5, grey, gap: 0) } };
        Control card = style switch
        {
            "elegant" => new Border { BorderBrush = acc, BorderThickness = new(2, 0, 0, 0), Padding = new(5, 2), Child = cardInner },
            "newspaper" => new Border { BorderBrush = ink, BorderThickness = new(0, 2, 0, 0), Padding = new(0, 3), Child = cardInner },
            "community" => new Border { Background = new SolidColorBrush(soft), CornerRadius = new(7), Padding = new(5), Child = cardInner },
            "nature" => new Border { Background = new SolidColorBrush(soft), BorderBrush = acc, BorderThickness = new(3, 0, 0, 0), CornerRadius = new(5), Padding = new(5), Child = cardInner },
            "modern" => new Border { Background = new SolidColorBrush(soft), BorderBrush = acc, BorderThickness = new(3, 1, 1, 1), CornerRadius = new(3), Padding = new(5), Child = cardInner },
            _ => new Border { Background = new SolidColorBrush(soft), BorderBrush = new SolidColorBrush(C("#d9d2c0")), BorderThickness = new(big ? 2 : 1), CornerRadius = new(3), Padding = new(5), Child = cardInner },
        };
        card.Margin = new(0, 6, 0, 0);

        return new Border
        {
            Width = 132, Height = 150, Background = Brushes.White, CornerRadius = new(3), Padding = new(11, 11), HorizontalAlignment = HorizontalAlignment.Center,
            BoxShadow = BoxShadows.Parse("0 1 6 0 #33000000"), ClipToBounds = true,
            Child = new StackPanel { Spacing = 6, Children = { head, headingBar, text, card } },
        };
    }
}
