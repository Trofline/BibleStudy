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
/// networks with two codes. Several people form a group: each program passes on what it gets (see <see cref="CollabGroup"/>).
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
    CollabPeer? _pendingCode;      // the code connection while the codes are swapped
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
            _group.Message += (link, fid, fn, raw) => OnUi(() => OnGroupMessage(link, fid, fn, raw));
        }
        _group.MyName = _settings.CollabName;
        return _group;
    }

    // ---- before the connection ------------------------------------------------------------------------------------------

    void BuildCollabStart()
    {
        CollabPanel.Children.Add(new TextBlock
        {
            Text = L("Studiere die Bibel mit einer anderen Person, direkt von Programm zu Programm: verschlüsselt, ohne Server, ohne Konto und kostenlos."),
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap,
        });
        CollabPanel.Children.Add(Card(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = L("Was geteilt wird"), FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = L("Nur, was du hier ausdrücklich sendest: Chat, einzelne Verse und Kopien von Dokumenten, auf Wunsch deine aktuelle Bibelstelle. Dein Vault bleibt bei dir. Die andere Person sieht deine Internetadresse (wie bei jeder Direktverbindung), deshalb verbinde dich nur mit Menschen, denen du vertraust."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
            },
        }));

        var name = new TextBox { PlaceholderText = L("Dein Name (wird der anderen Person gezeigt)"), Text = _settings.CollabName, MaxLength = 40 };
        name.LostFocus += (_, _) => { _settings.CollabName = (name.Text ?? "").Trim(); SaveSettings(); EnsureNet(); };
        CollabPanel.Children.Add(name);
        CollabPanel.Children.Add(BuildNetCard());

        var area = new StackPanel { Spacing = 8 };
        var host = new Button { Content = L("Einladung erstellen"), Classes = { "primary" }, Margin = new(0, 0, 8, 0) };
        var join = new Button { Content = L("Einladung annehmen") };
        host.Click += async (_, _) => { if (CollabNameOk(name)) await HostFlowAsync(area); };
        join.Click += (_, _) => { if (CollabNameOk(name)) JoinFlow(area); };
        // the codes are for two people in different networks: they swap two codes, e.g. by messenger
        CollabPanel.Children.Add(new Expander
        {
            Header = L("Mit Codes (Beta, andere Netze)"), Margin = new(0, 8, 0, 0),
            Content = new StackPanel
            {
                Spacing = 8, Margin = new(0, 6, 0, 0),
                Children =
                {
                    new TextBlock { Text = L("Seid ihr nicht im selben Netzwerk, tauscht ihr zwei kurze Codes aus, zum Beispiel per Messenger."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = L("Beta: Über verschiedene Netzwerke klappt die Verbindung nicht immer. Getestet und zuverlässig ist der Weg im selben Netzwerk. Seid ihr weit voneinander entfernt, tretet beide demselben ZeroTier-Netzwerk bei (kostenlos) und verbindet euch oben unter „Im selben Netzwerk“."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#B54708")) },
                    new WrapPanel { Children = { host, join } },
                    area,
                    BuildRelayBox(),
                },
            },
        });
    }

    /// <summary>The optional own relay server (TURN): used when no direct connection is possible. Collapsed, because most people never need it.</summary>
    Control BuildRelayBox()
    {
        var url = new TextBox { PlaceholderText = L("Adresse, z. B. turn:server.example:3478"), Text = _settings.RelayUrl, MaxLength = 200 };
        var user = new TextBox { PlaceholderText = L("Benutzername"), Text = _settings.RelayUser, MaxLength = 100 };
        var pass = new TextBox { PlaceholderText = L("Passwort"), Text = _settings.RelayPassword, MaxLength = 200, PasswordChar = '•' };
        void Save() { _settings.RelayUrl = (url.Text ?? "").Trim(); _settings.RelayUser = (user.Text ?? "").Trim(); _settings.RelayPassword = pass.Text ?? ""; SaveSettings(); }
        url.LostFocus += (_, _) => Save(); user.LostFocus += (_, _) => Save(); pass.LostFocus += (_, _) => Save();
        return new Expander
        {
            Header = L("Eigener Relay-Server (optional)"), Margin = new(0, 8, 0, 0), IsExpanded = _settings.RelayUrl.Length > 0,
            Content = new StackPanel
            {
                Spacing = 6, Margin = new(0, 6, 0, 0),
                Children =
                {
                    new TextBlock { Text = L("Nur nötig, wenn die direkte Verbindung nicht klappt. Du brauchst dafür einen eigenen TURN-Server (zum Beispiel coturn) oder einen Zugang bei einem Anbieter. Die Daten bleiben verschlüsselt, der Server leitet sie nur weiter. Am besten tragen beide Seiten dasselbe ein. Das Passwort wird unverschlüsselt in den Einstellungen gespeichert."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                    url, user, pass,
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

    CollabPeer NewPeer()
    {
        _pendingCode?.Dispose();
        var peer = new CollabPeer(RelayServer.Parse(_settings.RelayUrl, _settings.RelayUser, _settings.RelayPassword));
        _pendingCode = peer;
        Wire(peer);
        return peer;
    }

    /// <summary>Adds a connection to the group once it stands (codes or same network alike); more people can join the same way.</summary>
    void Wire(ICollabLink link)
    {
        var wasOpen = false;
        link.Connected += () => OnUi(() =>
        {
            if (link is CollabPeer && _pendingCode != link) return;
            if (_pendingCode == link) _pendingCode = null;
            wasOpen = true;
            var first = !CollabOpen;
            Group().Add(link);
            InitCollabLive();
            if (first) ResetCollabLive();
            SendHello();
            BuildCollabUi();
        });
        // Only a connection that really stood ends anything. Before that the library reports "failed/closed" on its own (e.g. while the
        // other person still has to paste the code); rebuilding the page then wiped the half-finished invitation after ~15 seconds.
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
        var box = new TextBox { Text = code, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 110, FontSize = 11 };
        var copy = new Button { Content = L("Code kopieren"), HorizontalAlignment = HorizontalAlignment.Left };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync(code); copy.Content = L("Kopiert ✓"); }
        };
        return new StackPanel { Spacing = 6, Children = { box, copy } };
    }

    async Task HostFlowAsync(StackPanel area)
    {
        area.Children.Clear();
        var status = new TextBlock { Text = L("Einladung wird erstellt … (kann einige Sekunden dauern)"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        area.Children.Add(status);
        var peer = NewPeer();
        try
        {
            var code = await peer.CreateInvitationAsync();
            area.Children.Clear();
            area.Children.Add(new TextBlock { Text = L("1. Schicke diesen Code der anderen Person:"), FontWeight = FontWeight.SemiBold });
            area.Children.Add(CodeBox(code));
            area.Children.Add(new TextBlock { Text = L("2. Füge hier ihren Antwortcode ein:"), FontWeight = FontWeight.SemiBold, Margin = new(0, 6, 0, 0) });
            var answer = new TextBox { PlaceholderText = "BS1-…", TextWrapping = TextWrapping.Wrap, MaxHeight = 110, FontSize = 11 };
            var connect = new Button { Content = L("Verbinden"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
            var info = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
            connect.Click += async (_, _) =>
            {
                try
                {
                    peer.CompleteWithAnswer(answer.Text ?? "");
                    connect.IsEnabled = false; info.Text = L("Verbinde … ");
                    await WaitConnectedAsync(peer, info);
                    connect.IsEnabled = true;
                }
                catch (InvalidOperationException ex) when (ex.Message == "used") { info.Text = L("Diese Einladung wurde schon benutzt. Erstelle oben eine neue Einladung und tauscht die Codes noch einmal aus."); connect.IsEnabled = true; }
                catch { info.Text = L("Dieser Code passt nicht. Es muss der Antwortcode sein, der mit „BS1-“ beginnt."); connect.IsEnabled = true; }
            };
            area.Children.Add(answer); area.Children.Add(connect); area.Children.Add(info);
        }
        catch (Exception ex) { status.Text = L("Fehler: ") + ex.Message; }
    }

    void JoinFlow(StackPanel area)
    {
        area.Children.Clear();
        area.Children.Add(new TextBlock { Text = L("1. Füge hier den Einladungscode ein, den du bekommen hast:"), FontWeight = FontWeight.SemiBold });
        var invitation = new TextBox { PlaceholderText = "BS1-…", TextWrapping = TextWrapping.Wrap, MaxHeight = 110, FontSize = 11 };
        var next = new Button { Content = L("Weiter"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
        var result = new StackPanel { Spacing = 8 };
        next.Click += async (_, _) =>
        {
            result.Children.Clear();
            var info = new TextBlock { Text = L("Antwort wird erstellt … (kann einige Sekunden dauern)"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
            result.Children.Add(info);
            var peer = NewPeer();
            try
            {
                var code = await peer.AcceptInvitationAsync(invitation.Text ?? "");
                result.Children.Clear();
                result.Children.Add(new TextBlock { Text = L("2. Schicke diesen Antwortcode zurück:"), FontWeight = FontWeight.SemiBold });
                result.Children.Add(CodeBox(code));
                var waiting = new TextBlock { Text = L("Sobald die andere Person ihn eingefügt hat, seid ihr verbunden."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
                result.Children.Add(waiting);
                _ = WaitConnectedAsync(peer, waiting, 120);
            }
            catch (FormatException) { info.Text = L("Dieser Code passt nicht. Es muss der Einladungscode sein, der mit „BS1-“ beginnt."); }
            catch (Exception ex) { info.Text = L("Fehler: ") + ex.Message; }
        };
        area.Children.Add(invitation); area.Children.Add(next); area.Children.Add(result);
    }

    /// <summary>Waits for the connection and shows the state every second, so it is clear that something happens; on failure it says why it may be.</summary>
    async Task WaitConnectedAsync(CollabPeer peer, TextBlock info, int seconds = 45)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!peer.IsOpen && clock.Elapsed.TotalSeconds < seconds)
        {
            if (_pendingCode != peer && !peer.IsOpen) return;
            info.Text = L("Verbinde … {0} s (Status: {1})", (int)clock.Elapsed.TotalSeconds, peer.Status);
            await Task.Delay(500);
        }
        if (_pendingCode == peer && !peer.IsOpen)
            info.Text = L("Die Verbindung kam nicht zustande. Häufige Gründe: ein VPN, eine strenge Firewall oder ein Firmennetz. Versucht es im Heimnetz oder ohne VPN noch einmal.")
                + "\n" + L("Unter Windows beim ersten Start „Zugriff zulassen“ für private Netzwerke wählen; am Mac unter Systemeinstellungen → Datenschutz & Sicherheit → Lokales Netzwerk die App erlauben.")
                + "\n" + L("Klappt es nicht? Beide Seiten müssen im selben Netzwerk sein. Sonst hilft ein kostenloses privates Netzwerk wie ZeroTier oder Tailscale (beide installieren und beitreten, dann neue Codes erstellen) oder ein eigener Relay-Server.")
                + (OperatingSystem.IsWindows() ? "\n" + L("Mit ZeroTier oder Tailscale unter Windows: Das neue Netzwerk gilt dort oft als „Öffentlich“, und die Firewall lässt dann nichts durch. Stelle es unter Einstellungen → Netzwerk und Internet → Eigenschaften auf „Privat“.") : "")
                + "\n" + L("Technische Angaben: {0}", peer.Diagnose());
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
            item.Click += (_, _) =>
            {
                // one message of the connection holds at most about 256 KB: a longer document is sent shortened, and the sender is told so
                var shortened = doc.Body.Length > MaxSentBody;
                var sent = DocumentLinks.WithoutNumbers(doc.Body);   // the numbers of the links mean something only in this vault
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
        _collabSuppress = true;
        try { await GoToAsync(p.Book, p.Chapter, p.Verse); }
        finally { _collabSuppress = false; }
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
            // always a new document, tagged with its origin; nothing of the existing documents is touched
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
                new TextBlock { Text = Shorten(body.ReplaceLineEndings(" ").Trim(), 240), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                save,
            },
        });
        AddCollabItem(card);
    }
}

