using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>"Explain this word": mark a word in the Bible text, right click the verse and get a short explanation from Wiktionary (CC BY-SA).</summary>
public partial class MainWindow
{
    Window? _wordHelp;

    /// <summary>The word (or short phrase) marked inside the verse the menu was opened on; empty when nothing is marked.</summary>
    /// <summary>Everything marked with the mouse in the verse (no length limit), for copying.</summary>
    static string PickedText(Control anchor)
    {
        var blocks = anchor is SelectableTextBlock own ? [own] : anchor.GetVisualDescendants().OfType<SelectableTextBlock>().ToList();
        return blocks.Select(b => b.SelectedText).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))?.Trim() ?? "";
    }

    /// <summary>The word marked for "explain": the program's own selection (one verse) first, else what the built-in selection holds.</summary>
    string MarkedTextOf(Control anchor)
    {
        if (_sel is { Parts.Count: 1 })
        {
            var t = SelectedText().Trim().Trim('.', ',', ';', ':', '!', '?', '(', ')', '„', '“', '"', '«', '»', '–', '—', '¶');
            return t.Length is >= 2 and <= 60 && t.Count(char.IsWhiteSpace) <= 2 ? t : "";
        }
        return MarkedText(anchor);
    }

    static string MarkedText(Control anchor)
    {
        var blocks = anchor is SelectableTextBlock own ? [own] : anchor.GetVisualDescendants().OfType<SelectableTextBlock>().ToList();
        var text = blocks.Select(b => b.SelectedText).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";
        text = text.Trim().Trim('.', ',', ';', ':', '!', '?', '(', ')', '„', '“', '"', '«', '»', '–', '—', '¶');
        return text.Length is >= 2 and <= 60 && text.Count(char.IsWhiteSpace) <= 2 ? text : "";
    }

    string DictionaryLanguage()
    {
        var code = (TranslationBox.SelectedItem as TranslationInfo)?.Language ?? "";
        return code switch { "deu" or "ger" or "de" => "de", "eng" or "en" => "en", "spa" or "es" => "es", "fra" or "fre" or "fr" => "fr", "rus" or "ru" => "ru", _ => Loc.Code is "de" or "en" or "es" or "fr" or "ru" ? Loc.Code : "en" };
    }

    async Task ExplainWordAsync(string word)
    {
        var lang = DictionaryLanguage();
        var body = new StackPanel { Spacing = 8 };
        var title = new TextBlock { Text = word, FontSize = 26, FontFamily = ScriptureFont, FontWeight = FontWeight.SemiBold };
        title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        var info = new TextBlock { Text = L("Lade …"), TextWrapping = TextWrapping.Wrap };
        body.Children.Add(title); body.Children.Add(info);
        var pageUrl = $"https://{lang}.wiktionary.org/wiki/{Uri.EscapeDataString(word)}";
        var read = new Button { Content = L("Auf Wiktionary lesen"), Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
        read.Click += (_, _) => OpenUrl(pageUrl);
        var source = new TextBlock { Text = L("Quelle: Wiktionary (CC BY-SA 4.0)"), Classes = { "caption" } };
        var close = new Button { Content = L("Schließen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right };
        _wordHelp?.Close();
        var win = new Window { Title = L("Wort erklären: {0}", word), Width = 460, Height = 340, MinWidth = 320, MinHeight = 220, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var dock = new DockPanel { Margin = new(20) };
        DockPanel.SetDock(close, Dock.Bottom); DockPanel.SetDock(source, Dock.Bottom); DockPanel.SetDock(read, Dock.Bottom);
        dock.Children.Add(close); dock.Children.Add(source); dock.Children.Add(read);
        dock.Children.Add(new ScrollViewer { Content = body });
        win.Content = dock; close.Click += (_, _) => win.Close(); win.Closed += (_, _) => _wordHelp = null;
        _wordHelp = win; win.Show(this);

        string? extract = null;
        foreach (var candidate in new[] { word, word.ToLowerInvariant(), char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant() }.Distinct())
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(await _http.GetStreamAsync($"https://{lang}.wiktionary.org/api/rest_v1/page/summary/{Uri.EscapeDataString(candidate)}").WaitAsync(TimeSpan.FromSeconds(10)));
                if (doc.RootElement.TryGetProperty("extract", out var e) && e.GetString() is { Length: > 0 } text) { extract = text; pageUrl = $"https://{lang}.wiktionary.org/wiki/{Uri.EscapeDataString(candidate)}"; break; }
            }
            catch { /* not found under this spelling, or offline: the next one */ }
        }
        info.Text = extract ?? L("Keine Erklärung gefunden. Das Wort ist vielleicht gebeugt: probiere die Grundform, oder lies es auf Wiktionary nach.");
    }
}
