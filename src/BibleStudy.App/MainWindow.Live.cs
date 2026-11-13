using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core;
using BibleStudy.Core.Live;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// The Bible study on the phones (see <see cref="LiveRoom"/>): the participants scan a QR code and see in the browser of their phone
/// what the projector shows, the handout, can send a question and vote in a poll. No app, no account, no internet: the phones only
/// have to be in the same Wi-Fi as this computer. At the end the passages and questions can go into the archive of the Bible studies.
/// </summary>
public partial class MainWindow
{
    const int LiveTab = 32;
    LiveRoom? _live;
    string _liveAddress = "";
    List<StudyPassage> _livePassages = [];
    DateTime _liveStarted;
    long _liveHandoutDoc;
    bool _liveRefreshQueued;
    StackPanel? _liveDynamic;

    Dictionary<string, string> LivePageTexts() => new()
    {
        ["title"] = L("Bibelstunde"),
        ["now"] = L("Jetzt"),
        ["handout"] = L("Handout"),
        ["ask"] = L("Frage"),
        ["poll"] = L("Umfrage"),
        ["waiting"] = L("Gleich geht es los. Hier erscheint, was gerade gezeigt wird."),
        ["askHint"] = L("Schreib eine Frage oder einen Gedanken. Nur die Leitung sieht ihn; dein Name ist freiwillig."),
        ["name"] = L("Name (freiwillig)"),
        ["send"] = L("Senden"),
        ["sent"] = L("Danke, ist angekommen."),
        ["tooMany"] = L("Das ging nicht. Warte einen Moment und versuche es noch einmal."),
        ["vote"] = L("Tippe auf eine Antwort. Du kannst sie noch ändern."),
        ["voted"] = L("Deine Stimme ist gezählt. Du kannst sie noch ändern."),
        ["noPoll"] = L("Gerade läuft keine Umfrage."),
        ["noHandout"] = L("Es gibt noch kein Handout."),
        ["offline"] = L("Keine Verbindung zum Laptop. Bist du im selben WLAN?"),
    };

    // ---- starting and ending ------------------------------------------------------------------------------------------------

    void StartLive()
    {
        if (_live is not null) return;
        var room = new LiveRoom(() => LivePage.Build(LivePageTexts(), Loc.Code));
        if (!room.Start()) { StatusText.Text = L("Kein freier Netzwerk-Anschluss gefunden (49400–49499)."); return; }
        _live = room;
        _liveStarted = DateTime.Now;
        _livePassages = [];
        _liveAddress = LiveRoom.LocalAddresses().FirstOrDefault().Address ?? "";
        room.Changed += () => Dispatcher.UIThread.Post(QueueLiveRefresh);
        // what the projector shows now, the phones show from the start
        if (_projector is { } p && !p.ShowsQr && p.ShownText.Length > 0) room.Show(p.ShownTitle, p.ShownText);
        BuildLiveUi();
    }

    void StopLive(bool keep)
    {
        if (_live is not { } room) return;
        if (keep) KeepLiveInArchive(room);
        room.Dispose();
        _live = null;
        _liveHandoutDoc = 0;
        BuildLiveUi();
        if (keep) StatusText.Text = L("Die Bibelstunde ist im Archiv festgehalten.");
    }

    /// <summary>The passages shown and the questions sent go into the archive as one Bible study of today.</summary>
    void KeepLiveInArchive(LiveRoom room)
    {
        var questions = room.Questions.Select(q => "- " + q.Text + (q.Name.Length > 0 ? " (" + q.Name + ")" : "")).ToList();
        if (room.Poll is { } poll)
        {
            var r = room.Results();
            questions.Add("");
            questions.Add(L("Umfrage: {0}", poll.Question));
            questions.AddRange(poll.Options.Select((o, i) => $"  {o}: {(i < r.Length ? r[i] : 0)}"));
        }
        var title = _liveHandoutDoc > 0 && _vault.GetDocuments().FirstOrDefault(d => d.Id == _liveHandoutDoc) is { } doc ? doc.Title
            : _livePassages.Count > 0 ? PassageLabel(_livePassages[0]) : L("Bibelstunde");
        _vault.AddStudy(DateOnly.FromDateTime(_liveStarted), title, _livePassages, "", string.Join("\n", questions).Trim());
    }

