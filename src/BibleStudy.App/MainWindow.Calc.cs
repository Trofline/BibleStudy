using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// The word calculator: count a search per book, subtract the hits that lie inside another search ("Christus" minus "Jesus Christus"),
/// relate the rest to the words of each book (per 1000 words) and see which book comes out on top. Books can be put together (by writer,
/// by group, by testament), a second word can be counted next to it for a ratio, the table can be saved as CSV, and the barcode shows
/// in which chapters the hits stand.
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

    static string CategoryName(BookCategory c) => c switch
    {
        BookCategory.Law => L("Gesetz"), BookCategory.History => L("Geschichte"), BookCategory.Poetry => L("Poesie"),
        BookCategory.MajorProphets => L("Große Propheten"), BookCategory.MinorProphets => L("Kleine Propheten"), BookCategory.Gospels => L("Evangelien"),
        BookCategory.Acts => L("Apostelgeschichte"), BookCategory.Paul => L("Paulus"), BookCategory.General => L("Briefe"), BookCategory.Revelation => L("Offenbarung"),
        _ => L("Weitere Bücher"),
    };

    /// <summary>How the books are put together: 0 single books, 1 by writer, 2 by group, 3 by testament.</summary>
    static Func<string, string> CalcKey(int mode) => mode switch
    {
        1 => AuthorMap.Of,
        2 => id => CategoryName(BookCategories.Of(id)),
        3 => id => BookCategories.IsNewTestament(BookCategories.Of(id)) ? L("Neues Testament") : L("Altes Testament"),
        _ => id => id,
    };

    async Task ShowCalculatorAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return;
        var includeBox = new TextBox { PlaceholderText = L("Zählen, z. B. Christus"), Text = SearchBox.Text?.Trim() ?? "" };
        var excludeBox = new TextBox { PlaceholderText = L("Abziehen (leer lassen, wenn nichts), z. B. „Jesus Christus“ ODER „Christus Jesus“") };
        var compareBox = new TextBox { PlaceholderText = L("Vergleichen mit (leer lassen, wenn nichts), z. B. Jesus") };
        var whole = new CheckBox { Content = L("Nur ganze Wörter"), IsChecked = true, Margin = new(0, 0, 12, 0) };
        var groupBox = new ComboBox { ItemsSource = new[] { L("Je Buch"), L("Je Verfasser"), L("Je Gruppe"), L("Je Testament") }, SelectedIndex = 0, Margin = new(0, 0, 12, 0) };
        var sort = new ComboBox { ItemsSource = new[] { L("Sortiert nach: pro 1000 Wörter"), L("Sortiert nach: Anzahl"), L("Sortiert nach: Bibelreihenfolge"), L("Sortiert nach: Verhältnis") }, SelectedIndex = 0, Margin = new(0, 0, 12, 0) };
        var go = new Button { Content = L("Berechnen"), Classes = { "primary" }, Margin = new(0, 0, 8, 0) };
        var copy = new Button { Content = L("Als Tabelle kopieren"), IsEnabled = false, Margin = new(0, 0, 8, 0) };
        var csv = new Button { Content = L("Als CSV speichern"), IsEnabled = false };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 6) };
        var barcode = new ChapterBarcode { IsVisible = false, Margin = new(0, 0, 0, 8) };
        var barcodeTitle = new TextBlock { Text = L("Wo die Treffer stehen: ein Streifen je Kapitel. Ein Klick öffnet das Kapitel."), Classes = { "caption" }, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        var header = new Grid { Margin = new(10, 0, 10, 2) };
        var list = new ListBox { Background = Brushes.Transparent };
        var close = new Button { Content = L("Schließen"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var hint = new TextBlock
        {
            Text = L("„Abziehen“ nimmt die Treffer weg, die innerhalb eines Treffers der zweiten Suche liegen: Wird „Jesus Christus“ abgezogen, zählt nur das „Christus“ ohne „Jesus“ davor. Kleine Bücher können mit wenigen Treffern weit oben stehen; die Anzahl steht daneben. „Vergleichen mit“ zählt ein zweites Wort je Buch dazu und rechnet aus, wie viele Treffer auf einen Treffer des zweiten Wortes kommen."),
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0),
        };

        var buttons = new WrapPanel { Margin = new(0, 8, 0, 0), Children = { whole, groupBox, sort, go, copy, csv } };
        var top = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = L("Übersetzung: {0}", t.ShortName), Classes = { "caption" } }, includeBox, excludeBox, compareBox, buttons, info, barcodeTitle, barcode, header } };
        var dock = new DockPanel { Margin = new(18) };
        DockPanel.SetDock(close, Dock.Bottom); DockPanel.SetDock(hint, Dock.Bottom); DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(close); dock.Children.Add(hint); dock.Children.Add(top); dock.Children.Add(list);
        var win = new Window { Title = L("Wort-Rechner") + " · " + t.ShortName, Width = 820, Height = 760, MinWidth = 560, MinHeight = 460, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = dock, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } };
        close.Click += (_, _) => win.Close();

        List<CalcRow> rows = [];
        bool withCompare = false;
        List<string[]> table = [];   // the shown table, first row = headings

        string[] Heads() => withCompare
            ? [groupBox.SelectedIndex == 0 ? L("Buch") : L("Gruppe"), L("Treffer"), L("Abgezogen"), L("Übrig"), L("Wörter"), L("pro 1000"), L("Vergleich"), L("Verhältnis")]
            : [groupBox.SelectedIndex == 0 ? L("Buch") : L("Gruppe"), L("Treffer"), L("Abgezogen"), L("Übrig"), L("Wörter"), L("pro 1000")];

        void BuildHeader()
        {
            header.Children.Clear();
            header.ColumnDefinitions = CalcColumns(withCompare);
            var heads = Heads();
            for (var i = 0; i < heads.Length; i++)
            {
                var h = new TextBlock { Text = heads[i], Classes = { "caption" }, HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right };
                Grid.SetColumn(h, i); header.Children.Add(h);
            }
        }
        BuildHeader();

        string Num(double d) => d.ToString("0.00", CultureInfo.CurrentCulture);

        void Show()
        {
            if (rows.Count == 0) { list.ItemsSource = null; return; }
            BuildHeader();
            var mode = groupBox.SelectedIndex;
            var key = CalcKey(mode);
            var bookIds = _books.Select(b => b.Id).ToList();
            var order = bookIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            var grouped = mode == 0 ? rows : WordCalculator.Group(rows, key, bookIds);
            // position of a group = its earliest book
            var groupOrder = new Dictionary<string, int>();
            foreach (var r in rows) { var k = key(r.Book); groupOrder[k] = Math.Min(groupOrder.GetValueOrDefault(k, int.MaxValue), order.GetValueOrDefault(r.Book, int.MaxValue)); }
            int Pos(CalcRow r) => groupOrder.GetValueOrDefault(r.Book, int.MaxValue);
            string Name(CalcRow r) => mode == 0 ? BookName(r.Book) : r.Book;

            IEnumerable<CalcRow> sorted = sort.SelectedIndex switch
            {
                1 => grouped.OrderByDescending(r => r.Rest).ThenBy(Pos),
                2 => grouped.OrderBy(Pos),
                3 when withCompare => grouped.OrderByDescending(r => r.Ratio).ThenByDescending(r => r.Rest),
                _ => grouped.OrderByDescending(r => r.Per1000).ThenByDescending(r => r.Rest),
            };
            var shown = sorted.Where(r => r.Count > 0 || r.Other > 0).ToList();
            var all = new CalcRow("", rows.Sum(r => r.Count), rows.Sum(r => r.Subtracted), rows.Sum(r => r.Words), null, rows.Sum(r => r.Other));
            var items = new List<ListBoxItem>();
            for (var i = 0; i < shown.Count; i++) items.Add(CalcItem(shown[i], i == 0 && sort.SelectedIndex is 0 or 1 or 3, Name(shown[i]), withCompare));
            items.Add(CalcItem(all, false, L("Ganze Bibel"), withCompare));
            list.ItemsSource = items;

            table = [Heads()];
            foreach (var r in grouped.Where(r => r.Count > 0 || r.Other > 0).OrderBy(Pos))
            {
                var line = new List<string> { Name(r), r.Count.ToString(), r.Subtracted.ToString(), r.Rest.ToString(), r.Words.ToString(), Num(r.Per1000) };
                if (withCompare) { line.Add(r.Other.ToString()); line.Add(r.Other == 0 ? "" : Num(r.Ratio)); }
                table.Add([.. line]);
            }
        }

        async Task RunAsync()
        {
            var include = includeBox.Text?.Trim() ?? "";
            if (include.Length == 0) { info.Text = L("Gib oben ein, was gezählt werden soll."); return; }
            go.IsEnabled = false; copy.IsEnabled = false; csv.IsEnabled = false; barcode.IsVisible = false; barcodeTitle.IsVisible = false; info.Text = L("Berechne …"); list.ItemsSource = null;
            try
            {
                var corpus = await StatsCorpusAsync(t);
                if (corpus is null) { info.Text = L("Diese Übersetzung ist noch nicht für die Suche geladen. Such einmal etwas, dann geht es."); return; }
                _strongIndexes.TryGetValue(t.Id, out var strongs);
                var options = new SearchOptions(whole.IsChecked == true ? WordMatch.Whole : WordMatch.Prefix);
                var exclude = excludeBox.Text ?? "";
                var compare = compareBox.Text?.Trim() ?? "";
                var result = await Task.Run(() => WordCalculator.Run(corpus, include, exclude, strongs, options, compare));
                if (result.Error is not null) { info.Text = result.Error; return; }
                if (result.NeedsStrongs) { info.Text = L("Diese Suche braucht die Strong-Nummern. Such sie einmal im Suchfeld, dann wird die Konkordanz aufgebaut."); return; }
                rows = [.. result.Rows];
                withCompare = compare.Length > 0;
                if (rows.Count == 0) { info.Text = L("Keine Treffer in {0}.", t.ShortName); return; }
                var best = rows.Where(r => r.Rest > 0).OrderByDescending(r => r.Per1000).FirstOrDefault();
                var total = rows.Sum(r => r.Rest);
                var words = rows.Sum(r => r.Words);
                info.Text = best is null
                    ? L("Nach dem Abziehen bleibt nichts übrig.")
                    : L("Am dichtesten in {0}: {1} pro 1000 Wörter ({2} Treffer). In der ganzen Bibel: {3} pro 1000 Wörter.", BookName(best.Book), Num(best.Per1000), best.Rest, Num(words == 0 ? 0 : total * 1000.0 / words));
                if (withCompare && rows.Sum(r => r.Other) is var other && other > 0)
                    info.Text += " " + L("Auf einen Treffer von „{0}“ kommen in der ganzen Bibel {1} Treffer.", compare, Num((double)total / other));
                if (result.Chapters is { Count: > 0 } chapters)
                {
                    barcode.SetData(BarcodeCells(corpus, chapters), ShortBookId, BookName);
                    barcode.IsVisible = true; barcodeTitle.IsVisible = true;
                }
                copy.IsEnabled = true; csv.IsEnabled = true;
                Show();
            }
            catch (Exception ex) { info.Text = L("Rechnen fehlgeschlagen: ") + ex.Message; }
            finally { go.IsEnabled = true; }
        }

        go.Click += async (_, _) => await RunAsync();
        foreach (var box in new[] { includeBox, excludeBox, compareBox })
            box.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await RunAsync(); };
        sort.SelectionChanged += (_, _) => Show();
        groupBox.SelectionChanged += (_, _) => Show();
        copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(win)?.Clipboard is { } c) await c.SetTextAsync(string.Join("\n", table.Select(r => string.Join("\t", r)))); };
        csv.Click += async (_, _) =>
        {
            var file = await win.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L("Als CSV speichern"), SuggestedFileName = $"{L("Wort-Rechner")}-{includeBox.Text?.Trim()}.csv".Replace(' ', '_'),
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            });
            await using var target = await file.SaveTargetAsync();
        if (target?.Path is not { } path) return;
            try { await File.WriteAllTextAsync(path, CalcCsv(table), new UTF8Encoding(true)); info.Text = L("Gespeichert: {0}", path); }
            catch (Exception ex) { info.Text = L("Speichern fehlgeschlagen: ") + ex.Message; }
        };
        barcode.ChapterClicked += async (book, chapter) => { win.Close(); await GoToAsync(book, chapter); };
        list.SelectionChanged += async (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: VerseRef r }) { win.Close(); await GoToAsync(r.Book, r.Chapter, r.Verse); }
            list.SelectedItem = null;
        };
        win.Show(this);
        if (includeBox.Text!.Length > 0) await RunAsync();
    }

    /// <summary>Semicolon-free CSV (comma, quotes where needed); text that a spreadsheet would read as a formula gets a leading apostrophe.</summary>
    internal static string CalcCsv(IEnumerable<string[]> table)
    {
        static string Cell(string s)
        {
            if (s.Length > 0 && "=+-@\t\r".Contains(s[0]) && !double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out _)) s = "'" + s;
            return s.IndexOfAny([',', '"', '\n', '\r', ';']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        return string.Join("\r\n", table.Select(r => string.Join(",", r.Select(Cell)))) + "\r\n";
    }

    static ColumnDefinitions CalcColumns(bool compare = false) => compare ? new("*,70,90,70,80,80,80,90") : new("*,70,90,70,80,80");

    ListBoxItem CalcItem(CalcRow r, bool best, string title, bool compare)
    {
        var grid = new Grid { ColumnDefinitions = CalcColumns(compare) };
        var total = r.Book.Length == 0;
        var cells = new List<string> { title, r.Count.ToString(), r.Subtracted.ToString(), r.Rest.ToString(), r.Words.ToString("N0", CultureInfo.CurrentCulture), r.Per1000.ToString("0.00", CultureInfo.CurrentCulture) };
        if (compare) { cells.Add(r.Other.ToString()); cells.Add(r.Other == 0 ? "–" : r.Ratio.ToString("0.00", CultureInfo.CurrentCulture)); }
        for (var i = 0; i < cells.Count; i++)
        {
            var tb = new TextBlock { Text = cells[i], HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, FontWeight = best || total ? FontWeight.SemiBold : FontWeight.Normal };
            if (best && i == 5) tb.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
            Grid.SetColumn(tb, i); grid.Children.Add(tb);
        }
        return new ListBoxItem { Content = grid, Tag = r.First is { } f && !total ? f : null, IsEnabled = true };
    }
}
