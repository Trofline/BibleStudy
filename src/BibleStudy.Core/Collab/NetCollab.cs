using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BibleStudy.Core.Collab;

/// <summary>A connection to the other person, whichever way it was made (codes over the internet, or the same network).</summary>
public interface ICollabLink : IDisposable
{
    event Action? Connected;
    event Action<string>? Closed;
    event Action<string>? Received;
    bool IsOpen { get; }
    void Send(string text);
}

/// <summary>Another person with the app open in the same network (home Wi-Fi, ZeroTier, Tailscale …).</summary>
public sealed record CollabNeighbor(string Id, string Name, IPAddress Address, int Port, DateTime Seen);

/// <summary>
/// Studying together in the same network, without codes: like the sync, the apps call out with a short broadcast (only a random id, the port and
/// the chosen name) and so see each other. A tap on a name asks the other person, who has to accept. The connection is direct (TCP) and encrypted:
/// both sides make a new key pair (ECDH), the shared key seals every message with AES-GCM. A short check number made from the key is shown on both
/// sides, so two people can make sure nobody sits in between.
/// </summary>
public sealed class NetCollab : IDisposable
{
    public const int DiscoveryPort = 47823;
    public const int DataPort = 47824;
    const string Tag = "BCOLLAB1";

    readonly ConcurrentDictionary<string, CollabNeighbor> _seen = new();
    readonly ConcurrentDictionary<string, IPAddress> _typed = new();   // addresses typed in by hand: asked directly, for networks without broadcast
    CancellationTokenSource? _cts;
    TcpListener? _tcp;
    UdpClient? _udp;

