using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// The result views known from Logos: the verses, every found word aligned under each other, a grid of books against translations,
/// the verses side by side in several translations, and an analysis of where and in which forms the word occurs.
/// </summary>
public partial class MainWindow
{
    const int MaxCompare = 5, ShownCompared = 100;

    /// <summary>The last search, so that it can be repeated in other translations.</summary>
    sealed record SearchRun(string Query, SearchOptions Options, HashSet<string>? Books, bool Jesus, Func<VerseRef, bool> Filter);

    SearchRun? _run;
    readonly Dictionary<string, List<SearchResult>> _otherHits = [];
    readonly ComboBox _viewBox = new() { Width = 150 };
    readonly Button _compareBtn = new() { Classes = { "seg" }, Margin = new(0, 0, 8, 4), IsVisible = false };
    readonly WrapPanel _viewRow = new() { IsVisible = false };
    bool _fillingHits;
    int _viewVersion;
    double _alignedWidth;

    static readonly string[] SearchViews = [L("Verse"), L("Ausgerichtet"), L("Raster"), L("Vergleich"), L("Analyse")];

    void InitSearchViews()
    {
        _viewBox.ItemsSource = SearchViews;
        _viewBox.SelectedIndex = Math.Clamp(_settings.SearchView, 0, SearchViews.Length - 1);
        ToolTip.SetTip(_viewBox, L("Wie die Treffer angezeigt werden"));
        _viewBox.SelectionChanged += (_, _) => { _settings.SearchView = Math.Max(0, _viewBox.SelectedIndex); SaveSettings(); ShowSearchView(); };
        _compareBtn.Content = L("Übersetzungen ▾");
        ToolTip.SetTip(_compareBtn, L("Welche Übersetzungen zusätzlich durchsucht werden"));
        _compareBtn.Click += (_, _) => ShowComparePicker();
        _viewRow.Children.Add(new Border { Classes = { "pill" }, Margin = new(0, 0, 8, 4), Child = _viewBox });
        _viewRow.Children.Add(_compareBtn);
        // what to do with the hits, together in one button: four rows of buttons would leave the hits themselves no room
        var hitsMenu = new Button { Classes = { "seg" }, Margin = new(0, 0, 8, 4), Content = L("Treffer ▾") };
        ToolTip.SetTip(hitsMenu, L("Was mit den Treffern geschehen soll"));
        var actions = new StackPanel { Spacing = 2 };
        var hitsFlyout = new Flyout { Content = actions };
        foreach (var b in new[] { _hitsDocBtn, _hitsCsvBtn, _hitsMapBtn, _hitsStatsBtn })
        {
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Click += (_, _) => hitsFlyout.Hide();
            actions.Children.Add(b);
        }
        hitsMenu.Click += (_, _) => hitsFlyout.ShowAt(hitsMenu);
        _viewRow.Children.Add(hitsMenu);
        if (SearchDock.Children.IndexOf(SearchTopScroll) is var at and >= 0)
        {
            DockPanel.SetDock(_viewRow, Dock.Top);
            SearchDock.Children.Insert(at + 1, _viewRow);
        }
        // anything else that fills the list (a list of passages, a topic search) has to be visible
        SearchList.PropertyChanged += (_, e) => { if (e.Property == ItemsControl.ItemsSourceProperty && !_fillingHits) { _run = null; _viewRow.IsVisible = false; SearchAlt.IsVisible = false; SearchList.IsVisible = true; } };
        SearchAlt.PropertyChanged += (_, e) =>
        {
            if (e.Property != BoundsProperty || _viewBox.SelectedIndex != 1 || !SearchAlt.IsVisible || Math.Abs(SearchAlt.Bounds.Width - _alignedWidth) < 40) return;
            ShowSearchView();
        };
    }

    void NoteSearchRun(SearchRun run) { _run = run; _otherHits.Clear(); }

    IEnumerable<TranslationInfo> CompareTranslations(TranslationInfo current)
    {
        var all = TranslationBox.Items.OfType<TranslationInfo>().ToList();
        return new[] { current }.Concat(_settings.SearchCompare.Where(id => id != current.Id).Select(id => all.FirstOrDefault(x => x.Id == id)).OfType<TranslationInfo>()).Take(MaxCompare + 1);
    }

