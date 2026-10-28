using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Import;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>Search after the csv-bibel.de syntax: words, phrases, / - ( ), lemma, Strong numbers, passages, original-language words.</summary>
public partial class MainWindow
{
    const int ShownHits = 400;

    readonly Dictionary<string, List<CorpusVerse>> _corpora = [];
    readonly Dictionary<string, StrongIndex> _strongIndexes = [];
    List<SearchResult> _hits = [];
    string? _hitBook;
    List<VerseRef> _queueRefs = [];
    bool _searching;

    static readonly (string Name, Func<string, bool> Match)[] Scopes =
    [
        (L("Ganze Bibel"), _ => true),
        (L("Altes Testament"), id => BookCategories.IsOldTestament(BookCategories.Of(id))),
        (L("Neues Testament"), id => BookCategories.IsNewTestament(BookCategories.Of(id))),
        (L("Gesetz (1.–5. Mose)"), id => BookCategories.Of(id) == BookCategory.Law),
        (L("Geschichtsbücher"), id => BookCategories.Of(id) == BookCategory.History),
        (L("Poesie und Weisheit"), id => BookCategories.Of(id) == BookCategory.Poetry),
        (L("Propheten"), id => BookCategories.Of(id) is BookCategory.MajorProphets or BookCategory.MinorProphets),
        (L("Evangelien"), id => BookCategories.Of(id) == BookCategory.Gospels),
        (L("Briefe"), id => BookCategories.Of(id) is BookCategory.Paul or BookCategory.General),
        (L("Aktuelles Buch"), id => id == _currentBookForSearch),
    ];
    static string _currentBookForSearch = "";

    static readonly (string Query, string Text)[] Examples =
    [
        (L("Mose Elia"), L("Mehrere Wörter: alle kommen im Vers vor")),
        (L("\"Engel des HERRN\""), L("Wendung: Wörter direkt hintereinander")),
        (L("Gnade / Wahrheit"), L("ODER: eines der Wörter oder beide")),
        (L("Gnade -Wahrheit"), L("Minus schließt ein Wort aus")),
        (L("(L=hüten / L=weiden) (Schaf / Herde)"), L("Gruppen in Klammern, mit Grundformen")),
        (L("L=gehen"), L("Grundform: findet ging, gegangen, geht …")),
        (L("L=Haus*"), L("Platzhalter *: Hausbewohner, Hausgeborene …")),
        (L("L~*haus*"), L("Tilde ~: Groß-/Kleinschreibung egal")),
        ("G3056", L("Strong-Nummer (mehrere: alle müssen vorkommen)")),
        (L("H8392=Arche"), L("Strong-Nummer, übersetzt mit einem bestimmten Wort")),
        (L("Joh 3,16"), L("Bibelstelle aufschlagen")),
        (L("Joh 15,11; 16,24; 17,13"), L("Mehrere Stellen als Verslisten (bis 150 Verse)")),
        (L("λογος"), L("Griechisches/hebräisches Wort: öffnet das Strong-Wörterbuch")),
    ];

    void InitSearch()
    {
        SearchScopeBox.ItemsSource = Scopes.Select(s => s.Name).ToList();
        SearchScopeBox.SelectedIndex = 0;
        SearchExactCheck.IsChecked = _settings.SearchExactWords;
        SearchExactCheck.IsCheckedChanged += (_, _) => { _settings.SearchExactWords = SearchExactCheck.IsChecked == true; SaveSettings(); };
        SearchBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await SearchAsync(); };
        SearchGoBtn.Click += async (_, _) => await SearchAsync();
        SearchScopeBox.SelectionChanged += async (_, _) => { if (!string.IsNullOrWhiteSpace(SearchBox.Text) && IsLoaded) await SearchAsync(); };
        SearchHelpBtn.IsCheckedChanged += (_, _) => SearchHelp.IsVisible = SearchHelpBtn.IsChecked == true;
        SearchQueueBtn.Click += (_, _) => AddToQueue(_queueRefs.Count > 0 ? _queueRefs : CurrentHits().Take(150).Select(h => h.Ref));
        foreach (var (query, text) in Examples)
        {
            var b = new Button { Content = query, Classes = { "tool" }, Width = double.NaN, Padding = new(8, 2), Height = 28, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += async (_, _) => { SearchBox.Text = query; await SearchAsync(); };
            SearchHelpList.Children.Add(new StackPanel { Spacing = 2, Children = { b, new TextBlock { Text = text, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap } } });
        }
        RefreshSearchHistory();
    }