    /// <summary>The projector (or the "send" button) shows something: the phones show it too, and verses count for the archive.</summary>
    void LiveMirror(string title, string text, StudyPassage? passage)
    {
        if (_live is not { } room) return;
        room.Show(title, text);
        if (passage is not null) _livePassages = StudyPassage.Add(_livePassages, passage);
        QueueLiveRefresh();
    }

    /// <summary>The chosen verses of the reader to the phones only (the projector stays as it is).</summary>
    async Task SendChosenVersesToPhonesAsync()
    {
        if (_live is null || _curBook.Length == 0 || CurrentTranslationId.Length == 0) return;
        var chosen = _selVerses.Count > 0 ? _selVerses.OrderBy(v => v).ToList() : [_selected ?? 1];
        var first = new VerseRef(_curBook, _curChapter, chosen[0]);
        try
        {
            if (await VersesToShowAsync(first, chosen[^1], CurrentTranslationId) is { } shown)
                LiveMirror(shown.Title, shown.Text, new(first.Book, first.Chapter, chosen[0], chosen[^1]));
        }
        catch (Exception ex) { StatusText.Text = L("Der Vers lässt sich nicht zeigen: {0}", ex.Message); }
    }

    string LiveUrl => _live is { } r && _liveAddress.Length > 0 ? r.UrlFor(_liveAddress) : "";

    // ---- the tab --------------------------------------------------------------------------------------------------------

    void QueueLiveRefresh()
    {
        if (_liveRefreshQueued) return;
        _liveRefreshQueued = true;
        DispatcherTimer.RunOnce(() => { _liveRefreshQueued = false; FillLiveDynamic(false); }, TimeSpan.FromMilliseconds(250));
    }

