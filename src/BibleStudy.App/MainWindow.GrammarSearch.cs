using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>
/// Search by grammar in the Greek New Testament: for example all aorist passive participles, or every genitive of one word.
/// Pick the properties, leave the rest open. The data (MACULA Greek) is downloaded once.
/// </summary>
public partial class MainWindow
{
    readonly StackPanel _grammarView = new() { Spacing = 6 };
    bool _grammarBuilt;
    List<GrammarWord>? _greekWords;
    readonly Dictionary<string, ComboBox> _grammarBoxes = [];
    TextBox? _grammarWord;

    static readonly (string Key, string Label, string[] Values)[] GrammarFields =
    [
        ("class", K("Wortart"), ["verb", "noun", "adj", "adv", "det", "pron", "prep", "conj", "ptcl", "num", "intj"]),
        ("tense", K("Zeit"), ["present", "imperfect", "future", "aorist", "perfect", "pluperfect"]),
        ("mood", K("Modus"), ["indicative", "subjunctive", "optative", "imperative", "infinitive", "participle"]),
        ("voice", K("Genus Verbi"), ["active", "middle", "passive"]),
        ("person", K("Person"), ["first", "second", "third"]),
        ("number", K("Zahl"), ["singular", "plural"]),
        ("gender", K("Geschlecht"), ["masculine", "feminine", "neuter"]),
        ("case", K("Fall"), ["nominative", "genitive", "dative", "accusative", "vocative"]),
    ];

