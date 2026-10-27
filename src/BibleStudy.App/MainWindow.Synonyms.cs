using System.Net.Http.Json;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Word study extras: how else the original word is rendered in this translation, and German synonyms (openthesaurus.de).</summary>
public partial class MainWindow
{
    readonly Dictionary<string, IReadOnlyList<string>> _synonymCache = new(StringComparer.OrdinalIgnoreCase);

    async Task AddWordExtrasAsync(WordSpan span)
    {
        var t = TranslationBox.SelectedItem as TranslationInfo;

        // 1) other renderings of the same Strong number (from the concordance of this translation, if it was built)
        if (t is not null && span.Strongs.Length > 0 && _store.IsStrongsIndexed(t.Id))
        {
            var renderings = span.Strongs.SelectMany(id => _store.GetOccurrences(t.Id, id, 3000)).GroupBy(o => CleanWord(o.Word))
                .Where(g => g.Key.Length > 0).OrderByDescending(g => g.Count()).Take(14).ToList();
            if (renderings.Count > 1)
            {
                StudyPanel.Children.Add(Eyebrow("So wird das Wort hier noch wiedergegeben"));
                StudyPanel.Children.Add(Muted("Andere deutsche Wörter für dieselbe Grundtext-Nummer – ein Klick zeigt alle Stellen."));
                var wrap = new WrapPanel();
                foreach (var g in renderings)
                {
                    var word = g.Key; var ids = span.Strongs;
                    wrap.Children.Add(FilterChip($"{g.Key} {g.Count()}", g.Key == CleanWord(span.Word), null, async () => await ShowConcordanceAsync(ids[0], $"{ids[0]} „{word}“")));
                }
                StudyPanel.Children.Add(wrap);
            }
        }

        // 2) German synonyms of the word the reader clicked
        if (t?.Language is "deu" or "ger" or "de" || t?.LanguageName == "German")
        {
            var word = ExtractWord(span.Word);
            if (word.Length < 3) return;
            var panel = new StackPanel { Spacing = 4 };
            StudyPanel.Children.Add(Eyebrow($"Synonyme zu „{word}“"));
            StudyPanel.Children.Add(panel);
            panel.Children.Add(Muted("Lade …"));
            var groups = await SynonymsAsync(word);
            panel.Children.Clear();
            if (groups.Count == 0) { panel.Children.Add(Muted("Keine Synonyme gefunden.")); return; }
            var wrap = new WrapPanel();
            foreach (var s in groups.Take(24))
            {
                var term = s;
                wrap.Children.Add(FilterChip(s, false, null, () => { SearchBox.Text = term; SideTabs.SelectedIndex = 0; _ = SearchAsync(); }, "Im Text suchen"));
            }
            panel.Children.Add(wrap);
            panel.Children.Add(Muted("Ein Klick sucht das Wort im Bibeltext – so siehst du, ob ein anderes Wort die Stelle klarer macht. Quelle: openthesaurus.de (CC BY-SA 4.0)."));
        }
    }

    static string ExtractWord(string raw)
    {
        var w = raw.Trim().Split([' ', ' '], StringSplitOptions.RemoveEmptyEntries).OrderByDescending(x => x.Length).FirstOrDefault() ?? "";
        return w.Trim('.', ',', ';', ':', '!', '?', '(', ')', '„', '“', '"', '«', '»', '–', '—');
    }

    async Task<IReadOnlyList<string>> SynonymsAsync(string word)
    {
        if (_synonymCache.TryGetValue(word, out var cached)) return cached;
        var result = new List<string>();
        try
        {
            using var doc = await JsonDocument.ParseAsync(await _http.GetStreamAsync($"https://www.openthesaurus.de/synonyme/search?q={Uri.EscapeDataString(word)}&format=application/json&similar=true").WaitAsync(TimeSpan.FromSeconds(10)));
            foreach (var set in doc.RootElement.GetProperty("synsets").EnumerateArray())
                foreach (var term in set.GetProperty("terms").EnumerateArray())
                    if (term.TryGetProperty("term", out var tv) && tv.GetString() is { Length: > 0 } text && !string.Equals(text, word, StringComparison.OrdinalIgnoreCase) && !text.Contains('(') && !result.Contains(text))
                        result.Add(text);
        }
        catch { /* offline or rate limited: the section just stays empty */ }
        return _synonymCache[word] = result;
    }
}