    static string ShortBook(BookInfo b) { var i = Array.IndexOf(BookData.UsfmIds, b.Id); return i >= 0 ? BookData.GermanAbbreviations[i] : b.CommonName; }

    IEnumerable<SearchResult> CurrentHits() => _hitBook is null ? _hits : _hits.Where(h => h.Ref.Book == _hitBook);

    void RefreshSearchHistory()
    {
        SearchHistoryPanel.Children.Clear();
        foreach (var q in _settings.SearchHistory.Take(6))
        {
            var b = new Button { Content = q.Length > 26 ? q[..26] + "…" : q, Classes = { "tool" }, Width = double.NaN, Padding = new(8, 0), Height = 24, FontSize = 11, Margin = new(0, 0, 4, 4), Opacity = 0.8 };
            ToolTip.SetTip(b, q);
            b.Click += async (_, _) => { SearchBox.Text = q; await SearchAsync(); };
            SearchHistoryPanel.Children.Add(b);
        }
    }

    void RememberSearch(string q)
    {
        _settings.SearchHistory.RemoveAll(x => x == q);
        _settings.SearchHistory.Insert(0, q);
        if (_settings.SearchHistory.Count > 12) _settings.SearchHistory.RemoveRange(12, _settings.SearchHistory.Count - 12);
        SaveSettings();
        RefreshSearchHistory();
    }

    [GeneratedRegex(@"[\p{IsGreek}\p{IsGreekExtended}\p{IsHebrew}]")] private static partial Regex OriginalScript();

    async Task SearchAsync()
    {
        var text = SearchBox.Text?.Trim() ?? "";
        if (text.Length == 0 || _searching || TranslationBox.SelectedItem is not TranslationInfo t) return;
        _searching = true;
        try
        {
            SearchQueueBtn.IsVisible = false; SearchBooksPanel.Children.Clear(); _hitBook = null; _queueRefs = [];

            // 1. passages: "Joh 3,16" opens, several passages become a list of verses
            if (PassageParser.Parse(text, out var single) is { } passages)
            {
                RememberSearch(text);
                if (single)
                {
                    var p = passages[0];
                    await GoToAsync(p.Book, p.Chapter, p.FromVerse);
                    SearchInfo.Text = L("Aufgeschlagen: {0} {1}{2}", BookName(p.Book), p.Chapter, (p.FromVerse is int f ? "," + f : ""));
                    SearchList.ItemsSource = null; _hits = [];
                    return;
                }
                await ShowPassageListAsync(t, passages);
                return;
            }

            // 2. Hebrew/Greek words go to the Strong dictionary
            if (OriginalScript().IsMatch(text))
            {
                RememberSearch(text);
                StrongSearchBox.Text = text; SideTabs.SelectedIndex = StrongTab;
                await SearchStrongAsync();
                return;
            }

            // 3. full-text / lemma / Strong search
            if (!_store.IsIndexed(t.Id))
            {
                SearchInfo.Text = L("Einmalig: {0} wird für die Suche geladen und indexiert …", t.ShortName);
                var verses = await _provider.GetAllVersesAsync(t.Id);
                await Task.Run(() => _store.Index(t.Id, verses));
                _corpora.Remove(t.Id);
            }
            if (!_corpora.TryGetValue(t.Id, out var corpus))
                _corpora[t.Id] = corpus = await Task.Run(() => _store.GetCorpus(t.Id).Select(c => new CorpusVerse(c.Ref, c.Text)).ToList());

            _currentBookForSearch = _curBook;
            var scope = SearchScopeBox.SelectedIndex is var si and > 0 && si < Scopes.Length ? Scopes[si].Match : null;
            var books = scope is null ? null : _books.Select(b => b.Id).Where(scope).ToHashSet();
            var exact = SearchExactCheck.IsChecked == true;

            _strongIndexes.TryGetValue(t.Id, out var strongs);
            var outcome = await Task.Run(() => SearchEngine.Run(corpus, text, strongs, exact, books));
            if (outcome.NeedsStrongs)
            {
                if (!_store.IsStrongsIndexed(t.Id))
                {
                    if (_building) { SearchInfo.Text = L("Die Konkordanz wird gerade aufgebaut – bitte kurz warten."); return; }
                    _building = true;
                    try
                    {
                        var progress = new Progress<(int Done, int Total)>(p => SearchInfo.Text = L("Einmalig: Strong-Index für {0} wird aufgebaut … {1}/{2} Kapitel", t.ShortName, p.Done, p.Total));
                        await ConcordanceBuilder.BuildAsync(_provider, _store, t.Id, progress);
                    }
                    finally { _building = false; }
                }
                _strongIndexes[t.Id] = strongs = await Task.Run(() => new StrongIndex(_store.GetAllOccurrences(t.Id)));
                outcome = await Task.Run(() => SearchEngine.Run(corpus, text, strongs, exact, books));
            }
            if (outcome.Error is not null) { SearchInfo.Text = outcome.Error; SearchList.ItemsSource = null; _hits = []; return; }

            RememberSearch(text);
            _hits = outcome.Results.ToList();
            ShowHits(t);
        }
        catch (Exception ex) { SearchInfo.Text = L("Fehler: ") + ex.Message; }
        finally { _searching = false; }
    }

