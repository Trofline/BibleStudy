using System.Net;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BibleStudy.Core.Collab;

namespace BibleStudy.App;

/// <summary>
/// Studying together in the same network (home Wi-Fi, ZeroTier, Tailscale): the apps see each other by name like the sync does, a tap asks the
/// other person, who accepts or declines. No codes to swap. Only the chosen name is called out, and only while this page was opened.
/// </summary>
public partial class MainWindow
{
    NetCollab? _net;
    readonly StackPanel _netList = new() { Spacing = 6 };
    readonly StackPanel _netAsk = new() { Spacing = 6 };
    readonly TextBlock _netInfo = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

    static void Detach(Control c) { if (c.Parent is Panel p) p.Children.Remove(c); }

    /// <summary>Starts being visible in the network (once there is a name) and keeps the name up to date.</summary>
    void EnsureNet()
    {
        if (!CollabConsented || _settings.CollabName.Length == 0) { RefreshNeighbors(); return; }
        if (_net is null)
        {
            _net = new NetCollab();
            _net.NeighborsChanged += () => OnUi(RefreshNeighbors);
            _net.Approve = (name, _, check) => AskOnUi(name, check);
            _net.Incoming += link => OnUi(() => OpenNetLink(link));
        }
        _net.Name = _settings.CollabName;
        try { _net.Start(); _net.Announce(); }
        catch (Exception ex) { _netInfo.Text = L("Fehler: ") + ex.Message; }
        RefreshNeighbors();
    }

    void StopNet() { _net?.Stop(); _net = null; }

    /// <summary>The card "In the same network": the people seen, a field for an address, and the own address.</summary>
    Control BuildNetCard()
    {
        Detach(_netList); Detach(_netAsk); Detach(_netInfo);
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
        var list = _net?.Neighbors ?? [];
        if (list.Count == 0)
        {
            _netList.Children.Add(new TextBlock { Text = L("Suche nach anderen im Netzwerk …"), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var n in list)
        {
            var b = new Button { Content = L("Mit {0} verbinden", n.Name), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += async (_, _) => await AskNeighborAsync(n.Address, n.Port, n.Name);
            _netList.Children.Add(b);
        }
    }

    async Task AskNeighborAsync(IPAddress address, int port, string name)
    {
        if (_net is null) return;
        _netInfo.Text = L("Frage {0} … Die andere Person muss die Anfrage annehmen.", name);
        try
        {
            var link = await _net.ConnectAsync(address, port, TimeSpan.FromSeconds(40));
            _netInfo.Text = "";
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
    Task<bool> AskOnUi(string name, string check)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnUi(() =>
        {
            if (_peer?.IsOpen == true) { answer.TrySetResult(false); return; }
            var text = L("{0} möchte mit dir zusammen studieren.", name);
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
            void Done(bool ok) { if (answer.TrySetResult(ok)) HideNotice(); _netAsk.Children.Remove(card); }
            yes.Click += (_, _) => Done(true);
            no.Click += (_, _) => Done(false);
            _netAsk.Children.Add(card);
            Notify(L("Zusammen studieren"), text, true, L("Annehmen"), () => Done(true), TimeSpan.FromSeconds(30));
            DispatcherTimer.RunOnce(() => Done(false), TimeSpan.FromSeconds(30));
        });
        return answer.Task;
    }

    void OpenNetLink(NetLink link)
    {
        if (_peer?.IsOpen == true) { link.Dispose(); return; }
        _peerName = link.PeerName;
        Wire(link);
        link.Start();
        SetPanelOpen(true); SideTabs.SelectedIndex = CollabTab;
    }
}
