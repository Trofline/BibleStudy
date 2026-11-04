using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// The chiasm finder: looks in a chapter, a stretch of verses or a whole book for mirror structures (A B C … C' B' A') in which verses
/// from the outside in share a word. It points to candidates and names the connecting words; whether a chiasm was meant is for the reader to judge.
/// </summary>
public partial class MainWindow
{
    readonly Dictionary<string, Dictionary<string, int>> _docFreqs = [];

    async Task ShowChiasmFinderAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return;
        var book = _curBook; var chapter = _curChapter;
        var scope = new ComboBox { ItemsSource = new[] { L("Offenes Kapitel"), L("Ganzes Buch"), L("Verse von … bis …") }, SelectedIndex = 0, Margin = new(0, 0, 8, 0) };
        var from = new NumericUpDown { Minimum = 1, Maximum = 200, Value = 1, Increment = 1, FormatString = "0", Width = 110, Margin = new(0, 0, 8, 0), IsVisible = false };
        var to = new NumericUpDown { Minimum = 1, Maximum = 200, Value = 30, Increment = 1, FormatString = "0", Width = 110, Margin = new(0, 0, 8, 0), IsVisible = false };
        var levels = new ComboBox { ItemsSource = new[] { L("mindestens 2 Stufen"), L("mindestens 3 Stufen"), L("mindestens 4 Stufen") }, SelectedIndex = 1, Margin = new(0, 0, 8, 0) };
        var go = new Button { Content = L("Suchen"), Classes = { "primary" }, Margin = new(0, 0, 8, 0) };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 6) };
        var results = new StackPanel { Spacing = 10, Margin = new(0, 0, 14, 0) };
        var close = new Button { Content = L("Schließen"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var hint = new TextBlock
        {
            Text = L("Ein Chiasmus ist ein Spiegelaufbau: Der erste und der letzte Gedanke gehören zusammen, der zweite und der vorletzte ebenso, und in der Mitte steht oft der Höhepunkt. Der Finder vergleicht ganze Verse und sucht von der Mitte nach außen, wo gegenüberliegende Verse ein Wort teilen. Seltene Wörter zählen mehr, Wörter, die durch den ganzen Abschnitt laufen, zählen nicht. Er findet Kandidaten, keine Beweise: Ob ein Aufbau gewollt ist, entscheidest du. Ein Spiegel innerhalb eines einzelnen Verses wird nicht gesucht."),
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0),
        };
        var row = new WrapPanel { Margin = new(0, 6, 0, 0), Children = { scope, from, to, levels, go } };
        var top = new StackPanel { Children = { new TextBlock { Text = L("Übersetzung: {0}", t.ShortName), Classes = { "caption" } }, row, info } };
        var dock = new DockPanel { Margin = new(18) };
        DockPanel.SetDock(close, Dock.Bottom); DockPanel.SetDock(hint, Dock.Bottom); DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(close); dock.Children.Add(hint); dock.Children.Add(top); dock.Children.Add(new ScrollViewer { Content = results });
        var win = new Window { Title = L("Chiasmus-Finder") + " · " + BookName(book) + " " + chapter, Width = 880, Height = 780, MinWidth = 520, MinHeight = 420, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock };
        close.Click += (_, _) => win.Close();
        scope.SelectionChanged += (_, _) => { from.IsVisible = to.IsVisible = scope.SelectedIndex == 2; };

        async Task RunAsync()
        {
            go.IsEnabled = false; results.Children.Clear(); info.Text = L("Berechne …");
            try
            {
                var corpus = await StatsCorpusAsync(t);
                if (corpus is null) { info.Text = L("Diese Übersetzung ist noch nicht für die Suche geladen. Such einmal etwas, dann geht es."); return; }
                var passage = corpus.Where(v => v.Ref.Book == book).Where(v => scope.SelectedIndex switch
                    {
                        0 => v.Ref.Chapter == chapter,
                        2 => v.Ref.Chapter == chapter && v.Ref.Verse >= (int)(from.Value ?? 1) && v.Ref.Verse <= (int)(to.Value ?? 200),
                        _ => true,
                    })
                    .OrderBy(v => v.Ref.Chapter).ThenBy(v => v.Ref.Verse).ToList();
                if (passage.Count < 4) { info.Text = L("Der Abschnitt ist zu kurz für einen Spiegelaufbau."); return; }
                var minLevels = levels.SelectedIndex + 2;
                var found = await Task.Run(() =>
                {
                    if (!_docFreqs.TryGetValue(t.Id, out var df)) _docFreqs[t.Id] = df = ChiasmFinder.DocumentFrequencies(corpus);
                    return ChiasmFinder.Find(passage, df, corpus.Count, minLevels, 15);
                });
                var scopeName = scope.SelectedIndex == 1 ? BookName(book) : $"{BookName(book)} {chapter}";
                if (found.Count == 0) { info.Text = L("In {0} wurde kein Spiegelaufbau mit {1} Stufen gefunden.", scopeName, minLevels); return; }
                info.Text = L("{0} Kandidaten in {1}, der stärkste zuerst. Ein Klick auf die Überschrift öffnet die Stelle.", found.Count, scopeName);
                var byRef = passage.ToDictionary(v => v.Ref, v => v.Text);
                var forms = await DisplayFormsAsync(t, corpus);
                foreach (var c in found) results.Children.Add(ChiasmCard(c, byRef, forms, win));
            }
            catch (Exception ex) { info.Text = L("Suche fehlgeschlagen: ") + ex.Message; }
            finally { go.IsEnabled = true; }
        }

        go.Click += async (_, _) => await RunAsync();
        win.Show(this);
        await RunAsync();
    }

    Control ChiasmCard(Chiasm c, Dictionary<VerseRef, string> text, Dictionary<string, string> forms, Window win)
    {
        string Show(string w) => forms.GetValueOrDefault(w, w);
        string Ref(VerseRef r) => c.From.Chapter == c.To.Chapter ? r.Verse.ToString() : $"{r.Chapter},{r.Verse}";
        static string Cut(string s) => s.Length <= 110 ? s : s[..107].TrimEnd() + "…";

        var span = Ref(c.From) + "–" + Ref(c.To);
        var title = new Button
        {
            Content = L("{0} {1} · {2} Stufen · Mitte: {3}", BookName(c.From.Book), c.From.Chapter == c.To.Chapter ? c.From.Chapter + "," + span : span, c.Levels.Count,
                c.SingleCenter ? Ref(c.CenterFrom) : Ref(c.CenterFrom) + "/" + Ref(c.CenterTo)),
            HorizontalAlignment = HorizontalAlignment.Left, FontWeight = FontWeight.SemiBold,
        };
        title.Click += async (_, _) => { win.Close(); await GoToAsync(c.From.Book, c.From.Chapter, c.From.Verse); };
        var stack = new StackPanel { Spacing = 4, Children = { title } };

        foreach (var l in c.Levels)
        {
            var grid = new Grid { ColumnDefinitions = new("34,*,*") };
            var tag = new TextBlock { Text = l.Label, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Top };
            tag.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
            Grid.SetColumn(tag, 0);
            var left = new TextBlock { Text = $"{Ref(l.Left)}  {Cut(text.GetValueOrDefault(l.Left, ""))}", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 10, 0) };
            var right = new TextBlock { Text = $"{Ref(l.Right)}  {Cut(text.GetValueOrDefault(l.Right, ""))}", TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(left, 1); Grid.SetColumn(right, 2);
            grid.Children.Add(tag); grid.Children.Add(left); grid.Children.Add(right);
            var word = new TextBlock { Text = l.LeftWord == l.RightWord ? $"„{Show(l.LeftWord)}“" : $"„{Show(l.LeftWord)}“ ↔ „{Show(l.RightWord)}“", Classes = { "caption" }, Margin = new(34, 0, 0, 4) };
            stack.Children.Add(grid); stack.Children.Add(word);
        }
        if (c.SingleCenter && text.GetValueOrDefault(c.CenterFrom) is { } mid)
        {
            var m = new TextBlock { Text = L("Mitte") + $"  {Ref(c.CenterFrom)}  {Cut(mid)}", TextWrapping = TextWrapping.Wrap, FontStyle = FontStyle.Italic, Margin = new(34, 2, 0, 0) };
            stack.Children.Add(m);
        }
        var card = new Border { CornerRadius = new(10), Padding = new(12, 10), BorderThickness = new(1), Child = stack };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        return card;
    }
}
