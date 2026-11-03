using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Diagram;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>
/// The pericope analysis: pick a passage and see it in pictures – word classes, verb forms, key words and where they occur, the rhythm of the verses,
/// the length and build of the sentences and how the clauses are joined – with a few sentences that say what stands out. Worked out on the computer from
/// the grammar data of the Greek and Hebrew text (MACULA, Clear Bible, CC BY 4.0).
/// </summary>
public partial class MainWindow
{
    const int PericopeTab = 29;

    readonly NumericUpDown _periFrom = new() { Minimum = 1, Maximum = 200, Value = 1, Increment = 1, FormatString = "0", Width = 110 };
    readonly NumericUpDown _periTo = new() { Minimum = 1, Maximum = 200, Value = 1, Increment = 1, FormatString = "0", Width = 110 };
    readonly StackPanel _periResults = new() { Spacing = 10 };
    readonly TextBlock _periStatus = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    readonly Dictionary<(string Book, int Chapter), List<GrammarWord>> _periWords = [];
    (string Book, int Chapter) _periFor;
    bool _periBuilt, _periQuiet;
    int _periRun;

    void BuildPericopeShell()
    {
        _periBuilt = true;
        PericopePanel.Children.Clear();
        PericopePanel.Children.Add(new TextBlock { Text = L("PERIKOPEN-ANALYSE"), Classes = { "eyebrow" } });
        PericopePanel.Children.Add(new TextBlock
        {
            Text = L("Eine Perikope ist ein zusammenhängender Abschnitt der Bibel, zum Beispiel eine Erzählung, ein Gleichnis oder eine Rede. Wähle unten „Von Vers … bis …“ (oder „Ganzes Kapitel“) und klicke auf „Analysieren“. Du siehst den Abschnitt dann in Bildern: Wortarten, Verbformen, Schlüsselwörter und wo sie vorkommen, Rhythmus der Verse, Satzbau und wie die Sätze verbunden sind. Ausgewertet aus dem griechischen oder hebräischen Text."),
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap,
        });
        var go = new Button { Content = L("Analysieren"), Classes = { "primary" }, Padding = new(14, 0), Height = 32 };
        var all = new Button { Content = L("Ganzes Kapitel"), Padding = new(12, 0), Height = 32 };
        var save = new Button { Content = L("Als Bild speichern"), Padding = new(12, 0), Height = 32 };
        go.Click += async (_, _) => await RunPericopeAsync();
        all.Click += async (_, _) => { _periQuiet = true; _periFrom.Value = 1; _periTo.Value = _periTo.Maximum; _periQuiet = false; await RunPericopeAsync(); };
        save.Click += (_, _) => { if (_periResults.Bounds.Width > 10) ExportControlImage(_periResults, L("Perikopen-Analyse") + "_" + BookName(_curBook) + "_" + _curChapter + "_" + (int)(_periFrom.Value ?? 1) + "-" + (int)(_periTo.Value ?? 1)); };
        var row = new WrapPanel
        {
            Margin = new(0, 6, 0, 0),
            Children =
            {
                Pad(new TextBlock { Text = L("Von Vers"), VerticalAlignment = VerticalAlignment.Center }), Pad(_periFrom),
                Pad(new TextBlock { Text = L("bis"), VerticalAlignment = VerticalAlignment.Center }), Pad(_periTo),
                Pad(go), Pad(all), Pad(save),
                Pad(PopOutButton(() => _periResults.Bounds.Width > 10 ? _periResults : null, () => L("Perikopen-Analyse") + " · " + BookName(_curBook) + " " + _curChapter)),
            },
        };
        PericopePanel.Children.Add(row);
        PericopePanel.Children.Add(_periStatus);
        PericopePanel.Children.Add(_periResults);
    }

    static Control Pad(Control c) { c.Margin = new(0, 0, 8, 6); return c; }

    /// <summary>The tab was opened or the chapter changed: set the range (the whole chapter, from the clicked verse) and analyse.</summary>
    async Task ShowPericopeAsync(bool chapterChanged)
    {
        if (!_periBuilt) BuildPericopeShell();
        if (_curBook == "") return;
        var last = _curChapterData?.Verses.Count > 0 ? _curChapterData.Verses.Max(v => v.Number) : 1;
        _periQuiet = true;
        _periFrom.Maximum = _periTo.Maximum = Math.Max(1, last);
        if (chapterChanged || _periFor != (_curBook, _curChapter))
        {
            _periFrom.Value = _selected ?? 1; _periTo.Value = last;
        }
        _periQuiet = false;
        if (!chapterChanged && _periFor == (_curBook, _curChapter) && _periResults.Children.Count > 0) return;
        await RunPericopeAsync();
    }