    void BuildLiveUi()
    {
        LivePanel.Children.Clear();
        _liveDynamic = null;
        LivePanel.Children.Add(new TextBlock { Text = L("BIBELSTUNDE LIVE"), Classes = { "eyebrow" } });
        if (_live is not { } room)
        {
            LivePanel.Children.Add(new TextBlock { Text = L("Die Teilnehmer lesen auf ihrem Handy mit: Sie scannen einen QR-Code und sehen im Browser, was der Beamer zeigt, und das Handout. Sie können dir still eine Frage schicken und bei Umfragen abstimmen."), TextWrapping = TextWrapping.Wrap });
            LivePanel.Children.Add(new TextBlock { Text = L("Dafür braucht es keine App, kein Konto und kein Internet. Die Handys müssen nur im selben WLAN sein wie dieser Computer. Nichts verlässt euer Netzwerk."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            var start = new Button { Content = L("Bibelstunde live starten"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 6, 0, 0) };
            start.Click += (_, _) => StartLive();
            LivePanel.Children.Add(start);
            if (LiveRoom.LocalAddresses().Count == 0)
                LivePanel.Children.Add(new TextBlock { Text = L("Dieser Computer ist gerade mit keinem Netzwerk verbunden. Verbinde ihn mit dem WLAN, in dem auch die Handys sind."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed });
            return;
        }

        // the QR code: always whole, it shrinks with the panel and never gets cut off
        var addresses = LiveRoom.LocalAddresses();
        if (addresses.All(a => a.Address != _liveAddress)) _liveAddress = addresses.FirstOrDefault().Address ?? "";
        var qr = new QrView { Size = 260, HorizontalAlignment = HorizontalAlignment.Center, Text = LiveUrl.Length > 0 ? LiveUrl : "http://127.0.0.1" };
        var url = new SelectableTextBlock { Text = LiveUrl.Length > 0 ? LiveUrl : L("Kein Netzwerk"), FontFamily = new FontFamily("Consolas, Menlo, monospace"), HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        var qrCard = new StackPanel { Spacing = 8, Children = { new Border { Background = Brushes.White, CornerRadius = new(10), Padding = new(8), HorizontalAlignment = HorizontalAlignment.Center, Child = qr }, url } };
        if (addresses.Count > 1)
        {
            var nets = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = addresses.Select(a => $"{a.Address}  ({a.Network})").ToList(), SelectedIndex = Math.Max(0, addresses.FindIndex(a => a.Address == _liveAddress)) };
            nets.SelectionChanged += (_, _) => { if (nets.SelectedIndex >= 0) { _liveAddress = addresses[nets.SelectedIndex].Address; BuildLiveUi(); } };
            qrCard.Children.Add(new TextBlock { Text = L("Netzwerk (das WLAN der Handys):"), Classes = { "caption" } });
            qrCard.Children.Add(nets);
        }
        LivePanel.Children.Add(new Border { Classes = { "card" }, Child = qrCard });

        var toProjector = new Button { Content = L("QR-Code auf den Beamer") };
        toProjector.Click += (_, _) => { if (LiveUrl.Length > 0) { Projector().ShowQr(LiveUrl, L("Mit dem Handy scannen und mitlesen")); _projectVerses = null; UpdateProjectorChip(); } };
        var copy = new Button { Content = L("Adresse kopieren") };
        copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } c && LiveUrl.Length > 0) { await c.SetTextAsync(LiveUrl); StatusText.Text = L("Adresse kopiert."); } };
        var stop = new Button { Content = L("Beenden …") };
        stop.Click += (_, _) =>
        {
            var keep = new Button { Content = L("Beenden und im Archiv festhalten"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch };
            var just = new Button { Content = L("Nur beenden"), HorizontalAlignment = HorizontalAlignment.Stretch };
            var fly = new Flyout { Content = new StackPanel { Spacing = 6, Width = 280, Children = { new TextBlock { Text = L("Die Handys verlieren die Verbindung. Die Bibelstellen und Fragen dieses Abends können ins Archiv."), TextWrapping = TextWrapping.Wrap }, keep, just } } };
            keep.Click += (_, _) => { fly.Hide(); StopLive(true); };
            just.Click += (_, _) => { fly.Hide(); StopLive(false); };
            fly.ShowAt(stop);
        };
        LivePanel.Children.Add(new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Children = { toProjector, copy, stop } });
        LivePanel.Children.Add(new TextBlock { Text = L("Fragt Windows beim Start, ob das Programm im Netzwerk erreichbar sein darf, wähle „Zulassen“ für private Netzwerke. Öffnet sich die Seite am Handy nicht, ist es oft ein Gäste-WLAN, das Geräte voneinander trennt."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });

        // what the phones show
        LivePanel.Children.Add(Eyebrow(L("Auf den Handys")));
        LivePanel.Children.Add(new TextBlock { Text = L("Was der Beamer zeigt, sehen die Handys gleich mit. Ohne Beamer schickst du die gewählten Verse des Lesefensters von hier."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        var send = new Button { Content = L("Gewählte Verse an die Handys") };
        send.Click += async (_, _) => await SendChosenVersesToPhonesAsync();
        LivePanel.Children.Add(send);

        // the handout
        LivePanel.Children.Add(Eyebrow(L("Handout")));
        var docs = _vault.GetDocuments(templates: false).OrderByDescending(d => d.Updated).ToList();
        var docBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = L("Dokument wählen …"), ItemsSource = docs.Select(d => d.Title.Length > 0 ? d.Title : L("Ohne Titel")).ToList() };
        var current = docs.FindIndex(d => d.Id == _liveHandoutDoc);
        if (current >= 0) docBox.SelectedIndex = current;
        var share = new Button { Content = room.HasHandout ? L("Erneut senden") : L("An die Handys senden"), Classes = { "primary" } };
        share.Click += async (_, _) =>
        {
            if (docBox.SelectedIndex < 0) return;
            var d = docs[docBox.SelectedIndex];
            var html = await HandoutHtmlAsync(d.Title, d.Body, string.Join(", ", d.Tags), d.Id, LinkedDocuments(d.Body, d.Id), false);
            room.SetHandout(html.Replace("<div class=\"noprint bar\">", "<div class=\"noprint bar\" style=\"display:none\">"));
            _liveHandoutDoc = d.Id;
            StatusText.Text = L("Das Handout „{0}“ ist auf den Handys.", d.Title);
            BuildLiveUi();
        };
        var unshare = new Button { Content = L("Zurückziehen"), IsVisible = room.HasHandout };
        unshare.Click += (_, _) => { room.SetHandout(null); _liveHandoutDoc = 0; BuildLiveUi(); };
        LivePanel.Children.Add(docBox);
        LivePanel.Children.Add(new WrapPanel { ItemSpacing = 6, Children = { share, unshare } });
        if (docs.Count == 0) LivePanel.Children.Add(new TextBlock { Text = L("Noch keine Dokumente. Schreibe eines unter Dokumente → Schreiben."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });

        // poll and questions change while the room is open
        _liveDynamic = new StackPanel { Spacing = 8 };
        LivePanel.Children.Add(_liveDynamic);
        FillLiveDynamic();
    }

    /// <summary>
    /// The parts that change while the room is open. A new question or vote rebuilds the list of questions and the results; the form
    /// for a new poll is left alone then, so that nothing typed in it is lost and the cursor stays where it is.
    /// </summary>
    void FillLiveDynamic() => FillLiveDynamic(true);

    void FillLiveDynamic(bool all)
    {
        if (_liveDynamic is not { } host || _live is not { } room) return;
        if (all || host.Children.Count < 3)
        {
            host.Children.Clear();
            host.Children.Add(new TextBlock { Classes = { "caption" }, Margin = new(0, 6, 0, 0) });
            host.Children.Add(new StackPanel { Spacing = 8 });   // the poll
            host.Children.Add(new StackPanel { Spacing = 8 });   // the questions
            all = true;
        }
        ((TextBlock)host.Children[0]).Text = L("{0} Handys haben die Seite geöffnet.", room.Phones);
        if (all || room.Poll is not null) FillLivePoll((StackPanel)host.Children[1], room);
        FillLiveQuestions((StackPanel)host.Children[2], room);
    }

    void FillLivePoll(StackPanel host, LiveRoom room)
    {
        host.Children.Clear();
        host.Children.Add(Eyebrow(L("Umfrage")));
        if (room.Poll is not { } poll)
        {
            var question = new TextBox { PlaceholderText = L("Frage, z. B. Welcher Vers hat dich angesprochen?"), Text = _livePollDraft.Question, TextWrapping = TextWrapping.Wrap };
            var options = new TextBox { PlaceholderText = L("Antworten, eine pro Zeile"), Text = _livePollDraft.Options, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
            question.TextChanged += (_, _) => _livePollDraft.Question = question.Text ?? "";
            options.TextChanged += (_, _) => _livePollDraft.Options = options.Text ?? "";
            var presets = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
            foreach (var (name, opts) in new[]
            {
                (L("Ja / Nein"), L("Ja") + "\n" + L("Nein") + "\n" + L("Weiß nicht")),
                (L("Skala 1–5"), "1\n2\n3\n4\n5"),
                (L("Verse des Abschnitts"), _curChapterData is null ? "" : string.Join("\n", (_selVerses.Count > 1 ? _selVerses.OrderBy(v => v) : Enumerable.Range(1, Math.Min(8, _curChapterData.Verses.Count))).Take(8).Select(v => Label(new VerseRef(_curBook, _curChapter, v))))),
            })
            {
                var b = FilterChip(name, false, null, () => { _livePollDraft.Options = opts; options.Text = opts; });
                presets.Children.Add(b);
            }
            var go = new Button { Content = L("Umfrage starten"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
            go.Click += (_, _) =>
            {
                var opts = (options.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if ((question.Text ?? "").Trim().Length == 0 || opts.Count < 2) { StatusText.Text = L("Eine Umfrage braucht eine Frage und mindestens zwei Antworten."); return; }
                room.StartPoll(question.Text!, opts);
                _livePollDraft = new();
                FillLiveDynamic();
            };
            host.Children.Add(question); host.Children.Add(options); host.Children.Add(presets); host.Children.Add(go);
        }
        else
        {
            var results = room.Results();
            var total = Math.Max(1, results.Sum());
            var bars = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = poll.Question, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap } } };
            for (var i = 0; i < poll.Options.Count; i++)
            {
                var share = results[i] / (double)total;
                var bar = new Border { Height = 8, CornerRadius = new(4), Background = Res("BsAccent"), HorizontalAlignment = HorizontalAlignment.Left };
                var track = new Border { Height = 8, CornerRadius = new(4), Background = Res("BsBorder") };
                var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(new Avalonia.Controls.GridLength(Math.Max(share, 0.001), GridUnitType.Star)), new ColumnDefinition(new Avalonia.Controls.GridLength(Math.Max(1 - share, 0.001), GridUnitType.Star)) } };
                grid.Children.Add(track); Grid.SetColumnSpan(track, 2);
                grid.Children.Add(bar);
                bar.HorizontalAlignment = HorizontalAlignment.Stretch;
                bar.IsVisible = results[i] > 0;
                bars.Children.Add(new DockPanel { Children = { new TextBlock { Text = results[i].ToString(), [DockPanel.DockProperty] = Dock.Right, Margin = new(8, 0, 0, 0) }, new TextBlock { Text = poll.Options[i], TextWrapping = TextWrapping.Wrap } } });
                bars.Children.Add(grid);
            }
            bars.Children.Add(new TextBlock { Text = L("{0} Stimmen", results.Sum()), Classes = { "caption" } });
            host.Children.Add(new Border { Classes = { "card" }, Child = bars });
            var show = new ToggleButton { Content = L("Ergebnis auf den Handys"), IsChecked = poll.ShowResults };
            show.IsCheckedChanged += (_, _) => room.ShowPollResults(show.IsChecked == true);
            var beam = new Button { Content = L("Ergebnis auf den Beamer") };
            beam.Click += (_, _) =>
            {
                var r = room.Results();
                ProjectText(poll.Question, string.Join("\n", poll.Options.Select((o, i) => $"{o}   {r[i]}")));
            };
            var end = new Button { Content = L("Umfrage beenden") };
            end.Click += (_, _) => { room.EndPoll(); FillLiveDynamic(); };
            host.Children.Add(new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Children = { show, beam, end } });
        }
    }

    void FillLiveQuestions(StackPanel host, LiveRoom room)
    {
        host.Children.Clear();
        var questions = room.Questions;
        host.Children.Add(Eyebrow(L("Fragen ({0})", questions.Count(q => !q.Answered))));
        if (questions.Count == 0)
            host.Children.Add(new TextBlock { Text = L("Fragen von den Handys erscheinen hier. Nur du siehst sie."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        foreach (var q in questions.OrderBy(q => q.Answered).ThenByDescending(q => q.Id))
        {
            var meta = (q.Name.Length > 0 ? q.Name + " · " : "") + q.At.ToString("t", Loc.Culture);
            var beam = new Button { Content = L("Auf den Beamer") };
            beam.Click += (_, _) => ProjectText(q.Name.Length > 0 ? L("Frage von {0}", q.Name) : L("Frage"), q.Text);
            var done = new Button { Content = q.Answered ? L("Wieder offen") : L("✓ Besprochen") };
            done.Click += (_, _) => { room.MarkAnswered(q.Id, !q.Answered); FillLiveDynamic(); };
            var del = new Button { Content = L("Löschen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0) };
            del.Click += (_, _) => { room.RemoveQuestion(q.Id); FillLiveDynamic(); };
            host.Children.Add(new Border
            {
                Classes = { "card" }, Opacity = q.Answered ? 0.6 : 1,
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new SelectableTextBlock { Text = q.Text, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = meta, Classes = { "caption" } },
                        new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Children = { beam, done, del } },
                    },
                },
            });
        }
    }

    sealed class PollDraft { public string Question = "", Options = ""; }
    PollDraft _livePollDraft = new();
}
