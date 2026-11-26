using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Collab;
using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;
using Avalonia.Input.Platform;

namespace BibleStudy.App;

/// <summary>
/// Studying together: direct encrypted connections to other people (no server, no account, no cost), in the same network by name, across
/// networks with a short room code through a relay that only passes on sealed messages. Several people form a group: each program passes on what it gets (see <see cref="CollabGroup"/>).
/// Only what a person sends on purpose arrives: their place in the Bible (if they switch that on), chat lines, verses, highlights, documents.
/// Everything received is treated as untrusted text: nothing is run, saved or opened without a click (following the place is opt-in).
/// </summary>
public partial class MainWindow
{
    const int CollabTab = 23;
    const int MaxSharedBody = 200_000;
    const int MaxSentBody = 60_000;
    static readonly string[] PeopleInks = ["#8E4EC6", "#0090FF", "#E54666", "#12A594", "#F76B15", "#3E63DD", "#D6409F", "#978365"];

    /// <summary>Someone else in the group, with what is known of them right now.</summary>
    sealed class CollabPerson
    {
        public string Id = "", Name = "";
        public Color Ink;
        public ICollabLink? Via;
        public bool Greeted, Leads;
        public (string Book, int Chapter, int? Verse)? Place;
        public (string Book, int Chapter, int[] Verses, string Words)? Sel;
        public (string Book, int Chapter, int Verse, double X, double Y)? Ptr;
        public (string Book, int Chapter, int Verse)? Reading;
        public (string Book, int Chapter, List<(int Verse, int End, string Color)> Items)? Marks;
        public Control? Pointer;
        public TextBlock? PointerName;
    }

    CollabGroup? _group;
    readonly Dictionary<string, (ICollabLink Link, string Name, string Raw)> _latestPictures = [];
    RelayLink? _pendingCode;       // the room across networks while waiting for the other person
    readonly Dictionary<string, CollabPerson> _people = [];
    CollabPerson? _from;           // who wrote the message being handled
    int _inkNext;
    readonly Dictionary<string, Color> _inkOf = [];
    string _peopleChapter = "";
    bool _collabSuppress;          // true while the app itself jumps to another person's place, so that it does not echo back
    bool _follow;
    readonly StackPanel _collabLog = new() { Spacing = 6 };
    readonly StackPanel _peopleBox = new() { Spacing = 6 };

    bool CollabOpen => _group?.Any == true;
    string PeerWho => _from?.Name is { Length: > 0 } n ? n : L("Die andere Person");
    string PeopleNames => string.Join(", ", _people.Values.Select(p => p.Name.Length > 0 ? p.Name : "?"));

    static void OnUi(Action a) => Dispatcher.UIThread.Post(a);

    void BuildCollabUi()
    {
        CollabPanel.Children.Clear();
        CollabPanel.Children.Add(new TextBlock { Text = L("GEMEINSAM STUDIEREN"), Classes = { "eyebrow" } });
        if (!CollabConsented) { BuildCollabConsent(); return; }
        if (CollabOpen) BuildCollabConnected(); else BuildCollabStart();
        var revoke = new Button { Content = L("Zustimmung zurücknehmen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0) };
        revoke.Click += (_, _) => RevokeCollabConsent();
        CollabPanel.Children.Add(revoke);
    }

    /// <summary>The group with the lasting id of this person (made once).</summary>
    CollabGroup Group()
    {
        if (_settings.CollabId.Length == 0) { _settings.CollabId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)); SaveSettings(); }
        if (_group is null)
        {
            _group = new CollabGroup(_settings.CollabId);
            _group.Message += (link, fid, fn, raw) =>
            {
                // camera pictures: only the newest of each person waits for the screen, so a slow moment never builds up a delay
                if (raw.StartsWith("{\"t\":\"vf\"", StringComparison.Ordinal))
                {
                    bool post;
                    lock (_latestPictures) { post = _latestPictures.Count == 0; _latestPictures[fid] = (link, fn, raw); }
                    if (post) OnUi(() =>
                    {
                        List<(ICollabLink, string, string, string)> all;
                        lock (_latestPictures) { all = _latestPictures.Select(x => (x.Value.Link, x.Key, x.Value.Name, x.Value.Raw)).ToList(); _latestPictures.Clear(); }
                        foreach (var (l, f, n, r) in all) OnGroupMessage(l, f, n, r);
                    });
                    return;
                }
                OnUi(() => OnGroupMessage(link, fid, fn, raw));
            };
        }
        _group.MyName = _settings.CollabName;
        return _group;
    }

    // ---- before the connection ------------------------------------------------------------------------------------------

