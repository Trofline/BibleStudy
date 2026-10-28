using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>An on-screen keyboard for Greek and Hebrew letters next to the search boxes, and typing with Latin letters ("logos" → λογος).</summary>
public partial class MainWindow
{
    static readonly string[] GreekKeys = ["α", "β", "γ", "δ", "ε", "ζ", "η", "θ", "ι", "κ", "λ", "μ", "ν", "ξ", "ο", "π", "ρ", "σ", "ς", "τ", "υ", "φ", "χ", "ψ", "ω"];
    static readonly string[] HebrewKeys = ["א", "ב", "ג", "ד", "ה", "ו", "ז", "ח", "ט", "י", "כ", "ך", "ל", "מ", "ם", "נ", "ן", "ס", "ע", "פ", "ף", "צ", "ץ", "ק", "ר", "ש", "ת"];

    void InitKeyboards()
    {
        if (SearchScopeBox.Parent?.Parent is Panel options) options.Children.Insert(Math.Min(2, options.Children.Count), KeyboardButton(SearchBox, new(0, 0, 8, 4)));
        if (StrongSearchBox.Parent is Panel strongPanel) strongPanel.Children.Insert(1, KeyboardButton(StrongSearchBox, new(0)));
    }

    static void InsertText(TextBox target, string s)
    {
        var text = target.Text ?? "";
        var at = Math.Clamp(target.CaretIndex, 0, text.Length);
        target.Text = text.Insert(at, s);
        target.CaretIndex = at + s.Length;
        target.Focus();
    }

    Button KeyboardButton(TextBox target, Thickness margin)
    {
        var button = new Button { Content = "α א", Classes = { "seg" }, Margin = margin, HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(button, L("Tastatur für griechische und hebräische Buchstaben"));
        button.Flyout = new Flyout { Content = BuildKeyboard(target) };
        return button;
    }

    Control BuildKeyboard(TextBox target)
    {
        Control Keys(string[] keys, bool rtl)
        {
            var wrap = new WrapPanel { Width = 340, FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            foreach (var k in keys)
            {
                var key = k;
                var b = new Button { Content = key, Width = 36, Height = 36, Padding = new(0), Margin = new(2), FontSize = 18, HorizontalContentAlignment = HorizontalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
                b.Click += (_, _) => InsertText(target, key);
                wrap.Children.Add(b);
            }
            return wrap;
        }

        var latin = new TextBox { Watermark = L("Mit lateinischen Buchstaben tippen: logos, shalom …"), Width = 340 };
        var toGreek = new Button { Content = L("→ Griechisch"), Margin = new(0, 0, 6, 0) };
        var toHebrew = new Button { Content = L("→ Hebräisch"), Margin = new(0, 0, 6, 0) };
        var back = new Button { Content = "⌫" };
        ToolTip.SetTip(back, L("Letztes Zeichen löschen"));
        var preview = new TextBlock { Classes = { "caption" }, FontSize = 14 };
        latin.TextChanged += (_, _) => preview.Text = (latin.Text ?? "").Length == 0 ? "" : $"{Transliteration.Greek(latin.Text!)}   ·   {Transliteration.Hebrew(latin.Text!)}";
        toGreek.Click += (_, _) => { if ((latin.Text ?? "").Length > 0) { InsertText(target, Transliteration.Greek(latin.Text!)); latin.Text = ""; } };
        toHebrew.Click += (_, _) => { if ((latin.Text ?? "").Length > 0) { InsertText(target, Transliteration.Hebrew(latin.Text!)); latin.Text = ""; } };
        back.Click += (_, _) =>
        {
            var t = target.Text ?? ""; var at = Math.Clamp(target.CaretIndex, 0, t.Length);
            if (at > 0) { target.Text = t.Remove(at - 1, 1); target.CaretIndex = at - 1; }
            target.Focus();
        };
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = L("Griechisch"), FontWeight = FontWeight.SemiBold }, Keys(GreekKeys, false),
                new TextBlock { Text = L("Hebräisch"), FontWeight = FontWeight.SemiBold, Margin = new(0, 6, 0, 0) }, Keys(HebrewKeys, true),
                new TextBlock { Text = L("Oder mit lateinischen Buchstaben"), FontWeight = FontWeight.SemiBold, Margin = new(0, 6, 0, 0) },
                latin, preview, new WrapPanel { Children = { toGreek, toHebrew, back } },
                new TextBlock { Text = L("Hebräisch ohne Vokale; ē und ō stehen für η und ω. Akzente sind bei der Suche egal."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 },
            },
        };
    }
}
