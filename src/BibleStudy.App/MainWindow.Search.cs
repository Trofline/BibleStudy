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
        (L("Paulusbriefe"), id => BookCategories.Of(id) == BookCategory.Paul),
        (L("Kleine Propheten"), id => BookCategories.Of(id) == BookCategory.MinorProphets),
        (L("Aktuelles Buch"), id => id == _currentBookForSearch),
        (L("Nur Worte Jesu (rot gedruckt)"), _ => true),
    ];
    static bool IsJesusScope(int index) => index == Scopes.Length - 1;
    static string _currentBookForSearch = "";

    static readonly string[] MatchModes = [L("Wortanfang"), L("Ganzes Wort"), L("Wortende"), L("Wortteil")];
    static readonly (string Name, int Range)[] Ranges = [(L("im selben Vers"), 0), (L("innerhalb von 2 Versen"), 2), (L("innerhalb von 5 Versen"), 5), (L("im selben Kapitel"), -1)];

    /// <summary>Kinds of conditions of the search builder: what the user picks, and how the words typed after it become search syntax.</summary>
    static readonly (string Name, Func<string[], string> Make)[] BuilderKinds =
    [
        (L("alle diese Wörter"), w => string.Join(" ", w)),
        (L("eines dieser Wörter"), w => w.Length == 1 ? w[0] : "(" + string.Join(" / ", w) + ")"),
        (L("genau diese Wendung"), w => "\"" + string.Join(" ", w) + "\""),
        (L("ohne diese Wörter"), w => string.Join(" ", w.Select(x => "-" + x))),
        (L("beginnt mit …"), w => string.Join(" ", w.Select(x => x.Trim('*') + "*"))),
        (L("endet auf …"), w => string.Join(" ", w.Select(x => "*" + x.Trim('*')))),
        (L("enthält den Wortteil …"), w => string.Join(" ", w.Select(x => "*" + x.Trim('*') + "*"))),
        (L("Grundform von …"), w => string.Join(" ", w.Select(x => "L=" + x))),
        (L("Grundtextwort zu …"), w => string.Join(" ", w.Select(x => "O=" + x))),
        (L("Strong-Nummer"), w => string.Join(" ", w)),
        (L("Person in der Bibel"), w => "Person:\"" + string.Join(" ", w) + "\""),
        (L("Ort in der Bibel"), w => "Ort:\"" + string.Join(" ", w) + "\""),
        (L("Bezug zu einer Bibelstelle (AT↔NT)"), w => "Bezug:\"" + string.Join(" ", w) + "\""),
    ];
    readonly List<(ComboBox Kind, TextBox Text)> _builderRows = [];

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
        (L("*keit"), L("Wortende: Barmherzigkeit, Gerechtigkeit …")),
        (L("*geist*"), L("Wortteil: Geist, Heiliger Geist, geistlich …")),
        (L("O=Gnade"), L("Grundtext: alle Verse mit dem Hebräisch/Griechisch-Wort hinter „Gnade“")),
        (L("Person:Abraham"), L("Person: alle Verse, in denen sie erwähnt wird (auch unter anderen Namen); mit Wörtern kombinierbar: Person:Abraham Verheißung")),
        (L("Ort:Jerusalem"), L("Ort: alle Verse, in denen er erwähnt wird")),
        (L("Bezug:\"Jer 31\""), L("Altes Testament: welche Neutestament-Verse darauf verweisen. Neues Testament (Bezug:\"Hebr 10\"): auf welche AT-Verse es verweist (Querverweise, nicht nur Zitate)")),
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
        if (_settings.SearchExactWords) { _settings.SearchExactWords = false; _settings.SearchMatch = 1; SaveSettings(); }   // the old checkbox became the choice below
        SearchMatchBox.ItemsSource = MatchModes;
        SearchMatchBox.SelectedIndex = Math.Clamp(_settings.SearchMatch, 0, MatchModes.Length - 1);
        SearchMatchBox.SelectionChanged += (_, _) => { _settings.SearchMatch = Math.Max(0, SearchMatchBox.SelectedIndex); SaveSettings(); };
        SearchRangeBox.ItemsSource = Ranges.Select(r => r.Name).ToList();
        SearchRangeBox.SelectedIndex = Math.Clamp(_settings.SearchRange, 0, Ranges.Length - 1);
        SearchRangeBox.SelectionChanged += (_, _) => { _settings.SearchRange = Math.Max(0, SearchRangeBox.SelectedIndex); SaveSettings(); };
        SearchOriginalCheck.IsChecked = _settings.SearchOriginal;
        SearchOriginalCheck.IsCheckedChanged += (_, _) => { _settings.SearchOriginal = SearchOriginalCheck.IsChecked == true; SaveSettings(); };
        SearchCaseCheck.IsChecked = _settings.SearchCase;
        SearchCaseCheck.IsCheckedChanged += (_, _) => { _settings.SearchCase = SearchCaseCheck.IsChecked == true; SaveSettings(); };
        SearchOptionsBtn.IsCheckedChanged += (_, _) => SearchOptionsCard.IsVisible = SearchOptionsBtn.IsChecked == true;
        SearchBuilderBtn.IsCheckedChanged += (_, _) => { SearchBuilder.IsVisible = SearchBuilderBtn.IsChecked == true; if (SearchBuilder.IsVisible && _builderRows.Count == 0) { AddBuilderRow(0); AddBuilderRow(0); } };
        SearchBuilderAddBtn.Click += (_, _) => AddBuilderRow(0);
        SearchBuilderRunBtn.Click += async (_, _) => await RunBuilderAsync();
        // a changed option searches again at once, so the effect is visible
        foreach (var box in new[] { SearchMatchBox, SearchRangeBox })
            box.SelectionChanged += async (_, _) => { if (!string.IsNullOrWhiteSpace(SearchBox.Text) && _hits.Count > 0 && IsLoaded) await SearchAsync(); };
        foreach (var check in new[] { SearchOriginalCheck, SearchCaseCheck })
            check.IsCheckedChanged += async (_, _) => { if (!string.IsNullOrWhiteSpace(SearchBox.Text) && _hits.Count > 0 && IsLoaded) await SearchAsync(); };
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

    // ---- search builder: pick the kind of condition from a list instead of knowing the syntax -------------------------------

    void AddBuilderRow(int kind)
    {
        var box = new ComboBox { ItemsSource = BuilderKinds.Select(k => k.Name).ToList(), SelectedIndex = kind, Width = 190 };
        var text = new TextBox { Watermark = L("Wort(e) eingeben") };
        text.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await RunBuilderAsync(); };
        var remove = new Button { Content = "✕", Classes = { "tool" }, Width = 28, Height = 28 };
        ToolTip.SetTip(remove, L("Entfernen"));
        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 6 };
        var pill = new Border { Classes = { "pill" }, Child = box };
        Grid.SetColumn(text, 1); Grid.SetColumn(remove, 2);
        row.Children.Add(pill); row.Children.Add(text); row.Children.Add(remove);
        _builderRows.Add((box, text));
        SearchBuilderRows.Children.Add(row);
        remove.Click += (_, _) => { _builderRows.RemoveAll(r => r.Text == text); SearchBuilderRows.Children.Remove(row); };
        AnimateIn(row, 0, 8);
    }

    string BuilderQuery() => string.Join(" ", _builderRows
        .Select(r => BuilderKinds[Math.Max(0, r.Kind.SelectedIndex)].Make((r.Text.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)) is var q && (r.Text.Text ?? "").Trim().Length > 0 ? q : "")
        .Where(q => q.Length > 0));

    async Task RunBuilderAsync()
    {
        var query = BuilderQuery();
        if (query.Length == 0) { SearchInfo.Text = L("Gib in mindestens ein Feld ein Wort ein."); return; }
        SearchBox.Text = query;   // the finished search stays visible, so the syntax can be learned from it
        await SearchAsync();
    }

    /// <summary>The Strong numbers behind the translated search word, as chips: a click searches that one word of the original text.</summary>
    void ShowSearchStrongs(IReadOnlyCollection<string>? ids)
    {
        SearchStrongsPanel.Children.Clear();
        if (ids is not { Count: > 0 }) return;
        SearchStrongsPanel.Children.Add(new TextBlock { Text = L("Grundtext-Wörter hinter dem Suchwort:"), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 4) });
        foreach (var id in ids.Take(14))
        {
            var chip = FilterChip(id, false, null, () => { SearchBox.Text = id; _ = SearchAsync(); }, L("Nur dieses Wort des Grundtexts suchen"));
            SearchStrongsPanel.Children.Add(chip);
            _ = FillLemmaAsync(chip, id);
        }
        if (ids.Count > 14) SearchStrongsPanel.Children.Add(new TextBlock { Text = $"+{ids.Count - 14}", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center });
    }

    async Task FillLemmaAsync(Button chip, string id)
    {
        try
        {
            var entry = (await _strongs.SearchAsync(id, 1)).FirstOrDefault(e => e.Id == id);
            if (entry is not null) { chip.Content = $"{id}  {entry.Lemma}"; ToolTip.SetTip(chip, $"{entry.Lemma} – {Shorten(Mean(entry.Definition), 80)}"); }
        }
        catch { /* offline: the number alone is still useful */ }
    }

    static string ShortBook(BookInfo b) { var i = Array.IndexOf(BookData.UsfmIds, b.Id); return i >= 0 ? BookData.GermanAbbreviations[i] : b.CommonName; }

    IEnumerable<SearchResult> CurrentHits() => _hitBook is null ? _hits : _hits.Where(h => h.Ref.Book == _hitBook);

    void RefreshSearchHistory()
    {
        SearchHistoryPanel.Children.Clear();
        if (_settings.SearchHistory.Count > 0)
        {
            var clear = new Button { Content = "✕", Classes = { "tool" }, Width = 24, Height = 24, FontSize = 11, Margin = new(0, 0, 6, 4) };
            ToolTip.SetTip(clear, L("Suchverlauf löschen"));
            clear.Click += (_, _) => { _settings.SearchHistory.Clear(); SaveSettings(); RefreshSearchHistory(); };
            SearchHistoryPanel.Children.Add(clear);
        }
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
            SearchQueueBtn.IsVisible = false; UpdateHitActions(false); SearchBooksPanel.Children.Clear(); _hitBook = null; _queueRefs = []; SearchStrongsPanel.Children.Clear();
            var narrowTo = SearchInHitsCheck.IsChecked == true && _hits.Count > 0 ? _hits.Select(h => h.Ref).ToHashSet() : null;

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

            // 2b. Person:… / Ort:… / Bezug:"…": what a text is about; the rest of the query is an ordinary search within those verses
            _hitNotes.Clear();
            var query = text;
            HashSet<VerseRef>? entityFilter = null;
            Dictionary<VerseRef, int>? entityBest = null;
            if (await ResolveEntitiesAsync(text) is { } entity)
            {
                if (entity.Verses.Count == 0)
                {
                    if (!entity.HasMessage) SearchInfo.Text = L("Keine Treffer in {0}.", t.ShortName);
                    SearchList.ItemsSource = null; _hits = []; return;
                }
                entityFilter = entity.Verses; query = entity.Remaining; entityBest = entity.Best;
                foreach (var (k, v) in entity.Notes) _hitNotes[k] = v;
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
            var options = new SearchOptions((WordMatch)Math.Max(0, SearchMatchBox.SelectedIndex), SearchCaseCheck.IsChecked == true, SearchOriginalCheck.IsChecked == true,
                Ranges[Math.Clamp(SearchRangeBox.SelectedIndex, 0, Ranges.Length - 1)].Range);
            if (IsJesusScope(SearchScopeBox.SelectedIndex))
            {
                corpus = await JesusCorpusAsync(t) ?? [];
                if (corpus.Count == 0) { SearchInfo.Text = L("Diese Übersetzung kennzeichnet die Worte Jesu nicht (Rotdruck). Wähle eine andere, z. B. eine englische oder die Schlachter."); SearchList.ItemsSource = null; _hits = []; return; }
                books = null;
            }
            var searched = narrowTo is null ? corpus : corpus.Where(c => narrowTo.Contains(c.Ref)).ToList();
            if (entityFilter is not null) searched = searched.Where(c => entityFilter.Contains(c.Ref)).ToList();

            _strongIndexes.TryGetValue(t.Id, out var strongs);
            var outcome = query.Length == 0
                ? new SearchOutcome(searched.Where(c => books is null || books.Contains(c.Ref.Book)).Select(c => new SearchResult(c.Ref, c.Text, [])).ToList(), null, false)
                : await Task.Run(() => SearchEngine.Run(searched, query, strongs, options, books));
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
                outcome = await Task.Run(() => SearchEngine.Run(searched, query, strongs, options, books));
            }
            if (outcome.Error is not null) { SearchInfo.Text = outcome.Error; SearchList.ItemsSource = null; _hits = []; return; }

            RememberSearch(text);
            _hits = outcome.Results.ToList();
            if (entityBest is { Count: > 0 }) _hits = _hits.OrderByDescending(h => entityBest.GetValueOrDefault(h.Ref)).ToList();   // strongest references first
            ShowHits(t);
            ShowSearchStrongs(outcome.Strongs);
        }
        catch (Exception ex) { SearchInfo.Text = L("Fehler: ") + ex.Message; }
        finally { _searching = false; }
    }

    void ShowHits(TranslationInfo t)
    {
        var list = CurrentHits().ToList();
        SearchList.ItemsSource = list.Take(ShownHits).Select(h => new Entry(h.Ref, $"{Label(h.Ref)}\n{h.Marked()}" + (_hitNotes.TryGetValue(h.Ref, out var note) ? "\n" + note : ""))).ToList();
        var words = list.Sum(h => h.Spans.Count);
        SearchInfo.Text = _hits.Count == 0 ? L("Keine Treffer in {0}.", t.ShortName)
            : words == 0 ? L("{0} Verse in {1}{2}", list.Count, t.ShortName, (list.Count > ShownHits ? L(" (erste {0} angezeigt)", ShownHits) : ""))
            : L("{0} Verse · {1} Fundstellen in {2}{3}", list.Count, words, t.ShortName, (list.Count > ShownHits ? L(" (erste {0} angezeigt)", ShownHits) : ""));
        SearchQueueBtn.IsVisible = list.Count > 0; UpdateHitActions(list.Count > 0);

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
