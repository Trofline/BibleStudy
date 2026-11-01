using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;

namespace BibleStudy.App;

/// <summary>The long Greek entry (Liddell-Scott-Jones, edited by Tyndale House) under a Greek Strong entry; loaded once, then kept in the cache.</summary>
public partial class MainWindow
{
    GreekLsj? _lsj;

    async Task<Control?> LsjCardAsync(string strongId)
    {
        _lsj ??= new GreekLsj(_http, Path.Combine(_root, "cache"));
        IReadOnlyList<LsjEntry> entries;
        try { entries = await _lsj.GetAsync(strongId); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        { return Muted(L("Das LSJ-Lexikon konnte nicht geladen werden: {0}", ex.Message)); }
        if (entries.Count == 0) return null;

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = L("Liddell-Scott-Jones (ausführlich)"), FontWeight = FontWeight.SemiBold });
        var lines = new StackPanel { Spacing = 3 };
        var all = entries.SelectMany(en => en.Lines.Select((l, i) => i == 0 && entries.Count > 1 ? en.Greek + " — " + l : l)).ToList();
        var expanded = false;
        void Fill()
        {
            lines.Children.Clear();
            foreach (var line in expanded ? all : all.Take(6))
                lines.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        }
        Fill();
        stack.Children.Add(lines);
        if (all.Count > 6)
        {
            var more = new Button { Content = L("Mehr anzeigen"), Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
            more.Click += (_, _) => { expanded = !expanded; more.Content = expanded ? L("Weniger anzeigen") : L("Mehr anzeigen"); Fill(); };
            stack.Children.Add(more);
        }
        stack.Children.Add(new TextBlock { Text = L("Quelle: Liddell-Scott-Jones, Greek-English Lexicon, bearbeitet von Tyndale House Cambridge (STEPBible), CC BY 4.0"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        var card = new Border { Child = stack, Padding = new(12), CornerRadius = new(10), BorderThickness = new(1) };
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        return card;
    }
}