    async Task<List<CorpusVerse>> CorpusForAsync(TranslationInfo t)
    {
        if (!_store.IsIndexed(t.Id))
        {
            SearchInfo.Text = L("Einmalig: {0} wird für die Suche geladen und indexiert …", t.ShortName);
            var verses = await _provider.GetAllVersesAsync(t.Id);
            await Task.Run(() => _store.Index(t.Id, verses));
            _corpora.Remove(t.Id);
        }
        if (!_corpora.TryGetValue(t.Id, out var corpus))
            _corpora[t.Id] = corpus = await Task.Run(() => _store.GetCorpus(t.Id).Select(c => new CorpusVerse(c.Ref, c.Text)).ToList());
        return corpus;
    }

    /// <summary>The Strong's tags of a translation, built on first use; null while another build is running.</summary>
    async Task<StrongIndex?> StrongsForAsync(TranslationInfo t)
    {
        if (!_store.IsStrongsIndexed(t.Id))
        {
            if (_building) { SearchInfo.Text = L("Die Konkordanz wird gerade aufgebaut – bitte kurz warten."); return null; }
            _building = true;
            try
            {
                var progress = new Progress<(int Done, int Total)>(p => SearchInfo.Text = L("Einmalig: Strong-Index für {0} wird aufgebaut … {1}/{2} Kapitel", t.ShortName, p.Done, p.Total));
                await ConcordanceBuilder.BuildAsync(_provider, _store, t.Id, progress);
            }
            finally { _building = false; }
        }
        return _strongIndexes[t.Id] = await Task.Run(() => new StrongIndex(_store.GetAllOccurrences(t.Id)));
    }

    /// <summary>The same search in another translation.</summary>
    async Task<List<SearchResult>> HitsInAsync(TranslationInfo t, SearchRun run)
    {
        if (_otherHits.TryGetValue(t.Id, out var known)) return known;
        var corpus = run.Jesus ? await JesusCorpusAsync(t) ?? [] : await CorpusForAsync(t);
        var searched = corpus.Where(c => run.Filter(c.Ref)).ToList();
        _strongIndexes.TryGetValue(t.Id, out var strongs);
        List<SearchResult> Everything() => searched.Where(c => run.Books is null || run.Books.Contains(c.Ref.Book)).Select(c => new SearchResult(c.Ref, c.Text, [])).ToList();
        if (run.Query.Length == 0) return _otherHits[t.Id] = Everything();
        var outcome = await Task.Run(() => SearchEngine.Run(searched, run.Query, strongs, run.Options, run.Books));
        if (outcome.NeedsStrongs)
        {
            strongs = await StrongsForAsync(t);
            if (strongs is null) return [];
            outcome = await Task.Run(() => SearchEngine.Run(searched, run.Query, strongs, run.Options, run.Books));
        }
        return _otherHits[t.Id] = outcome.Results.ToList();
    }

    void ShowSearchView()
    {
        var view = Math.Max(0, _viewBox.SelectedIndex);
        var any = _hits.Count > 0 && _run is not null;
        _viewRow.IsVisible = any;
        _compareBtn.IsVisible = any && view is 2 or 3;
        SearchList.IsVisible = !any || view == 0;
        SearchAlt.IsVisible = any && view != 0;
        if (!SearchAlt.IsVisible) return;
        _ = FillSearchViewAsync(view);
    }

