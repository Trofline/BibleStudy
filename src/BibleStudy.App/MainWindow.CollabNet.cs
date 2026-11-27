using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Collab;
using BibleStudy.Core.Storage;

namespace BibleStudy.App;

/// <summary>
/// Studying together in the same network (home Wi-Fi, ZeroTier, Tailscale): the apps see each other by name like the sync does, a tap asks the
/// other person, who accepts or declines. No codes to swap. Only the chosen name is called out, and only once this page was opened.
/// Friends are remembered (their lasting id, last address and a shared secret): one tap connects, and with auto-accept no question is asked.
/// </summary>
public partial class MainWindow
{
    NetCollab? _net;
    readonly StackPanel _netList = new() { Spacing = 6 };
    readonly StackPanel _netAsk = new() { Spacing = 6 };
    readonly StackPanel _friendsBox = new() { Spacing = 6 };
    readonly TextBlock _netInfo = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

    static void Detach(Control c) { if (c.Parent is Panel p) p.Children.Remove(c); else if (c.Parent is Decorator d) d.Child = null; else if (c.Parent is ContentControl cc) cc.Content = null; }

    /// <summary>Starts being visible in the network (once there is a name) and keeps the name up to date.</summary>
    void EnsureNet()
    {
        if (!CollabConsented || _settings.CollabName.Length == 0) { RefreshNeighbors(); return; }
        if (_net is null)
        {
            Group();   // makes the lasting id
            _net = new NetCollab { Id = _settings.CollabId };
            _net.NeighborsChanged += () => OnUi(RefreshNeighbors);
            _net.Approve = ApproveAsync;
            _net.Incoming += link => OnUi(() => OpenNetLink(link));
        }
        _net.Name = _settings.CollabName;
        try { _net.Start(); _net.Announce(); }
        catch (Exception ex) { _netInfo.Text = L("Fehler: ") + ex.Message; }
        RefreshNeighbors();
    }

    void StopNet() { _net?.Stop(); _net = null; }

