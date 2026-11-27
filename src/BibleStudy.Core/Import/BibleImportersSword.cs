using System.Net;
using System.Text.RegularExpressions;

namespace BibleStudy.Core.Import;

/// <summary>Bibles of the SWORD project (Xiphos, BibleTime, AndBible) and the exports of Logos.</summary>
public static partial class BibleImporters
{
    /// <summary>Logos keeps its Bibles in protected files of its own; the text that Logos lets you export can be read in.</summary>
    public static NotSupportedException LogosProtected() =>
        new(L("Logos-Dateien sind kopiergeschützt und werden nicht gelesen. Exportiere die Übersetzung in Logos als Word-Datei (Drucken/Exportieren → Microsoft Word) oder kopiere die Verse mit Bibelstellen in eine Textdatei, und lies diese Datei ein."));

    /// <summary>A SWORD Bible (RawText or zText) from the .zip of CrossWire or the .conf of an installed module. Locked modules are refused.</summary>
    public static ParsedBible ReadSword(string path, string languageCode)
    {
        var module = SwordModule.Read(path, wantBible: true);
        var c = new Collector();
        var osis = module.SourceType.Equals("OSIS", StringComparison.OrdinalIgnoreCase);
        var jesus = false;   // a milestone <q who="Jesus" sID/> may stay open over several verses
        foreach (var e in module.Entries)
        {
            if (e.Verse < 1 || e.Chapter < 1) continue;   // introductions of books and chapters are no verses
            var raw = e.Markup;
            if (osis)
            {
                foreach (Match t in OsisTitle().Matches(raw)) c.Heading(e.Book, e.Chapter, e.Verse, WebUtility.HtmlDecode(Tags().Replace(t.Groups[1].Value, "")));
                raw = OsisTitle().Replace(raw, "");
                raw = OsisNote().Replace(raw, "");
                raw = (jesus ? JesusOpen.ToString() : "") + OsisQuote().Replace(raw, m =>
                {
                    var who = m.Value.Contains("Jesus", StringComparison.OrdinalIgnoreCase);
                    if (m.Value.Contains("eID=", StringComparison.Ordinal)) { var was = jesus; jesus = false; return was ? JesusClose.ToString() : ""; }
                    if (m.Value.Contains("sID=", StringComparison.Ordinal)) { if (!who) return ""; jesus = true; return JesusOpen.ToString(); }
                    return "";
                });
                // <q who="Jesus">…</q> inside one verse
                raw = OsisJesusContainer().Replace(raw, m => JesusOpen + m.Groups[1].Value + JesusClose);
                if (jesus) raw += JesusClose;
                raw = Tags().Replace(raw, " ");
                c.Add(e.Book, e.Chapter, e.Verse, WebUtility.HtmlDecode(raw));
            }
            else c.Add(e.Book, e.Chapter, e.Verse, CleanPlain(EswordJesus(raw)));
        }
        var lang = module.Language.Length is 2 or 3 ? module.Language.ToLowerInvariant() : languageCode;
        var parsed = c.Done(module.Description.Length > 0 ? module.Description : module.Name, languageCode.Length > 0 ? languageCode : lang);
        return parsed with { ShortName = module.Name.Length <= 12 ? module.Name : module.Name[..12] };
    }

    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex OsisTitle();
    [GeneratedRegex(@"<note\b[^>]*/>|<note\b.*?</note>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex OsisNote();
    [GeneratedRegex(@"<q\b[^>]*(?:sID|eID)=[^>]*/>")] private static partial Regex OsisQuote();
    [GeneratedRegex(@"<q\b[^>]*who=""Jesus""[^>]*>(.*?)</q>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex OsisJesusContainer();

    /// <summary>
    /// Bible text exported from Logos (Word, RTF): the chapter number stands big at the start of the paragraph, without a mark, and verse 1 has no number
    /// ("1 Im Anfang schuf Gott … <sup>2</sup> Und die Erde …"). Such a paragraph is given back with the chapter as a heading and verse 1 marked.
    /// </summary>
    static string? LogosChapterStart(string line, int chapter, int firstMark)
    {
        if (firstMark != 2) return null;
        var m = LeadingChapter().Match(line);
        if (!m.Success || Num(m.Groups["c"].Value) != chapter + 1) return null;
        return "<sup>1</sup>" + m.Groups["t"].Value;
    }

    [GeneratedRegex(@"^\s*(?:\*\*)?(?<c>\d{1,3})(?:\*\*)?\s+(?<t>\D.*)$", RegexOptions.Singleline)] private static partial Regex LeadingChapter();
}