    async Task FillSearchViewAsync(int view)
    {
        var version = ++_viewVersion;
        SearchAltPanel.Children.Clear();
        if (TranslationBox.SelectedItem is not TranslationInfo t || _run is not { } run) return;
        var hits = CurrentHits().ToList();
        try
        {
            switch (view)
            {
                case 1: ShowAligned(hits); break;
                case 4: ShowAnalysis(hits); break;
                default:
                    var status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Text = L("Suche in den anderen Übersetzungen …") };
                    SearchAltPanel.Children.Add(status);
                    var columns = new List<(TranslationInfo T, List<SearchResult> Hits)>();
                    foreach (var x in CompareTranslations(t))
                    {
                        var found = x.Id == t.Id ? _hits : await HitsInAsync(x, run);
                        if (version != _viewVersion) return;   // another view or search came first
                        columns.Add((x, found));
                    }
                    SearchAltPanel.Children.Clear();
                    if (columns.Count == 1) SearchAltPanel.Children.Add(new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Text = L("Wähle mit „Übersetzungen“ weitere aus, dann stehen sie hier nebeneinander.") });
                    else SearchAltPanel.Children.Add(new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Text = L("Wörter werden in jeder Übersetzung wörtlich gesucht. Für dasselbe Wort des Grundtexts über Sprachen hinweg nimm eine Strong-Nummer, z. B. G3056.") });
                    if (view == 2) ShowGrid(columns); else ShowComparison(hits, columns, t);
                    break;
            }
        }
        catch (Exception ex)
        {
            if (version != _viewVersion) return;
            SearchAltPanel.Children.Clear();
            SearchAltPanel.Children.Add(new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Text = L("Fehler: ") + ex.Message });
        }
    }

    Button HitRow(VerseRef r, Control content)
    {
        var b = new Button { Classes = { "toolx" }, Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(4, 2), Height = double.NaN };   // the style has a fixed height for icon buttons; a row grows with its text
        b.Click += (_, _) => _ = GoToAsync(r.Book, r.Chapter, r.Verse);
        return b;
    }

    // ---- every found word under each other ----------------------------------------------------------------------------------

    void ShowAligned(List<SearchResult> hits)
    {
        var width = SearchAlt.Bounds.Width > 0 ? SearchAlt.Bounds.Width : 320;
        _alignedWidth = width;
        const double charWidth = 7;
        var labelAbove = width < 480;   // on a narrow panel the reference gets its own line, otherwise hardly any text would be left
        var labelWidth = labelAbove ? 0.0 : 90;
        var longest = hits.SelectMany(h => h.Spans).Select(s => s.Len).DefaultIfEmpty(0).Max();
        var wordWidth = Math.Min(longest * 7.2 + 8, Math.Max(70, width / 3));
        var context = (int)Math.Max(6, (width - labelWidth - wordWidth - 30) / 2 / charWidth);
        var lines = HitViews.Align(hits, context, ShownHits);
        if (lines.Count == 0) SearchAltPanel.Children.Add(new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Text = L("Diese Suche markiert keine einzelnen Wörter. Nimm die Ansicht „Verse“.") });
        foreach (var line in lines)
        {
            var row = new Grid { ColumnDefinitions = new($"{labelWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)},*,{wordWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)},*"), ClipToBounds = true };
            var cells = new (string Text, TextAlignment Align, bool Bold)[] { (Label(line.Hit.Ref), TextAlignment.Left, false), (line.Left, TextAlignment.Right, false), (line.Word, TextAlignment.Center, true), (line.Right, TextAlignment.Left, false) };
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = new TextBlock { Text = cells[i].Text, TextAlignment = cells[i].Align, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = i == 0 ? 0.65 : 1, FontWeight = cells[i].Bold ? FontWeight.SemiBold : FontWeight.Normal };
                if (cells[i].Bold) cell.Foreground = Res("BsAccent");
                Grid.SetColumn(cell, i);
                row.Children.Add(cell);
            }
            ToolTip.SetTip(row, line.Hit.Text);
            Control content = row;
            if (labelAbove)
            {
                var label = row.Children[0];
                row.Children.Remove(label);
                content = new StackPanel { Children = { label, row } };
            }
            SearchAltPanel.Children.Add(HitRow(line.Hit.Ref, content));
        }
    }

    // ---- books against translations --------------------------------------------------------------------------------------------

    void ShowGrid(List<(TranslationInfo T, List<SearchResult> Hits)> columns)
    {
        var counts = columns.Select(c => c.Hits.GroupBy(h => h.Ref.Book).ToDictionary(g => g.Key, g => g.Count())).ToList();
        var books = _books.Where(b => counts.Any(c => c.ContainsKey(b.Id))).ToList();
        var max = Math.Max(1, counts.SelectMany(c => c.Values).DefaultIfEmpty(1).Max());
        var grid = new Grid { Margin = new(0, 6, 0, 0), ColumnDefinitions = new("Auto," + string.Join(",", columns.Select(_ => "*"))) };
        var y = 0;
        void Row() => grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        void Cell(Control c, int col) { Grid.SetRow(c, y); Grid.SetColumn(c, col); grid.Children.Add(c); }
        Row();
        for (var i = 0; i < columns.Count; i++)
        {
            var head = new TextBlock { Text = columns[i].T.ShortName, FontWeight = FontWeight.SemiBold, FontSize = 12, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(3, 0) };
            ToolTip.SetTip(head, columns[i].T.Name);
            Cell(head, i + 1);
        }
        y++; Row();
        Cell(new TextBlock { Text = L("Gesamt"), FontWeight = FontWeight.SemiBold, FontSize = 12, Margin = new(0, 2, 8, 4) }, 0);
        for (var i = 0; i < columns.Count; i++) Cell(new TextBlock { Text = columns[i].Hits.Count.ToString(), FontWeight = FontWeight.SemiBold, FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new(0, 2, 0, 4) }, i + 1);
        foreach (var b in books)
        {
            y++; Row();
            var name = FilterChip(ShortBook(b), _hitBook == b.Id, HomeView.ColorOf(b.Id), () => { var id = b.Id; _hitBook = _hitBook == id ? null : id; if (TranslationBox.SelectedItem is TranslationInfo t) ShowHits(t); }, b.CommonName);
            Cell(name, 0);
            for (var i = 0; i < columns.Count; i++)
            {
                var n = counts[i].GetValueOrDefault(b.Id);
                // the darker the cell, the more hits: the pattern shows at a glance
                var cell = new Border
                {
                    Margin = new(1), CornerRadius = new(3), Background = n == 0 ? null : new SolidColorBrush(HomeView.ColorOf(b.Id), 0.15 + 0.6 * n / max),
                    Child = new TextBlock { Text = n == 0 ? "–" : n.ToString(), FontSize = 12, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = n == 0 ? 0.4 : 1 },
                };
                Cell(cell, i + 1);
            }
        }
        SearchAltPanel.Children.Add(grid);
    }

    // ---- the verses side by side -----------------------------------------------------------------------------------------------

    /// <summary>A verse with the found words emphasised.</summary>
    TextBlock Emphasised(string text, IReadOnlyList<(int Start, int Len)> spans)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = ScriptureFont, FontSize = 13.5, Inlines = [] };
        var at = 0;
        foreach (var (s, l) in spans)
        {
            if (s > at) block.Inlines!.Add(new Run(text[at..s]));
            block.Inlines!.Add(new Run(text.Substring(s, l)) { FontWeight = FontWeight.Bold, Foreground = Res("BsAccent") });
            at = s + l;
        }
        if (at < text.Length) block.Inlines!.Add(new Run(text[at..]));
        return block;
    }

    void ShowComparison(List<SearchResult> hits, List<(TranslationInfo T, List<SearchResult> Hits)> columns, TranslationInfo current)
    {
        var found = columns.Select(c => c.Hits.GroupBy(h => h.Ref).ToDictionary(g => g.Key, g => g.First())).ToList();
        var verses = columns.Select(c => _corpora.GetValueOrDefault(c.T.Id)?.GroupBy(v => v.Ref).ToDictionary(g => g.Key, g => g.First().Text) ?? []).ToList();
        foreach (var h in hits.Take(ShownCompared))
        {
            var box = new StackPanel { Spacing = 3, Margin = new(0, 6, 0, 6) };
            box.Children.Add(new TextBlock { Text = Label(h.Ref), FontWeight = FontWeight.SemiBold, FontSize = 12.5 });
            for (var i = 0; i < columns.Count; i++)
            {
                var hit = found[i].GetValueOrDefault(h.Ref);
                var text = hit?.Text ?? verses[i].GetValueOrDefault(h.Ref);
                var name = new TextBlock { Text = columns[i].T.ShortName + (hit is null && text is not null ? "  " + L("(kein Treffer)") : ""), Classes = { "caption" } };
                box.Children.Add(name);
                box.Children.Add(text is null ? new TextBlock { Text = L("(Vers fehlt in dieser Übersetzung)"), Classes = { "caption" } } : Emphasised(text, hit?.Spans ?? []));
            }
            SearchAltPanel.Children.Add(HitRow(h.Ref, box));
        }
        if (hits.Count > ShownCompared) SearchAltPanel.Children.Add(new TextBlock { Text = L(" (erste {0} angezeigt)", ShownCompared), Classes = { "caption" } });
    }

    // ---- where and in which forms ------------------------------------------------------------------------------------------------

    void ShowAnalysis(List<SearchResult> hits)
    {
        void Title(string text) => SearchAltPanel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new(0, 10, 0, 2) });
        var old = hits.Count(h => BookCategories.IsOldTestament(BookCategories.Of(h.Ref.Book)));
        Title(L("Verteilung über die Bibel"));
        SearchAltPanel.Children.Add(new TextBlock { Text = L("Altes Testament: {0} Verse · Neues Testament: {1} Verse", old, hits.Count - old), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        SearchAltPanel.Children.Add(SpreadBars(hits.Select(h => h.Ref).ToList()));

        var forms = HitViews.WordForms(hits);
        if (forms.Count > 0)
        {
            Title(L("Gefundene Wortformen"));
            var wrap = new WrapPanel();
            foreach (var (form, count) in forms.Take(40)) wrap.Children.Add(new Border { Classes = { "pill" }, Margin = new(0, 0, 4, 4), Padding = new(8, 2), Child = new TextBlock { Text = $"{form}  {count}×", FontSize = 12 } });
            SearchAltPanel.Children.Add(wrap);
            if (forms.Count > 40) SearchAltPanel.Children.Add(new TextBlock { Text = $"+{forms.Count - 40}", Classes = { "caption" } });
        }

        Title(L("Kapitel mit den meisten Treffern"));
        var chapters = new WrapPanel();
        foreach (var g in hits.GroupBy(h => (h.Ref.Book, h.Ref.Chapter)).OrderByDescending(g => g.Count()).Take(12))
        {
            var key = g.Key;
            chapters.Children.Add(FilterChip($"{BookName(key.Book)} {key.Chapter}  {g.Count()}", false, HomeView.ColorOf(key.Book), () => _ = GoToAsync(key.Book, key.Chapter, g.First().Ref.Verse), L("Springt zur Stelle")));
        }
        SearchAltPanel.Children.Add(chapters);
    }

    // ---- which translations ---------------------------------------------------------------------------------------------------------

    void ShowComparePicker()
    {
        var all = TranslationBox.Items.OfType<TranslationInfo>().ToList();
        var filter = new TextBox { PlaceholderText = L("Übersetzung suchen") };
        var list = new StackPanel { Spacing = 2 };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Text = L("Bis zu {0} zusätzlich. Eine Übersetzung wird beim ersten Mal einmalig geladen.", MaxCompare) };
        var flyout = new Flyout { Content = new StackPanel { Spacing = 6, Width = 310, Children = { new TextBlock { Text = L("Zusätzlich durchsuchen"), FontWeight = FontWeight.SemiBold }, info, filter, new ScrollViewer { MaxHeight = 300, Content = list } } } };
        var currentId = (TranslationBox.SelectedItem as TranslationInfo)?.Id;
        void Fill()
        {
            list.Children.Clear();
            var q = SearchQuery.Fold((filter.Text ?? "").Trim(), true);
            var shown = all.Where(x => x.Id != currentId && (q.Length == 0 || SearchQuery.Fold(x.Name + " " + x.ShortName + " " + x.LanguageName, true).Contains(q, StringComparison.Ordinal)))
                .OrderByDescending(x => _settings.SearchCompare.Contains(x.Id)).Take(60);
            foreach (var x in shown)
            {
                var id = x.Id;
                var picked = _settings.SearchCompare.Contains(id);
                var check = new CheckBox { Content = $"{x.ShortName} – {x.Name}", IsChecked = picked, IsEnabled = picked || _settings.SearchCompare.Count < MaxCompare };
                check.IsCheckedChanged += (_, _) =>
                {
                    if (check.IsChecked == true) { if (!_settings.SearchCompare.Contains(id)) _settings.SearchCompare.Add(id); } else _settings.SearchCompare.Remove(id);
                    SaveSettings();
                    Avalonia.Threading.Dispatcher.UIThread.Post(Fill);
                };
                list.Children.Add(check);
            }
        }
        filter.TextChanged += (_, _) => Fill();
        Fill();
        flyout.Closed += (_, _) => ShowSearchView();
        flyout.ShowAt(_compareBtn);
    }
}
