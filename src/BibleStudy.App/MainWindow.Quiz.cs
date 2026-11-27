using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BibleStudy.Core.Models;

namespace BibleStudy.App;

/// <summary>
/// A small quiz about the program, in its own window: questions on where things are and what they do, and tasks that are done in the real program
/// ("look up agapaō in the word study: how often is it in John?"). The answers of those are counted from the data the person sees (the translation that
/// is open), so they always fit. The best result is kept.
/// </summary>
public partial class MainWindow
{
    /// <summary>A question: either choices (the first one given is right, they are shown mixed) or a number that is typed.</summary>
    sealed record QuizQuestion(string Text, string[]? Choices, int? Number, string Explain);

    Window? _quiz;

    List<QuizQuestion> QuizQuestions()
    {
        var list = new List<QuizQuestion>
        {
            new(L("Wie springst du am schnellsten zu Röm 8,28?"),
                [L("Ins Feld „Gehe zu …“ (Strg+K) „Röm 8,28“ tippen"), L("Rechtsklick auf einen Vers"), L("In den Einstellungen unter „Lesen“")], null,
                L("Das Feld „Gehe zu …“ oben findet Bibelstellen, Bücher, Wörter, Befehle und Dokumente.")),
            new(L("Was erscheint, wenn du bei eingeschalteter Wortstudie ein unterstrichenes Wort antippst?"),
                [L("Das hebräische oder griechische Grundwort mit Strong-Nummer und Bedeutung"), L("Eine Notiz zu dem Wort"), L("Die englische Übersetzung des Verses")], null,
                L("Die Wortstudie zeigt das Grundwort, seine Bedeutung und führt zu allen Vorkommen.")),
            new(L("Wo legst du einen eigenen Querverweis an?"),
                [L("Rechtsklick auf den Vers › „Studium“ › „Eigenen Querverweis setzen …“"), L("Unter „Planen“ › „To-dos“"), L("Im Handbuch")], null,
                L("Er erscheint dann als goldener Chip unter dem Vers, und am Ziel führt einer zurück.")),
            new(L("Wie löschst du einen eigenen Querverweis wieder?"),
                [L("Rechtsklick auf den goldenen Chip › „Eigenen Querverweis löschen“"), L("Das Kapitel neu laden"), L("Das geht nur über die Sicherung")], null,
                L("Danach ist er auf beiden Seiten verschwunden.")),
            new(L("Wonach sucht die Suche, wenn du „G25“ eingibst?"),
                [L("Nach allen Stellen mit dem Grundwort agapaō, egal wie es übersetzt ist"), L("Nach Kapitel 25"), L("Nach dem Wort „G25“ im Text")], null,
                L("Mit der Strong-Nummer findest du das Grundwort unabhängig von der Übersetzung.")),
            new(L("Wozu dient ein Vault?"),
                [L("Eine Mappe für ein Studienprojekt: Notizen, Markierungen, Querverweise und Dokumente"), L("Eine weitere Bibelübersetzung"), L("Ein Ordner nur für Sicherungen")], null,
                L("Du kannst mehrere Vaults haben, z. B. einen für eine Predigtreihe und einen privaten.")),
            new(L("Wo schaltest du die gelegentlichen Vorschläge aus?"),
                [L("Einstellungen › „Hilfe, Feedback, Updates“ › „Mitteilungen und Vorschläge“"), L("Gar nicht, sie kommen immer"), L("In den Windows-Einstellungen")], null,
                L("Dort kannst du jeden Vorschlag einzeln an- und ausschalten oder alle auf einmal.")),
        };

        // tasks in the real program: counted from the translation that is open, so the answer is the one the person finds there
        if (TranslationBox.SelectedItem is TranslationInfo t)
        {
            try
            {
                if (_store.IsStrongsIndexed(t.Id))
                {
                    var inJohn = _store.GetOccurrences(t.Id, "G25", ConcordanceLimit).Count(o => o.Ref.Book == "JHN");
                    if (inJohn > 0)
                        list.Insert(2, new(L("Aufgabe: Schalte die Wortstudie ein, tippe in Joh 3,16 auf „geliebt“ (agapaō, G25) und öffne „Alle Vorkommen“. Wie oft steht es im Johannesevangelium ({0})?", t.ShortName),
                            null, inJohn, L("Bei den Vorkommen steht jedes Buch mit seiner Zahl; bei „Joh“ sind es {0}.", inJohn)));
                    var inFirstJohn = _store.GetOccurrences(t.Id, "G26", ConcordanceLimit).Count(o => o.Ref.Book == "1JN");
                    if (inFirstJohn > 0)
                        list.Insert(5, new(L("Aufgabe: Geh zu 1. Johannes 4,8 („Gott ist Liebe“), tippe bei eingeschalteter Wortstudie auf „Liebe“ (agapē, G26) und öffne „Alle Vorkommen“. Wie oft steht es im 1. Johannesbrief ({0})?", t.ShortName),
                            null, inFirstJohn, L("Der 1. Johannesbrief spricht besonders oft von der Liebe: {0}-mal agapē.", inFirstJohn)));
                }
            }
            catch (Microsoft.Data.Sqlite.SqliteException) { /* without the data the questions about the program remain */ }
        }
        return list;
    }

