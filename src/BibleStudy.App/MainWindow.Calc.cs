using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// The word calculator: count a search per book, subtract the hits that lie inside another search ("Christus" minus "Jesus Christus"),
/// relate the rest to the words of each book (per 1000 words) and see which book comes out on top.
/// </summary>
public partial class MainWindow
{
    readonly Button _calcBtn = new() { Margin = new(0, 0, 8, 4) };

    void InitCalculator()
    {
        _calcBtn.Content = L("Wort-Rechner");
        ToolTip.SetTip(_calcBtn, L("Zählt ein Wort je Buch, zieht Treffer einer zweiten Suche ab und rechnet auf 1000 Wörter um"));
        _calcBtn.Click += async (_, _) => await ShowCalculatorAsync();
        if (SearchScopeBox.Parent?.Parent is Panel options) options.Children.Add(_calcBtn);
    }

    async Task ShowCalculatorAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return;
        var includeBox = new TextBox { PlaceholderText = L("Zählen, z. B. Christus"), Text = SearchBox.Text?.Trim() ?? "" };
        var excludeBox = new TextBox { PlaceholderText = L("Abziehen (leer lassen, wenn nichts), z. B. „Jesus Christus“ ODER „Christus Jesus“") };
        var whole = new CheckBox { Content = L("Nur ganze Wörter"), IsChecked = true, Margin = new(0, 0, 12, 0) };
        var sort = new ComboBox { ItemsSource = new[] { L("Sortiert nach: pro 1000 Wörter"), L("Sortiert nach: Anzahl"), L("Sortiert nach: Bibelreihenfolge") }, SelectedIndex = 0, Margin = new(0, 0, 12, 0) };
        var go = new Button { Content = L("Berechnen"), Classes = { "primary" }, Margin = new(0, 0, 8, 0) };
        var copy = new Button { Content = L("Als Tabelle kopieren"), IsEnabled = false };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 6) };
        var header = new Grid { ColumnDefinitions = CalcColumns(), Margin = new(10, 0, 10, 2) };
        var list = new ListBox { Background = Brushes.Transparent };
        var close = new Button { Content = L("Schließen"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var hint = new TextBlock
        {
            Text = L("„Abziehen“ nimmt die Treffer weg, die innerhalb eines Treffers der zweiten Suche liegen: Wird „Jesus Christus“ abgezogen, zählt nur das „Christus“ ohne „Jesus“ davor. Kleine Bücher können mit wenigen Treffern weit oben stehen; die Anzahl steht daneben."),
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0),
        };
        string[] heads = [L("Buch"), L("Treffer"), L("Abgezogen"), L("Übrig"), L("Wörter"), L("pro 1000")];
        for (var i = 0; i < heads.Length; i++)
        {
            var h = new TextBlock { Text = heads[i], Classes = { "caption" }, HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right };
            Grid.SetColumn(h, i); header.Children.Add(h);
        }

        var buttons = new WrapPanel { Margin = new(0, 8, 0, 0), Children = { whole, sort, go, copy } };
        var top = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = L("Übersetzung: {0}", t.ShortName), Classes = { "caption" } }, includeBox, excludeBox, buttons, info, header } };
        var dock = new DockPanel { Margin = new(18) };
        DockPanel.SetDock(close, Dock.Bottom); DockPanel.SetDock(hint, Dock.Bottom); DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(close); dock.Children.Add(hint); dock.Children.Add(top); dock.Children.Add(list);
        var win = new Window { Title = L("Wort-Rechner") + " · " + t.ShortName, Width = 700, Height = 720, MinWidth = 520, MinHeight = 420, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock };
        close.Click += (_, _) => win.Close();

        List<CalcRow> rows = [];
        string tsv = "";

        void Show()
        {
            if (rows.Count == 0) { list.ItemsSource = null; return; }
            var order = _books.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
            IEnumerable<CalcRow> sorted = sort.SelectedIndex switch
            {
                1 => rows.OrderByDescending(r => r.Rest).ThenBy(r => order.GetValueOrDefault(r.Book, int.MaxValue)),
                2 => rows.OrderBy(r => order.GetValueOrDefault(r.Book, int.MaxValue)),
                _ => rows.OrderByDescending(r => r.Per1000).ThenByDescending(r => r.Rest),
            };
            var shown = sorted.Where(r => r.Count > 0).ToList();
            var items = new List<ListBoxItem>();
            for (var i = 0; i < shown.Count; i++) items.Add(CalcItem(shown[i], i == 0 && sort.SelectedIndex != 2, win));
            var all = new CalcRow("", rows.Sum(r => r.Count), rows.Sum(r => r.Subtracted), rows.Sum(r => r.Words), null);
            items.Add(CalcItem(all, false, win, L("Ganze Bibel")));
            list.ItemsSource = items;
        }

        async Task RunAsync()
        {
            var include = includeBox.Text?.Trim() ?? "";
            if (include.Length == 0) { info.Text = L("Gib oben ein, was gezählt werden soll."); return; }
            go.IsEnabled = false; copy.IsEnabled = false; info.Text = L("Berechne …"); list.ItemsSource = null;
            try
            {
                var corpus = await StatsCorpusAsync(t);
                if (corpus is null) { info.Text = L("Diese Übersetzung ist noch nicht für die Suche geladen. Such einmal etwas, dann geht es."); return; }
                _strongIndexes.TryGetValue(t.Id, out var strongs);
                var options = new SearchOptions(whole.IsChecked == true ? WordMatch.Whole : WordMatch.Prefix);
                var exclude = excludeBox.Text ?? "";
                var result = await Task.Run(() => WordCalculator.Run(corpus, include, exclude, strongs, options));
                if (result.Error is not null) { info.Text = result.Error; return; }
                if (result.NeedsStrongs) { info.Text = L("Diese Suche braucht die Strong-Nummern. Such sie einmal im Suchfeld, dann wird die Konkordanz aufgebaut."); return; }
                rows = [.. result.Rows];
                if (rows.Count == 0) { info.Text = L("Keine Treffer in {0}.", t.ShortName); return; }
                var best = rows.Where(r => r.Rest > 0).OrderByDescending(r => r.Per1000).FirstOrDefault();
                var total = rows.Sum(r => r.Rest);
                var words = rows.Sum(r => r.Words);
                info.Text = best is null
                    ? L("Nach dem Abziehen bleibt nichts übrig.")
                    : L("Am dichtesten in {0}: {1} pro 1000 Wörter ({2} Treffer). In der ganzen Bibel: {3} pro 1000 Wörter.", BookName(best.Book), best.Per1000.ToString("0.00", CultureInfo.CurrentCulture), best.Rest, (words == 0 ? 0 : total * 1000.0 / words).ToString("0.00", CultureInfo.CurrentCulture));
                tsv = string.Join("\n", new[] { string.Join("\t", heads) }.Concat(rows.OrderBy(r => _books.ToList().FindIndex(b => b.Id == r.Book))
                    .Select(r => string.Join("\t", BookName(r.Book), r.Count, r.Subtracted, r.Rest, r.Words, r.Per1000.ToString("0.00", CultureInfo.CurrentCulture)))));
                copy.IsEnabled = true;
                Show();
            }
            catch (Exception ex) { info.Text = L("Rechnen fehlgeschlagen: ") + ex.Message; }
            finally { go.IsEnabled = true; }
        }

        go.Click += async (_, _) => await RunAsync();
        includeBox.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await RunAsync(); };
        excludeBox.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await RunAsync(); };
        sort.SelectionChanged += (_, _) => Show();
        copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(win)?.Clipboard is { } c) await c.SetTextAsync(tsv); };
        list.SelectionChanged += async (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: VerseRef r }) { win.Close(); await GoToAsync(r.Book, r.Chapter, r.Verse); }
            list.SelectedItem = null;
        };
        win.Show(this);
        if (includeBox.Text!.Length > 0) await RunAsync();
    }

    static ColumnDefinitions CalcColumns() => new("*,70,90,70,80,80");

    ListBoxItem CalcItem(CalcRow r, bool best, Window win, string? title = null)
    {
        var grid = new Grid { ColumnDefinitions = CalcColumns() };
        string[] cells = [title ?? BookName(r.Book), r.Count.ToString(), r.Subtracted.ToString(), r.Rest.ToString(), r.Words.ToString("N0", CultureInfo.CurrentCulture), r.Per1000.ToString("0.00", CultureInfo.CurrentCulture)];
        for (var i = 0; i < cells.Length; i++)
        {
            var tb = new TextBlock { Text = cells[i], HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, FontWeight = best || title is not null ? FontWeight.SemiBold : FontWeight.Normal };
            if (best && i == 5) tb.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
            Grid.SetColumn(tb, i); grid.Children.Add(tb);
        }
        return new ListBoxItem { Content = grid, Tag = r.First is { } f && title is null ? f : null, IsEnabled = true };
    }
}
