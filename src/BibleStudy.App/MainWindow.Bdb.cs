using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>The long entry of the Brown-Driver-Briggs lexicon under a Hebrew Strong entry (loaded once, then kept in the cache).</summary>
public partial class MainWindow
{
    BdbLexicon? _bdb;

    async Task<Control?> BdbCardAsync(string strongId)
    {
        _bdb ??= new BdbLexicon(_http, Path.Combine(_root, "cache"));
        BdbEntry? entry;
        try { entry = await _bdb.GetAsync(strongId); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException or IOException)
        { return Muted(L("Das BDB-Lexikon konnte nicht geladen werden: {0}", ex.Message)); }
        if (entry is null || entry.Lines.Count == 0) return null;

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = L("Brown-Driver-Briggs (ausführlich)"), FontWeight = FontWeight.SemiBold });
        var lines = new StackPanel { Spacing = 3 };
        var all = entry.Lines;
        var expanded = false;
        void Fill()
        {
            lines.Children.Clear();
            foreach (var line in expanded ? all : all.Take(6))
                lines.Children.Add(new TextBlock { Text = line.TrimStart(), TextWrapping = TextWrapping.Wrap, Margin = new(line.Length - line.TrimStart().Length > 0 ? 14 : 0, 0, 0, 0), FontSize = 13 });
        }
        Fill();
        stack.Children.Add(lines);
        if (all.Count > 6)
        {
            var more = new Button { Content = L("Mehr anzeigen"), Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
            more.Click += (_, _) => { expanded = !expanded; more.Content = expanded ? L("Weniger anzeigen") : L("Mehr anzeigen"); Fill(); };
            stack.Children.Add(more);
        }
        stack.Children.Add(new TextBlock { Text = L("Quelle: Brown-Driver-Briggs Hebrew and English Lexicon (1906, gemeinfrei), digitalisiert vom Open Scriptures Hebrew Lexicon Project, CC BY 4.0"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        var card = new Border { Child = stack, Padding = new(12), CornerRadius = new(10), BorderThickness = new(1) };
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        return card;
    }
}
