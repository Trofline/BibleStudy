using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Providers;

namespace BibleStudy.App;

/// <summary>Morphology (every word of the clicked verse with its grammar) and syntax (clause structure with the role of each part) of the Greek and Hebrew text.</summary>
public partial class MainWindow
{
    static readonly FontFamily HebrewFont = new("Ezra SIL, SBL Hebrew, David, Times New Roman, Segoe UI, Arial");
    readonly StackPanel _syntaxView = new() { Spacing = 6 };
    bool _syntaxOutline;   // false = morphology, true = sentence structure

    static readonly Dictionary<string, string> GrammarTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["present"] = K("Präsens"), ["imperfect"] = K("Imperfekt"), ["future"] = K("Futur"), ["aorist"] = K("Aorist"), ["perfect"] = K("Perfekt"), ["pluperfect"] = K("Plusquamperfekt"),
        ["active"] = K("Aktiv"), ["middle"] = K("Medium"), ["passive"] = K("Passiv"), ["middle or passive"] = K("Medium/Passiv"),
        ["indicative"] = K("Indikativ"), ["subjunctive"] = K("Konjunktiv"), ["optative"] = K("Optativ"), ["imperative"] = K("Imperativ"), ["infinitive"] = K("Infinitiv"), ["participle"] = K("Partizip"),
        ["first"] = K("1. Person"), ["second"] = K("2. Person"), ["third"] = K("3. Person"),
        ["singular"] = K("Singular"), ["plural"] = K("Plural"), ["dual"] = K("Dual"),
        ["masculine"] = K("Maskulinum"), ["feminine"] = K("Femininum"), ["neuter"] = K("Neutrum"), ["common"] = K("Maskulinum oder Femininum"), ["both"] = K("Maskulinum oder Femininum"),
        ["nominative"] = K("Nominativ"), ["genitive"] = K("Genitiv"), ["dative"] = K("Dativ"), ["accusative"] = K("Akkusativ"), ["vocative"] = K("Vokativ"),
        ["comparative"] = K("Komparativ"), ["superlative"] = K("Superlativ"),
        ["absolute"] = K("Absolutus"), ["construct"] = K("Konstruktus"),
        ["noun"] = K("Nomen"), ["verb"] = K("Verb"), ["adj"] = K("Adjektiv"), ["adv"] = K("Adverb"), ["det"] = K("Artikel"), ["pron"] = K("Pronomen"), ["prep"] = K("Präposition"),
        ["conj"] = K("Konjunktion"), ["cj"] = K("Konjunktion"), ["ptcl"] = K("Partikel"), ["num"] = K("Zahlwort"), ["intj"] = K("Interjektion"), ["rel"] = K("Relativpronomen"),
        ["s"] = K("Subjekt"), ["v"] = K("Verb (Prädikat)"), ["o"] = K("Objekt"), ["io"] = K("Indirektes Objekt"), ["o2"] = K("Zweites Objekt"), ["adv_role"] = K("Adverbiale Bestimmung"),
        ["p"] = K("Prädikatsergänzung"), ["pp"] = K("Präpositionalphrase"), ["vc"] = K("Verbalergänzung"), ["aux"] = K("Hilfsverb"),
    };

    static string Term(string v) => GrammarTerms.TryGetValue(v, out var k) ? L(k) : v;

    static string RoleLabel(string? role) => role switch { null or "" => "", "adv" => L("Adverbiale Bestimmung"), _ => Term(role) };

    async Task ShowSyntaxAsync()
    {
        _syntaxView.Children.Clear();
        _syntaxView.Children.Add(new TextBlock { Text = L("Grammatik und Satzbau (Urtext)"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (outline, label) in new[] { (false, L("Morphologie")), (true, L("Satzbau")) })
        {
            var b = new Button { Classes = { "seg" }, Content = label };
            b.Classes.Set("active", outline == _syntaxOutline);
            b.Click += async (_, _) => { _syntaxOutline = outline; await ShowSyntaxAsync(); };
            modes.Children.Add(b);
        }
        _syntaxView.Children.Add(modes);

        if (_selected is not int verse) { _syntaxView.Children.Add(new TextBlock { Text = L("Erst einen Vers anklicken."), Classes = { "caption" } }); return; }
        if (!SyntaxProvider.IsAvailable(_curBook)) { _syntaxView.Children.Add(new TextBlock { Text = L("Für dieses Buch gibt es keine Syntaxdaten."), Classes = { "caption" } }); return; }

        var status = new TextBlock { Text = L("Syntaxdaten werden geladen …"), Classes = { "caption" } };
        _syntaxView.Children.Add(status);
        SyntaxVerse? data;
        var (book, chapter) = (_curBook, _curChapter);
        try { data = await _syntax.GetVerseAsync(book, chapter, verse); }
        catch (Exception ex) { status.Text = L("Syntaxdaten nicht verfügbar: ") + ex.Message; return; }
        if (_selected != verse || _curBook != book || _curChapter != chapter) return;   // the reader moved on meanwhile
        _syntaxView.Children.Remove(status);
        if (data is null || data.Words.Count == 0) { _syntaxView.Children.Add(new TextBlock { Text = L("Für diesen Vers gibt es keine Syntaxdaten."), Classes = { "caption" } }); return; }

        if (_syntaxOutline) AddOutline(data); else AddMorphology(data);
        var glosses = data.Words.Select(w => w.Gloss).Concat(data.Outline.Select(l => l.Gloss)).Where(g => g.Length > 0).ToList();
        if (glosses.Any(_meanings.IsMissing)) _ = WarmMeaningsAsync(glosses, () => _ = ShowSyntaxAsync());
        _syntaxView.Children.Add(new TextBlock { Text = SyntaxProvider.Attribution, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
    }

    void AddMorphology(SyntaxVerse data)
    {
        foreach (var w in data.Words)
        {
            var head = new TextBlock { Text = w.Text, FontSize = w.Hebrew ? 26 : 20, FontFamily = w.Hebrew ? HebrewFont : ScriptureFont, FlowDirection = w.Hebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
            head.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
            var sub = new TextBlock { Text = string.Join("  ·  ", new[] { w.Translit, w.Strong, Mean(w.Gloss) }.Where(s => s.Length > 0)), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
            var grammar = string.Join(" · ", new[] { Term(w.PartOfSpeech) }.Concat(w.Features.Select(f => Term(f.Value))).Where(s => s.Length > 0));
            var card = new Border
            {
                Padding = new(10, 6), CornerRadius = new(8), BorderThickness = new(1), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { Children = { head, sub, new TextBlock { Text = grammar, TextWrapping = TextWrapping.Wrap } } },
            };
            card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
            if (w.Strong.Length > 0) { var id = w.Strong; card.PointerPressed += (_, _) => _ = AnalyzeWordAsync(id); ToolTip.SetTip(card, L("Klicken: Wort analysieren")); }
            _syntaxView.Children.Add(card);
        }
    }

    void AddOutline(SyntaxVerse data)
    {
        var hebrew = data.Words[0].Hebrew;
        foreach (var line in data.Outline)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new(line.Depth * 14, 0, 0, 0) };
            var label = line.Kind == "clause" ? L("Satz") + (line.Role is { Length: > 0 } ? " · " + RoleLabel(line.Role) : "") : RoleLabel(line.Role);
            if (label.Length > 0)
            {
                var badge = new Border { CornerRadius = new(6), Padding = new(6, 1), Margin = new(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeight.SemiBold } };
                badge.Bind(Border.BackgroundProperty, this.GetResourceObservable(line.Kind == "clause" ? "BsAccentSoft" : "BsSurface2"));
                row.Children.Add(badge);
            }
            if (line.Text.Length > 0)
            {
                var texts = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = line.Text, FontSize = hebrew ? 22 : 17, FontFamily = hebrew ? HebrewFont : ScriptureFont, TextWrapping = TextWrapping.Wrap, FlowDirection = hebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, HorizontalAlignment = hebrew ? HorizontalAlignment.Right : HorizontalAlignment.Left },
                        new TextBlock { Text = Mean(line.Gloss), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                    },
                };
                Grid.SetColumn(texts, 1);
                row.Children.Add(texts);
            }
            _syntaxView.Children.Add(row);
        }
        if (data.Outline.Count == 0) _syntaxView.Children.Add(new TextBlock { Text = L("Für diesen Vers gibt es keine Syntaxdaten."), Classes = { "caption" } });
    }
}
