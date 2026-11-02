using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;
using BibleStudy.Core.Search;

namespace BibleStudy.App;

/// <summary>
/// The chapter report: everything the program can say about the open chapter on one page. The numbers, the typical and the rare words, people and places,
/// the grammar of the original text, where the chapter points to, and what you wrote about it. Every part fills in when its data is there.
/// </summary>
public partial class MainWindow
{
    async Task ShowChapterReportAsync()
    {
        if (_curBook == "" || TranslationBox.SelectedItem is not TranslationInfo t) return;
        var book = _curBook; var chapter = _curChapter;
        var title = $"{BookName(book)} {chapter}";
        var stack = new StackPanel { Spacing = 6, Margin = new(0, 0, 14, 0) };
        var md = new StringBuilder($"# {title}\n\n");

        void Head(string text) { stack.Children.Add(Eyebrow(text)); md.Append("## ").Append(text).Append("\n\n"); }
        void Line(string text) { stack.Children.Add(Muted(text)); md.Append(text).Append("\n\n"); }
        WrapPanel Chips() { var w = new WrapPanel(); stack.Children.Add(w); return w; }

        var copy = new Button { Content = L("Als Markdown kopieren"), Classes = { "tool" }, Padding = new(10, 0), Height = 28, Margin = new(0, 0, 8, 0) };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { await cb.SetTextAsync(md.ToString()); StatusText.Text = L("Bericht kopiert."); }
        };
        var close = new Button { Content = L("Schließen"), Classes = { "primary" } };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0), Children = { copy, close } };
        var dock = new DockPanel { Margin = new(18) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var heading = new TextBlock { Text = L("Kapitel-Bericht") + " · " + title, Classes = { "h2" }, Margin = new(0, 0, 0, 6) };
        DockPanel.SetDock(heading, Dock.Top);
        dock.Children.Add(buttons); dock.Children.Add(heading); dock.Children.Add(new ScrollViewer { Content = stack });
        var win = new Window { Title = L("Kapitel-Bericht") + " · " + title, Width = 640, Height = 760, MinWidth = 380, MinHeight = 400, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock };
        close.Click += (_, _) => win.Close();
        win.Show(this);

        void SearchFor(string text) { SearchBox.Text = text; win.Close(); _ = SearchAsync(); }

        // 1. in numbers, key words, rare words (from the text of the open translation)
        var corpus = await StatsCorpusAsync(t);
        if (corpus is null)
        {
            Head(L("In Zahlen"));
            Line(L("Diese Übersetzung ist noch nicht für die Suche geladen. Such einmal etwas, dann geht es."));
        }
        else
        {
            if (!_freqs.TryGetValue(t.Id, out var freq)) _freqs[t.Id] = freq = await Task.Run(() => TextStats.Frequencies(corpus));
            bool Part(CorpusVerse v) => v.Ref.Book == book && v.Ref.Chapter == chapter;
            var verses = corpus.Where(Part).ToList();
            Head(L("In Zahlen"));
            var words = verses.Sum(v => v.Words.Length);
            var distinct = verses.SelectMany(v => Enumerable.Range(0, v.Words.Length).Select(i => v.Word(i, true))).Distinct().Count();
            Line(L("{0} Verse · {1} Wörter · {2} verschiedene Wörter in {3}", verses.Count, words, distinct, t.ShortName));

            var keys = await Task.Run(() => TextStats.KeyWords(corpus, Part, 24, freq, 2));
            Head(L("Typische Wörter dieses Kapitels"));
            if (keys.Count == 0) Line(L("Nichts Auffälliges gefunden."));
            else
            {
                var chips = Chips();
                foreach (var k in keys) { var w = k.Word; chips.Children.Add(FilterChip($"{k.Word}  {k.Count}×", false, null, () => SearchFor(w), L("{0}× hier · {1}× insgesamt", k.Count, k.Total))); }
                md.Append(string.Join(", ", keys.Select(k => $"{k.Word} ({k.Count}×)"))).Append("\n\n");
            }

            var rare = await Task.Run(() => TextStats.Rare(corpus, Part, 40, freq));
            Head(L("Wörter, die sonst nirgends vorkommen"));
            if (rare.Count == 0) Line(L("Nichts Auffälliges gefunden."));
            else
            {
                var chips = Chips();
                foreach (var r in rare.DistinctBy(x => x.Word.ToLowerInvariant())) { var w = r.Word; chips.Children.Add(FilterChip(r.Word, false, null, () => SearchFor(w), Label(r.Ref))); }
                md.Append(string.Join(", ", rare.Select(r => r.Word).Distinct())).Append("\n\n");
            }
        }

        // 2. people and places
        try
        {
            var lex = await _hello.GetChapterLexiconAsync(book, chapter);
            Head(L("Personen und Orte"));
            if (lex.Count == 0) Line(L("Keine Personen oder Orte erfasst."));
            else
            {
                var chips = Chips();
                foreach (var (item, itemVerses) in lex.OrderByDescending(x => x.Item.References).Take(40))
                {
                    var it = item;
                    chips.Children.Add(FilterChip((item.Kind == "places" ? "📍 " : "") + item.Name, false, null,
                        () => { win.Close(); OpenWork(NavGroups.First(g => g.Name == "Bücher"), LexiconTab); _ = ShowLexEntryAsync(it); }, L("{0} Erwähnungen · öffnet das Lexikon", item.References)));
                }
                md.Append(string.Join(", ", lex.OrderByDescending(x => x.Item.References).Take(40).Select(x => x.Item.Name))).Append("\n\n");
            }
        }
        catch { /* offline: the other parts stay */ }

        // 3. the grammar of the original text
        if (SyntaxProvider.IsAvailable(book))
        {
            try
            {
                var grammar = await _syntax.LoadChapterWordsAsync(book, chapter);
                if (grammar.Count > 0)
                {
                    Head(L("Grundtext in Zahlen"));
                    Line(L("{0} Wörter im Grundtext", grammar.Count));
                    var classes = grammar.Where(w => w.Class.Length > 0).GroupBy(w => w.Class).OrderByDescending(g => g.Count()).Take(7).Select(g => $"{Term(g.Key)} {g.Count()}").ToList();
                    if (classes.Count > 0) Line(L("Wortarten") + ": " + string.Join(" · ", classes));

                    var verbs = grammar.Where(w => w.Class == "verb").ToList();
                    if (verbs.Count > 0)
                    {
                        var forms = verbs.GroupBy(w => string.Join(" ", new[] { w.Tense, w.Mood, w.Voice }.Where(x => !string.IsNullOrEmpty(x)).Select(x => Term(x!)))).OrderByDescending(g => g.Count()).Take(8)
                            .Select(g => $"{(g.Key.Length == 0 ? "?" : g.Key)} {g.Count()}×").ToList();
                        Line(L("Verbformen ({0} Verben)", verbs.Count) + ": " + string.Join(" · ", forms));
                    }

                    var top = grammar.Where(w => w.Class is "verb" or "noun" or "adj" && w.Strong.Length > 0).GroupBy(w => w.Strong).OrderByDescending(g => g.Count()).Take(14).ToList();
                    if (top.Count > 0)
                    {
                        Line(L("Häufigste Wörter") + ":");
                        var chips = Chips();
                        foreach (var g in top)
                        {
                            var first = g.First(); var id = first.Strong;
                            chips.Children.Add(FilterChip($"{first.Lemma}  {Mean(first.Gloss)}  {g.Count()}×", false, null, () => SearchFor(id), id));
                        }
                        md.Append(string.Join(", ", top.Select(g => $"{g.First().Lemma} {g.First().Gloss} ({g.Count()}×)"))).Append("\n\n");
                    }
                }
            }
            catch { /* no grammar data (offline) */ }
        }

        // 4. where the chapter points to
        try
        {
            var lastVerse = corpus?.Where(v => v.Ref.Book == book && v.Ref.Chapter == chapter).Select(v => v.Ref.Verse).DefaultIfEmpty(0).Max() ?? 0;
            if (lastVerse == 0) lastVerse = 60;
            var all = await Task.WhenAll(Enumerable.Range(1, Math.Min(lastVerse, 120)).Select(async v =>
            {
                try { return VisibleXrefs(new VerseRef(book, chapter, v), await _hello.GetCrossReferencesAsync(book, chapter, v)); }
                catch { return new List<CrossRef>(); }
            }));
            var targets = all.SelectMany(x => x).Where(x => !(x.Target.Book == book && x.Target.Chapter == chapter))
                .GroupBy(x => (x.Target.Book, x.Target.Chapter)).Select(g => (g.Key, Votes: g.Sum(x => x.Score), Count: g.Count()))
                .OrderByDescending(x => x.Votes).Take(16).ToList();
            Head(L("Worauf dieses Kapitel verweist"));
            if (targets.Count == 0) Line(L("Keine Querverweise gefunden."));
            else
            {
                Line(L("Die Kapitel, auf die die Verse dieses Kapitels am stärksten verweisen (nach Stimmen):"));
                var chips = Chips();
                foreach (var tg in targets)
                {
                    var (b, c) = tg.Key;
                    chips.Children.Add(FilterChip($"{BookName(b)} {c}  {tg.Count}×", false, HomeView.ColorOf(b), () => { win.Close(); _ = GoToAsync(b, c); }, L("{0} Querverweise", tg.Count)));
                }
                md.Append(string.Join(", ", targets.Select(tg => $"{BookName(tg.Key.Book)} {tg.Key.Chapter} ({tg.Count}×)"))).Append("\n\n");
            }
        }
        catch { /* offline */ }

        // 5. what you wrote
        Head(L("Deine Arbeit an diesem Kapitel"));
        var notes = _vault.GetNotes().Count(n => n.Ref.Book == book && n.Ref.Chapter == chapter);
        var marks = _vault.GetMarks(book, chapter).Count;
        Line(L("{0} Notizen · {1} Markierungen", notes, marks));
    }
}