    void ShowHits(TranslationInfo t)
    {
        var list = CurrentHits().ToList();
        SearchList.ItemsSource = list.Take(ShownHits).Select(h => new Entry(h.Ref, $"{Label(h.Ref)}\n{h.Marked()}")).ToList();
        var words = list.Sum(h => h.Spans.Count);
        SearchInfo.Text = _hits.Count == 0 ? L("Keine Treffer in {0}.", t.ShortName)
            : L("{0} Verse · {1} Fundstellen in {2}{3}", list.Count, words, t.ShortName, (list.Count > ShownHits ? L(" (erste {0} angezeigt)", ShownHits) : ""));
        SearchQueueBtn.IsVisible = list.Count > 0;

        // hits per book, in Bible order; click narrows the list to that book, click again shows all
        SearchBooksPanel.Children.Clear();
        var perBook = _hits.GroupBy(h => h.Ref.Book).ToDictionary(g => g.Key, g => g.Count());
        if (perBook.Count > 1)
            foreach (var b in _books.Where(b => perBook.ContainsKey(b.Id)))
            {
                var active = _hitBook == b.Id;
                var chip = new Button
                {
                    Content = $"{(ShortBook(b))} {perBook[b.Id]}", Classes = { "tool" }, Width = double.NaN, Height = 24, Padding = new(7, 0),
                    FontSize = 11, Margin = new(0, 0, 4, 4), Background = active ? Res("BsAccentSoft") : null, BorderBrush = new SolidColorBrush(HomeView.ColorOf(b.Id)), BorderThickness = new(0, 0, 0, 2),
                };
                ToolTip.SetTip(chip, L("{0}: {1} Verse", b.CommonName, perBook[b.Id]));
                var id = b.Id;
                chip.Click += (_, _) => { _hitBook = _hitBook == id ? null : id; ShowHits(t); };
                SearchBooksPanel.Children.Add(chip);
            }
    }

    async Task ShowPassageListAsync(TranslationInfo t, IReadOnlyList<PassageRef> passages)
    {
        var entries = new List<Entry>();
        var capped = false;
        foreach (var p in passages)
        {
            for (var c = p.Chapter; c <= (p.ToChapter ?? p.Chapter) && !capped; c++)
            {
                var ch = await _provider.GetChapterAsync(t.Id, p.Book, c);
                foreach (var v in ch.Verses)
                {
                    if (p.FromVerse is int from && p.ToChapter is null && (v.Number < from || v.Number > (p.ToVerse ?? from))) continue;
                    if (p.FromVerse is int f2 && p.ToChapter is int tc && ((c == p.Chapter && v.Number < f2) || (c == tc && v.Number > (p.ToVerse ?? int.MaxValue)))) continue;
                    var r = new VerseRef(p.Book, c, v.Number);
                    entries.Add(new Entry(r, $"{Label(r)}\n{v.Text.Trim()}"));
                    if (entries.Count >= 150) { capped = true; break; }
                }
            }
        }
        _hits = [];
        SearchList.ItemsSource = entries;
        SearchInfo.Text = (passages.Count == 1 ? L("{0} Verse aus 1 Stelle{1} · {2}", entries.Count, (capped ? L(" (Maximum 150)") : ""), t.ShortName) : L("{0} Verse aus {1} Stellen{2} · {3}", entries.Count, passages.Count, (capped ? L(" (Maximum 150)") : ""), t.ShortName));
        SearchQueueBtn.IsVisible = false;
        if (entries.Count > 0)
        {
            SearchQueueBtn.IsVisible = true;
            _queueRefs = entries.Select(e => e.Ref).ToList();
        }
    }
}
