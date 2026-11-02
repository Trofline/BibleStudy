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
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new(0, 0, 0, 8) };
        var distance = new ComboBox { Width = 130, Margin = new(8, 0, 0, 0), ItemsSource = new[] { 3, 5, 10 }.Select(n => L("{0} Wörter", n)).ToList(), SelectedIndex = 1 };
        var seg = new WrapPanel { Margin = new(0, 0, 0, 8) };
        var modes = new[] { L("Nachbarwörter"), L("Schlüsselwörter"), L("Seltene Wörter") };
        var buttons = modes.Select(m => new Button { Content = m, Classes = { "seg" }, Margin = new(0, 0, 6, 4) }).ToList();
        foreach (var b in buttons) seg.Children.Add(b);
        seg.Children.Add(distance);

        var dock = new DockPanel { Margin = new(18) };
        var close = new Button { Content = L("Schließen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom); DockPanel.SetDock(seg, Dock.Top); DockPanel.SetDock(info, Dock.Top);
        dock.Children.Add(close); dock.Children.Add(seg); dock.Children.Add(info); dock.Children.Add(list);
        var win = new Window { Title = L("Wortstatistik") + " · " + t.ShortName, Width = 560, Height = 640, MinWidth = 380, MinHeight = 360, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock };
        close.Click += (_, _) => win.Close();

        void Fill(List<(string Text, Action? Click)> rows, string empty)
        {
            list.ItemsSource = rows.Count == 0
                ? new[] { new ListBoxItem { Content = new TextBlock { Text = empty, Opacity = 0.7, TextWrapping = TextWrapping.Wrap }, IsEnabled = false } }
                : rows.Select(r => new ListBoxItem { Content = new TextBlock { Text = r.Text, TextWrapping = TextWrapping.Wrap }, Tag = r.Click }).ToList();
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: Action click }) click();
            list.SelectedItem = null;
        };

        async Task ShowAsync(int mode)
        {
            for (var i = 0; i < buttons.Count; i++) buttons[i].Background = i == mode ? Res("BsAccentSoft") : null;
            distance.IsVisible = mode == 0;
            info.Text = L("Berechne …");
            Fill([], "");
            var corpus = await StatsCorpusAsync(t);
            if (corpus is null) { info.Text = L("Diese Übersetzung ist noch nicht für die Suche geladen. Such einmal etwas, dann geht es."); return; }
            if (!_freqs.TryGetValue(t.Id, out var freq)) _freqs[t.Id] = freq = await Task.Run(() => TextStats.Frequencies(corpus));

            if (mode == 0)
            {
                var window = new[] { 3, 5, 10 }[Math.Clamp(distance.SelectedIndex, 0, 2)];
                if (hits.Count == 0) { info.Text = L("Such zuerst etwas: Dann zeigt dieser Reiter, welche Wörter auffällig oft neben den Treffern stehen."); return; }
                var scores = await Task.Run(() => TextStats.Collocates(corpus, hits, window, 60, freq));
                info.Text = L("Wörter, die auffällig oft höchstens {0} Wörter von den Treffern entfernt stehen. Ein Klick sucht beide zusammen.", window);
                Fill(scores.Select(s => (s.Word + "    " + L("{0}× in der Nähe · {1}× insgesamt", s.Count, s.Total), (Action?)(async () =>
                {
                    SearchBox.Text = $"({query}) NAH{window} {s.Word}";
                    win.Close();
                    await SearchAsync();
                }))).ToList(), L("Nichts Auffälliges gefunden."));
            }
            else if (mode == 1)
            {
                var scores = await Task.Run(() => TextStats.KeyWords(corpus, [book], 60, freq));
                info.Text = L("Typisch für {0}: Wörter, die hier viel häufiger vorkommen als im Rest der Bibel. Ein Klick sucht das Wort.", BookName(book));
                Fill(scores.Select(s => (s.Word + "    " + L("{0}× hier · {1}× insgesamt", s.Count, s.Total), (Action?)(async () =>
                {
                    SearchBox.Text = s.Word;
                    win.Close();
                    await SearchAsync();
                }))).ToList(), L("Nichts Auffälliges gefunden."));
            }
            else
            {
                var rare = await Task.Run(() => TextStats.Rare(corpus, [book], 300, freq));
                info.Text = L("Wörter in {0}, die sonst nirgends in dieser Übersetzung stehen. Ein Klick springt zur Stelle.", BookName(book));
                Fill(rare.Select(r => ($"{r.Word}    {Label(r.Ref)}", (Action?)(async () => { win.Close(); await GoToAsync(r.Ref.Book, r.Ref.Chapter, r.Ref.Verse); }))).ToList(), L("Nichts Auffälliges gefunden."));
            }
        }

        for (var i = 0; i < buttons.Count; i++) { var m = i; buttons[i].Click += async (_, _) => await ShowAsync(m); }
        distance.SelectionChanged += async (_, _) => { if (distance.IsVisible && IsLoaded) await ShowAsync(0); };
        win.Show(this);
        await ShowAsync(hits.Count > 0 ? 0 : 1);
    }
}
