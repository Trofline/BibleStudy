using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// Word statistics for the search: which words stand next to the hits, which words make up the book that is open (key words), and which words
/// occur only once in the translation. A click on a word searches for it or jumps to its place.
/// </summary>
public partial class MainWindow
{
    readonly Button _hitsStatsBtn = new() { Classes = { "seg" }, IsVisible = false, Margin = new(0, 0, 8, 4) };
    readonly Dictionary<string, Dictionary<string, int>> _freqs = [];
    readonly Dictionary<string, Dictionary<string, string>> _forms = [];

    /// <summary>How the text spells the words that the search shows folded (ü instead of u …).</summary>
    async Task<Dictionary<string, string>> DisplayFormsAsync(TranslationInfo t, List<CorpusVerse> corpus)
    {
        if (!_forms.TryGetValue(t.Id, out var forms)) _forms[t.Id] = forms = await Task.Run(() => TextStats.DisplayForms(corpus));
        return forms;
    }

    void InitWordStats()
    {
        _hitsStatsBtn.Content = L("Wortstatistik");
        ToolTip.SetTip(_hitsStatsBtn, L("Zeigt, welche Wörter auffällig oft neben den Treffern stehen, welche Wörter das offene Buch ausmachen und welche nur einmal vorkommen"));
        _hitsStatsBtn.Click += async (_, _) => await ShowWordStatsAsync();
    }

    async Task<List<CorpusVerse>?> StatsCorpusAsync(TranslationInfo t)
    {
        if (_corpora.TryGetValue(t.Id, out var have)) return have;
        if (!_store.IsIndexed(t.Id)) return null;
        return _corpora[t.Id] = await Task.Run(() => _store.GetCorpus(t.Id).Select(c => new CorpusVerse(c.Ref, c.Text)).ToList());
    }