    void FollowPericope(bool chapterChanged) { if (_periBuilt && TabActive(PericopeTab) && chapterChanged) _ = ShowPericopeAsync(true); }

    async Task RunPericopeAsync()
    {
        var run = ++_periRun;
        var (book, chapter) = (_curBook, _curChapter);
        _periResults.Children.Clear();
        if (!SyntaxProvider.IsAvailable(book)) { _periStatus.Text = L("Für dieses Buch gibt es keine Grundtextdaten."); return; }
        var from = (int)(_periFrom.Value ?? 1); var to = (int)(_periTo.Value ?? from);
        if (to < from) (from, to) = (to, from);
        _periStatus.Text = L("Grundtextdaten werden geladen …");
        List<GrammarWord> words; List<DSentence> sentences;
        try
        {
            if (!_periWords.TryGetValue((book, chapter), out words!)) { words = await _syntax.LoadChapterWordsAsync(book, chapter); if (_periWords.Count > 8) _periWords.Clear(); _periWords[(book, chapter)] = words; }
            sentences = await SentencesOfAsync(book, chapter);
        }
        catch (Exception ex) { if (run == _periRun) _periStatus.Text = L("Syntaxdaten nicht verfügbar: ") + ex.Message; return; }
        if (run != _periRun || _curBook != book || _curChapter != chapter) return;
        if (words.Count == 0) { _periStatus.Text = L("Für dieses Kapitel gibt es keine Syntaxdaten."); return; }

        _periFor = (book, chapter);
        var hebrew = words[0].Verse > 0 && sentences.Count > 0 ? sentences[0].Hebrew : OriginalIsHebrew(book);
        var r = PericopeStats.Compute(words, from, to, sentences, hebrew);
        _periStatus.Text = $"{BookName(book)} {chapter},{from}" + (to > from ? "–" + to : "") + " · " + (hebrew ? L("hebräischer Text (WLC)") : L("griechischer Text (Nestle 1904)"));
        BuildPericopeResults(r, book, chapter);
        var glosses = r.Keywords.Select(k => k.Gloss).Where(g => g.Length > 0).Distinct().ToList();
        if (glosses.Any(_meanings.IsMissing)) _ = WarmMeaningsAsync(glosses, () => { if (run == _periRun) BuildPericopeResults(r, book, chapter); });
    }

    /// <summary>The names of the relations between clauses (the translation keys are listed here because they are looked up by a variable).</summary>
    static readonly string[] RelationKeys = [K("Addition"), K("Fortführung"), K("Gegensatz"), K("Begründung"), K("Folgerung"), K("Zweck"), K("Ergebnis"), K("Bedingung"), K("Zeit"), K("Alternative"), K("Vergleich")];

    static bool OriginalIsHebrew(string book) => BookCategories.IsOldTestament(BookCategories.Of(book));

