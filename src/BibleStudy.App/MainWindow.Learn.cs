using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>Learning Greek and Hebrew: alphabet, letter quiz and a vocabulary trainer with spaced repetition (words from the Strong's dictionary).</summary>
public partial class MainWindow
{
    const int LearnTab = 22;
    const int NewCardsPerSession = 10;

    LearnStore? _learnStore;
    LearnStore Learn => _learnStore ??= new LearnStore(Path.Combine(AppPaths.Root, "learn.json"));
    bool _learnGreek = true, _showAlphabet;
    readonly StackPanel _learnArea = new() { Spacing = 8 };
    readonly Random _rng = new();
    int _quizRight, _quizTotal;

    void BuildLearnUi()
    {
        LearnPanel.Children.Clear();
        LearnPanel.Children.Add(new TextBlock { Text = L("GRIECHISCH UND HEBRÄISCH LERNEN"), Classes = { "eyebrow" } });

        var pick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (greek, label) in new[] { (true, L("Griechisch")), (false, L("Hebräisch")) })
        {
            var b = new Button { Classes = { "seg" }, Content = label };
            b.Classes.Set("active", greek == _learnGreek);
            b.Click += (_, _) => { _learnGreek = greek; _showAlphabet = false; BuildLearnUi(); };
            pick.Children.Add(b);
        }
        LearnPanel.Children.Add(pick);

        var ids = Learning.Vocabulary(_learnGreek);
        var now = DateTime.Now;
        var due = ids.Count(i => Learn.IsDue(i, now));
        var fresh = ids.Count(i => Learn.Get(i) is null);
        var stats = new TextBlock { Text = L("Gelernt: {0} von {1} · heute fällig: {2} · neu: {3}", ids.Count(i => Learn.Get(i)?.Box >= 3), ids.Count, due, fresh), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        LearnPanel.Children.Add(stats);

        var modes = new WrapPanel();
        var vocab = new Button { Content = L("Vokabeln üben"), Classes = { "primary" }, Margin = new(0, 0, 8, 8) };
        vocab.Click += (_, _) => StartVocabulary();
        var quiz = new Button { Content = L("Buchstaben-Quiz"), Margin = new(0, 0, 8, 8) };
        quiz.Click += (_, _) => { _quizRight = _quizTotal = 0; ShowQuizQuestion(); };
        var alpha = new Button { Content = _showAlphabet ? L("Alphabet verbergen") : L("Alphabet zeigen"), Margin = new(0, 0, 8, 8) };
        alpha.Click += (_, _) => { _showAlphabet = !_showAlphabet; BuildLearnUi(); };
        modes.Children.Add(vocab); modes.Children.Add(quiz); modes.Children.Add(alpha);
        LearnPanel.Children.Add(modes);

        _learnArea.Children.Clear();
        LearnPanel.Children.Add(_learnArea);
        if (_showAlphabet) LearnPanel.Children.Add(AlphabetView());
        LearnPanel.Children.Add(new TextBlock { Text = L("Die Bedeutungen sind englisch (Strong's Dictionary). Die Wörter kommen nach Häufigkeit und Wichtigkeit; richtig Gewusstes kommt später wieder, Vergessenes früher."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
    }

    Control AlphabetView()
    {
        var wrap = new WrapPanel { Margin = new(0, 4, 0, 0) };
        foreach (var l in _learnGreek ? Learning.Greek : Learning.Hebrew)
        {
            var glyph = new TextBlock { Text = l.Glyph, FontSize = 26, FontFamily = ScriptureFont, HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = _learnGreek ? FlowDirection.LeftToRight : FlowDirection.RightToLeft };
            glyph.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
            var card = new Border
            {
                Width = 96, Padding = new(6, 8), Margin = new(0, 0, 8, 8), CornerRadius = new(10), BorderThickness = new(1),
                Child = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        glyph,
                        new TextBlock { Text = l.Name, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = l.Translit + "  " + l.Sound, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.7, FontSize = 12 },
                    },
                },
            };
            card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
            wrap.Children.Add(card);
        }
        return wrap;
    }

    // ---- letter quiz -----------------------------------------------------------------------------------------------

    void ShowQuizQuestion()
    {
        var letters = _learnGreek ? Learning.Greek : Learning.Hebrew;
        var right = letters[_rng.Next(letters.Length)];
        var options = letters.Where(l => l != right).OrderBy(_ => _rng.Next()).Take(3).Append(right).OrderBy(_ => _rng.Next()).ToList();
        _learnArea.Children.Clear();
        var glyph = new TextBlock { Text = right.Glyph.Split(' ')[0], FontSize = 64, FontFamily = ScriptureFont, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        var feedback = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var next = new Button { Content = L("Weiter"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };
        next.Click += (_, _) => ShowQuizQuestion();
        var buttons = new List<Button>();
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var o in options)
        {
            var b = new Button { Content = o.Name, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new(3) };
            b.Click += (_, _) =>
            {
                _quizTotal++;
                var ok = o == right;
                if (ok) _quizRight++;
                feedback.Text = ok ? L("Richtig!") : L("Falsch – das ist {0} ({1}).", right.Name, right.Translit);
                foreach (var x in buttons) x.IsEnabled = false;
                b.Classes.Set("primary", ok);
                next.IsVisible = true;
            };
            buttons.Add(b); grid.Children.Add(b);
        }
        _learnArea.Children.Add(Card(new StackPanel { Spacing = 8, Children = { new TextBlock { Text = L("Wie heißt dieser Buchstabe?"), Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center }, glyph, grid, feedback, next, new TextBlock { Text = L("Richtig: {0} von {1}", _quizRight, _quizTotal), Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center } } }));
    }

    // ---- vocabulary trainer ----------------------------------------------------------------------------------------

    List<string> _queue = [];
    int _queuePos;

    void StartVocabulary()
    {
        var ids = Learning.Vocabulary(_learnGreek);
        var now = DateTime.Now;
        _queue = [.. ids.Where(i => Learn.IsDue(i, now)), .. ids.Where(i => Learn.Get(i) is null).Take(NewCardsPerSession)];
        _queuePos = 0;
        if (_queue.Count == 0)
        {
            _learnArea.Children.Clear();
            _learnArea.Children.Add(Card(new TextBlock { Text = L("Für heute ist alles gelernt. Komm morgen wieder – oder übe das Alphabet."), TextWrapping = TextWrapping.Wrap }));
            return;
        }
        _ = ShowCardAsync();
    }

    Border Card(Control content)
    {
        var b = new Border { Child = content, Padding = new(14), CornerRadius = new(12), BorderThickness = new(1) };
        b.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BsBorder"));
        return b;
    }

    async Task ShowCardAsync()
    {
        if (_queuePos >= _queue.Count)
        {
            BuildLearnUi();
            _learnArea.Children.Add(Card(new TextBlock { Text = L("Fertig für jetzt – {0} Karten geübt.", _queue.Count), TextWrapping = TextWrapping.Wrap }));
            return;
        }
        var id = _queue[_queuePos];
        _learnArea.Children.Clear();
        StrongsEntry? e;
        try { e = await _strongs.LookupAsync(id); }
        catch (Exception ex) { _learnArea.Children.Add(Card(new TextBlock { Text = L("Wörterbuch nicht verfügbar: ") + ex.Message, TextWrapping = TextWrapping.Wrap })); return; }
        if (e is null) { _queuePos++; await ShowCardAsync(); return; }

        var rtl = id[0] == 'H';
        var lemma = new TextBlock { Text = e.Lemma, FontSize = 46, FontFamily = ScriptureFont, HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        lemma.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("BsAccent"));
        var sound = new TextBlock { Text = (e.Translit + (e.Pronunciation.Length > 0 ? "  ·  " + e.Pronunciation : "")).Trim(), HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.75 };
        var answer = new StackPanel { Spacing = 3, IsVisible = false };
        var meaning = e.Definition.Length > 0 ? e.Definition : e.KjvUsage;
        answer.Children.Add(new TextBlock { Text = meaning, TextWrapping = TextWrapping.Wrap });
        if (e.KjvUsage.Length > 0 && e.Definition.Length > 0) answer.Children.Add(new TextBlock { Text = "KJV: " + e.KjvUsage, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        if (e.Derivation.Length > 0) answer.Children.Add(new TextBlock { Text = L("Herkunft:") + " " + e.Derivation, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        answer.Children.Add(new TextBlock { Text = id, Classes = { "caption" } });

        var reveal = new Button { Content = L("Bedeutung zeigen"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Center };
        var rate = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };
        var knew = new Button { Content = L("Gewusst"), Classes = { "primary" } };
        var again = new Button { Content = L("Noch nicht") };
        rate.Children.Add(again); rate.Children.Add(knew);
        reveal.Click += (_, _) => { answer.IsVisible = true; rate.IsVisible = true; reveal.IsVisible = false; };
        async void Rate(bool ok)
        {
            Learn.Answer(id, ok, DateTime.Now);
            if (!ok) _queue.Add(id);   // comes back at the end of this session
            _queuePos++;
            await ShowCardAsync();
        }
        knew.Click += (_, _) => Rate(true);
        again.Click += (_, _) => Rate(false);

        var cardView = Card(new StackPanel
        {
            Spacing = 8,
            Children = { new TextBlock { Text = L("Karte {0} von {1}", _queuePos + 1, _queue.Count), Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center }, lemma, sound, reveal, answer, rate },
        });
        _learnArea.Children.Add(cardView);
        AnimateIn(cardView, 24, 0);
    }
}

