using Avalonia.Controls;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>Concordance of a Strong's number: every place with the word in its verse, filterable by rendering and by book.</summary>
public partial class MainWindow
{
    List<StrongsOccurrence> _concOcc = [];
    string? _concForm, _concBook;
    Dictionary<VerseRef, string>? _concVerses;

    static string CleanWord(string w) => w.Trim().Trim('.', ',', ';', ':', '!', '?', '(', ')', '„', '“', '"', '«', '»').ToLowerInvariant();

    async Task ShowConcordanceAsync(string strongId, string title)
    {
        if (TranslationBox.SelectedItem is not TranslationInfo t || _building) return;
        SideTabs.SelectedIndex = 5;
        ConcTitle.Text = title;
        ConcList.ItemsSource = null; ConcForms.Children.Clear(); ConcBooks.Children.Clear();
        _concForm = _concBook = null; _concVerses = null;
        try
        {
            if (!_store.IsStrongsIndexed(t.Id))
            {
                _building = true;
                var progress = new Progress<(int Done, int Total)>(p => ConcInfo.Text = L("Einmalig: Konkordanz für {0} wird aufgebaut … {1}/{2} Kapitel", t.ShortName, p.Done, p.Total));
                await ConcordanceBuilder.BuildAsync(_provider, _store, t.Id, progress);
            }
            _concOcc = _store.GetOccurrences(t.Id, strongId, ConcordanceLimit);
            // verse texts let the list show every occurrence in its sentence (needs the search index, which is built on demand)
            if (_store.IsIndexed(t.Id) && _concOcc.Count > 0)
            {
                if (!_corpora.TryGetValue(t.Id, out var corpus))
                    _corpora[t.Id] = corpus = await Task.Run(() => _store.GetCorpus(t.Id).Select(c => new CorpusVerse(c.Ref, c.Text)).ToList());
                _concVerses = corpus.ToDictionary(v => v.Ref, v => v.Text);
            }
            ShowConcordance(t);
        }
        catch (Exception ex) { ConcInfo.Text = L("Konkordanz nicht verfügbar: ") + ex.Message; }
        finally { _building = false; }
    }

    void ShowConcordance(TranslationInfo t)
    {
        var all = _concOcc;
        var shown = all.Where(o => (_concForm is null || CleanWord(o.Word) == _concForm) && (_concBook is null || o.Ref.Book == _concBook)).ToList();
        ConcInfo.Text = all.Count == 0 ? L("Keine Vorkommen in {0}.", t.ShortName)
            : L("{0}{1} von {2} Vorkommen in {3}", shown.Count, (all.Count >= ConcordanceLimit ? "+" : ""), all.Count, t.ShortName) + (_concVerses is null ? L(" · Verstexte erscheinen, sobald die Suche für diese Übersetzung einmal lief.") : "");

        // renderings: how this word was translated, click to filter
        ConcForms.Children.Clear();
        var forms = all.GroupBy(o => CleanWord(o.Word)).OrderByDescending(g => g.Count()).Take(14).ToList();
        if (forms.Count > 1)
            foreach (var g in forms) ConcForms.Children.Add(FilterChip($"{g.Key} {g.Count()}", _concForm == g.Key, null, () => { _concForm = _concForm == g.Key ? null : g.Key; ShowConcordance(t); }));

        // distribution over the books
        ConcBooks.Children.Clear();
        var perBook = all.Where(o => _concForm is null || CleanWord(o.Word) == _concForm).GroupBy(o => o.Ref.Book).ToDictionary(g => g.Key, g => g.Count());
        if (perBook.Count > 1)
            foreach (var b in _books.Where(b => perBook.ContainsKey(b.Id)))
            {
                var id = b.Id;
                ConcBooks.Children.Add(FilterChip($"{ShortBook(b)} {perBook[b.Id]}", _concBook == id, HomeView.ColorOf(id), () => { _concBook = _concBook == id ? null : id; ShowConcordance(t); }, b.CommonName));
            }

        ConcList.ItemsSource = shown.Take(500).Select(o =>
        {
            var word = o.Word.Trim();
            var text = _concVerses is not null && _concVerses.TryGetValue(o.Ref, out var v) ? Highlight(v, word) : word;
            return new Entry(o.Ref, $"{Label(o.Ref)}\n{text}");
        }).ToList();
    }

    /// <summary>Wraps the first whole-word occurrence of the rendering in « ».</summary>
    static string Highlight(string verse, string word)
    {
        if (word.Length == 0) return verse;
        var from = 0;
        while ((from = verse.IndexOf(word, from, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = from + word.Length;
            if ((from == 0 || !char.IsLetterOrDigit(verse[from - 1])) && (end >= verse.Length || !char.IsLetterOrDigit(verse[end])))
                return $"{verse[..from]}«{verse[from..end]}»{verse[end..]}";
            from = end;
        }
        return verse;
    }

    Button FilterChip(string text, bool active, Color? underline, Action click, string? tip = null)
    {
        var chip = new Button
        {
            Content = text, Classes = { "tool" }, Width = double.NaN, Height = 24, Padding = new(8, 0), FontSize = 11, Margin = new(0, 0, 4, 4),
            Background = active ? Res("BsAccentSoft") : null, Foreground = active ? Res("BsAccent") : Res("BsMuted"),
            BorderBrush = underline is Color c ? new SolidColorBrush(c) : null, BorderThickness = underline is null ? new(0) : new(0, 0, 0, 2),
        };
        if (tip is not null) ToolTip.SetTip(chip, tip);
        chip.Click += (_, _) => click();
        return chip;
    }
}
