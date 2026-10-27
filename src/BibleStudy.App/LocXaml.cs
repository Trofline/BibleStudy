#pragma warning disable CS0618 // TextBox.Watermark is what the XAML files use
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;

namespace BibleStudy.App;

/// <summary>
/// Translates the fixed texts written in the XAML files once, right after they were loaded (the language is chosen at start).
/// Texts created in code use <c>L("…")</c> themselves, so nothing here touches data the user typed or loaded.
/// </summary>
static class LocXaml
{
    public static void Apply(ILogical root)
    {
        if (Loc.Code == "de") return;
        Do(root);
        foreach (var c in root.GetLogicalDescendants()) Do(c);
    }

    static void Do(ILogical node)
    {
        switch (node)
        {
            case TextBlock tb:
                if (tb.Inlines is { Count: > 0 } inlines)
                {
                    foreach (var run in inlines.OfType<Run>()) { if (run.Text is { Length: > 0 } rt) run.Text = L(rt); }
                }
                else if (tb.Text is { Length: > 0 } t) tb.Text = L(t);
                break;
            case TextBox box:
                if (box.Watermark is { Length: > 0 } w) box.Watermark = L(w);
                break;
            case HeaderedContentControl h when h.Header is string hs && hs.Length > 0:
                h.Header = L(hs);
                break;
            case MenuItem mi when mi.Header is string ms && ms.Length > 0:
                mi.Header = L(ms);
                break;
        }
        if (node is ContentControl cc && cc.Content is string s && s.Length > 0) cc.Content = L(s);

        if (node is Control ctl && ToolTip.GetTip(ctl) is string tip && tip.Length > 0) ToolTip.SetTip(ctl, L(tip));
        if (node is Window win && win.Title is { Length: > 0 } title) win.Title = L(title);
    }
}