    void BuildCollabStart()
    {
        CollabPanel.Children.Add(new TextBlock
        {
            Text = L("Studiere die Bibel mit einer anderen Person, von Programm zu Programm: verschlüsselt, ohne Konto und kostenlos."),
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap,
        });
        CollabPanel.Children.Add(Card(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = L("Was geteilt wird"), FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = L("Nur, was du hier ausdrücklich sendest: Chat, einzelne Verse und Kopien von Dokumenten, auf Wunsch deine aktuelle Bibelstelle. Dein Vault bleibt bei dir. Im selben Netzwerk sieht die andere Person deine Internetadresse. Verbinde dich nur mit Menschen, denen du vertraust."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
            },
        }));

        var name = new TextBox { PlaceholderText = L("Dein Name (wird der anderen Person gezeigt)"), Text = _settings.CollabName, MaxLength = 40 };
        name.LostFocus += (_, _) => { _settings.CollabName = (name.Text ?? "").Trim(); SaveSettings(); EnsureNet(); };
        CollabPanel.Children.Add(name);

        // the way that works everywhere comes first: both programs meet at a small relay server, which passes on only sealed messages
        var area = new StackPanel { Spacing = 8 };
        var host = new Button { Content = L("Raum erstellen"), Classes = { "primary" }, Margin = new(0, 0, 8, 0) };
        var join = new Button { Content = L("Raum beitreten") };
        host.Click += async (_, _) => { if (CollabNameOk(name)) await HostRoomAsync(area); };
        join.Click += (_, _) => { if (CollabNameOk(name)) JoinRoomFlow(area); };
        CollabPanel.Children.Add(Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = L("Über das Internet (Raum-Code)"), FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = L("Eine Person erstellt einen Raum und schickt den anderen den kurzen Raum-Code, zum Beispiel per Messenger. Die anderen tippen ihn ein, fertig. Das klappt von überall, auch wenn ihr in verschiedenen Netzwerken seid. Bis zu fünf Personen passen in einen Raum."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                new WrapPanel { Children = { host, join } },
                area,
                new TextBlock { Text = L("Die Verbindung läuft über einen kleinen Vermittler-Server im Internet. Er reicht nur verschlüsselte Nachrichten weiter: Der Schlüssel entsteht aus dem Raum-Code auf euren Geräten, der Server kann nichts lesen und sieht den Code nicht. Ein Konto braucht ihr nicht."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = L("Der Vermittler ist kostenlos und hat deshalb ein Tageslimit. Ist es aufgebraucht, verbindet euch bis zum nächsten Tag im selben Netzwerk (unten). Seid ihr an verschiedenen Orten, geht das, wenn ihr beide demselben kostenlosen ZeroTier-Netzwerk beitretet (siehe Handbuch)."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                BuildServerBox(),
            },
        }));
        CollabPanel.Children.Add(BuildNetCard());
    }

    /// <summary>The relay server the program uses unless the person chose an own one (see relay/ in the repository).</summary>
    const string DefaultCollabServer = "wss://biblestudy-relay.troflineblack.workers.dev";

    string CollabServer => _settings.CollabServer.Trim() is { Length: > 0 } own ? own : DefaultCollabServer;