    CollabFriend? FriendWith(string id, string token) =>
        _settings.CollabFriends.FirstOrDefault(f => f.Id == id && f.Token.Length > 0 && token.Length == f.Token.Length
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(f.Token), Encoding.UTF8.GetBytes(token)));

    /// <summary>A friend with auto-accept is let in at once (proven by the shared secret, not only the name); everyone else is asked about.</summary>
    Task<bool> ApproveAsync(CollabAsk ask)
    {
        var friend = FriendWith(ask.Id, ask.Token);
        if (friend is { AutoAccept: true })
        {
            OnUi(() => Notify(L("Zusammen studieren"), L("{0} ist dazugekommen.", ask.Name), true, stay: TimeSpan.FromSeconds(5)));
            return Task.FromResult(true);
        }
        return AskOnUi(ask.Name, ask.CheckCode, friend is not null);
    }

    /// <summary>The card "In the same network": friends, the people seen, a field for an address, and the own address.</summary>
    Control BuildNetCard()
    {
        Detach(_netList); Detach(_netAsk); Detach(_netInfo); Detach(_friendsBox);
        var address = new TextBox { PlaceholderText = L("Adresse der anderen Person, z. B. 10.147.20.5"), MaxLength = 100 };
        var go = new Button { Content = L("Verbinden"), Margin = new(8, 0, 0, 0) };
        async Task ByAddress()
        {
            var text = (address.Text ?? "").Trim();
            if (text.Length == 0) return;
            EnsureNet();
            if (_net is null) { _netInfo.Text = L("Bitte gib zuerst deinen Namen ein."); return; }
            IPAddress? ip = IPAddress.TryParse(text, out var parsed) ? parsed : null;
            if (ip is null)
                try { ip = (await Dns.GetHostAddressesAsync(text)).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork); } catch { }
            if (ip is null) { _netInfo.Text = L("Diese Adresse kenne ich nicht."); return; }
            _net.AddAddress(ip);
            await AskNeighborAsync(ip, NetCollab.DataPort, text);
        }
        go.Click += async (_, _) => await ByAddress();
        address.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await ByAddress(); };
        DockPanel.SetDock(go, Dock.Right);
        var own = NetCollab.OwnAddresses();
        var stack = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = L("Im selben Netzwerk"), FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = L("Ohne Codes: Seid ihr im selben WLAN oder im selben ZeroTier- oder Tailscale-Netzwerk, seht ihr euch hier mit Namen. Tippe auf den Namen, die andere Person nimmt an. Beide müssen diese Seite geöffnet haben."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                _netAsk,
                _friendsBox,
                _netList,
                _netInfo,
                new Expander
                {
                    Header = L("Mit Adresse verbinden"),
                    Content = new StackPanel
                    {
                        Spacing = 6, Margin = new(0, 6, 0, 0),
                        Children =
                        {
                            new TextBlock { Text = L("Manche Netzwerke lassen das Suchen nicht zu. Dann gib die Adresse der anderen Person ein (bei ZeroTier steht sie in der ZeroTier-App unter „Managed IPs“)."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                            new DockPanel { Children = { go, address } },
                            new TextBlock { Text = own.Count > 0 ? L("Deine Adresse: {0}", string.Join(", ", own)) : L("Keine Netzwerkverbindung gefunden."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
                        },
                    },
                },
            },
        };
        EnsureNet();
        RefreshFriends();
        return Card(stack);
    }

    void RefreshNeighbors()
    {
        _netList.Children.Clear();
        if (_settings.CollabName.Length == 0)
        {
            _netList.Children.Add(new TextBlock { Text = L("Gib oben deinen Namen ein, dann sehen dich die anderen."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            return;
        }
        // friends have their own line above; people already in the group are not offered again
        var list = (_net?.Neighbors ?? []).Where(n => !_people.ContainsKey(n.Id) && _settings.CollabFriends.All(f => f.Id != n.Id)).ToList();
        if (list.Count == 0)
        {
            _netList.Children.Add(new TextBlock { Text = L("Suche nach anderen im Netzwerk …"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        }
        foreach (var n in list)
        {
            var b = new Button { Content = L("Mit {0} verbinden", n.Name), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += async (_, _) => await AskNeighborAsync(n.Address, n.Port, n.Name);
            _netList.Children.Add(b);
        }
        RefreshFriends();
    }

    /// <summary>The friends: one tap connects (to where they are seen now, else to the address used last); auto-accept; remove.</summary>
    void RefreshFriends()
    {
        _friendsBox.Children.Clear();
        if (_settings.CollabFriends.Count == 0) return;
        _friendsBox.Children.Add(new TextBlock { Text = L("Freunde"), FontWeight = FontWeight.SemiBold });
        _friendsBox.Children.Add(new TextBlock { Text = L("Seid ihr an verschiedenen Orten, tippt ihr beide auf „Übers Internet“: Dann seid ihr ohne Raum-Code verbunden."), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        foreach (var f in _settings.CollabFriends.ToList())
        {
            var friend = f;
            var seen = _net?.Neighbors.FirstOrDefault(n => n.Id == f.Id);
            var inGroup = _people.ContainsKey(f.Id);
            var connect = new Button { Content = inGroup ? L("Verbunden ✓") : L("Mit {0} verbinden", f.Name), Classes = { seen is not null && !inGroup ? "primary" : "" }, IsEnabled = !inGroup, Margin = new(0, 0, 8, 0) };
            connect.Click += async (_, _) => await ConnectFriendAsync(friend);
            var auto = new CheckBox { Content = L("Automatisch annehmen"), IsChecked = f.AutoAccept, Margin = new(0, 0, 8, 0) };
            ToolTip.SetTip(auto, L("Fragt {0} an, wirst du nicht mehr gefragt.", f.Name));
            auto.IsCheckedChanged += (_, _) => { friend.AutoAccept = auto.IsChecked == true; SaveSettings(); };
            var remove = new Button { Content = "✕", Classes = { "tool" } };
            ToolTip.SetTip(remove, L("Nicht mehr als Freund merken"));
            remove.Click += (_, _) => { _settings.CollabFriends.Remove(friend); SaveSettings(); RefreshFriends(); RefreshNeighbors(); };
            var state = new TextBlock { Text = seen is not null ? L("im Netzwerk") : f.Address.Length > 0 ? L("zuletzt unter {0}", f.Address) : "", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0) };
            var internet = new Button { Content = L("Übers Internet"), IsEnabled = !inGroup && f.Token.Length > 0, Margin = new(0, 0, 8, 0) };
            ToolTip.SetTip(internet, L("Verbindet euch über den Vermittler, auch an verschiedenen Orten, ohne Raum-Code. Ihr tippt beide hier auf „Übers Internet“."));
            internet.Click += async (_, _) => await ConnectFriendRoomAsync(friend);
            _friendsBox.Children.Add(new WrapPanel { Children = { connect, internet, auto, state, remove } });
        }
    }

    async Task ConnectFriendAsync(CollabFriend f)
    {
        EnsureNet();
        if (_net is null) { _netInfo.Text = L("Bitte gib zuerst deinen Namen ein."); return; }
        var seen = _net.Neighbors.FirstOrDefault(n => n.Id == f.Id);
        if (seen is not null) { await AskNeighborAsync(seen.Address, seen.Port, f.Name, f.Token); return; }
        if (!IPAddress.TryParse(f.Address, out var ip)) { _netInfo.Text = L("{0} ist gerade nicht im Netzwerk zu sehen.", f.Name); return; }
        _net.AddAddress(ip);
        await AskNeighborAsync(ip, f.Port > 0 ? f.Port : NetCollab.DataPort, f.Name, f.Token);
    }

    async Task AskNeighborAsync(IPAddress address, int port, string name, string token = "")
    {
        if (_net is null) return;
        _netInfo.Text = L("Frage {0} … Die andere Person muss die Anfrage annehmen.", name);
        try
        {
            var link = await _net.ConnectAsync(address, port, TimeSpan.FromSeconds(40), token);
            _netInfo.Text = "";
            if (_settings.CollabFriends.FirstOrDefault(f => f.Id == link.PeerId) is { } friend) { friend.Address = address.ToString(); friend.Port = port; SaveSettings(); }
            OpenNetLink(link);
        }
        catch (UnauthorizedAccessException) { _netInfo.Text = L("{0} hat die Anfrage abgelehnt.", name); }
        catch (OperationCanceledException) { _netInfo.Text = L("{0} hat die Anfrage nicht rechtzeitig angenommen.", name); }
        catch (Exception)
        {
            _netInfo.Text = L("{0} ist nicht erreichbar. Sind beide im selben Netzwerk und ist bei der anderen Person diese Seite geöffnet?", name)
                + (OperatingSystem.IsWindows() ? "\n" + L("Mit ZeroTier oder Tailscale unter Windows: Das neue Netzwerk gilt dort oft als „Öffentlich“, und die Firewall lässt dann nichts durch. Stelle es unter Einstellungen → Netzwerk und Internet → Eigenschaften auf „Privat“.") : "");
        }
    }

    /// <summary>Someone asks: a card on this page and a notice; no answer within 30 seconds counts as no.</summary>
    Task<bool> AskOnUi(string name, string check, bool friend = false)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnUi(() =>
        {
            var text = friend ? L("Dein Freund {0} möchte mit dir zusammen studieren.", name) : L("{0} möchte mit dir zusammen studieren.", name);
            if (CollabOpen) text += " " + L("Die Person kommt zu eurer Gruppe dazu.");
            var yes = new Button { Content = L("Annehmen"), Classes = { "primary" }, Margin = new(0, 0, 8, 0) };
            var no = new Button { Content = L("Ablehnen") };
            var card = Card(new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = L("Prüfzahl: {0}", check), Classes = { "caption" } },
                    new WrapPanel { Children = { yes, no } },
                },
            });
            void Done(bool ok) { if (answer.TrySetResult(ok)) HideNotice(); Detach(card); }
            yes.Click += (_, _) => Done(true);
            no.Click += (_, _) => Done(false);
            // in a running group the question comes on top of the chat, where it is seen; else on the card of the network
            if (CollabOpen) AddCollabItem(card, toEnd: true); else _netAsk.Children.Add(card);
            Notify(L("Zusammen studieren"), text, true, L("Annehmen"), () => Done(true), TimeSpan.FromSeconds(30));
            DispatcherTimer.RunOnce(() => Done(false), TimeSpan.FromSeconds(30));
        });
        return answer.Task;
    }

    void OpenNetLink(NetLink link)
    {
        Wire(link);
        link.Start();
        SetPanelOpen(true); SideTabs.SelectedIndex = CollabTab;
    }

    // ---- friends ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The connection over which a person can become a friend: only one that leads straight to them, so that the shared secret reaches
    /// nobody else: a direct one in the network, or a room on the relay where only the two of them are.
    /// </summary>
    ICollabLink? CanBefriend(CollabPerson p) => p.Via switch
    {
        NetLink nl when nl.PeerId == p.Id => nl,
        RelayLink rl when rl.Others == 1 && _people.Values.Count(x => x.Via == rl) == 1 => rl,
        _ => null,
    };

    /// <summary>Remembers a person as a friend; both sides keep the same secret, so it cannot be faked by the name. It also makes their own room on the relay.</summary>
    void MakeFriend(CollabPerson p, ICollabLink link)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        StoreFriend(p.Id, p.Name, (link as NetLink)?.RemoteAddress?.ToString() ?? "", 0, token);
        Group().SendTo(link, new { t = "friend", token, port = _net?.Port ?? NetCollab.DataPort, to = p.Id });
        AddCollabLog(L("{0} ist jetzt als Freund gemerkt.", p.Name), system: true);
    }

    void StoreFriend(string id, string name, string address, int port, string token)
    {
        var f = _settings.CollabFriends.FirstOrDefault(x => x.Id == id);
        if (f is null) { f = new CollabFriend { Id = id }; _settings.CollabFriends.Add(f); }
        f.Name = name; f.Token = token;
        if (address.Length > 0) f.Address = address;
        if (port > 0) f.Port = port;
        SaveSettings();
        RefreshFriends();
    }

    /// <summary>The other side of <see cref="MakeFriend"/>: only from a person connected straight to this program (see <see cref="CanBefriend"/>).</summary>
    void OnFriendMessage(string type, string token, int port, string to)
    {
        if (_from is not { } p || CanBefriend(p) is not { } link) return;
        if (to.Length > 0 && to != _settings.CollabId) return;
        var address = (link as NetLink)?.RemoteAddress?.ToString() ?? "";
        if (type == "friend")
        {
            if (token.Length is < 16 or > 64) return;
            StoreFriend(p.Id, p.Name, address, link is NetLink ? port : 0, token);
            Group().SendTo(link, new { t = "friendok", port = _net?.Port ?? NetCollab.DataPort, to = p.Id });
            AddCollabLog(L("{0} hat dich als Freund gemerkt. Ihr seid jetzt gegenseitig Freunde.", p.Name), system: true);
            ShowPeople();
        }
        else if (link is NetLink && _settings.CollabFriends.FirstOrDefault(f => f.Id == p.Id) is { } f && port is > 0 and < 65536) { f.Port = port; SaveSettings(); }
    }

    /// <summary>
    /// Meets a friend in your own room on the relay, without a code: both tap it, and whoever is first waits for the other. It costs the relay
    /// exactly as much as a room with a code, and nothing while nobody has it open.
    /// </summary>
    async Task ConnectFriendRoomAsync(CollabFriend f)
    {
        if (f.Token.Length == 0) return;
        if (_settings.CollabName.Length == 0) { _netInfo.Text = L("Bitte gib zuerst deinen Namen ein."); return; }
        _netInfo.Text = L("Verbinde …");
        try
        {
            var room = NewRoom(RelayLink.FriendCode(f.Token));
            await room.StartAsync();
            await WaitRoomAsync(room, _netInfo, TimeSpan.FromMinutes(10));
        }
        catch (Exception ex) { _netInfo.Text = RoomFailed(ex); }
    }
}