    Border Section(string title, string hint, Control body) =>
        Card(new StackPanel
        {
            Spacing = 6,
            Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 15 }, new TextBlock { Text = hint, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }, body },
        });

    void BuildPericopeResults(PericopeResult r, string book, int chapter)
    {
        _periResults.Children.Clear();
        var hebrew = r.Hebrew;
        string Names(string key) => Term(key);

        // tiles
        var perSentence = r.Sentences > 0 ? (double)r.SentenceRows.Sum(s => s.Words) / r.Sentences : 0;
        _periResults.Children.Add(PericopeCharts.Tiles(
        [
            (r.Verses.ToString(), L("Verse")), (r.Words.ToString(), L("Wörter")), (r.Lemmas.ToString(), L("verschiedene Wörter")),
            (r.Sentences.ToString(), L("Sätze")), (r.Clauses.ToString(), L("Teilsätze")), (r.Verbs.ToString(), L("Verben")),
            (perSentence.ToString("0.#"), L("Wörter pro Satz")),
        ]));

        // what stands out
        var notes = Insights(r, hebrew);
        if (notes.Count > 0)
        {
            var list = new StackPanel { Spacing = 4 };
            foreach (var n in notes) list.Children.Add(new TextBlock { Text = "• " + n, TextWrapping = TextWrapping.Wrap });
            _periResults.Children.Add(Section(L("Auf einen Blick"), L("Was in diesem Abschnitt auffällt. Das sind Hinweise aus den Zahlen, keine Auslegung."), list));
        }

        // word classes
        var classes = r.Classes.Select(c => (Names(c.Class), (double)c.Count, PericopeCharts.ColorOfClass(c.Class))).ToList();
        _periResults.Children.Add(Section(L("Wortarten"), L("Aus welchen Wortarten der Text besteht. Viele Verben sprechen für Handlung, viele Nomen für Beschreibung und Begriffe."),
            PericopeCharts.Donut(classes, r.Words.ToString())));

        // verb forms
        if (r.VerbGrid.Count > 0)
        {
            string[] rowOrder = hebrew ? ["qal", "niphal", "piel", "pual", "hiphil", "hophal", "hithpael"] : ["present", "imperfect", "future", "aorist", "perfect", "pluperfect"];
            string[] colOrder = hebrew ? ["qatal", "wayyiqtol", "yiqtol", "imperative", "infinitive absolute", "infinitive construct", "participle active", "participle passive", "jussive", "cohortative"]
                                       : ["indicative", "subjunctive", "imperative", "optative", "infinitive", "participle"];
            _periResults.Children.Add(Section(L("Verbformen"), hebrew
                    ? L("Die Verben nach Stamm (Zeilen) und Form (Spalten). Wayyiqtol trägt meist die Erzählung, Yiqtol und Imperativ Zukünftiges und Aufforderungen.")
                    : L("Die Verben nach Zeit (Zeilen) und Modus (Spalten). Aorist und Imperfekt erzählen, das Präsens beschreibt oder lehrt, der Imperativ fordert auf."),
                PericopeCharts.Heatmap(r.VerbGrid, rowOrder, colOrder, Names)));
        }

        // key words
        if (r.Keywords.Count > 0)
        {
            var rows = r.Keywords.Select((k, i) => (k.Lemma, (string?)(k.Gloss.Length > 0 ? Mean(k.Gloss) : null), (double)k.Count, Color.Parse(PericopeCharts.Palette[i % PericopeCharts.Palette.Length]), (Action?)(() => OpenStrong(k.Strong)))).ToList();
            var body = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    PericopeCharts.HBars(rows),
                    new TextBlock { Text = L("Wo kommen sie vor?"), FontWeight = FontWeight.SemiBold },
                    PericopeCharts.Traces(r.Keywords, r.FromVerse, r.ToVerse, k => k.Gloss.Length > 0 ? $"{k.Lemma} · {Mean(k.Gloss)}" : k.Lemma, k => OpenStrong(k.Strong)),
                },
            };
            _periResults.Children.Add(Section(L("Schlüsselwörter"), L("Inhaltswörter, die mindestens zweimal vorkommen, nach Häufigkeit. Ein Klick schlägt das Wort im Strong-Wörterbuch nach. In der Spur steht jeder Punkt für einen Vers, in dem das Wort vorkommt: so sieht man Wiederholungen, Rahmen und Wendepunkte."), body));
        }

        // rhythm
        if (r.VerseRows.Count > 0)
        {
            var legend = new WrapPanel
            {
                Children =
                {
                    PericopeLegend(L("Nomen, Pronomen, Adjektive"), PericopeCharts.ColorOfClass("noun")), PericopeLegend(L("Verben"), PericopeCharts.ColorOfClass("verb")),
                    PericopeLegend(L("Übrige Wörter"), Color.Parse("#BAB0AC")), PericopeLegend(L("Punkt darüber: verbindendes Wort"), PericopeCharts.ColorOfClass("conj")),
                },
            };
            var body = new StackPanel { Spacing = 6, Children = { legend, PericopeCharts.VerseRhythm(r.VerseRows, v => { SelectVerse(v); }) } };
            _periResults.Children.Add(Section(L("Rhythmus der Verse"), L("Eine Säule je Vers: die Höhe ist die Zahl der Wörter. Lange Säulen sind dichte Verse, kurze sind Einwürfe. Wechselt das Verhältnis von Verben zu Nomen, ändert sich der Ton. Ein Klick wählt den Vers."), body));
        }

        // sentences
        if (r.SentenceRows.Count > 0)
        {
            var body = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new WrapPanel { Children = { PericopeLegend(L("Satz mit Nebensatz"), Color.Parse("#4C78A8")), PericopeLegend(L("Satz ohne Nebensatz"), Color.Parse("#72B7B2")) } },
                    PericopeCharts.SentenceBars(r.SentenceRows, s => s.FirstVerse == s.LastVerse ? s.FirstVerse.ToString() : $"{s.FirstVerse}–{s.LastVerse}",
                        s => { SelectVerse(s.FirstVerse); RevealSideTab(SentenceTab); }),
                },
            };
            _periResults.Children.Add(Section(L("Satzbau"), L("Eine Säule je Satz: die Höhe ist die Länge in Wörtern, die Zahl darüber die Teilsätze. Ein Klick öffnet das Satzdiagramm dieses Satzes."), body));
        }

        // connectors
        if (r.Connectors.Count > 0)
        {
            var rows = r.Connectors.Select((c, i) => (L(c.Relation), (string?)string.Join(", ", c.Words.Take(4)), (double)c.Count, Color.Parse(PericopeCharts.Palette[i % PericopeCharts.Palette.Length]), (Action?)null)).ToList();
            _periResults.Children.Add(Section(L("Verbindungen"), L("Wie die Sätze miteinander verbunden sind (nach den verbindenden Wörtern). Viele Begründungen sprechen für eine Beweisführung, viele Gegensätze für eine Abgrenzung."), PericopeCharts.HBars(rows)));
        }

        _periResults.Children.Add(new TextBlock { Text = SyntaxProvider.Attribution, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
    }

    static Control PericopeLegend(string text, Color color) =>
        new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(0, 0, 14, 4),
            Children = { new Border { Width = 12, Height = 12, CornerRadius = new(3), Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = text, FontSize = 12 } },
        };

    void OpenStrong(string strong)
    {
        if (strong.Length == 0) return;
        StrongSearchBox.Text = strong; RevealSideTab(StrongTab);
        _ = SearchStrongAsync();
    }

    /// <summary>A few plain sentences about what the numbers show.</summary>
    List<string> Insights(PericopeResult r, bool hebrew)
    {
        var notes = new List<string>();
        if (r.Words > 0) notes.Add(L("{0} Wörter in {1} Versen; {2} verschiedene Wörter, also kommen etwa {3} % der Wörter wieder vor.", r.Words, r.Verses, r.Lemmas, 100 - 100 * r.Lemmas / r.Words));

        var verbs = r.VerbGrid.Sum(v => v.Count);
        if (verbs >= 3)
        {
            int Of(Func<(string Row, string Col, int Count), bool> f) => r.VerbGrid.Where(f).Sum(v => v.Count);
            if (hebrew)
            {
                var narrative = Of(v => v.Col == "wayyiqtol"); var request = Of(v => v.Col is "yiqtol" or "imperative" or "jussive" or "cohortative");
                if (narrative * 100 >= verbs * 40) notes.Add(L("Viel Wayyiqtol ({0} von {1} Verben): der Abschnitt erzählt.", narrative, verbs));
                else if (request * 100 >= verbs * 40) notes.Add(L("Viel Yiqtol und Imperativ ({0} von {1} Verben): der Abschnitt spricht von Zukünftigem oder fordert auf.", request, verbs));
            }
            else
            {
                var past = Of(v => v.Row is "aorist" or "imperfect" or "pluperfect" && v.Col is "indicative"); var present = Of(v => v.Row == "present" && v.Col is "indicative" or "imperative" or "subjunctive");
                if (past * 100 >= verbs * 40) notes.Add(L("Viel Aorist und Imperfekt im Indikativ ({0} von {1} Verben): der Abschnitt erzählt.", past, verbs));
                else if (present * 100 >= verbs * 40) notes.Add(L("Viel Präsens ({0} von {1} Verben): der Abschnitt lehrt, beschreibt oder ruft auf.", present, verbs));
            }
        }
        if (r.Imperatives > 0) notes.Add(L("{0} Imperative: der Abschnitt enthält Aufforderungen.", r.Imperatives));
        if (r.Keywords.Count > 0) { var k = r.Keywords[0]; notes.Add(L("Häufigstes Schlüsselwort: {0}{1}, {2}-mal.", k.Lemma, k.Gloss.Length > 0 ? " (" + Mean(k.Gloss) + ")" : "", k.Count)); }
        if (r.SentenceRows.Count > 0)
        {
            var longest = r.SentenceRows.MaxBy(s => s.Words)!;
            var span = longest.FirstVerse == longest.LastVerse ? longest.FirstVerse.ToString() : $"{longest.FirstVerse}–{longest.LastVerse}";
            notes.Add(L("Längster Satz: Vers {0} mit {1} Wörtern und {2} Teilsätzen.", span, longest.Words, longest.Clauses));
        }
        if (r.Connectors.Count > 0) notes.Add(L("Häufigste Verbindung: {0} ({1}-mal).", L(r.Connectors[0].Relation), r.Connectors[0].Count));
        return notes;
    }
}