    void EnsureGrammarSearch()
    {
        if (_grammarBuilt) return;
        _grammarBuilt = true;
        _grammarView.Children.Add(new TextBlock { Text = L("Grammatik-Suche (Neues Testament)"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) });
        var fields = new WrapPanel();
        foreach (var (key, label, values) in GrammarFields)
        {
            var box = new ComboBox { Width = 150, ItemsSource = new[] { L("(egal)") }.Concat(values.Select(Term)).ToList(), SelectedIndex = 0 };
            _grammarBoxes[key] = box;
            fields.Children.Add(new StackPanel { Margin = new(0, 0, 8, 6), Children = { new TextBlock { Text = L(label), Classes = { "caption" } }, new Border { Classes = { "pill" }, Child = box } } });
        }
        _grammarView.Children.Add(fields);
        _grammarWord = new TextBox { Watermark = L("Grundform oder Strong-Nummer (optional), z. B. G26") };
        _grammarView.Children.Add(_grammarWord);
        var go = new Button { Content = L("Suchen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var result = new StackPanel { Spacing = 6 };
        go.Click += async (_, _) => await RunGrammarSearchAsync(result, go);
        _grammarView.Children.Add(go);
        _grammarView.Children.Add(result);

        // ---- clause search: roles in the same clause ------------------------------------------------------------------------
        _grammarView.Children.Add(new TextBlock { Text = L("Satzrollen-Suche (Neues Testament)"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 18, 0, 0) });
        _grammarView.Children.Add(new TextBlock { Text = L("Findet Sätze, in denen alle Rollen zusammen vorkommen, z. B. Subjekt Jesus und Objekt Petrus. Das Wort kann eine Strong-Nummer, die griechische Grundform oder die englische Bedeutung sein; leer heißt: irgendein Wort in dieser Rolle."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        var rows = new StackPanel { Spacing = 6 };
        var roleResult = new StackPanel { Spacing = 6 };
        void AddRow(int roleIndex)
        {
            var role = new ComboBox { ItemsSource = RoleNames.Select(r => L(r.Label)).ToList(), SelectedIndex = roleIndex, Width = 170 };
            var term = new TextBox { Watermark = L("Wort (optional), z. B. Jesus, G2424") };
            var remove = new Button { Content = "✕", Classes = { "tool" }, Width = 28, Height = 28 };
            ToolTip.SetTip(remove, L("Entfernen"));
            var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 6 };
            var pill = new Border { Classes = { "pill" }, Child = role };
            Grid.SetColumn(term, 1); Grid.SetColumn(remove, 2);
            row.Children.Add(pill); row.Children.Add(term); row.Children.Add(remove);
            _roleRows.Add((role, term));
            rows.Children.Add(row);
            remove.Click += (_, _) => { _roleRows.RemoveAll(r => r.Term == term); rows.Children.Remove(row); };
        }
        _roleRows.Clear();
        AddRow(0); AddRow(1);
        var more = new Button { Content = L("＋ Rolle"), Margin = new(0, 0, 8, 0) };
        more.Click += (_, _) => AddRow(2);
        var run = new Button { Content = L("Suchen"), Classes = { "primary" } };
        run.Click += async (_, _) => await RunRoleSearchAsync(roleResult, run);
        _grammarView.Children.Add(rows);
        _grammarView.Children.Add(new WrapPanel { Children = { more, run } });
        _grammarView.Children.Add(roleResult);
    }

    GrammarQuery ReadGrammarQuery()
    {
        string? Pick(string key)
        {
            var box = _grammarBoxes[key];
            if (box.SelectedIndex <= 0) return null;
            return GrammarFields.First(f => f.Key == key).Values[box.SelectedIndex - 1];
        }
        return new GrammarQuery(Pick("class"), Pick("tense"), Pick("voice"), Pick("mood"), Pick("person"), Pick("number"), Pick("gender"), Pick("case"), _grammarWord?.Text);
    }

    static readonly (string Code, string Label)[] RoleNames =
    [
        ("s", K("Subjekt")), ("o", K("Objekt")), ("v", K("Verb")), ("io", K("Indirektes Objekt")), ("adv", K("Umstandsbestimmung")), ("p", K("Prädikat")), ("vc", K("Verbalergänzung")),
    ];
    readonly List<(ComboBox Role, TextBox Term)> _roleRows = [];

    async Task RunRoleSearchAsync(StackPanel result, Button go)
    {
        go.IsEnabled = false;
        result.Children.Clear();
        var status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        result.Children.Add(status);
        try
        {
            if (_greekWords is null)
            {
                var progress = new Progress<(int Done, int Total)>(p => status.Text = L("Lade Grammatikdaten … {0}/{1}", p.Done, p.Total));
                _greekWords = await _syntax.LoadGreekWordsAsync(progress);
            }
            var conditions = _roleRows.Select(r => new RoleCondition(RoleNames[Math.Max(0, r.Role.SelectedIndex)].Code, string.IsNullOrWhiteSpace(r.Term.Text) ? null : r.Term.Text!.Trim())).ToList();
            var hits = await Task.Run(() => ClauseSearch.Run(_greekWords, conditions));
            result.Children.Clear();
            if (hits.Count == 0) { result.Children.Add(new TextBlock { Text = L("Keine Treffer."), Classes = { "caption" } }); return; }
            result.Children.Add(new TextBlock { Text = L("{0} Sätze", hits.Count), FontWeight = FontWeight.SemiBold });
            result.Children.Add(SpreadBars(hits.Select(h => new VerseRef(h.Book, h.Chapter, h.Verse)).ToList()));
            foreach (var h in hits.Take(60))
            {
                var hit = h;
                var line = new StackPanel { Spacing = 2 };
                var open = FilterChip(Label(new VerseRef(h.Book, h.Chapter, h.Verse)), false, HomeView.ColorOf(h.Book), () => _ = GoToAsync(hit.Book, hit.Chapter, hit.Verse), L("Springt zur Stelle"));
                line.Children.Add(open);
                line.Children.Add(new TextBlock { Text = h.Text, FontFamily = ScriptureFont, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
                result.Children.Add(line);
            }
            if (hits.Count > 60) result.Children.Add(new TextBlock { Text = L(" (erste {0} angezeigt)", 60), Classes = { "caption" } });
        }
        catch (Exception ex) { status.Text = L("Fehler: ") + ex.Message; }
        finally { go.IsEnabled = true; }
    }

    async Task RunGrammarSearchAsync(StackPanel result, Button go)
    {
        go.IsEnabled = false;
        result.Children.Clear();
        var status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        result.Children.Add(status);
        try
        {
            if (_greekWords is null)
            {
                var progress = new Progress<(int Done, int Total)>(p => status.Text = L("Lade Grammatikdaten … {0}/{1}", p.Done, p.Total));
                _greekWords = await _syntax.LoadGreekWordsAsync(progress);
            }
            var query = ReadGrammarQuery();
            var hits = await Task.Run(() => _greekWords.Where(query.Matches).ToList());
            result.Children.Clear();
            if (hits.Count == 0) { result.Children.Add(new TextBlock { Text = L("Keine Treffer."), Classes = { "caption" } }); return; }

            var verses = hits.Select(h => new VerseRef(h.Book, h.Chapter, h.Verse)).Distinct().ToList();
            result.Children.Add(new TextBlock { Text = L("{0} Treffer in {1} Versen", hits.Count, verses.Count), FontWeight = FontWeight.SemiBold });
            result.Children.Add(SpreadBars(hits.Select(h => new VerseRef(h.Book, h.Chapter, h.Verse)).ToList()));

            result.Children.Add(new TextBlock { Text = L("Häufigste Wörter unter den Treffern"), Classes = { "caption" }, Margin = new(0, 6, 0, 0) });
            var top = new WrapPanel();
            foreach (var g in hits.GroupBy(h => h.Lemma).OrderByDescending(g => g.Count()).Take(10))
            {
                var first = g.First();
                var lemma = g.Key;
                top.Children.Add(FilterChip($"{lemma}  {Mean(first.Gloss)}  {g.Count()}×", false, null, () => { if (_grammarWord is not null) _grammarWord.Text = first.Strong; }, first.Strong));
            }
            result.Children.Add(top);

            var list = new WrapPanel();
            foreach (var h in hits.Take(80))
            {
                var hit = h;
                list.Children.Add(FilterChip($"{Label(new VerseRef(h.Book, h.Chapter, h.Verse))}  {h.Text}", false, HomeView.ColorOf(h.Book), () => _ = GoToAsync(hit.Book, hit.Chapter, hit.Verse), hit.Gloss));
            }
            result.Children.Add(list);
            if (hits.Count > 80) result.Children.Add(new TextBlock { Text = L(" (erste {0} angezeigt)", 80), Classes = { "caption" } });
        }
        catch (Exception ex) { status.Text = L("Fehler: ") + ex.Message; }
        finally { go.IsEnabled = true; }
    }
}
