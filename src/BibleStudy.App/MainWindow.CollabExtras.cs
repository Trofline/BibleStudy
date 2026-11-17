using System.Text;
using Avalonia;
using Avalonia.VisualTree;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BibleStudy.App;

/// <summary>
/// Studying together, for groups: one person leads (the others follow until they turn the page themselves), a shared list of questions to
/// the text, a shared notepad, short reactions to chat lines and verses, and the whole meeting saved as one document.
/// </summary>
public partial class MainWindow
{
    static readonly string[] Reactions = ["♥", "Amen", "✓", "!", "?"];

    // leading
    bool _iLead, _followLeader;
    string? _leaderId;
    readonly Border _leaderBar = new() { CornerRadius = new(8), Padding = new(10, 6), IsVisible = false };

    // questions, notepad, reactions, the meeting
    sealed class CollabQuestion { public string Id = "", Text = "", By = ""; public bool Done; }
    readonly List<CollabQuestion> _questions = [];
    readonly StackPanel _questionsBox = new() { Spacing = 4 };
    Expander? _questionsExpander;
    readonly TextBox _padBox = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110, MaxHeight = 320, MaxLength = 60_000, PlaceholderText = "" };
    bool _padRemote, _padWired;
    IDisposable? _padTimer;
    readonly Dictionary<string, WrapPanel> _bubbles = [];
    readonly List<(DateTime When, string Who, string Text)> _meeting = [];
    readonly List<(string Book, int Chapter, int Verse, string Text, Color Ink, DateTime Until)> _floating = [];

    // ---- leading ----------------------------------------------------------------------------------------------------------

    void SetLeading(bool on)
    {
        _iLead = on;
        if (on) _leaderId = null;
        CollabSend(new { t = "lead", on });
        AddCollabLog(on ? L("Du leitest jetzt. Die anderen folgen deiner Stelle.") : L("Du leitest nicht mehr."), mine: true, system: true);
        if (on) CollabSendLocation(_selected);
        UpdateLeaderBar();
    }

    /// <summary>The line "Anna leads": while following, says so; after turning the page oneself, offers to follow again.</summary>
    void UpdateLeaderBar()
    {
        _leaderBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("BsSurface2"));
        if (_leaderId is null || !_people.TryGetValue(_leaderId, out var leader)) { _leaderBar.IsVisible = false; return; }
        var text = new TextBlock { Text = _followLeader ? L("{0} leitet. Du folgst automatisch.", leader.Name) : L("{0} leitet. Du liest gerade woanders.", leader.Name), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel();
        if (!_followLeader)
        {
            var back = new Button { Content = L("Wieder folgen"), Classes = { "primary" }, Margin = new(8, 0, 0, 0) };
            back.Click += async (_, _) => { _followLeader = true; UpdateLeaderBar(); if (leader.Place is { } p) await FollowAsync(p); };
            DockPanel.SetDock(back, Dock.Right);
            row.Children.Add(back);
        }
        row.Children.Add(text);
        _leaderBar.BorderBrush = new SolidColorBrush(leader.Ink); _leaderBar.BorderThickness = new(3, 0, 0, 0);
        _leaderBar.Child = row;
        _leaderBar.IsVisible = true;
    }

    /// <summary>Called when this person turns to another place: while someone leads, that ends following (until "Follow again").</summary>
    void CollabNavigatedByMe()
    {
        if (_collabSuppress || _leaderId is null || !_followLeader) return;
        _followLeader = false;
        UpdateLeaderBar();
    }

    // ---- what a newcomer gets ---------------------------------------------------------------------------------------------

    void SendSharedState()
    {
        if (_iLead) CollabSend(new { t = "lead", on = true });
        if (_questions.Count > 0) CollabSend(new { t = "qall", items = _questions.Select(q => new { id = q.Id, text = q.Text, by = q.By, done = q.Done }) });
        if ((_padBox.Text ?? "").Length > 0) CollabSend(new { t = "pad", text = _padBox.Text });
    }

    // ---- questions --------------------------------------------------------------------------------------------------------

    Control BuildQuestionsBox()
    {
        Detach(_questionsBox);
        var input = new TextBox { PlaceholderText = L("Frage zum Text, Enter fügt hinzu"), MaxLength = 400 };
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            var q = new CollabQuestion { Id = Guid.NewGuid().ToString("N")[..10], Text = input.Text.Trim(), By = _settings.CollabName };
            input.Text = "";
            _questions.Add(q);
            CollabSend(new { t = "q", op = "add", id = q.Id, text = q.Text, by = q.By });
            RefreshQuestions();
        };
        _questionsExpander = new Expander { Header = QuestionsHeader(), Content = new StackPanel { Spacing = 6, Margin = new(0, 6, 0, 0), Children = { input, _questionsBox } }, IsExpanded = _questions.Count > 0 };
        RefreshQuestions();
        return _questionsExpander;
    }

    string QuestionsHeader() => _questions.Count == 0 ? L("Fragen zum Text") : L("Fragen zum Text ({0} offen)", _questions.Count(q => !q.Done));

    void RefreshQuestions()
    {
        _questionsBox.Children.Clear();
        foreach (var q in _questions)
        {
            var question = q;
            var box = new CheckBox { IsChecked = q.Done, Content = new TextBlock { Text = q.Text + (q.By.Length > 0 ? $"  ({q.By})" : ""), TextWrapping = TextWrapping.Wrap, TextDecorations = q.Done ? TextDecorations.Strikethrough : null } };
            ToolTip.SetTip(box, L("Abhaken, wenn die Frage besprochen ist"));
            box.IsCheckedChanged += (_, _) => { question.Done = box.IsChecked == true; CollabSend(new { t = "q", op = question.Done ? "done" : "undo", id = question.Id }); RefreshQuestions(); };
            var del = new Button { Content = "✕", Classes = { "tool" } };
            del.Click += (_, _) => { _questions.Remove(question); CollabSend(new { t = "q", op = "del", id = question.Id }); RefreshQuestions(); };
            DockPanel.SetDock(del, Dock.Right);
            _questionsBox.Children.Add(new DockPanel { Children = { del, box } });
        }
        if (_questionsExpander is not null) _questionsExpander.Header = QuestionsHeader();
    }

    // ---- notepad ----------------------------------------------------------------------------------------------------------

    Control BuildPadBox()
    {
        Detach(_padBox);
        _padBox.PlaceholderText = L("Hier schreibt ihr gemeinsam: Gedanken, Gliederung, Gebetsanliegen …");
        if (!_padWired)
        {
            _padWired = true;
            _padBox.TextChanged += (_, _) =>
            {
                if (_padRemote) return;
                _padTimer?.Dispose();
                _padTimer = DispatcherTimer.RunOnce(() => CollabSend(new { t = "pad", text = _padBox.Text ?? "" }), TimeSpan.FromMilliseconds(400));
            };
        }
        var save = new Button { Content = L("Als Dokument speichern"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_padBox.Text)) return;
            _vault.AddDocument(L("Notizblatt {0:yyyy-MM-dd HH:mm}", DateTime.Now), _padBox.Text!, null, false, [L("geteilt")]);
            RefreshDocs();
            save.Content = L("Gespeichert ✓");
        };
        return new Expander
        {
            Header = L("Gemeinsames Notizblatt"), IsExpanded = (_padBox.Text ?? "").Length > 0,
            Content = new StackPanel
            {
                Spacing = 6, Margin = new(0, 6, 0, 0),
                Children =
                {
                    new TextBlock { Text = L("Alle sehen dieselbe Seite. Schreiben zwei gleichzeitig, gilt die zuletzt gesendete Fassung."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                    _padBox, save,
                },
            },
        };
    }

    // ---- chat and reactions -----------------------------------------------------------------------------------------------

    /// <summary>A chat line with the name in the person's color; a tap on it offers a reaction.</summary>
    void AddChat(string who, string text, string mid, bool mine, Color? ink)
    {
        var reactions = new WrapPanel();
        var body = new StackPanel { Spacing = 2 };
        if (!mine) body.Children.Add(new TextBlock { Text = who, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = ink is { } c ? new SolidColorBrush(c) : null });
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(reactions);
        var bubble = new Border { Padding = new(10, 6), CornerRadius = new(10), HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = 340, Child = body, Cursor = new Cursor(StandardCursorType.Hand) };
        bubble.Bind(Border.BackgroundProperty, this.GetResourceObservable(mine ? "BsAccentSoft" : "BsSurface2"));
        ToolTip.SetTip(bubble, L("Antippen, um zu reagieren"));
        if (mid.Length > 0)
        {
            _bubbles[mid] = reactions;
            bubble.PointerReleased += (_, _) =>
            {
                var menu = new MenuFlyout();
                foreach (var r in Reactions)
                {
                    var item = new MenuItem { Header = r };
                    item.Click += (_, _) => { CollabSend(new { t = "react", to = mid, r }); AddReaction(reactions, r, _settings.CollabName); };
                    menu.Items.Add(item);
                }
                menu.ShowAt(bubble);
            };
        }
        AddCollabItem(bubble);
        _meeting.Add((DateTime.Now, mine ? _settings.CollabName : who, text));
    }

    static void AddReaction(WrapPanel to, string r, string who)
    {
        var chip = new Border { CornerRadius = new(8), Padding = new(6, 0), Margin = new(0, 2, 4, 0), Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), Child = new TextBlock { Text = r, FontSize = 12 } };
        ToolTip.SetTip(chip, who);
        to.Children.Add(chip);
    }

    void ShowVerseReactMenu(Control anchor)
    {
        if (_selected is not int v) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
        var menu = new MenuFlyout();
        foreach (var r in Reactions)
        {
            var item = new MenuItem { Header = r };
            item.Click += (_, _) =>
            {
                CollabSend(new { t = "vreact", book = _curBook, chapter = _curChapter, verse = v, r });
                AddCollabLog(L("Du: {0} zu {1}", r, $"{BookName(_curBook)} {_curChapter},{v}"), mine: true, system: true);
            };
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }

    /// <summary>Reactions to a verse float over it for a few seconds (when that chapter is open).</summary>
    void ShowFloatingReactions()
    {
        if (_peerLayer is null) return;
        _floating.RemoveAll(f => f.Until < DateTime.UtcNow);
        var stack = 0;
        foreach (var f in _floating)
        {
            if (f.Book != _curBook || f.Chapter != _curChapter || !_verseBoxes.TryGetValue(f.Verse, out var box) || box.TranslatePoint(default, _peerLayer) is not { } at) continue;
            var tag = NameTag(f.Text, f.Ink);
            Avalonia.Controls.Canvas.SetLeft(tag, at.X + box.Bounds.Width - 40 - stack * 8); Avalonia.Controls.Canvas.SetTop(tag, at.Y + 4 + stack * 18);
            _peerFrames.Add(tag); _peerLayer.Children.Add(tag);
            stack++;
        }
    }

    // ---- the meeting as a document ----------------------------------------------------------------------------------------

    void SaveMeetingAsDocument()
    {
        var sb = new StringBuilder();
        sb.AppendLine(L("Gemeinsames Studium am {0:dd.MM.yyyy}", DateTime.Now)).AppendLine();
        if (_people.Count > 0) sb.AppendLine(L("Mit: {0}", PeopleNames)).AppendLine();
        if (_meeting.Count > 0)
        {
            sb.AppendLine("## " + L("Gespräch"));
            foreach (var (when, who, text) in _meeting) sb.AppendLine($"- {when:HH:mm} {(who.Length > 0 ? who + ": " : "")}{text}");
            sb.AppendLine();
        }
        if (_questions.Count > 0)
        {
            sb.AppendLine("## " + L("Fragen"));
            foreach (var q in _questions) sb.AppendLine($"- [{(q.Done ? "x" : " ")}] {q.Text}{(q.By.Length > 0 ? $" ({q.By})" : "")}");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(_padBox.Text)) sb.AppendLine("## " + L("Notizblatt")).AppendLine(_padBox.Text);
        _vault.AddDocument(L("Gemeinsames Studium {0:yyyy-MM-dd HH:mm}", DateTime.Now), sb.ToString(), null, false, [L("geteilt")]);
        RefreshDocs();
        AddCollabLog(L("Das Treffen ist als Dokument gespeichert."), system: true);
    }

    // ---- receiving --------------------------------------------------------------------------------------------------------

    bool HandleCollabExtras(string type, JsonElement root)
    {
        string Str(string key, int max) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String ? Clip(e.GetString(), max) : "";
        int Int(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : 0;
        bool Bool(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.True;
        var p = _from!;
        switch (type)
        {
            case "lead":
                p.Leads = Bool("on");
                if (p.Leads)
                {
                    foreach (var other in _people.Values.Where(o => o != p)) other.Leads = false;
                    _iLead = false; _leaderId = p.Id; _followLeader = true;
                    AddCollabLog(L("{0} leitet jetzt. Du folgst automatisch, bis du selbst blätterst.", p.Name), system: true);
                    if (p.Place is { } at) _ = FollowAsync(at);
                }
                else if (_leaderId == p.Id) { _leaderId = null; AddCollabLog(L("{0} leitet nicht mehr.", p.Name), system: true); }
                UpdateLeaderBar(); ShowPeople();
                if (CollabPanel.Children.Count > 0 && _iLead == false) BuildCollabUi();
                return true;
            case "q":
            {
                var id = Str("id", 16);
                if (id.Length == 0) return true;
                var q = _questions.FirstOrDefault(x => x.Id == id);
                switch (Str("op", 6))
                {
                    case "add" when q is null && _questions.Count < 200:
                        if (Str("text", 400).Trim() is { Length: > 0 } text) _questions.Add(new CollabQuestion { Id = id, Text = text, By = Str("by", 40) });
                        break;
                    case "done" when q is not null: q.Done = true; break;
                    case "undo" when q is not null: q.Done = false; break;
                    case "del" when q is not null: _questions.Remove(q); break;
                }
                RefreshQuestions();
                return true;
            }
            case "qall":
                if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    foreach (var e in items.EnumerateArray().Take(200))
                    {
                        if (e.ValueKind != JsonValueKind.Object) continue;
                        string S(string k, int max) => e.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? Clip(x.GetString(), max) : "";
                        var id = S("id", 16); var text = S("text", 400).Trim();
                        if (id.Length == 0 || text.Length == 0 || _questions.Any(x => x.Id == id) || _questions.Count >= 200) continue;
                        _questions.Add(new CollabQuestion { Id = id, Text = text, By = S("by", 40), Done = e.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True });
                    }
                RefreshQuestions();
                return true;
            case "pad":
            {
                var text = Str("text", 60_000);
                if (text == (_padBox.Text ?? "")) return true;
                _padRemote = true;
                var caret = _padBox.CaretIndex;
                _padBox.Text = text;
                _padBox.CaretIndex = Math.Min(caret, text.Length);
                _padRemote = false;
                return true;
            }
            case "react":
            {
                var r = Str("r", 8);
                if (Reactions.Contains(r) && _bubbles.TryGetValue(Str("to", 32), out var panel)) AddReaction(panel, r, p.Name);
                return true;
            }
            case "vreact":
            {
                var r = Str("r", 8); var book = Str("book", 8); var chapter = Int("chapter"); var verse = Int("verse");
                if (!Reactions.Contains(r) || _books.All(b => b.Id != book) || chapter is < 1 or > 200 || verse is < 1 or > 200) return true;
                AddCollabLog(L("{0}: {1} zu {2}", p.Name, r, $"{BookName(book)} {chapter},{verse}"), system: true);
                _floating.Add((book, chapter, verse, $"{r} {p.Name}", p.Ink, DateTime.UtcNow.AddSeconds(6)));
                RenderPeerLayer();
                DispatcherTimer.RunOnce(RenderPeerLayer, TimeSpan.FromSeconds(6.2));
                return true;
            }
            case "friend":
            case "friendok":
                OnFriendMessage(type, Str("token", 64), Int("port"));
                return true;
        }
        return false;
    }
}
