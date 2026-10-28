using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Print / PDF and slides. Everything is written as a self-contained HTML page into Documents\BibleStudy\Export and opened in the
/// browser; the print page opens the print dialog right away (choose "Save as PDF" there), the slides are a full-screen show.
/// </summary>
public partial class MainWindow
{
    static string H(string s) => WebUtility.HtmlEncode(s);

    static string ExportFolder
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BibleStudy", "Export");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    static string SafeName(string s, string fallback) =>
        string.Concat((s.Trim().Length > 0 ? s.Trim() : fallback).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    const string PrintCss = "body{font-family:Georgia,'Times New Roman',serif;max-width:760px;margin:36px auto;padding:0 20px;line-height:1.55;color:#222}"
        + "h1{margin-bottom:2px}h2{border-bottom:1px solid #ccc;padding-bottom:3px;margin-top:26px}h3{margin-bottom:4px}"
        + ".meta{color:#666;margin-top:0}blockquote{border-left:4px solid #c9a227;margin:12px 0;padding:2px 14px;background:#faf6e8}"
        + "li{margin:3px 0}.done{color:#666}.small{color:#888;font-size:.85em}footer{margin-top:40px;color:#999;font-size:.8em;border-top:1px solid #ddd;padding-top:6px}"
        + "@media print{body{margin:0;max-width:none}h2,h3{break-after:avoid}blockquote,li{break-inside:avoid}}";

    /// <summary>Writes a print page and opens it; the browser's print dialog appears at once.</summary>
    void ExportPrint(string title, string bodyHtml, string fileName)
    {
        var html = "<!doctype html><html lang=\"" + Loc.Code + "\"><meta charset=\"utf-8\"><title>" + H(title) + "</title><style>" + PrintCss + "</style>"
                 + bodyHtml + "<footer>BibleStudy · " + DateTime.Now.ToString("d") + "</footer>"
                 + "<script>addEventListener('load',()=>setTimeout(()=>print(),400))</script></html>";
        OpenExported(fileName + ".html", html, L("Zum Drucken gespeichert: {0}", ""));
    }

    void OpenExported(string fileName, string html, string statusPrefix)
    {
        var path = Path.Combine(ExportFolder, fileName);
        File.WriteAllText(path, html, new UTF8Encoding(false));
        StatusText.Text = statusPrefix + path;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { /* no default browser: the path is in the status line */ }
    }

    /// <summary>The little Markdown the editor uses: headings, quotes, lists, bold, italic; [[links]] become plain titles.</summary>
    static string MarkdownToHtml(string md)
    {
        md = Regex.Replace(md, "<!--.*?-->", "", RegexOptions.Singleline);
        string Inline(string s)
        {
            s = H(s);
            s = Regex.Replace(s, @"\[\[(.+?)\]\]", "$1");
            s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<b>$1</b>");
            s = Regex.Replace(s, @"(?<![\*\w])\*(?!\s)(.+?)(?<!\s)\*(?![\*\w])", "<i>$1</i>");
            return s;
        }
        var sb = new StringBuilder();
        var list = "";
        void CloseList() { if (list.Length > 0) { sb.Append("</" + list + ">"); list = ""; } }
        var para = new List<string>();
        void FlushPara() { if (para.Count > 0) { sb.Append("<p>" + string.Join("<br>", para.Select(Inline)) + "</p>"); para.Clear(); } }
        foreach (var raw in md.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) { FlushPara(); CloseList(); continue; }
            var h = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (h.Success) { FlushPara(); CloseList(); var n = Math.Min(h.Groups[1].Length, 3); sb.Append($"<h{n}>{Inline(h.Groups[2].Value)}</h{n}>"); continue; }
            if (line.StartsWith('>')) { FlushPara(); CloseList(); sb.Append("<blockquote>" + Inline(line.TrimStart('>', ' ')) + "</blockquote>"); continue; }
            var li = Regex.Match(line, @"^\s*(?:[-*•]|\d+[.)])\s+(.*)$");
            if (li.Success)
            {
                FlushPara();
                var kind = char.IsDigit(line.TrimStart()[0]) ? "ol" : "ul";
                if (list != kind) { CloseList(); sb.Append("<" + kind + ">"); list = kind; }
                sb.Append("<li>" + Inline(li.Groups[1].Value) + "</li>");
                continue;
            }
            CloseList(); para.Add(line);
        }
        FlushPara(); CloseList();
        return sb.ToString();
    }