    public string Id { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
    public string Name { get; set; } = "";
    public int Port { get; private set; }
    public bool Running => _cts is not null;

    /// <summary>Someone asks to connect. Return true to accept (the call may wait for the person to decide).</summary>
    public Func<string, IPAddress, string, Task<bool>>? Approve;
    /// <summary>An accepted connection from someone who asked us.</summary>
    public event Action<NetLink>? Incoming;
    public event Action? NeighborsChanged;

    /// <summary>The others seen in the last seconds, by name.</summary>
    public IReadOnlyList<CollabNeighbor> Neighbors =>
        _seen.Values.Where(n => DateTime.UtcNow - n.Seen < TimeSpan.FromSeconds(12)).OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public void Start(int dataPort = DataPort, int discoveryPort = DiscoveryPort)
    {
        if (_cts is not null) return;
        _discovery = discoveryPort;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try { _tcp = new TcpListener(IPAddress.Any, dataPort); _tcp.Start(); }
        catch (SocketException) { _tcp = new TcpListener(IPAddress.Any, 0); _tcp.Start(); }
        Port = ((IPEndPoint)_tcp.LocalEndpoint).Port;
        _ = AcceptLoop(_tcp, ct);
        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
            _ = ListenLoop(_udp, ct);
        }
        catch (SocketException) { _udp?.Dispose(); _udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true }; }
        _ = AnnounceLoop(ct);
    }

    int _discovery = DiscoveryPort;

    public void Stop()
    {
        _cts?.Cancel(); _cts = null;
        try { _tcp?.Stop(); } catch { }
        _udp?.Dispose(); _udp = null; _tcp = null;
        _seen.Clear();
    }

    public void Dispose() => Stop();

    // ---- being seen ---------------------------------------------------------------------------------------------------

    byte[] Packet() => Encoding.UTF8.GetBytes($"{Tag}|{Id}|{Port}|{Name.Replace('|', ' ')}");

    async Task AnnounceLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Announce();
            var before = _seen.Count;
            foreach (var old in _seen.Values.Where(n => DateTime.UtcNow - n.Seen > TimeSpan.FromSeconds(12)).ToList()) _seen.TryRemove(old.Id, out _);
            if (_seen.Count != before) NeighborsChanged?.Invoke();
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Calls out once into every network of this device, and to the addresses typed in by hand.</summary>
    public void Announce()
    {
        var udp = _udp;
        if (udp is null) return;
        var data = Packet();
        var targets = new HashSet<IPAddress>(_typed.Values) { IPAddress.Broadcast };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask is null) continue;
                    var a = ua.Address.GetAddressBytes(); var m = ua.IPv4Mask.GetAddressBytes();
                    var b = new byte[4];
                    for (var i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                    targets.Add(new IPAddress(b));
                }
            }
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or SocketException) { }
        foreach (var t in targets)
            try { udp.Send(data, data.Length, new IPEndPoint(t, _discovery)); } catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
    }

    async Task ListenLoop(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; } continue; }
            try
            {
                var p = Encoding.UTF8.GetString(r.Buffer).Split('|', 4);
                if (p.Length < 4 || p[0] != Tag || p[1] == Id || p[1].Length > 32 || !int.TryParse(p[2], out var port) || port is < 1 or > 65535) continue;
                var name = p[3].Trim();
                if (name.Length == 0) continue;
                if (name.Length > 40) name = name[..40];
                var known = _seen.TryGetValue(p[1], out var before) && before.Name == name;
                _seen[p[1]] = new CollabNeighbor(p[1], name, r.RemoteEndPoint.Address, port, DateTime.UtcNow);
                if (!known)
                {
                    NeighborsChanged?.Invoke();
                    try { var d = Packet(); udp.Send(d, d.Length, new IPEndPoint(r.RemoteEndPoint.Address, _discovery)); } catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
                }
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException) { }
        }
    }

    /// <summary>An address typed in by hand (e.g. the ZeroTier address of the other person): it is called out to directly from now on.</summary>
    public void AddAddress(IPAddress address) { _typed[address.ToString()] = address; Announce(); }

    /// <summary>The addresses of this device, to tell the other person when the names do not show up.</summary>
    public static IReadOnlyList<string> OwnAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address) && !ua.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)) list.Add(ua.Address.ToString());
            }
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or SocketException) { }
        return list.Distinct().ToList();
    }

    // ---- asking -------------------------------------------------------------------------------------------------------

    /// <summary>Asks the other person and waits until they decide (at most <paramref name="wait"/>). Throws <see cref="UnauthorizedAccessException"/> when they say no.</summary>
    public async Task<NetLink> ConnectAsync(IPAddress address, int port, TimeSpan wait, CancellationToken ct = default)
    {
        var client = new TcpClient(address.AddressFamily) { NoDelay = true };
        try
        {
            using (var reach = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                reach.CancelAfter(TimeSpan.FromSeconds(8));
                await client.ConnectAsync(address, port, reach.Token);
            }
            var link = await NetLink.Open(client.GetStream(), client, true, ct);
            await link.SendRaw(JsonSerializer.Serialize(new { t = "ask", id = Id, name = Name }), ct);
            using var answer = CancellationTokenSource.CreateLinkedTokenSource(ct);
            answer.CancelAfter(wait);
            var reply = await link.ReceiveRaw(answer.Token) ?? throw new IOException("closed");
            using var doc = JsonDocument.Parse(reply);
            if (doc.RootElement.GetProperty("t").GetString() != "yes")
            {
                link.Dispose();
                throw new UnauthorizedAccessException(doc.RootElement.TryGetProperty("why", out var w) ? w.GetString() : "no");
            }
            link.PeerName = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            return link;
        }
        catch { client.Dispose(); throw; }
    }

    async Task AcceptLoop(TcpListener tcp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await tcp.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => Serve(c, ct), ct);
        }
    }

    async Task Serve(TcpClient c, CancellationToken ct)
    {
        c.NoDelay = true;
        NetLink? link = null;
        try
        {
            using (var hello = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                hello.CancelAfter(TimeSpan.FromSeconds(10));
                link = await NetLink.Open(c.GetStream(), c, false, hello.Token);
                var ask = await link.ReceiveRaw(hello.Token) ?? throw new IOException("closed");
                using var doc = JsonDocument.Parse(ask);
                if (doc.RootElement.GetProperty("t").GetString() != "ask") throw new InvalidDataException("ask");
                var name = (doc.RootElement.GetProperty("name").GetString() ?? "").Trim();
                if (name.Length > 40) name = name[..40];
                link.PeerName = name;
            }
            var remote = ((IPEndPoint)c.Client.RemoteEndPoint!).Address;
            var ok = Approve is { } approve && await approve(link.PeerName, remote, link.CheckCode);
            await link.SendRaw(ok ? JsonSerializer.Serialize(new { t = "yes", name = Name }) : JsonSerializer.Serialize(new { t = "no", why = "declined" }), ct);
            if (!ok) { link.Dispose(); return; }
            Incoming?.Invoke(link);
        }
        catch (Exception) { link?.Dispose(); c.Dispose(); }
    }
}

/// <summary>One encrypted connection in the same network. Every message is one sealed frame; a small ping every 15 seconds notices a lost partner.</summary>
public sealed class NetLink : ICollabLink
{
    const int MaxFrame = 4 * 1024 * 1024;
    readonly Stream _s;
    readonly TcpClient _client;
    readonly AesGcm _out, _in;
    readonly SemaphoreSlim _write = new(1, 1);
    readonly CancellationTokenSource _cts = new();
    ulong _no, _ni;
    int _closed;
    readonly object _queue = new();
    Task _tail = Task.CompletedTask;
    bool _started;

    public event Action? Connected;
    public event Action<string>? Closed;
    public event Action<string>? Received;