    async Task ShowWordStatsAsync()
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t) return;
        var query = SearchBox.Text?.Trim() ?? "";
        var hits = CurrentHits().Where(h => h.Spans.Count > 0).ToList();
        var book = _curBook;

        var list = new ListBox { Background = Brushes.Transparent };
        var cloud = new WordCloudView { IsVisible = false };
        var star = new CollocationStarView { IsVisible = false };
        var body = new Grid { Children = { list, cloud, star } };
        var viewBox = new ComboBox { Width = 110, Margin = new(8, 0, 0, 0), ItemsSource = new[] { L("Liste"), L("Wolke"), L("Stern") }, SelectedIndex = 0 };
        var fillers = new CheckBox { Content = L("Füllwörter ausblenden"), IsChecked = true, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(fillers, L("Blendet sehr kurze und sehr häufige Wörter wie „und“, „ist“ oder „in“ aus"));
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new(0, 0, 0, 8) };
        var distance = new ComboBox { Width = 130, Margin = new(8, 0, 0, 0), ItemsSource = new[] { 3, 5, 10 }.Select(n => L("{0} Wörter", n)).ToList(), SelectedIndex = 1 };
        var seg = new WrapPanel { Margin = new(0, 0, 0, 8) };
        var modes = new[] { L("Nachbarwörter"), L("Schlüsselwörter"), L("Seltene Wörter") };
        var buttons = modes.Select(m => new Button { Content = m, Classes = { "seg" }, Margin = new(0, 0, 6, 4) }).ToList();
        foreach (var b in buttons) seg.Children.Add(b);
        seg.Children.Add(distance);
        seg.Children.Add(viewBox);
        seg.Children.Add(fillers);

        var dock = new DockPanel { Margin = new(18) };
        var close = new Button { Content = L("Schließen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom); DockPanel.SetDock(seg, Dock.Top); DockPanel.SetDock(info, Dock.Top);
        dock.Children.Add(close); dock.Children.Add(seg); dock.Children.Add(info); dock.Children.Add(body);
        var win = new Window { Title = L("Wortstatistik") + " · " + t.ShortName, Width = 760, Height = 720, MinWidth = 380, MinHeight = 360, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock };
        close.Click += (_, _) => win.Close();

        List<StatRow> current = [];
        var currentEmpty = "";
        var currentCenter = "";
        var currentMode = 0;

        // the same rows, as list, cloud or star
        void Fill(List<StatRow> rows, string empty, string center = "")
        {
            current = rows; currentEmpty = empty; currentCenter = center;
            var view = viewBox.SelectedIndex;
            if (view == 2 && currentMode != 0) view = 1;   // the star needs a search word in the middle
            if (rows.Count == 0) view = 0;
            list.IsVisible = view == 0; cloud.IsVisible = view == 1; star.IsVisible = view == 2;
            if (view == 1) cloud.SetWords(rows.Select(r => new WordItem(r.Word, r.Weight, r.Detail)).ToList());
            if (view == 2) star.SetData(center, rows.Select(r => new WordItem(r.Word, r.Weight, r.Detail)).ToList());
            list.ItemsSource = rows.Count == 0
                ? new[] { new ListBoxItem { Content = new TextBlock { Text = empty, Opacity = 0.7, TextWrapping = TextWrapping.Wrap }, IsEnabled = false } }
                : rows.Select(r => new ListBoxItem { Content = new TextBlock { Text = r.Word + "    " + r.Detail, TextWrapping = TextWrapping.Wrap }, Tag = r.Click }).ToList();
        }
        cloud.WordClicked += i => { if (i >= 0 && i < current.Count) current[i].Click?.Invoke(); };
        star.WordClicked += i => { if (i >= 0 && i < current.Count) current[i].Click?.Invoke(); };
        viewBox.SelectionChanged += (_, _) => { if (IsLoaded) Fill(current, currentEmpty, currentCenter); };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: Action click }) click();
            list.SelectedItem = null;
        };

        async Task ShowAsync(int mode)
        {
            for (var i = 0; i < buttons.Count; i++) buttons[i].Background = i == mode ? Res("BsAccentSoft") : null;
            distance.IsVisible = mode == 0;
            currentMode = mode;
            fillers.IsVisible = mode != 2;
            info.Text = L("Berechne …");
            Fill([], "");
            var corpus = await StatsCorpusAsync(t);
            if (corpus is null) { info.Text = L("Diese Übersetzung ist noch nicht für die Suche geladen. Such einmal etwas, dann geht es."); return; }
            if (!_freqs.TryGetValue(t.Id, out var freq)) _freqs[t.Id] = freq = await Task.Run(() => TextStats.Frequencies(corpus));
            // function words ("und", "ist", "in") stand next to everything; very short or very frequent words are left out on request
            var forms = await DisplayFormsAsync(t, corpus);
            var total = freq.Values.Sum(v => (long)v);
            var skipFillers = fillers.IsChecked == true;   // read here: the filter runs on another thread
            bool Keep(string w) => !skipFillers || (w.Length > 3 && freq.GetValueOrDefault(w) <= total * 0.006);

            if (mode == 0)
            {
                var window = new[] { 3, 5, 10 }[Math.Clamp(distance.SelectedIndex, 0, 2)];
                if (hits.Count == 0) { info.Text = L("Such zuerst etwas: Dann zeigt dieser Reiter, welche Wörter auffällig oft neben den Treffern stehen."); return; }
                var scores = await Task.Run(() => TextStats.Collocates(corpus, hits, window, 200, freq)).ContinueWith(x => x.Result.Where(w => Keep(w.Word)).Take(60).ToList());
                info.Text = L("Wörter, die auffällig oft höchstens {0} Wörter von den Treffern entfernt stehen. Ein Klick sucht beide zusammen.", window);
                Fill(scores.Select(s => new StatRow(forms.GetValueOrDefault(s.Word, s.Word), s.Score, L("{0}× in der Nähe · {1}× insgesamt", s.Count, s.Total), async () =>
                {
                    SearchBox.Text = $"({query}) NAH{window} {s.Word}";
                    win.Close();
                    await SearchAsync();
                })).ToList(), L("Nichts Auffälliges gefunden."), query);
            }
            else if (mode == 1)
            {
                var scores = await Task.Run(() => TextStats.KeyWords(corpus, [book], 200, freq)).ContinueWith(x => x.Result.Where(w => Keep(w.Word)).Take(60).ToList());
                info.Text = L("Typisch für {0}: Wörter, die hier viel häufiger vorkommen als im Rest der Bibel. Ein Klick sucht das Wort.", BookName(book));
                Fill(scores.Select(s => new StatRow(forms.GetValueOrDefault(s.Word, s.Word), s.Score, L("{0}× hier · {1}× insgesamt", s.Count, s.Total), async () =>
                {
                    SearchBox.Text = s.Word;
                    win.Close();
                    await SearchAsync();
                })).ToList(), L("Nichts Auffälliges gefunden."));
            }
            else
            {
                var rare = await Task.Run(() => TextStats.Rare(corpus, [book], 300, freq));
                info.Text = L("Wörter in {0}, die sonst nirgends in dieser Übersetzung stehen. Ein Klick springt zur Stelle.", BookName(book));
                Fill(rare.Select(r => new StatRow(r.Word, 1, Label(r.Ref), async () => { win.Close(); await GoToAsync(r.Ref.Book, r.Ref.Chapter, r.Ref.Verse); })).ToList(), L("Nichts Auffälliges gefunden."));
            }
        }

        for (var i = 0; i < buttons.Count; i++) { var m = i; buttons[i].Click += async (_, _) => await ShowAsync(m); }
        fillers.IsCheckedChanged += async (_, _) => { if (IsLoaded && fillers.IsVisible) await ShowAsync(currentMode); };
        distance.SelectionChanged += async (_, _) => { if (distance.IsVisible && IsLoaded) await ShowAsync(0); };
        win.Show(this);
        await ShowAsync(hits.Count > 0 ? 0 : 1);
    }

    /// <summary>One line of the word statistics: the word, how strong it is, the explaining text and what a click does.</summary>
    sealed record StatRow(string Word, double Weight, string Detail, Action? Click);
}