    // ---- what can be printed -------------------------------------------------------------------------------------------

    void PrintDocument()
    {
        var title = DocTitleBox.Text?.Trim() ?? "";
        var body = DocBodyBox.Text ?? "";
        if (title.Length == 0 && body.Trim().Length == 0) { DocInfo.Text = L("Nichts zu drucken."); return; }
        var tags = DocTagsBox.Text?.Trim() ?? "";
        ExportPrint(title, $"<h1>{H(title)}</h1>" + (tags.Length > 0 ? $"<p class=\"meta\">{H(tags)}</p>" : "") + MarkdownToHtml(body), SafeName(title, L("Dokument")));
    }

    void PrintPrayers()
    {
        var all = _vault.GetPrayers();
        var sb = new StringBuilder($"<h1>{H(L("Gebetsliste"))}</h1>");
        string Item(Prayer p) =>
            $"<li>{(p.Answered is not null ? "<span class=\"done\">" : "<span>")}{H(p.Text)}" + (p.Ref is { } r ? $" <i>({H(Label(r))})</i>" : "")
            + $" <span class=\"small\">{p.Created:dd.MM.yyyy}" + (p.Answered is not null ? $" → {p.Answered:dd.MM.yyyy}" : "") + "</span>"
            + (p.Answered is not null && p.Note.Length > 0 ? $"<br><span class=\"small\">{H(p.Note)}</span>" : "") + "</span></li>";
        var open = all.Where(p => p.Answered is null).ToList();
        var done = all.Where(p => p.Answered is not null).ToList();
        sb.Append($"<h2>{H(L("OFFEN ({0})", open.Count))}</h2><ul>" + string.Concat(open.Select(Item)) + "</ul>");
        if (done.Count > 0) sb.Append($"<h2>{H(L("ERHÖRT UND ERLEDIGT ({0})", done.Count))}</h2><ul>" + string.Concat(done.Select(Item)) + "</ul>");
        ExportPrint(L("Gebetsliste"), sb.ToString(), SafeName(L("Gebetsliste"), "prayers"));
    }

    void PrintPlan()
    {
        var plan = Plans.FirstOrDefault(p => p.Id == _settings.PlanId);
        if (plan is null || !DateTime.TryParse(_settings.PlanStart, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var start)) return;
        var sb = new StringBuilder($"<h1>{H(plan.Name)}</h1><p class=\"meta\">{H(plan.About)} · {H(L("{0} Tage", plan.Days))}</p><ul style=\"list-style:none;padding:0\">");
        for (var d = 1; d <= plan.Days; d++)
        {
            var chapters = PlanDay(plan, d);
            var done = chapters.Count > 0 && chapters.All(c => _read.Contains(c));
            var refs = string.Join(", ", chapters.GroupBy(c => c.Book).Select(g => g.Count() == 1 ? $"{BookName(g.Key)} {g.First().Chapter}" : $"{BookName(g.Key)} {g.First().Chapter}–{g.Last().Chapter}"));
            sb.Append($"<li>{(done ? "☑" : "☐")} <b>{H(L("Tag {0}", d))}</b> <span class=\"small\">{start.AddDays(d - 1):dd.MM.yyyy}</span> · {H(refs)}</li>");
        }
        sb.Append("</ul>");
        ExportPrint(plan.Name, sb.ToString(), SafeName(plan.Name, "plan"));
    }

    void PrintSermon()
    {
        var sb = new StringBuilder($"<h1>{H(_sermon.Title)}</h1><p class=\"meta\">{H(_sermon.Passage)} · {_sermon.Minutes} min</p>");
        if (_sermon.Message.Length > 0) sb.Append($"<blockquote>{H(_sermon.Message)}</blockquote>");
        foreach (var s in _sermon.Sections)
        {
            sb.Append($"<h2>{H(s.Heading)} <span class=\"small\">({s.Minutes} min)</span></h2>");
            if (s.Verses.Count > 0) sb.Append($"<p><i>{H(string.Join(" · ", s.Verses.Select(VerseLabel)))}</i></p>");
            foreach (var para in s.Notes.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)) sb.Append($"<p>{H(para).Replace("\n", "<br>")}</p>");
        }
        ExportPrint(_sermon.Title, sb.ToString(), SafeName(_sermon.Title, L("Predigt")));
    }

    // ---- slides --------------------------------------------------------------------------------------------------------

    async Task ExportSlidesAsync()
    {
        var t = TranslationBox.SelectedItem as TranslationInfo;
        var slides = new List<string>();
        string Slide(string inner, string cls = "") => $"<section class=\"{cls}\">{inner}</section>";
        slides.Add(Slide($"<h1>{H(_sermon.Title.Length > 0 ? _sermon.Title : L("Predigt"))}</h1><p class=\"ref\">{H(_sermon.Passage)}</p>", "title"));
        if (_sermon.Message.Length > 0) slides.Add(Slide($"<p class=\"big\">{H(_sermon.Message)}</p>", "message"));
        var cache = new Dictionary<(string, int), Chapter?>();
        foreach (var s in _sermon.Sections)
        {
            slides.Add(Slide($"<h2>{H(s.Heading)}</h2>", "heading"));
            foreach (var key in s.Verses)
            {
                var p = key.Split('|');
                string text = "";
                if (t is not null && p.Length == 3 && int.TryParse(p[1], out var ch) && int.TryParse(p[2], out var vs))
                {
                    if (!cache.TryGetValue((p[0], ch), out var chapter))
                        try { cache[(p[0], ch)] = chapter = await _provider.GetChapterAsync(t.Id, p[0], ch); } catch { cache[(p[0], ch)] = chapter = null; }
                    text = chapter?.Verses.FirstOrDefault(v => v.Number == vs) is { } verse ? string.Concat(verse.Segments.Where(x => x.NoteId is null).Select(x => x.Text)).Trim() : "";
                }
                slides.Add(Slide($"<p class=\"verse\">{H(text)}</p><p class=\"ref\">{H(VerseLabel(key))}{(t is not null ? " · " + H(t.ShortName) : "")}</p>", "bible"));
            }
        }
        var html = "<!doctype html><html lang=\"" + Loc.Code + "\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>" + H(_sermon.Title) + "</title><style>"
            + "html,body{margin:0;height:100%;background:#111;color:#f3efe6;font-family:Georgia,'Times New Roman',serif;overflow:hidden}"
            + "section{display:none;position:absolute;inset:0;padding:7vh 9vw;box-sizing:border-box;flex-direction:column;justify-content:center}"
            + "section.on{display:flex}h1{font-size:6vw;margin:0 0 2vh;line-height:1.1}h2{font-size:5.5vw;margin:0;color:#e3b65f}"
            + ".ref{font-size:2.4vw;color:#b9b2a2;margin:2vh 0 0}.verse{font-size:3.6vw;line-height:1.35;margin:0}.big{font-size:4.4vw;line-height:1.3;margin:0;color:#e3b65f}"
            + ".heading{align-items:flex-start}.title{text-align:left}.bible{border-left:.8vw solid #e3b65f}"
            + "#n{position:fixed;right:2vw;bottom:2vh;font:14px sans-serif;color:#777}#h{position:fixed;left:2vw;bottom:2vh;font:14px sans-serif;color:#555}"
            + "</style><body>" + string.Concat(slides) + "<div id=\"h\">← → / Space · F</div><div id=\"n\"></div>"
            + "<script>const s=[...document.querySelectorAll('section')];let i=0;function go(k){i=Math.max(0,Math.min(s.length-1,k));s.forEach((e,j)=>e.classList.toggle('on',j==i));document.getElementById('n').textContent=(i+1)+' / '+s.length;location.hash=i+1}"
            + "addEventListener('keydown',e=>{if(['ArrowRight','ArrowDown','PageDown',' ','Enter'].includes(e.key)){go(i+1);e.preventDefault()}else if(['ArrowLeft','ArrowUp','PageUp','Backspace'].includes(e.key)){go(i-1);e.preventDefault()}else if(e.key=='f'||e.key=='F'){document.fullscreenElement?document.exitFullscreen():document.documentElement.requestFullscreen()}else if(e.key=='Home')go(0);else if(e.key=='End')go(s.length-1)});"
            + "addEventListener('click',e=>go(e.clientX<innerWidth/3?i-1:i+1));go((parseInt(location.hash.slice(1))||1)-1)</script></html>";
        OpenExported(SafeName(_sermon.Title, L("Predigt")) + "-slides.html", html, L("Folien gespeichert: {0}", ""));
    }
}