    public string PeerName { get; internal set; } = "";
    /// <summary>Four digits made from the shared key: the same on both sides when nobody sits in between.</summary>
    public string CheckCode { get; }
    public bool IsOpen => _started && _closed == 0;

    NetLink(Stream s, TcpClient client, byte[] key, byte[] transcript, bool client1)
    {
        _s = s; _client = client;
        byte[] Derive(string label) => HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(label).Concat(transcript).ToArray());
        var c2s = new AesGcm(Derive("c2s"), 16); var s2c = new AesGcm(Derive("s2c"), 16);
        (_out, _in) = client1 ? (c2s, s2c) : (s2c, c2s);
        CheckCode = (BitConverter.ToUInt32(Derive("check"), 0) % 10000).ToString("0000");
    }

    internal static async Task<NetLink> Open(Stream s, TcpClient client, bool isClient, CancellationToken ct)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var mine = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
        async Task Write(byte[] b) { await s.WriteAsync(BitConverter.GetBytes((ushort)b.Length), ct); await s.WriteAsync(b, ct); await s.FlushAsync(ct); }
        async Task<byte[]> Read()
        {
            var h = new byte[2]; await s.ReadExactlyAsync(h, ct);
            var n = BitConverter.ToUInt16(h, 0);
            if (n is < 32 or > 1024) throw new InvalidDataException("key");
            var b = new byte[n]; await s.ReadExactlyAsync(b, ct); return b;
        }
        await s.WriteAsync("BSC1"u8.ToArray(), ct);
        var magic = new byte[4]; await s.ReadExactlyAsync(magic, ct);
        if (!magic.AsSpan().SequenceEqual("BSC1"u8)) throw new InvalidDataException("magic");
        byte[] theirs;
        if (isClient) { await Write(mine); theirs = await Read(); } else { theirs = await Read(); await Write(mine); }
        using var other = ECDiffieHellman.Create();
        other.ImportSubjectPublicKeyInfo(theirs, out _);
        var key = ecdh.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256);
        var transcript = isClient ? mine.Concat(theirs).ToArray() : theirs.Concat(mine).ToArray();
        return new NetLink(s, client, key, SHA256.HashData(transcript), isClient);
    }

    static byte[] Nonce(ulong n) { var b = new byte[12]; BitConverter.GetBytes(n).CopyTo(b, 4); return b; }

    internal async Task SendRaw(string text, CancellationToken ct)
    {
        var plain = Encoding.UTF8.GetBytes(text);
        var frame = new byte[4 + plain.Length + 16];
        await _write.WaitAsync(ct);
        try
        {
            BitConverter.GetBytes(plain.Length).CopyTo(frame, 0);
            _out.Encrypt(Nonce(_no++), plain, frame.AsSpan(4, plain.Length), frame.AsSpan(4 + plain.Length, 16));
            await _s.WriteAsync(frame, ct);
            await _s.FlushAsync(ct);
        }
        finally { _write.Release(); }
    }

    internal async Task<string?> ReceiveRaw(CancellationToken ct)
    {
        var head = new byte[4];
        if (await _s.ReadAtLeastAsync(head, 4, false, ct) < 4) return null;
        var len = BitConverter.ToInt32(head, 0);
        if (len < 0 || len > MaxFrame) throw new InvalidDataException("frame");
        var cipher = new byte[len]; var tag = new byte[16];
        await _s.ReadExactlyAsync(cipher, ct); await _s.ReadExactlyAsync(tag, ct);
        var plain = new byte[len];
        _in.Decrypt(Nonce(_ni++), cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Starts reading; call it after the events are wired. <see cref="Connected"/> comes at once.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _ = ReadLoop(_cts.Token);
        _ = PingLoop(_cts.Token);
        Connected?.Invoke();
    }

    async Task ReadLoop(CancellationToken ct)
    {
        var why = "closed";
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var m = await ReceiveRaw(ct);
                if (m is null) break;
                if (m.Length == 0) continue;   // ping
                Received?.Invoke(m);
            }
        }
        catch (Exception ex) { why = ex.Message; }
        End(why);
    }

    async Task PingLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); await SendRaw("", ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { End(ex.Message); return; }
        }
    }

    public void Send(string text)
    {
        if (!IsOpen) return;
        // one after the other, in the order they were sent
        lock (_queue) _tail = _tail.ContinueWith(async _ => { try { await SendRaw(text, _cts.Token); } catch (Exception ex) { End(ex.Message); } }, TaskScheduler.Default).Unwrap();
    }

    void End(string why)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _cts.Cancel();
        try { _client.Dispose(); } catch { }
        Closed?.Invoke(why);
    }

    public void Dispose() => End("left");
}