    void ShowQuiz()
    {
        if (_quiz is not null) { _quiz.Activate(); return; }
        var questions = QuizQuestions();
        var index = 0; var right = 0;
        var body = new StackPanel { Spacing = 10 };
        var close = new Button { Content = L("Schließen"), VerticalAlignment = VerticalAlignment.Center };
        var win = new Window { Title = L("Quiz"), Width = 560, Height = 520, MinWidth = 340, MinHeight = 320, Background = Res("BsSurface"), Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };

        void Show()
        {
            body.Children.Clear();
            if (index >= questions.Count) { ShowResult(); return; }
            var q = questions[index];
            body.Children.Add(new TextBlock { Text = L("Frage {0} von {1}", index + 1, questions.Count), Classes = { "caption" } });
            body.Children.Add(new ProgressBar { Minimum = 0, Maximum = questions.Count, Value = index, Height = 4, MinHeight = 4 });
            body.Children.Add(new TextBlock { Text = q.Text, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            var next = new Button { Content = index == questions.Count - 1 ? L("Ergebnis") : L("Weiter"), Classes = { "primary" }, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
            next.Click += (_, _) => { index++; Show(); };
            void Answered(bool ok, string shown)
            {
                if (ok) right++;
                feedback.Text = (ok ? "✓ " + L("Richtig!") : "✗ " + L("Leider nicht. Richtig ist: {0}", shown)) + "\n" + q.Explain;
                feedback.Foreground = new SolidColorBrush(Color.Parse(ok ? NoticeGood : "#C0392B"));
                feedback.IsVisible = true; next.IsVisible = true;
            }
            if (q.Choices is { } choices)
            {
                var correct = choices[0];
                var buttons = new List<Button>();
                foreach (var c in choices.OrderBy(_ => Random.Shared.Next()))
                {
                    var b = new Button { Content = new TextBlock { Text = c, TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
                    var choice = c;
                    b.Click += (_, _) =>
                    {
                        foreach (var other in buttons) other.IsEnabled = false;
                        b.Classes.Add(choice == correct ? "primary" : "ghost");
                        Answered(choice == correct, correct);
                    };
                    buttons.Add(b); body.Children.Add(b);
                }
            }
            else if (q.Number is int number)
            {
                var box = new TextBox { PlaceholderText = L("Zahl eingeben"), Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
                var check = new Button { Content = L("Prüfen"), Margin = new(8, 0, 0, 0) };
                var show = new Button { Content = L("Zum Nachschauen öffnen"), Classes = { "ghost" }, Margin = new(8, 0, 0, 0) };
                show.Click += async (_, _) =>
                {
                    // the place where the answer can be found, beside the quiz
                    var love = q.Text.Contains("G26");
                    if (love) await GoToAsync("1JN", 4, 8); else await GoToAsync("JHN", 3, 16);
                    await ShowConcordanceAsync(love ? "G26" : "G25", love ? "G26  ἀγάπη" : "G25  ἀγαπάω");
                };
                void Check()
                {
                    if (!int.TryParse((box.Text ?? "").Trim(), out var given)) { feedback.Text = L("Bitte eine Zahl eingeben."); feedback.Foreground = Res("BsMuted"); feedback.IsVisible = true; return; }
                    box.IsEnabled = false; check.IsEnabled = false;
                    Answered(given == number, number.ToString());
                }
                check.Click += (_, _) => Check();
                box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Check(); };
                body.Children.Add(new WrapPanel { Children = { box, check, show } });
            }
            body.Children.Add(feedback);
            body.Children.Add(next);
        }

        void ShowResult()
        {
            var best = Math.Max(_settings.QuizBest, right);
            _settings.QuizBest = best; SaveSettings();
            body.Children.Add(new TextBlock { Text = L("{0} von {1} richtig", right, questions.Count), FontSize = 22, FontWeight = FontWeight.SemiBold });
            body.Children.Add(new TextBlock
            {
                Text = right == questions.Count ? L("Alles richtig, du kennst dich aus!") : right * 2 >= questions.Count ? L("Gut gemacht! Was noch unsicher ist, zeigt dir der Tiefen-Rundgang.") : L("Der Tiefen-Rundgang zeigt dir alles Schritt für Schritt; danach klappt das Quiz bestimmt."),
                TextWrapping = TextWrapping.Wrap,
            });
            body.Children.Add(new TextBlock { Text = L("Dein bestes Ergebnis: {0}", best), Classes = { "caption" } });
            var again = new Button { Content = L("Nochmal"), Classes = { "primary" } };
            again.Click += (_, _) => { questions = QuizQuestions(); index = 0; right = 0; Show(); };
            var tour = new Button { Content = L("Tiefen-Rundgang"), Margin = new(8, 0, 0, 0) };
            tour.Click += (_, _) => { win.Close(); StartDeepTour(); };
            body.Children.Add(new WrapPanel { Children = { again, tour } });
        }

        var dock = new DockPanel { Margin = new(20) };
        var title = new TextBlock { Text = L("Quiz: Kennst du BibleStudy?"), FontSize = 20, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(close, Dock.Right);
        var top = new DockPanel { Margin = new(0, 0, 0, 10), Children = { close, title } };
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(new ScrollViewer { Content = body });
        win.Content = dock;
        close.Click += (_, _) => win.Close();
        win.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) win.Close(); };
        win.Closed += (_, _) => _quiz = null;
        _quiz = win;
        Show();
        win.Show(this);
    }
}
