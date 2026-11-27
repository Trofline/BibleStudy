using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BibleStudy.Core.Storage;

namespace BibleStudy.Core.Sync;

/// <summary>Another device of the same group that was seen in the network.</summary>
public sealed record SyncPeer(string Id, string Name, IPAddress Address, int Port, DateTime Seen);

/// <summary>The outcome of one comparison. Changed = vaults that got something new here, Sent = vaults handed over, Error = what went wrong (null = all well),
/// Bibles = whole translations that came over or went over (see <see cref="ISyncBibles"/>).</summary>
public sealed record SyncReport(string PeerName, int Changed, int Sent, string? Error, int Bibles = 0);

/// <summary>
/// Compares the vaults of the user's own devices in the same network, without an account and without a server in between.
/// Devices find each other by a short broadcast (it carries only a tag that is made from the pairing code and a port), connect directly and talk over
/// an encrypted channel whose key is made from the pairing code: a device without the code cannot take part.
/// Each side sends a consistent copy of a vault that differs and the other side merges it (<see cref="VaultStore.MergeFrom"/>: nothing is deleted or
/// overwritten, where both differ both are kept). The fingerprint seen after the last comparison is remembered, so an unchanged vault is not sent again.
/// </summary>
public sealed class LanSync : IDisposable
{
    public const int DiscoveryPort = 47821;
    /// <summary>The port of the direct connection (another one when it is taken). It is in the pairing address, so that a device that cannot hear the call-out (an iPhone may not receive broadcasts) still finds the other.</summary>
    public const int DataPort = 47822;
    /// <summary>How often the devices in reach are asked even without a change here.</summary>
    public static TimeSpan PollEvery = TimeSpan.FromSeconds(20);
    const byte MsgList = 1, MsgGet = 2, MsgData = 3, MsgPut = 4, MsgOk = 5, MsgNone = 6, MsgGetBible = 7, MsgBible = 8, MsgPutBible = 9;
    const int MaxFrame = 512 * 1024 * 1024;

    readonly ISyncVaults _vaults;
    readonly SyncConfig _cfg;
    readonly string _cfgPath;
    readonly string _tmp;
    readonly ConcurrentDictionary<string, SyncPeer> _peers = new();
    readonly ConcurrentDictionary<string, DateTime> _tried = new();
    /// <summary>Devices and addresses whose last comparison failed, and when (the regular look leaves them out for two minutes).</summary>
    readonly ConcurrentDictionary<string, DateTime> _failed = new();
    readonly SemaphoreSlim _mergeGate = new(1, 1);
    CancellationTokenSource? _cts;
    TcpListener? _tcp;
    UdpClient? _udp;

    public LanSync(ISyncVaults vaults, SyncConfig cfg, string configPath, string tempDir)
    {
        _vaults = vaults; _cfg = cfg; _cfgPath = configPath; _tmp = tempDir;
    }

    /// <summary>The imported translations of this device (null = none go over). Only sent to a device that lists its own, so an older version is never asked.</summary>
    public ISyncBibles? Bibles { get; set; }

    /// <summary>Whole translations that came from another device (their names).</summary>
    public event Action<IReadOnlyList<string>>? BiblesReceived;

    ISyncBibles? ActiveBibles => _cfg.Bibles ? Bibles : null;

    public int Port { get; private set; }
    public bool Running => _cts is not null;
    public IReadOnlyCollection<SyncPeer> Peers => [.. _peers.Values.Where(p => p.Seen > DateTime.UtcNow.AddMinutes(-2))];

    /// <summary>Vaults that got something new from another device (the program shows it).</summary>
    public event Action<IReadOnlyList<string>>? VaultsChanged;
    /// <summary>A comparison with a device is over.</summary>
    public event Action<SyncReport>? Finished;
    /// <summary>A device with the same code reached this one (its name): it compares from its side.</summary>
    public event Action<string>? Contacted;

    // ---- start and stop -------------------------------------------------------------------------------------------------

