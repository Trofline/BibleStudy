using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace BibleStudy.App;

/// <summary>
/// Test aid (control tool "scan_ui"): lists texts on screen that still look German although another language is chosen.
/// It looks at every open window, including the hidden tab pages.
/// </summary>
public partial class MainWindow
{
    static readonly Regex GermanWords = new(
        @"[äöüÄÖÜß]|\b(und|der|die|das|den|dem|des|nicht|mit|für|oder|wird|werden|ein|eine|einen|zum|zur|bei|nach|auf|über|von|noch|kein|keine|Klick|Vers|Verse|Kapitel|Buch|Bücher|Suche|Notiz|Notizen|Dokument|Dokumente|Wort|Wörter|Einstellungen|Speichern|Löschen|Abbrechen|Zurück|Weiter|Fehler|Hilfe|Lesen|Planen|Aufgaben)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    object ScanUi()
    {
        var found = new SortedDictionary<string, string>();
        void Check(string? s, string where)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length < 3 || !GermanWords.IsMatch(s)) return;
            found.TryAdd(s.Length > 160 ? s[..160] + "…" : s, where);
        }
        var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? [this];
        foreach (var w in windows)
        {
            var where = w.Title ?? w.GetType().Name;
            Check(w.Title, "window title");
            foreach (var n in w.GetLogicalDescendants().OfType<ILogical>().Prepend(w))
            {
                if (n is TextBlock tb)
                {
                    Check(tb.Text, where);
                    foreach (var run in tb.Inlines?.OfType<Run>() ?? []) Check(run.Text, where);
                }
                if (n is TextBox box) Check(box.Watermark, where + " (hint)");
                if (n is HeaderedContentControl h && h.Header is string hs) Check(hs, where + " (header)");
                if (n is ContentControl cc && cc.Content is string cs) Check(cs, where);
                if (n is MenuItem mi && mi.Header is string ms) Check(ms, where + " (menu)");
                if (n is Control ctl && ToolTip.GetTip(ctl) is string tip) Check(tip, where + " (tooltip)");
            }
        }
        return new { language = Loc.Code, count = found.Count, texts = found.Take(250).Select(p => new { text = p.Key, where = p.Value }) };
    }
}
