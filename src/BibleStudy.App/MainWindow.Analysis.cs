using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>
/// Analysis tools for understanding words and chapters: the words of the clicked verse, key words of a chapter, rare words,
/// and for any word its relatives, usual company and spread over the Bible. Based on the KJV's Strong tags (loaded once).
/// </summary>
public partial class MainWindow
{
    const int AnalysisTab = 21;
    const string AnalysisTranslation = "eng_kjv";

    WordAnalyzer? _analyzer;
    bool _analyzerLoading;
    readonly StackPanel _analysisResult = new() { Spacing = 8 };
    readonly Dictionary<string, StrongsEntry?> _entryCache = [];

    static string Gloss(StrongsEntry e)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(e.KjvUsage.Length > 0 ? e.KjvUsage : e.Definition, @"\[[^\]]*\]|\([^)]*\)", "").Split(',', ';', '(')[0].Trim().TrimStart(':', '-', ' ');
        return s.Length > 26 ? s[..26] + "…" : s;
    }

    async Task<StrongsEntry?> EntryAsync(string id)
    {
        if (_entryCache.TryGetValue(id, out var e)) return e;
        try { e = await _strongs.LookupAsync(id); } catch { e = null; }
        return _entryCache[id] = e;
    }

    async Task<Control> WordChipAsync(string id, string? suffix = null)
    {
        var e = await EntryAsync(id);
        var label = e is null ? id : $"{e.Lemma}  {Gloss(e)}";
        return FilterChip(label + (suffix is null ? "" : "  " + suffix), false, null, () => _ = AnalyzeWordAsync(id), id);
    }

    void BuildAnalysisUi()
    {
        AnalysisPanel.Children.Clear();
        AnalysisPanel.Children.Add(new TextBlock { Text = L("ANALYSE"), Classes = { "eyebrow" } });
        AnalysisPanel.Children.Add(new TextBlock { Text = L("Werkzeuge, um Wörter und Kapitel besser zu verstehen. Grundlage sind die Strong-Nummern der KJV; sie werden einmalig geladen."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });

        // grammar and sentence structure of the clicked verse (original text); needs no downloaded index
        AnalysisPanel.Children.Add(_syntaxView);
        _ = ShowSyntaxAsync();

        if (_analyzer is null)
        {
            var go = new Button { Content = _store.IsStrongsIndexed(AnalysisTranslation) ? L("Analyse starten") : L("Analysedaten laden (einmalig)"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
            var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
            go.Click += async (_, _) => { go.IsEnabled = false; await LoadAnalyzerAsync(t => info.Text = t); BuildAnalysisUi(); };
            AnalysisPanel.Children.Add(go); AnalysisPanel.Children.Add(info);
            return;
        }

        // 1. words of the clicked verse
        AnalysisPanel.Children.Add(new TextBlock { Text = L("Wörter des angeklickten Verses"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var verseWords = new WrapPanel();
        AnalysisPanel.Children.Add(verseWords);
        if (_selected is int v)
            _ = FillChipsAsync(verseWords, _analyzer.WordsOf(new VerseRef(_curBook, _curChapter, v)).Select(w => (w.Id, (string?)null)).ToList(), L("Für diesen Vers gibt es keine Strong-Daten."));
        else verseWords.Children.Add(new TextBlock { Text = L("Erst einen Vers anklicken."), Classes = { "caption" } });

        // 2. the chapter
        AnalysisPanel.Children.Add(new TextBlock { Text = L("Schlüsselwörter von {0} {1}", BookName(_curBook), _curChapter), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var keys = new WrapPanel();
        AnalysisPanel.Children.Add(keys);
        _ = FillChipsAsync(keys, _analyzer.Keywords(_curBook, _curChapter).Select(k => (k.Id, (string?)$"{k.InChapter}×")).ToList(), L("Keine auffälligen Wörter."));
        AnalysisPanel.Children.Add(new TextBlock { Text = L("Einmalige Wörter (kommen nur hier in der Bibel vor)"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var hapax = new WrapPanel();
        AnalysisPanel.Children.Add(hapax);
        _ = FillChipsAsync(hapax, _analyzer.Hapax(_curBook, _curChapter).Take(24).Select(h => (h.Id, (string?)$"V. {h.Verse}")).ToList(), L("Keine einmaligen Wörter."));

        // 3. any word
        AnalysisPanel.Children.Add(new TextBlock { Text = L("Ein Wort analysieren"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var box = new TextBox { Watermark = L("G26, H157, agape …  (Enter)") };
        box.KeyDown += async (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            var q = box.Text?.Trim() ?? "";
            if (q.Length == 0) return;
            var id = WordAnalyzer.Normalize(q) ?? (await _strongs.SearchAsync(q, 1)).FirstOrDefault()?.Id;
            if (id is not null) await AnalyzeWordAsync(id);
        };
        AnalysisPanel.Children.Add(box);
        AnalysisPanel.Children.Add(_analysisResult);
    }

    async Task FillChipsAsync(WrapPanel panel, List<(string Id, string? Suffix)> items, string emptyText)
    {
        if (items.Count == 0) { panel.Children.Add(new TextBlock { Text = emptyText, Classes = { "caption" } }); return; }
        foreach (var (id, suffix) in items) panel.Children.Add(await WordChipAsync(id, suffix));
    }

    async Task LoadAnalyzerAsync(Action<string> status)
    {
        if (_analyzerLoading) return;
        _analyzerLoading = true;
        try
        {
            if (!_store.IsStrongsIndexed(AnalysisTranslation))
            {
                if (_building) { status(L("Die Konkordanz wird gerade aufgebaut – bitte kurz warten.")); return; }
                _building = true;
                try
                {
                    var progress = new Progress<(int Done, int Total)>(p => status(L("Einmalig: Strong-Index für {0} wird aufgebaut … {1}/{2} Kapitel", "KJV", p.Done, p.Total)));
                    await ConcordanceBuilder.BuildAsync(_provider, _store, AnalysisTranslation, progress);
                }
                finally { _building = false; }
            }
            status(L("Analyse wird vorbereitet …"));
            _analyzer = await Task.Run(() => new WordAnalyzer(_store.GetAllOccurrences(AnalysisTranslation)));
        }
        catch (Exception ex) { status(L("Fehler: ") + ex.Message); }
        finally { _analyzerLoading = false; }
    }

    /// <summary>Everything the analyzer knows about one word, in one card.</summary>
    async Task AnalyzeWordAsync(string rawId)
    {
        if (_analyzer is null || WordAnalyzer.Normalize(rawId) is not { } id) return;
        SideTabs.SelectedIndex = AnalysisTab;
        _analysisResult.Children.Clear();
        var e = await EntryAsync(id);
        if (e is not null) _analysisResult.Children.Add(StudyCard(e));

        var verses = _analyzer.Occurrences(id);
        var order = _books.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
        int Pos(VerseRef r) => order.GetValueOrDefault(r.Book, 999);
        var ot = verses.Count(r => Pos(r) < 39);
        var line = verses.Count == 0 ? L("In der KJV nicht gefunden.") : L("Kommt {0}× vor · Altes Testament {1}× · Neues Testament {2}×", verses.Count, ot, verses.Count - ot);
        _analysisResult.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });

        if (verses.Count > 0)
        {
            var sorted = verses.OrderBy(Pos).ThenBy(r => r.Chapter).ThenBy(r => r.Verse).ToList();
            var first = sorted[0]; var last = sorted[^1];
            _analysisResult.Children.Add(new WrapPanel
            {
                Children =
                {
                    new TextBlock { Text = L("Erste Stelle:") + " ", VerticalAlignment = VerticalAlignment.Center },
                    FilterChip(Label(first), false, null, () => _ = GoToAsync(first.Book, first.Chapter, first.Verse), L("Springt zur Stelle")),
                    new TextBlock { Text = L("Letzte Stelle:") + " ", VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) },
                    FilterChip(Label(last), false, null, () => _ = GoToAsync(last.Book, last.Chapter, last.Verse), L("Springt zur Stelle")),
                },
            });
            _analysisResult.Children.Add(SpreadBars(verses));
        }

        if (e is not null && WordAnalyzer.Related(e.Derivation, id) is { Count: > 0 } rel)
        {
            _analysisResult.Children.Add(new TextBlock { Text = L("Verwandte Wörter (Herkunft)"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) });
            var w = new WrapPanel(); _analysisResult.Children.Add(w);
            foreach (var r in rel) w.Children.Add(await WordChipAsync(r));
        }

        if (verses.Count > 0)
        {
            _analysisResult.Children.Add(new TextBlock { Text = L("Steht oft zusammen mit"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) });
            var w = new WrapPanel(); _analysisResult.Children.Add(w);
            foreach (var (other, shared) in _analyzer.CoOccurring(id)) w.Children.Add(await WordChipAsync(other, $"{shared}×"));
        }

        var padded = $"{id[0]}{int.Parse(id[1..]):D4}";
        var all = new Button { Content = L("Alle Vorkommen"), Margin = new(0, 6, 8, 0) };
        all.Click += async (_, _) => await ShowConcordanceAsync(padded, $"{id}  {e?.Lemma}");
        _analysisResult.Children.Add(all);
    }

    /// <summary>A bar per Bible book showing where the word occurs; the color is the book's genre.</summary>
    Control SpreadBars(IReadOnlyList<VerseRef> verses)
    {
        var perBook = verses.GroupBy(r => r.Book).ToDictionary(g => g.Key, g => g.Count());
        var max = Math.Max(1, perBook.Values.Max());
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Height = 64, Margin = new(0, 6, 0, 0) };
        foreach (var b in _books)
        {
            var n = perBook.GetValueOrDefault(b.Id);
            var bar = new Border
            {
                Width = 7, Height = n == 0 ? 2 : Math.Max(4, 60.0 * n / max), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new(2),
                Background = new SolidColorBrush(HomeView.ColorOf(b.Id), n == 0 ? 0.15 : 1),
            };
            ToolTip.SetTip(bar, $"{b.CommonName}: {n}×");
            row.Children.Add(bar);
        }
        return new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = row };
    }
}