    public void Start()
    {
        if (_cts is not null || _cfg.KeyBytes is null) return;
        try { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* leftovers of a crash */ }
        Directory.CreateDirectory(_tmp);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try { _tcp = new TcpListener(IPAddress.Any, DataPort); _tcp.Start(); }
        catch (SocketException) { _tcp = new TcpListener(IPAddress.Any, 0); _tcp.Start(); }
        Port = ((IPEndPoint)_tcp.LocalEndpoint).Port;
        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            _ = Task.Run(() => ListenLoop(_udp, ct));
        }
        catch (SocketException) { _udp?.Dispose(); _udp = null; }   // no discovery: the other device can still find us
        _ = Task.Run(() => AcceptLoop(_tcp, ct));
        _ = Task.Run(() => AnnounceLoop(ct));
        _ = Task.Run(() => HostLoop(ct));
        _ = Task.Run(() => WatchLoop(ct));
    }

    public void Stop()
    {
        var cts = _cts; _cts = null;
        if (cts is null) return;
        cts.Cancel();
        try { _tcp?.Stop(); } catch (SocketException) { }
        try { _udp?.Dispose(); } catch (ObjectDisposedException) { }
        _tcp = null; _udp = null; _peers.Clear();
    }

    public void Dispose() => Stop();

    // ---- finding each other ---------------------------------------------------------------------------------------------

    string Tag() => Convert.ToHexString(HMACSHA256.HashData(_cfg.KeyBytes!, "tag"u8.ToArray()), 0, 8);

    byte[] Packet() => Encoding.UTF8.GetBytes($"BSYNC1|{Tag()}|{Port}|{_cfg.DeviceId}|{_cfg.DeviceName.Replace('|', ' ')}");

    async Task AnnounceLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Announce();
            try { await Task.Delay(TimeSpan.FromSeconds(25), ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Calls out to the network once (all the networks the device is in).</summary>
    public void Announce()
    {
        var udp = _udp;
        if (udp is null || _cfg.KeyBytes is null) return;
        var data = Packet();
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
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
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or SocketException) { /* the plain broadcast has to do */ }
        foreach (var t in targets)
            try { udp.Send(data, data.Length, new IPEndPoint(t, DiscoveryPort)); } catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
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
            try { OnPacket(r); } catch (Exception ex) when (ex is FormatException or ArgumentException or SocketException) { /* not ours */ }
        }
    }

    void OnPacket(UdpReceiveResult r)
    {
        if (_cfg.KeyBytes is null) return;
        var parts = Encoding.UTF8.GetString(r.Buffer).Split('|');
        if (parts.Length < 5 || parts[0] != "BSYNC1" || parts[1] != Tag() || parts[3] == _cfg.DeviceId) return;
        if (!int.TryParse(parts[2], out var port) || port is < 1 or > 65535) return;
        var known = _peers.ContainsKey(parts[3]);
        var peer = new SyncPeer(parts[3], parts[4], r.RemoteEndPoint.Address, port, DateTime.UtcNow);
        _peers[peer.Id] = peer;
        if (!known) try { var d = Packet(); _udp?.Send(d, d.Length, new IPEndPoint(peer.Address, DiscoveryPort)); } catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }   // so it finds us at once
        // of two devices the one with the higher id starts the comparison; every two minutes at the most
        if (!_cfg.Auto || string.CompareOrdinal(_cfg.DeviceId, peer.Id) <= 0) return;
        if (_tried.TryGetValue(peer.Id, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(2)) return;
        _tried[peer.Id] = DateTime.UtcNow;
        _ = Task.Run(() => SyncWithAsync(peer));
    }

    // ---- at once after a change -------------------------------------------------------------------------------------------
    readonly Dictionary<string, DateTime> _stamps = new(StringComparer.OrdinalIgnoreCase);
    long _changedAt;   // ticks of the first change that is not sent yet (0 = none)
    int _running;

    /// <summary>Something was changed here (or the app came to the front): the devices that can be reached get it in a moment.</summary>
    public void Nudge() => Interlocked.Exchange(ref _changedAt, DateTime.UtcNow.AddSeconds(-10).Ticks);

    /// <summary>Looks every few seconds whether a vault was written; three seconds after the last change the devices in reach get it (not only every two minutes).</summary>
    async Task WatchLoop(CancellationToken ct)
    {
        ReadStamps();
        var lastRound = DateTime.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); } catch (OperationCanceledException) { return; }
            if (!_cfg.Auto) continue;
            if (ReadStamps()) Interlocked.CompareExchange(ref _changedAt, DateTime.UtcNow.Ticks, 0);
            var since = Interlocked.Read(ref _changedAt);
            var settled = since != 0 && DateTime.UtcNow - new DateTime(since, DateTimeKind.Utc) >= TimeSpan.FromSeconds(1);
            // besides after a change here: a short look every 20 seconds (only sends what differs), so a change over there
            // that could not be sent at once (the other app was closed, the line broke) still arrives soon
            if (!settled && DateTime.UtcNow - lastRound < PollEvery) continue;
            lastRound = DateTime.UtcNow;
            await SyncKnownAsync(poll: !settled);
        }
    }

    /// <summary>True when a vault was written since the last look.</summary>
    bool ReadStamps()
    {
        var changed = false;
        lock (_stamps)
        try
        {
            foreach (var n in _vaults.Names())
            {
                var s = _vaults.Stamp(n);
                if (_stamps.TryGetValue(n, out var old) && old != s) changed = true;
                _stamps[n] = s;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return changed;
    }

    /// <summary>Compares at once with every device that was seen or whose address is known (one round at a time). The program calls it before it closes too.</summary>
    public Task SyncKnownAsync() => SyncKnownAsync(false);

    /// <param name="poll">the regular look: devices that failed in the last two minutes are left out (a closed iPhone is not asked every 20 seconds)</param>
    async Task SyncKnownAsync(bool poll)
    {
        if (poll) { if (Interlocked.Exchange(ref _running, 1) == 1) return; }   // a regular look: one round at a time is enough
        else while (Interlocked.Exchange(ref _running, 1) == 1) await Task.Delay(100);   // asked for (a change, the app goes away): after the round that runs, one more with what is new
        try
        {
            Interlocked.Exchange(ref _changedAt, 0);
            var peers = Peers.ToList();
            bool Resting(string id) => poll && _failed.TryGetValue(id, out var at) && DateTime.UtcNow - at < TimeSpan.FromMinutes(2);
            foreach (var p in peers) { if (Resting(p.Id)) continue; _tried[p.Id] = DateTime.UtcNow; await SyncWithAsync(p); }
            foreach (var h in _cfg.HostList())
            {
                var at = h.LastIndexOf(':');
                if (at < 0 || !IPAddress.TryParse(h[..at], out var ip) || !int.TryParse(h[(at + 1)..], out var port) || peers.Any(p => p.Address.Equals(ip)) || Resting("host:" + h)) continue;
                _tried["host:" + h] = DateTime.UtcNow;
                await SyncWithAsync(new SyncPeer("host:" + h, h, ip, port, DateTime.UtcNow));
            }
            await CloudAndReport();
            ReadStamps();   // what the comparison itself wrote is no change to send
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    /// <summary>Tries the addresses of devices that were seen or given before (the pairing address carries one): for a device that does not hear the call-out.</summary>
    async Task HostLoop(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            if (_cfg.Auto)
                foreach (var h in _cfg.HostList())
                {
                    var at = h.LastIndexOf(':');
                    if (at < 0 || !IPAddress.TryParse(h[..at], out var ip) || !int.TryParse(h[(at + 1)..], out var port)) continue;
                    if (Peers.Any(p => p.Address.Equals(ip))) continue;   // found by the call-out: that one takes care
                    if (_tried.TryGetValue("host:" + h, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(2)) continue;
                    _tried["host:" + h] = DateTime.UtcNow;
                    await SyncWithAsync(new SyncPeer("host:" + h, h, ip, port, DateTime.UtcNow));
                }
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>The addresses of this device in the networks it is in (for the pairing address).</summary>
    public static IReadOnlyList<string> LocalAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address) && !ua.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)) list.Add(ua.Address.ToString());
            }
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or SocketException) { }
        return list;
    }

    // ---- being asked --------------------------------------------------------------------------------------------------

    async Task AcceptLoop(TcpListener tcp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await tcp.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException) { return; }
            _ = Task.Run(() => Serve(c, ct));
        }
    }

    async Task Serve(TcpClient c, CancellationToken ct)
    {
        using (c)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                using var stream = c.GetStream();
                using var ch = await Channel.Open(stream, _cfg.KeyBytes!, false, limit.Token);
                var asker = ""; var askerName = "";
                int got = 0, gave = 0, bibles = 0;
                var gotBibles = new List<string>();
                while (await ch.Receive(limit.Token) is { } msg)
                {
                    switch (msg.Type)
                    {
                        case MsgList:
                            if (ReadInfo(msg.Data) is { } asked) asker = asked.Id;
                            if (c.Client.RemoteEndPoint is IPEndPoint remote && ReadInfo(msg.Data) is { Port: > 0 } who) _cfg.RememberHost(remote.Address, who.Port, _cfgPath);   // so that it can be found again without the call-out
                            await ch.Send(MsgList, ListPayload(), limit.Token);
                            // vaults that are the same on both sides need no exchange, but the base for the next merge with that device must be this state
                            // (they may have become the same through a third device or the cloud folder): else a later deletion would come back
                            if (asker.Length > 0 && ReadInfo(msg.Data) is { } same)
                                foreach (var n in _vaults.Names())
                                    if (same.Vaults.TryGetValue(n, out var theirFp) && await Task.Run(() => _vaults.Fingerprint(n), limit.Token) is var fp && fp == theirFp)
                                        await Task.Run(() => EnsureBase(asker, n, fp), limit.Token);
                            // translations both have: a deletion on one side later is not undone from the other
                            if (asker.Length > 0 && ActiveBibles is { } here && ReadInfo(msg.Data) is { Bibles: { } theirBibles })
                                foreach (var n in here.Names().Intersect(theirBibles, StringComparer.Ordinal)) MarkBible(asker, n);
                            if (ReadInfo(msg.Data) is { Name.Length: > 0 } caller)
                            {
                                askerName = caller.Name;
                                // this side was reached: it counts as a comparison here too (the reminder after some days without one looks at it)
                                _cfg.LastTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture); _cfg.LastPeer = caller.Name;
                                _cfg.Save(_cfgPath);
                                Contacted?.Invoke(caller.Name);
                            }
                            break;
                        case MsgGet:
                        {
                            var name = Encoding.UTF8.GetString(msg.Data);
                            if (!MarkLogic.IsValidVaultName(name) || !_vaults.Names().Contains(name, StringComparer.OrdinalIgnoreCase)) { await ch.Send(MsgNone, [], limit.Token); break; }
                            await ch.Send(MsgData, await Snapshot(name), limit.Token);
                            gave++;
                            break;
                        }
                        case MsgGetBible:
                        {
                            var name = Encoding.UTF8.GetString(msg.Data);
                            var packed = ActiveBibles is { } bs && bs.Names().Contains(name, StringComparer.Ordinal) ? await bs.ExportAsync(name) : null;
                            if (packed is null) { await ch.Send(MsgNone, [], limit.Token); break; }
                            await ch.Send(MsgBible, packed, limit.Token);
                            if (asker.Length > 0) MarkBible(asker, name);
                            bibles++;
                            break;
                        }
                        case MsgPutBible:
                        {
                            var len = BitConverter.ToInt32(msg.Data, 0);
                            var name = Encoding.UTF8.GetString(msg.Data, 4, len);
                            if (ActiveBibles is not { } bs || name.Trim().Length == 0) { await ch.Send(MsgNone, [], limit.Token); break; }
                            var took = !bs.Names().Contains(name, StringComparer.Ordinal) && await bs.ImportAsync(name, msg.Data[(4 + len)..]);
                            if (asker.Length > 0) MarkBible(asker, name);
                            if (took) { bibles++; gotBibles.Add(name); }
                            await ch.Send(took ? MsgOk : MsgNone, [], limit.Token);
                            break;
                        }
                        case MsgPut:
                        {
                            var len = BitConverter.ToInt32(msg.Data, 0);
                            var name = Encoding.UTF8.GetString(msg.Data, 4, len);
                            if (!MarkLogic.IsValidVaultName(name)) { await ch.Send(MsgNone, [], limit.Token); break; }
                            var file = TempFile();
                            try
                            {
                                await File.WriteAllBytesAsync(file, msg.Data.AsMemory(4 + len).ToArray(), limit.Token);
                                if (await MergeInto(name, file, asker)) got++;
                                var fpNow = await Task.Run(() => _vaults.Fingerprint(name), limit.Token);
                                if (asker.Length > 0) await Task.Run(() => SaveBase(asker, name, fpNow), limit.Token);
                                await ch.Send(MsgOk, Encoding.UTF8.GetBytes(fpNow), limit.Token);
                            }
                            finally { TryDelete(file); }
                            break;
                        }
                    }
                }
                // the other device closed the line: its round is over, here as well (an iPhone is only ever asked, so it learns of the end only this way)
                if (gotBibles.Count > 0) BiblesReceived?.Invoke(gotBibles);
                if (bibles > 0) _cfg.Save(_cfgPath);
                if (askerName.Length > 0) Finished?.Invoke(new SyncReport(askerName, got, gave, null, bibles));
            }
            catch (Exception ex) when (ex is IOException or CryptographicException or OperationCanceledException or SocketException or InvalidDataException or ObjectDisposedException or ArgumentException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            { /* a device with another code, or the line broke: nothing happens */ }
        }
    }

    // ---- asking ----------------------------------------------------------------------------------------------------------

    /// <summary>Compares now with every device seen in the last minutes (also those that would have been left to the other side).</summary>
    public async Task<IReadOnlyList<SyncReport>> SyncAllNowAsync()
    {
        Announce();
        try { await Task.Delay(1500); } catch (OperationCanceledException) { }
        var list = new List<SyncReport>();
        var peers = Peers.ToList();
        foreach (var p in peers) { _tried[p.Id] = DateTime.UtcNow; list.Add(await SyncWithAsync(p)); }
        // also the remembered addresses: an iPhone never answers the call-out, it is only reached there
        foreach (var h in _cfg.HostList())
        {
            var at = h.LastIndexOf(':');
            if (at < 0 || !IPAddress.TryParse(h[..at], out var ip) || !int.TryParse(h[(at + 1)..], out var port) || peers.Any(p => p.Address.Equals(ip))) continue;
            _tried["host:" + h] = DateTime.UtcNow;
            list.Add(await SyncWithAsync(new SyncPeer("host:" + h, h, ip, port, DateTime.UtcNow)));
        }
        if (await CloudRoundAsync() is { } cloud) { list.Add(cloud); _cloudGreeted = true; Finished?.Invoke(cloud); }
        return list;
    }

    /// <summary>One comparison with one device (it can be called for an address from a test too).</summary>
    public async Task<SyncReport> SyncWithAsync(SyncPeer peer)
    {
        SyncReport report;
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = limit.Token;
        int changed = 0, sent = 0, bibles = 0;
        try
        {
            using var tcp = new TcpClient();
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(6));
                await tcp.ConnectAsync(peer.Address, peer.Port, connect.Token);
            }
            using var stream = tcp.GetStream();
            using var ch = await Channel.Open(stream, _cfg.KeyBytes!, true, ct);
            await ch.Send(MsgList, ListPayload(), ct);
            var reply = await ch.Receive(ct) ?? throw new InvalidDataException("no answer");
            var info = ReadList(reply.Data);
            var theirs = info.Vaults;
            if (info.Id.Length > 0) peer = peer with { Id = info.Id, Name = info.Name.Length > 0 ? info.Name : peer.Name };
            if (info.Port > 0) _cfg.RememberHost(peer.Address, info.Port, _cfgPath);
            var mine = _vaults.Names();
            foreach (var name in mine.Concat(theirs.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                if (!MarkLogic.IsValidVaultName(name)) continue;
                var have = mine.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                var theirName = theirs.Keys.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                var myFp = have is null ? "" : await Task.Run(() => _vaults.Fingerprint(have), ct);
                var theirFp = theirName is null ? "" : theirs[theirName];
                var key = peer.Id + "|" + (have ?? name);
                if (myFp == theirFp)
                {
                    Remember(key, myFp, theirFp);
                    if (have is not null && peer.Id.Length > 0) await Task.Run(() => EnsureBase(peer.Id, have, myFp), ct);   // see Serve: the base must be this state
                    continue;
                }
                string? done; lock (_cfg.Done) _cfg.Done.TryGetValue(key, out done);
                if (done == myFp + "|" + theirFp) continue;   // neither side changed since the last comparison

                if (theirName is not null)
                {
                    await ch.Send(MsgGet, Encoding.UTF8.GetBytes(theirName), ct);
                    var data = await ch.Receive(ct) ?? throw new InvalidDataException("no answer");
                    if (data.Type == MsgData)
                    {
                        var file = TempFile();
                        try
                        {
                            await File.WriteAllBytesAsync(file, data.Data, ct);
                            if (await MergeInto(have ?? theirName, file, peer.Id)) changed++;
                        }
                        finally { TryDelete(file); }
                    }
                }
                var nowMine = have ?? (theirName is null ? null : _vaults.Names().FirstOrDefault(n => string.Equals(n, theirName, StringComparison.OrdinalIgnoreCase)));
                var theirNow = theirFp;
                if (have is not null)   // what we had goes over too (not when the vault was just made from theirs)
                {
                    var file = TempFile();
                    try
                    {
                        await Task.Run(() => _vaults.Snapshot(have, file), ct);
                        var nameBytes = Encoding.UTF8.GetBytes(theirName ?? have);
                        var payload = new byte[4 + nameBytes.Length + new FileInfo(file).Length];
                        BitConverter.GetBytes(nameBytes.Length).CopyTo(payload, 0);
                        nameBytes.CopyTo(payload, 4);
                        using (var fs = File.OpenRead(file)) await fs.ReadExactlyAsync(payload.AsMemory(4 + nameBytes.Length), ct);
                        await ch.Send(MsgPut, payload, ct);
                        var ok = await ch.Receive(ct) ?? throw new InvalidDataException("no answer");
                        if (ok.Type == MsgOk) { theirNow = Encoding.UTF8.GetString(ok.Data); sent++; }
                    }
                    finally { TryDelete(file); }
                }
                if (nowMine is not null)
                {
                    var fpNow = await Task.Run(() => _vaults.Fingerprint(nowMine), ct);
                    Remember(key, fpNow, theirNow);
                    if (peer.Id.Length > 0) await Task.Run(() => SaveBase(peer.Id, nowMine, fpNow), ct);
                }
            }
            // the notes first, then the translations (they are bigger, and a line that breaks there leaves the notes done)
            if (ActiveBibles is { } here && info.Bibles is { } theirBibles && peer.Id.Length > 0)
                bibles = await ExchangeBibles(ch, here, peer.Id, theirBibles, ct);
            _cfg.LastTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture); _cfg.LastPeer = peer.Name;
            _cfg.Save(_cfgPath);
            report = new SyncReport(peer.Name, changed, sent, null, bibles);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or OperationCanceledException or SocketException or InvalidDataException or ObjectDisposedException or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            report = new SyncReport(peer.Name, changed, sent, ex is CryptographicException ? "code" : ex.Message, bibles);
        }
        if (report.Error is null) _failed.TryRemove(peer.Id, out _); else _failed[peer.Id] = DateTime.UtcNow;
        Finished?.Invoke(report);
        return report;
    }

    // ---- whole translations -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Translations that only one side has go to the other one: fetched or handed over as packed .txt. One that was on both sides once (or went over)
    /// is not brought back after it was deleted on one of them; one of the same name on both sides is left as it is.
    /// </summary>
    async Task<int> ExchangeBibles(Channel ch, ISyncBibles here, string peerId, IReadOnlyList<string> theirs, CancellationToken ct)
    {
        var count = 0;
        var mine = here.Names();
        var got = new List<string>();
        foreach (var n in mine.Intersect(theirs, StringComparer.Ordinal)) MarkBible(peerId, n);
        foreach (var name in theirs.Except(mine, StringComparer.Ordinal).ToList())
        {
            if (name.Trim().Length == 0 || KnowsBible(peerId, name)) continue;
            await ch.Send(MsgGetBible, Encoding.UTF8.GetBytes(name), ct);
            var data = await ch.Receive(ct) ?? throw new InvalidDataException("no answer");
            if (data.Type != MsgBible) continue;
            if (!here.Names().Contains(name, StringComparer.Ordinal) && await here.ImportAsync(name, data.Data)) { count++; got.Add(name); }
            MarkBible(peerId, name);
        }
        foreach (var name in mine.Except(theirs, StringComparer.Ordinal).ToList())
        {
            if (KnowsBible(peerId, name) || await here.ExportAsync(name) is not { } packed) continue;
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var payload = new byte[4 + nameBytes.Length + packed.Length];
            BitConverter.GetBytes(nameBytes.Length).CopyTo(payload, 0);
            nameBytes.CopyTo(payload, 4);
            packed.CopyTo(payload, 4 + nameBytes.Length);
            await ch.Send(MsgPutBible, payload, ct);
            var ok = await ch.Receive(ct) ?? throw new InvalidDataException("no answer");
            if (ok.Type == MsgOk) count++;
            MarkBible(peerId, name);
        }
        if (got.Count > 0) BiblesReceived?.Invoke(got);
        return count;
    }

    static string BibleKey(string peerId, string name) => "bible|" + peerId + "|" + name;

    void MarkBible(string peerId, string name) { lock (_cfg.Done) _cfg.Done[BibleKey(peerId, name)] = "1"; }

    bool KnowsBible(string peerId, string name) { lock (_cfg.Done) return _cfg.Done.ContainsKey(BibleKey(peerId, name)); }

    // ---- small things ----------------------------------------------------------------------------------------------------

    void Remember(string key, string mine, string theirs)
    {
        lock (_cfg.Done) _cfg.Done[key] = mine + "|" + theirs;
    }

    /// <summary>The state of a vault after the last comparison with one device: the third copy that shows which side changed or deleted what.</summary>
    string BasePath(string peerId, string vault) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_cfgPath))!, "sync-base", string.Concat(peerId.Where(char.IsLetterOrDigit)), vault.ToLowerInvariant() + ".db");

    void SaveBase(string peerId, string vault, string? fingerprint = null)
    {
        // the fingerprint of what the base holds is noted with it (taken before the copy: a change while copying makes it differ, so the
        // base is written anew next time, never the other way round); else EnsureBase could keep an older base that only looked current
        fingerprint ??= _vaults.Fingerprint(vault);
        var path = BasePath(peerId, vault);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var fresh = path + "." + Guid.NewGuid().ToString("N") + ".new";   // two comparisons at once must not write the same file
        _vaults.Snapshot(vault, fresh);
        File.Move(fresh, path, true);
        lock (_cfg.Done) _cfg.Done[BaseKey(peerId, vault)] = fingerprint;
    }

    static string BaseKey(string peerId, string vault) => "base|" + peerId + "|" + vault.ToLowerInvariant();

    /// <summary>
    /// True when a device already has this state of the vault (its fingerprint): a comparison with it ended with this state on both sides.
    /// Then there is nothing to send, whatever files were written meanwhile (opening a vault, the settings of a window).
    /// </summary>
    public bool IsOnADevice(string vault, string fingerprint)
    {
        lock (_cfg.Done)
            foreach (var (key, value) in _cfg.Done)
            {
                if (key.StartsWith("cloud|", StringComparison.Ordinal) || key.StartsWith("bible|", StringComparison.Ordinal) || !key.EndsWith("|" + vault, StringComparison.OrdinalIgnoreCase)) continue;
                // "device|vault" holds "mine|theirs" of the last comparison, "base|device|vault" the state both had
                if (key.StartsWith("base|", StringComparison.Ordinal) ? value == fingerprint : value.StartsWith(fingerprint + "|", StringComparison.Ordinal)) return true;
            }
        return false;
    }

    /// <summary>Makes the base for a device the state of a vault that is the same on both sides; written only when that state changed since the last time.</summary>
    void EnsureBase(string peerId, string vault, string fp)
    {
        string? had; lock (_cfg.Done) _cfg.Done.TryGetValue(BaseKey(peerId, vault), out had);
        if (had == fp && File.Exists(BasePath(peerId, vault))) return;
        SaveBase(peerId, vault, fp);
    }

    // ---- the cloud folder ------------------------------------------------------------------------------------------------

    /// <summary>A folder that a cloud service keeps the same on all devices (null = none): a mailbox, so the devices need not be on at the same time.</summary>
    public ICloudFolder? Cloud { get; set; }

    /// <summary>
    /// One look into the cloud folder: what the other devices put there since the last look is merged here (as with a device in the network), then this
    /// device's vaults go there when they changed. The base for the next merge with a device is its own copy that was merged now: what it changes or
    /// deletes later is taken over, what was added here meanwhile stays (it never had it).
    /// </summary>
    public async Task<SyncReport?> CloudRoundAsync()
    {
        if (Cloud is not { } cloud) return null;
        var me = _cfg.DeviceId;
        int changed = 0, sent = 0;
        string? who = null;
        // the automatic round and "sync now" may come at the same time: one look into the folder at a time (they would write the same files)
        await _cloudGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_tmp);
            foreach (var dev in await cloud.DevicesAsync())
            {
                if (dev == me || dev.Length < 8 || !dev.All(char.IsLetterOrDigit)) continue;
                var files = await cloud.FilesAsync(dev);
                // the newest name file (a device that was renamed has an old one too); a strange file there must not stop the whole round
                var name = files.Where(f => f.Name.StartsWith(CloudSync.NamePrefix, StringComparison.Ordinal) && f.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.Modified).FirstOrDefault()?.Name;
                var devName = name is null || name.Length <= CloudSync.NamePrefix.Length + 4 ? dev[..8] : name[CloudSync.NamePrefix.Length..^4];
                foreach (var f in files)
                {
                    if (!f.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase)) continue;
                    var vault = f.Name[..^3];
                    if (!MarkLogic.IsValidVaultName(vault)) continue;
                    var key = "cloud|" + dev + "|" + vault.ToLowerInvariant();
                    var stamp = f.Modified.UtcTicks + "/" + f.Size;
                    string? seen; lock (_cfg.Done) _cfg.Done.TryGetValue(key, out seen);
                    if (seen == stamp) continue;
                    var file = TempFile();
                    try
                    {
                        await cloud.DownloadAsync(dev, f.Name, file);
                        var target = _vaults.Names().FirstOrDefault(n => string.Equals(n, vault, StringComparison.OrdinalIgnoreCase)) ?? vault;
                        // their copy becomes the base for the next one: taken before the merge, which may change the file it gets
                        var basePath = BasePath(dev, target);
                        Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
                        File.Copy(file, basePath + ".new", true);
                        if (await MergeInto(target, file, dev)) changed++;
                        File.Move(basePath + ".new", basePath, true);
                        lock (_cfg.Done) _cfg.Done[key] = stamp;
                        who = devName;
                    }
                    finally { TryDelete(file); }
                }
            }
            // this device's vaults, when they are not there yet as they are now
            var files0 = await cloud.FilesAsync(me);
            var nameFile = CloudSync.NameFile(_cfg.DeviceName);
            if (!files0.Any(f => f.Name == nameFile))
            {
                var empty = TempFile();
                try { await File.WriteAllBytesAsync(empty, []); await cloud.UploadAsync(me, nameFile, empty); } finally { TryDelete(empty); }
            }
            foreach (var name in _vaults.Names())
            {
                var fp = await Task.Run(() => _vaults.Fingerprint(name));
                var key = "cloud|mine|" + name.ToLowerInvariant();
                string? done; lock (_cfg.Done) _cfg.Done.TryGetValue(key, out done);
                if (done == fp && files0.Any(f => string.Equals(f.Name, name + ".db", StringComparison.OrdinalIgnoreCase))) continue;
                var file = TempFile();
                try
                {
                    await Task.Run(() => _vaults.Snapshot(name, file));
                    await cloud.UploadAsync(me, name + ".db", file);
                    lock (_cfg.Done) _cfg.Done[key] = fp;
                    sent++;
                }
                finally { TryDelete(file); }
            }
            if (changed + sent > 0) { _cfg.LastTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture); _cfg.LastPeer = who ?? cloud.Label; }
            _cfg.Save(_cfgPath);
            ReadStamps();   // what the merge wrote is no change to send again
            return new SyncReport(who ?? cloud.Label, changed, sent, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or Microsoft.Data.Sqlite.SqliteException)
        {
            return new SyncReport(cloud.Label, changed, sent, ex.Message);
        }
        finally { _cloudGate.Release(); }
    }

    readonly SemaphoreSlim _cloudGate = new(1, 1);

    bool _cloudGreeted;

    /// <summary>The cloud round of the automatic sync: reported when something went over or failed (and once at the start, so the first contact shows).</summary>
    async Task CloudAndReport()
    {
        if (await CloudRoundAsync() is not { } r) return;
        if (r.Changed + r.Sent > 0 || r.Error is not null || !_cloudGreeted) { _cloudGreeted = true; Finished?.Invoke(r); }
    }

    /// <summary>Forgets what was taken from and put into the cloud folder (another folder was chosen).</summary>
    public void ForgetCloud()
    {
        lock (_cfg.Done) foreach (var k in _cfg.Done.Keys.Where(k => k.StartsWith("cloud|", StringComparison.Ordinal)).ToList()) _cfg.Done.Remove(k);
        _cfg.Save(_cfgPath);
    }

    string TempFile() => Path.Combine(_tmp, Guid.NewGuid().ToString("N") + ".db");

    static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* removed with the folder at the next start */ }
    }

    /// <summary>Merges a copy into a vault, one merge at a time. True when the vault is different afterwards.</summary>
    async Task<bool> MergeInto(string name, string file, string peerId)
    {
        await _mergeGate.WaitAsync();
        try
        {
            var before = _vaults.Names().Contains(name, StringComparer.OrdinalIgnoreCase) ? await Task.Run(() => _vaults.Fingerprint(name)) : "";
            // a copy of the base: the merge opens it, and an open file could not be replaced afterwards
            var basePath = peerId.Length > 0 ? BasePath(peerId, name) : null;
            string? baseCopy = null;
            if (basePath is not null && File.Exists(basePath)) { baseCopy = TempFile(); File.Copy(basePath, baseCopy); }
            try { await _vaults.MergeAsync(name, file, baseCopy); }
            finally { if (baseCopy is not null) TryDelete(baseCopy); }
            var after = await Task.Run(() => _vaults.Fingerprint(name));
            if (before == after) return false;
            VaultsChanged?.Invoke([name]);
            return true;
        }
        finally { _mergeGate.Release(); }
    }

    async Task<byte[]> Snapshot(string name)
    {
        var file = TempFile();
        try
        {
            await Task.Run(() => _vaults.Snapshot(name, file));
            return await File.ReadAllBytesAsync(file);
        }
        finally { TryDelete(file); }
    }

    byte[] ListPayload()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("id", _cfg.DeviceId); w.WriteString("name", _cfg.DeviceName); w.WriteNumber("port", Port);
            w.WriteStartArray("v");
            foreach (var n in _vaults.Names()) { w.WriteStartObject(); w.WriteString("n", n); w.WriteString("f", _vaults.Fingerprint(n)); w.WriteEndObject(); }
            w.WriteEndArray();
            if (ActiveBibles is { } bibles)
            {
                w.WriteStartArray("b");
                foreach (var n in bibles.Names()) w.WriteStringValue(n);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <param name="Bibles">the imported translations of the other device; null = it sends none (an older version, or switched off)</param>
    sealed record Info(string Id, string Name, int Port, Dictionary<string, string> Vaults, IReadOnlyList<string>? Bibles);

    static Info? ReadInfo(byte[] data)
    {
        try { return ReadList(data); } catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    static Info ReadList(byte[] data)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        string Str(string n) => root.TryGetProperty(n, out var s) && s.GetString() is { } t ? t : "";
        var port = root.TryGetProperty("port", out var p) && p.TryGetInt32(out var pn) ? pn : 0;
        if (doc.RootElement.TryGetProperty("v", out var v))
            foreach (var e in v.EnumerateArray())
                if (e.TryGetProperty("n", out var n) && e.TryGetProperty("f", out var f) && n.GetString() is { } name && f.GetString() is { } fp) d[name] = fp;
        List<string>? bibles = null;
        if (root.TryGetProperty("b", out var b) && b.ValueKind == JsonValueKind.Array)
            bibles = [.. b.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Distinct(StringComparer.Ordinal)];
        return new Info(Str("id"), Str("name"), port, d, bibles);
    }

    // ---- the encrypted line -----------------------------------------------------------------------------------------------

    sealed record Message(byte Type, byte[] Data);

    /// <summary>Both sides send a random number; from them and the pairing code two keys are made (one per direction). Every message is sealed with AES-GCM: with a wrong code the first message already fails.</summary>
    sealed class Channel : IDisposable
    {
        readonly Stream _s;
        readonly AesGcm _out, _in;
        ulong _no, _ni;

        Channel(Stream s, AesGcm o, AesGcm i) { _s = s; _out = o; _in = i; }

        public static async Task<Channel> Open(Stream s, byte[] key, bool client, CancellationToken ct)
        {
            var mine = RandomNumberGenerator.GetBytes(16);
            var theirs = new byte[16];
            if (client) { await s.WriteAsync(mine, ct); await s.ReadExactlyAsync(theirs, ct); }
            else { await s.ReadExactlyAsync(theirs, ct); await s.WriteAsync(mine, ct); }
            var nc = client ? mine : theirs; var ns = client ? theirs : mine;
            byte[] Derive(string label) => HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(label).Concat(nc).Concat(ns).ToArray());
            var c2s = new AesGcm(Derive("c2s"), 16); var s2c = new AesGcm(Derive("s2c"), 16);
            return client ? new Channel(s, c2s, s2c) : new Channel(s, s2c, c2s);
        }

        static byte[] Nonce(ulong n) { var b = new byte[12]; BitConverter.GetBytes(n).CopyTo(b, 4); return b; }

        public async Task Send(byte type, byte[] data, CancellationToken ct)
        {
            var plain = new byte[1 + data.Length];
            plain[0] = type; data.CopyTo(plain, 1);
            var frame = new byte[4 + plain.Length + 16];
            BitConverter.GetBytes(plain.Length).CopyTo(frame, 0);
            _out.Encrypt(Nonce(_no++), plain, frame.AsSpan(4, plain.Length), frame.AsSpan(4 + plain.Length, 16));
            await _s.WriteAsync(frame, ct);
            await _s.FlushAsync(ct);
        }

        public async Task<Message?> Receive(CancellationToken ct)
        {
            var head = new byte[4];
            if (await _s.ReadAtLeastAsync(head, 4, false, ct) < 4) return null;
            var len = BitConverter.ToInt32(head, 0);
            if (len < 1 || len > MaxFrame) throw new InvalidDataException("frame");
            var cipher = new byte[len]; var tag = new byte[16];
            await _s.ReadExactlyAsync(cipher, ct); await _s.ReadExactlyAsync(tag, ct);
            var plain = new byte[len];
            _in.Decrypt(Nonce(_ni++), cipher, tag, plain);
            return new Message(plain[0], plain[1..]);
        }

        public void Dispose() { _out.Dispose(); _in.Dispose(); }
    }
}