    /// <summary>An own relay server instead of the built-in one. Collapsed, because almost nobody needs it.</summary>
    Control BuildServerBox()
    {
        var url = new TextBox { PlaceholderText = DefaultCollabServer, Text = _settings.CollabServer, MaxLength = 200 };
        url.LostFocus += (_, _) => { _settings.CollabServer = (url.Text ?? "").Trim(); SaveSettings(); };
        return new Expander
        {
            Header = L("Eigener Vermittler-Server (optional)"), Margin = new(0, 8, 0, 0), IsExpanded = _settings.CollabServer.Length > 0,
            Content = new StackPanel
            {
                Spacing = 6, Margin = new(0, 6, 0, 0),
                Children =
                {
                    new TextBlock { Text = L("Leer lassen, dann wird der eingebaute benutzt. Wer einen eigenen betreiben möchte, findet ihn im Quelltext unter „relay“. Beide Seiten müssen denselben Server eintragen."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                    url,
                },
            },
        };
    }

    bool CollabNameOk(TextBox box)
    {
        _settings.CollabName = (box.Text ?? "").Trim(); SaveSettings();
        if (_settings.CollabName.Length > 0) return true;
        box.Focus();
        StatusText.Text = L("Bitte gib zuerst deinen Namen ein.");
        return false;
    }

    RelayLink NewRoom(string? code)
    {
        _pendingCode?.Dispose();
        var room = code is null ? RelayLink.Create(CollabServer) : RelayLink.Join(CollabServer, code);
        _pendingCode = room;
        Wire(room);
        return room;
    }

    /// <summary>Adds a connection to the group once it stands (codes or same network alike); more people can join the same way.</summary>
    void Wire(ICollabLink link)
    {
        var wasOpen = false;
        link.Connected += () => OnUi(() =>
        {
            if (link is RelayLink && _pendingCode != link) return;
            if (_pendingCode == link) _pendingCode = null;
            wasOpen = true;
            var first = !CollabOpen;
            Group().Add(link);
            InitCollabLive();
            if (first) ResetCollabLive();
            SendHello();
            BuildCollabUi();
        });
        // someone more came into the room: they learn who is here and what is shared (the others answer their hello the same way)
        if (link is RelayLink room) room.PeerJoined += () => OnUi(() => { if (_group?.Links.Contains(link) == true) { SendHello(); ShowPeople(); } });
        // Only a connection that really stood ends anything: a room that closes while still waiting is handled by the waiting page itself.
        link.Closed += _ => OnUi(() =>
        {
            if (!wasOpen) return;
            wasOpen = false;
            _group?.Remove(link);
            var gone = _people.Values.Where(p => p.Via == link).ToList();
            foreach (var p in gone) RemovePerson(p);
            AddCollabLog(gone.Count > 0 ? L("{0} hat die Verbindung beendet.", string.Join(", ", gone.Select(p => p.Name))) : L("Die Verbindung wurde beendet."), system: true);
            if (!CollabOpen) { _follow = false; _incoming.Clear(); ResetCollabLive(); }
            if (CollabPanel.Children.Count > 0) BuildCollabUi();
        });
    }

    /// <summary>Leaves the group: says goodbye and closes every connection.</summary>
    void DisconnectAll()
    {
        if (CollabOpen) Group().Send(new { t = "bye" });
        foreach (var l in _group?.Links ?? []) { _group!.Remove(l); l.Dispose(); }
        _pendingCode?.Dispose(); _pendingCode = null;
        foreach (var p in _people.Values.ToList()) RemovePerson(p);
        _follow = false; _incoming.Clear(); _iLead = false; _leaderId = null;
        StopMedia();
        ResetCollabLive();
    }

    void RemovePerson(CollabPerson p)
    {
        _people.Remove(p.Id);
        if (p.Pointer is not null) _peerLayer?.Children.Remove(p.Pointer);
        RemoveTile(p.Id); _mixer.Clear(p.Id);
        if (_leaderId == p.Id) _leaderId = null;
        ShowPeople(); RenderPeerLayer(); UpdateLeaderBar();
    }

    /// <summary>Tells the group who this is, and what is shared right now (questions, notepad, leading, place, chosen verses).</summary>
    void SendHello()
    {
        Group().Send(new { t = "hello", name = _settings.CollabName });
        _sentSel = ""; _sentMarks = "";
        CollabSendLocation(_selected);
        CollabSendSelection();
        CollabSendChapterMarks();
        SendSharedState();
    }

    // ---- connected ------------------------------------------------------------------------------------------------------

    void BuildCollabConnected()
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = new SolidColorBrush(Color.Parse("#30A46C")), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Text = _people.Count > 0 ? L("Verbunden mit {0}", PeopleNames) : L("Verbunden"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var leave = new Button { Content = L("Trennen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0) };
        leave.Click += (_, _) => { DisconnectAll(); BuildCollabUi(); };
        var top = new DockPanel();
        DockPanel.SetDock(leave, Dock.Right);
        top.Children.Add(leave); top.Children.Add(head);
        CollabPanel.Children.Add(top);
        if (_group?.Links.OfType<RelayLink>().FirstOrDefault() is { } room)
        {
            var copy = new Button { Content = L("Code kopieren"), Classes = { "tool" }, Width = double.NaN, Padding = new(8, 0), VerticalAlignment = VerticalAlignment.Center };
            copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync(room.Code); copy.Content = L("Kopiert ✓"); } };
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            line.Children.Add(new TextBlock { Text = L("Raum-Code: {0}", room.Code), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(copy);
            CollabPanel.Children.Add(line);
            CollabPanel.Children.Add(new TextBlock { Text = L("Mit diesem Code können bis zu {0} Personen in den Raum kommen.", RelayLink.MaxPeople), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        }
        Detach(_peopleBox);
        CollabPanel.Children.Add(_peopleBox);
        ShowPeople();

        // leading: the others follow the leader's place until they turn the page themselves
        var lead = new Button { Content = _iLead ? L("Leitung abgeben") : L("Ich leite"), Margin = new(0, 0, 8, 6) };
        ToolTip.SetTip(lead, L("Wer leitet, gibt die Stelle vor: Die anderen springen automatisch mit, bis sie selbst blättern."));
        lead.Click += (_, _) => { SetLeading(!_iLead); lead.Content = _iLead ? L("Leitung abgeben") : L("Ich leite"); };
        Detach(_leaderBar);
        CollabPanel.Children.Add(new WrapPanel { Children = { lead } });
        CollabPanel.Children.Add(_leaderBar);
        UpdateLeaderBar();

        var send = new CheckBox { Content = L("Meine Bibelstelle mitsenden"), IsChecked = _settings.CollabSendLocation };
        ToolTip.SetTip(send, L("Die andere Person sieht, welches Kapitel du gerade liest."));
        send.IsCheckedChanged += (_, _) => { _settings.CollabSendLocation = send.IsChecked == true; SaveSettings(); if (_settings.CollabSendLocation) CollabSendLocation(_selected); };
        var pointer = new CheckBox { Content = L("Meinen Mauszeiger zeigen"), IsChecked = _settings.CollabSendPointer };
        ToolTip.SetTip(pointer, L("Liest ihr dasselbe Kapitel, sieht die andere Person deinen Mauszeiger und die Verse, die du anklickst."));
        pointer.IsCheckedChanged += (_, _) => { _settings.CollabSendPointer = pointer.IsChecked == true; SaveSettings(); if (!_settings.CollabSendPointer) SendPointer(0, 0, 0); };
        var follow = new CheckBox { Content = L("Den anderen folgen"), IsChecked = _follow };
        ToolTip.SetTip(follow, L("Dein Lesefenster springt mit, wenn jemand anderes die Stelle wechselt."));
        follow.IsCheckedChanged += async (_, _) => { _follow = follow.IsChecked == true; if (_follow && _people.Values.FirstOrDefault(p => p.Place is not null)?.Place is { } p) await FollowAsync(p); };
        CollabPanel.Children.Add(send); CollabPanel.Children.Add(pointer); CollabPanel.Children.Add(follow);

        var actions = new WrapPanel();
        var verse = new Button { Content = L("Diesen Vers teilen"), Margin = new(0, 0, 8, 6) };
        verse.Click += (_, _) => ShareSelectedVerse();
        var come = new Button { Content = L("Hierher zeigen"), Margin = new(0, 0, 8, 6) };
        ToolTip.SetTip(come, L("Die andere Person bekommt einen Hinweis mit einem Knopf zu deiner Stelle."));
        come.Click += (_, _) => CollabCallOver();
        var react = new Button { Content = L("Reagieren ▾"), Margin = new(0, 0, 8, 6) };
        ToolTip.SetTip(react, L("Eine kurze Reaktion auf den gewählten Vers, ohne zu schreiben."));
        react.Click += (_, _) => ShowVerseReactMenu(react);
        var marksBtn = new Button { Content = L("Markierungen teilen ▾"), Margin = new(0, 0, 8, 6) };
        marksBtn.Click += (_, _) => ShowShareMarksMenu(marksBtn);
        var notesBtn = new Button { Content = L("Notizen teilen ▾"), Margin = new(0, 0, 8, 6) };
        notesBtn.Click += (_, _) => ShowShareNotesMenu(notesBtn);
        var doc = new Button { Content = L("Dokument teilen ▾"), Margin = new(0, 0, 8, 6) };
        doc.Click += (_, _) => ShowShareDocMenu(doc);
        var vaultBtn = new Button { Content = L("Vault senden ▾"), Margin = new(0, 0, 8, 6) };
        ToolTip.SetTip(vaultBtn, L("Sendet eine Kopie des ganzen Vaults. Die andere Person kann ihn als Gast ansehen oder als neuen Vault speichern."));
        vaultBtn.Click += (_, _) => ShowSendVaultMenu(vaultBtn);
        foreach (var b in new[] { verse, come, react, marksBtn, notesBtn, doc, vaultBtn }) actions.Children.Add(b);
        CollabPanel.Children.Add(actions);
        CollabPanel.Children.Add(BuildMediaBox());

        CollabPanel.Children.Add(new Expander { Header = L("Weitere Person dazuholen"), Content = BuildNetCard(), Margin = new(0, 2, 0, 0) });
        CollabPanel.Children.Add(BuildQuestionsBox());
        CollabPanel.Children.Add(BuildPadBox());

        var input = new TextBox { PlaceholderText = L("Nachricht schreiben, Enter sendet"), MaxLength = 2000, AcceptsReturn = false };
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            var text = input.Text.Trim(); input.Text = "";
            var mid = CollabSend(new { t = "chat", text });
            AddChat(_settings.CollabName, text, mid, mine: true, null);
        };
        input.TextChanged += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) CollabTyping(); };
        Detach(_peerTyping);
        CollabPanel.Children.Add(_peerTyping);
        CollabPanel.Children.Add(input);
        var save = new Button { Content = L("Treffen als Dokument speichern"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(save, L("Chat, geteilte Verse, Fragen und Notizblatt kommen als ein Dokument in deinen Vault."));
        save.Click += (_, _) => SaveMeetingAsDocument();
        CollabPanel.Children.Add(save);
        CollabPanel.Children.Add(_collabLog);
    }

    /// <summary>One line per person: color, name, where they read, whether they lead or read aloud, their highlights in your chapter.</summary>
    void ShowPeople()
    {
        _peopleBox.Children.Clear();
        foreach (var p in _people.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var person = p;
            var line = new List<string>();
            if (p.Place is { } at) line.Add(L("liest {0}", $"{BookName(at.Book)} {at.Chapter}" + (at.Verse is int pv ? "," + pv : "")));
            if (p.Leads) line.Add(L("leitet"));
            if (p.Reading is { } r) line.Add(L("liest gerade vor: {0}", $"{BookName(r.Book)} {r.Chapter},{r.Verse}"));
            if (p.Sel is { Words.Length: > 0 } s) line.Add($"»{Shorten(s.Words, 60)}«");
            var row = new StackPanel { Spacing = 2 };
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            name.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = new SolidColorBrush(p.Ink), VerticalAlignment = VerticalAlignment.Center });
            name.Children.Add(new TextBlock { Text = p.Name.Length > 0 ? p.Name : "?", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(name);
            if (line.Count > 0) row.Children.Add(new TextBlock { Text = string.Join(" · ", line), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            var buttons = new WrapPanel();
            if (p.Place is { } place)
            {
                var go = new Button { Content = L("Hingehen"), Classes = { "tool" }, Width = double.NaN, Padding = new(8, 0), Margin = new(0, 0, 6, 4) };
                go.Click += async (_, _) => await FollowAsync(person.Reading is { } rr ? (rr.Book, rr.Chapter, rr.Verse) : place);
                buttons.Children.Add(go);
            }
            if (p.Marks is { } mk && mk.Book == _curBook && mk.Chapter == _curChapter && mk.Items.Count > 0)
            {
                var take = new Button { Content = L("Markierungen übernehmen ({0})", mk.Items.Count), Classes = { "tool" }, Width = double.NaN, Padding = new(8, 0), Margin = new(0, 0, 6, 4) };
                ToolTip.SetTip(take, L("Die Markierungen dieser Person in diesem Kapitel kommen zu deinen (nur neue)."));
                take.Click += (_, _) => { TakeLiveMarks(person); take.IsEnabled = false; take.Content = L("Übernommen ✓"); };
                buttons.Children.Add(take);
            }
            if (p.Via is NetLink nl && nl.PeerId == p.Id)
            {
                if (_settings.CollabFriends.All(f => f.Id != p.Id))
                {
                    var friend = new Button { Content = L("Als Freund merken"), Classes = { "tool" }, Width = double.NaN, Padding = new(8, 0), Margin = new(0, 0, 6, 4) };
                    ToolTip.SetTip(friend, L("Beim nächsten Mal genügt ein Tipp zum Verbinden, auf Wunsch ohne Nachfrage."));
                    friend.Click += (_, _) => { MakeFriend(person, nl); friend.IsEnabled = false; friend.Content = L("Freund ✓"); };
                    buttons.Children.Add(friend);
                }
                row.Children.Add(new TextBlock { Text = L("Prüfzahl: {0}", nl.CheckCode), Classes = { "caption" } });
            }
            else if (p.Via is RelayLink rl) row.Children.Add(new TextBlock { Text = L("Prüfzahl: {0}", rl.CheckCode), Classes = { "caption" } });
            if (buttons.Children.Count > 0) row.Children.Add(buttons);
            _peopleBox.Children.Add(new Border { BorderBrush = new SolidColorBrush(p.Ink, 0.5), BorderThickness = new(3, 0, 0, 0), Padding = new(8, 2, 0, 2), Child = row });
        }
    }

    void AddCollabLog(string text, bool mine = false, bool system = false)
    {
        var bubble = new Border { Padding = new(10, 6), CornerRadius = new(10), HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = 340 };
        bubble.Bind(Border.BackgroundProperty, this.GetResourceObservable(mine ? "BsAccentSoft" : "BsSurface2"));
        bubble.Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { system ? "caption" : "" } };
        AddCollabItem(bubble);
        if (!system) _meeting.Add((DateTime.Now, mine ? _settings.CollabName : "", text));
    }

    void AddCollabItem(Control item)
    {
        _collabLog.Children.Insert(0, item);   // newest on top, so it is visible without scrolling
        while (_collabLog.Children.Count > 120) _collabLog.Children.RemoveAt(_collabLog.Children.Count - 1);
        AnimateIn(item, 0, -8);
    }

    // ---- sending ----------------------------------------------------------------------------------------------------------

    /// <summary>To everyone in the group; returns the id of the message.</summary>
    string CollabSend(object message)
    {
        if (!CollabOpen) return "";
        try { return Group().Send(message); } catch { return ""; /* a connection reports its end itself */ }
    }

    void CollabSendLocation(int? verse)
    {
        if (!CollabOpen || _collabSuppress || !_settings.CollabSendLocation) return;
        CollabSend(new { t = "nav", book = _curBook, chapter = _curChapter, verse });
    }

    Control CodeBox(string code)
    {
        var box = new TextBox { Text = code, IsReadOnly = true, FontSize = 26, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 200, HorizontalContentAlignment = HorizontalAlignment.Center };
        var copy = new Button { Content = L("Code kopieren"), HorizontalAlignment = HorizontalAlignment.Left };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync(code); copy.Content = L("Kopiert ✓"); }
        };
        return new StackPanel { Spacing = 6, Children = { box, copy } };
    }

    async Task HostRoomAsync(StackPanel area)
    {
        area.Children.Clear();
        var status = new TextBlock { Text = L("Raum wird erstellt …"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        area.Children.Add(status);
        RelayLink room;
        try { room = NewRoom(null); await room.StartAsync(); }
        catch (Exception ex) { status.Text = RoomFailed(ex); return; }
        area.Children.Clear();
        area.Children.Add(new TextBlock { Text = L("Schicke diesen Raum-Code den anderen:"), FontWeight = FontWeight.SemiBold });
        area.Children.Add(CodeBox(room.Code));
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        area.Children.Add(info);
        var cancel = new Button { Content = L("Abbrechen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        cancel.Click += (_, _) => { if (_pendingCode == room) { _pendingCode = null; room.Dispose(); } area.Children.Clear(); };
        area.Children.Add(cancel);
        await WaitRoomAsync(room, info, TimeSpan.FromMinutes(30));
    }

    void JoinRoomFlow(StackPanel area)
    {
        area.Children.Clear();
        area.Children.Add(new TextBlock { Text = L("Gib den Raum-Code ein, den du bekommen hast:"), FontWeight = FontWeight.SemiBold });
        var code = new TextBox { PlaceholderText = "ABCD-EFGH", MaxLength = 20, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 200 };
        var go = new Button { Content = L("Beitreten"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        async Task Join()
        {
            if (RelayLink.Normalize(code.Text) is null) { info.Text = L("Dieser Code passt nicht. Ein Raum-Code hat 8 Zeichen, zum Beispiel K7F2-QXM9."); return; }
            go.IsEnabled = false; info.Text = L("Verbinde …");
            try
            {
                var room = NewRoom(code.Text);
                await room.StartAsync();
                await WaitRoomAsync(room, info, TimeSpan.FromMinutes(2));
            }
            catch (Exception ex) { info.Text = RoomFailed(ex); }
            go.IsEnabled = true;
        }
        go.Click += async (_, _) => await Join();
        code.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Join(); };
        area.Children.Add(code); area.Children.Add(go); area.Children.Add(info);
    }

    string RoomFailed(Exception ex) =>
        L("Der Vermittler-Server ist gerade nicht erreichbar. Prüfe die Internetverbindung und versuche es gleich noch einmal.")
        + "\n" + L("Klappt es auch dann nicht, ist vielleicht das Tageslimit des kostenlosen Vermittlers aufgebraucht. Dann verbindet euch bis morgen im selben Netzwerk, an verschiedenen Orten über ein gemeinsames ZeroTier-Netzwerk (siehe Handbuch).")
        + "\n" + L("Technische Angaben: {0}", ex.Message);

    /// <summary>Waits until the other person is in the room and the key is agreed, and says what happens meanwhile.</summary>
    async Task WaitRoomAsync(RelayLink room, TextBlock info, TimeSpan wait)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!room.IsOpen && clock.Elapsed < wait && room.Status != "closed")
        {
            if (_pendingCode != room) return;
            info.Text = room.Status switch
            {
                "waiting" => L("Warte auf die anderen … ({0})", $"{(int)clock.Elapsed.TotalMinutes}:{clock.Elapsed.Seconds:00}"),
                "keys" => L("Jemand ist da. Verschlüsselung wird vereinbart …"),
                _ => L("Verbinde …"),
            };
            await Task.Delay(500);
        }
        if (room.IsOpen || _pendingCode != room) return;
        info.Text = room.CloseReason switch
        {
            "full" => L("In diesem Raum sind schon fünf Personen. Lass dir einen neuen Raum-Code geben."),
            "wrong code" => L("Die Verschlüsselung passte nicht zusammen. Prüft den Raum-Code und versucht es mit einem neuen Raum."),
            _ => L("Es ist niemand gekommen. Prüft den Raum-Code (beide müssen ihn gleichzeitig offen haben) und versucht es noch einmal."),
        };
        if (_pendingCode == room) { _pendingCode = null; room.Dispose(); }
    }

    void ShareSelectedVerse()
    {
        if (_selected is not int v) { StatusText.Text = L("Erst einen Vers anklicken."); return; }
        var r = new VerseRef(_curBook, _curChapter, v);
        var translation = (TranslationBox.SelectedItem as TranslationInfo)?.ShortName ?? "";
        // text of a privately imported translation (private use only) is never sent: only the place, the other person reads it in their own Bible
        var isPrivate = TranslationBox.SelectedItem is TranslationInfo { Id: var tid } && (BibleStudy.Core.Import.LocalBibleStore.Owns(tid) || BibleStudy.Core.Plugins.PluginBibleProvider.Owns(tid));
        CollabSend(new { t = "verse", book = r.Book, chapter = r.Chapter, verse = r.Verse, text = isPrivate ? "" : VerseTextOf(v).Trim(), translation = isPrivate ? "" : translation });
        AddCollabLog(L("Du hast {0} geteilt.", Label(r)), mine: true, system: true);
    }

    void ShowShareDocMenu(Control anchor)
    {
        var menu = new MenuFlyout();
        foreach (var d in _vault.GetDocuments().Where(x => x.Path is null).Take(40))
        {
            var doc = d; var item = new MenuItem { Header = doc.Title };
            item.Click += async (_, _) =>
            {
                // one message of the connection holds at most about 256 KB: a longer document is sent shortened, and the sender is told so
                var shortened = doc.Body.Length > MaxSentBody;
                var sent = DocumentLinks.WithoutNumbers(doc.Body);   // the numbers of the links mean something only in this vault
                await SendDocImagesAsync(shortened ? sent[..Math.Min(sent.Length, MaxSentBody)] : sent);   // its pictures go first, so they are there when the document arrives
                CollabSend(new { t = "doc", title = doc.Title, body = shortened ? sent[..Math.Min(sent.Length, MaxSentBody)] : sent });
                AddCollabLog(L("Du hast das Dokument „{0}“ als Kopie gesendet.", doc.Title) + (shortened ? " " + L("Es war sehr lang und wurde gekürzt gesendet.") : ""), mine: true, system: true);
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = L("Noch keine Dokumente"), IsEnabled = false });
        menu.ShowAt(anchor);
    }


    // ---- receiving (all of it untrusted) ---------------------------------------------------------------------------------

    static string Clip(string? s, int max) => s is null ? "" : s.Length > max ? s[..max] : s;

    void OnGroupMessage(ICollabLink link, string fid, string fn, string raw)
    {
        if (!_people.TryGetValue(fid, out var p))
        {
            if (!_inkOf.TryGetValue(fid, out var ink)) _inkOf[fid] = ink = Color.Parse(PeopleInks[_inkNext++ % PeopleInks.Length]);   // the same color again after a new connection
            p = new CollabPerson { Id = fid, Name = fn, Ink = ink, Via = link };
            _people[fid] = p;
            ShowPeople();
        }
        if (fn.Length > 0 && p.Name != fn) { p.Name = fn; ShowPeople(); }
        p.Via = link;
        _from = p;
        try { HandleCollab(raw); }
        finally { _from = null; }
    }

    void HandleCollab(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string Str(string key, int max) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String ? Clip(e.GetString(), max) : "";
            int Int(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : 0;
            var type = Str("t", 12);
            var p = _from!;
            switch (type)
            {
                case "hello":
                    if (Str("name", 40).Trim() is { Length: > 0 } hn) p.Name = hn;
                    if (!p.Greeted)
                    {
                        p.Greeted = true;
                        AddCollabLog(L("{0} ist verbunden.", PeerWho), system: true);
                        SendHello();   // so the newcomer learns about this person (each one answers once)
                    }
                    if (CollabPanel.Children.Count > 0) BuildCollabUi();
                    break;
                case "bye":
                    AddCollabLog(L("{0} hat die Gruppe verlassen.", PeerWho), system: true);
                    RemovePerson(p);
                    if (CollabPanel.Children.Count > 0) BuildCollabUi();
                    break;
                case "chat":
                    if (Str("text", 2000) is { Length: > 0 } text) { _peerTyping.IsVisible = false; AddChat(PeerWho, text, Str("mid", 32), mine: false, p.Ink); NotifyChat(text); }
                    break;
                case "nav":
                {
                    var book = Str("book", 8); var chapter = Int("chapter");
                    if (_books.All(b => b.Id != book) || chapter < 1 || chapter > 200) break;
                    int? verse = Int("verse") is > 0 and var vv ? vv : null;
                    p.Place = (book, chapter, verse);
                    ShowPeople();
                    if (_follow || (_leaderId == p.Id && _followLeader)) _ = FollowAsync(p.Place.Value);
                    RenderPeerLayer();
                    break;
                }
                case "verse":
                    ShowSharedVerse(Str("book", 8), Int("chapter"), Int("verse"), Str("text", 4000), Str("translation", 20));
                    break;
                case "marks":
                    if (root.TryGetProperty("items", out var markItems)) ShowSharedMarks(Str("book", 8), Int("chapter"), markItems);
                    break;
                case "doc":
                    ShowSharedDocument(Str("title", 200), Str("body", MaxSharedBody));
                    break;
                default:
                    if (!HandleCollabMedia(type, root) && !HandleCollabLive(type, root) && !HandleCollabShare(type, root)) HandleCollabExtras(type, root);
                    break;
            }
        }
        catch { /* a malformed message is ignored */ }
    }

    async Task FollowAsync((string Book, int Chapter, int? Verse) p)
    {
        // the chapter is already open: only the verse comes into view, the text is not loaded again
        if (p.Verse is int open && p.Book == _curBook && p.Chapter == _curChapter && _verseBoxes.ContainsKey(open)) { ScrollToVerse(open); return; }
        _collabSuppress = true;
        try { await GoToAsync(p.Book, p.Chapter, p.Verse); }
        finally { _collabSuppress = false; }
        // in the chapter already open the view keeps its place: the verse the other one clicked is brought to the top after that
        if (p.Verse is int v) Avalonia.Threading.Dispatcher.UIThread.Post(() => ScrollToVerse(v), Avalonia.Threading.DispatcherPriority.Background);
    }

    void ShowSharedVerse(string book, int chapter, int verse, string text, string translation)
    {
        if (_books.All(b => b.Id != book) || chapter < 1 || verse < 1) return;
        var who = PeerWho;
        _meeting.Add((DateTime.Now, who, $"{Label(new VerseRef(book, chapter, verse))}: {text}"));
        var r = new VerseRef(book, chapter, verse);
        var go = new Button { Content = L("Hingehen"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0) };
        go.Click += async (_, _) => await GoToAsync(book, chapter, verse);
        var keep = new Button { Content = L("Merken"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0) };
        keep.Click += (_, _) =>
        {
            _vault.AddMark(Mark.AnyTranslation, r, -1, -1, text, _settings.DefaultColor, L("Geteilt von {0}", who), null, AutoTags(r));
            keep.IsEnabled = false; keep.Content = L("Gemerkt ✓");
            LoadChapterMarks(); Paint(); RefreshCollection();
        };
        var card = Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = $"{who} · {Label(r)}{(translation.Length > 0 ? " (" + translation + ")" : "")}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = ScriptureFont, IsVisible = text.Length > 0 },
                new WrapPanel { Children = { go, keep } },
            },
        });
        AddCollabItem(card);
    }

    void ShowSharedDocument(string title, string body)
    {
        if (title.Trim().Length == 0) return;
        var save = new Button { Content = L("Als Kopie speichern"), Classes = { "tool" }, Width = double.NaN, Padding = new(10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) =>
        {
            // always a new document, tagged with its origin; nothing of the existing documents is touched. Its pictures come into the vault with it.
            KeepReceivedImages(body);
            _vault.AddDocument(title.Trim(), DocumentLinks.WithoutNumbers(body), null, false, [L("geteilt")]);
            save.IsEnabled = false; save.Content = L("Gespeichert ✓");
            RefreshDocs();
        };
        var card = Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = L("{0} sendet ein Dokument: „{1}“", PeerWho, title), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = Shorten(PlainPreview(body), 240), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                save,
            },
        });
        AddCollabItem(card);
    }
}

