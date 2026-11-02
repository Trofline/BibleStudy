using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// "Where does the word occur?" as a picture: a bar for every book of the testament it belongs to, and a ring of the words the translation
/// uses for it. The counts come from the Strong's numbers of the KJV; a click on a bar jumps to the first place in that book.
/// </summary>
public partial class MainWindow
{
    async Task ShowWordChartAsync(string rawId, string word)
    {
        var id = WordAnalyzer.Normalize(rawId);
        if (id is null) return;
        var hebrew = id[0] == 'H';
        var status = new TextBlock { Text = L("Berechne …"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        var stack = new StackPanel { Spacing = 8, Margin = new(0, 0, 14, 0) };
        var close = new Button { Content = L("Schließen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var dock = new DockPanel { Margin = new(20) };
        DockPanel.SetDock(close, Dock.Bottom);
        dock.Children.Add(close); dock.Children.Add(new ScrollViewer { Content = stack });
        var win = new Window { Title = L("Vorkommen") + " · " + id, Width = 980, Height = 760, MinWidth = 520, MinHeight = 420, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock };
        close.Click += (_, _) => win.Close();
        stack.Children.Add(status);
        win.Show(this);

        if (_analyzer is null) await LoadAnalyzerAsync(t => status.Text = t);
        if (_analyzer is null) return;
        var analyzer = _analyzer;
        stack.Children.Clear();

        StrongsEntry? entry = null;
        try { entry = await _strongs.LookupAsync(id); } catch { /* the chart does not need the dictionary */ }
        var total = analyzer.Count(id);
        var verses = analyzer.Occurrences(id).Distinct().Count();
        var head = new TextBlock { Text = id + (entry is null ? "" : "  ·  " + entry.Lemma), FontSize = 24, FontFamily = ScriptureFont, FontWeight = FontWeight.SemiBold };
        head.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        stack.Children.Add(head);
        var sub = new List<string>();
        if (entry is not null && entry.Translit.Length > 0) sub.Add(entry.Translit);
        if (entry is not null && entry.Definition.Length > 0) sub.Add(Mean(entry.Definition));
        else if (word.Trim().Length > 0) sub.Add($"„{word.Trim()}“");
        if (sub.Count > 0) stack.Children.Add(new TextBlock { Text = string.Join("  ·  ", sub), TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        stack.Children.Add(new TextBlock { Text = total == 0 ? L("Kommt in den Strong-Daten der KJV nicht vor.") : L("{0} Mal in {1} Versen", total, verses), FontWeight = FontWeight.SemiBold });
        if (total == 0) return;

        // bars: the books of the testament the word belongs to
        var books = _books.Where(b => BookCategories.IsNewTestament(BookCategories.Of(b.Id)) == !hebrew).OrderBy(b => b.Order).ToList();
        var counts = analyzer.BookCounts(id);
        var other = counts.Where(c => !books.Any(b => b.Id == c.Key)).Sum(c => c.Value);
        stack.Children.Add(new TextBlock { Text = hebrew ? L("Vorkommen im Alten Testament") : L("Vorkommen im Neuen Testament"), FontSize = 18, Margin = new(0, 10, 0, 0) });
        var bars = new BookBarChart();
        bars.SetData(books.Select(b => (b.Id, ShortBook(b), b.CommonName)).ToList(), counts);
        bars.BookClicked += book =>
        {
            if (analyzer.Occurrences(id).FirstOrDefault(r => r.Book == book) is { Book.Length: > 0 } r) { win.Close(); _ = GoToAsync(r.Book, r.Chapter, r.Verse); }
        };
        stack.Children.Add(bars);
        stack.Children.Add(new TextBlock { Text = L("Ein Klick auf einen Balken springt zur ersten Stelle in diesem Buch."), Classes = { "caption" } });
        if (other > 0) stack.Children.Add(new TextBlock { Text = L("{0} Vorkommen im anderen Testament", other), Classes = { "caption" } });

        // ring: how the translation renders the word
        var renderings = analyzer.Renderings(id);
        if (renderings.Count > 0)
        {
            stack.Children.Add(new TextBlock { Text = L("Wie die KJV das Wort übersetzt"), FontSize = 18, Margin = new(0, 14, 0, 0) });
            var shown = renderings.Take(12).ToList();
            if (renderings.Count > 12) shown.Add((L("übrige"), renderings.Skip(12).Sum(r => r.Count)));
            var ring = new RenderingRing();
            ring.SetData(shown, renderings.Count.ToString(), L("Übersetzungen in {0} Vorkommen", renderings.Sum(r => r.Count)));
            var legend = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new(24, 0, 0, 0) };
            for (var i = 0; i < shown.Count; i++)
            {
                var dot = new Border { Width = 12, Height = 12, CornerRadius = new(6), Background = new SolidColorBrush(RenderingRing.ColorFor(i)), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0) };
                legend.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, new TextBlock { Text = $"{shown[i].Word}  {shown[i].Count}×" } } });
            }
            stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { ring, legend } });
        }
        stack.Children.Add(new TextBlock { Text = L("Gezählt nach den Strong-Nummern der KJV; andere Übersetzungen können abweichen."), Classes = { "caption" }, Margin = new(0, 10, 0, 0) });
    }
}
