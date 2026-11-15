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

/// <summary>The outcome of one comparison. Peers = devices reached, Changed = vaults that got something new here, Sent = vaults handed over, Error = what went wrong (null = all well).</summary>
public sealed record SyncReport(string PeerName, int Changed, int Sent, string? Error);

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
    const byte MsgList = 1, MsgGet = 2, MsgData = 3, MsgPut = 4, MsgOk = 5, MsgNone = 6;
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
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { return; }
            if (!_cfg.Auto) continue;
            if (ReadStamps()) Interlocked.CompareExchange(ref _changedAt, DateTime.UtcNow.Ticks, 0);
            var since = Interlocked.Read(ref _changedAt);
            var settled = since != 0 && DateTime.UtcNow - new DateTime(since, DateTimeKind.Utc) >= TimeSpan.FromSeconds(3);
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
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
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
                var asker = "";
                while (await ch.Receive(limit.Token) is { } msg)
                {
                    switch (msg.Type)
                    {
                        case MsgList:
                            if (ReadInfo(msg.Data) is { } asked) asker = asked.Id;
                            if (c.Client.RemoteEndPoint is IPEndPoint remote && ReadInfo(msg.Data) is { Port: > 0 } who) _cfg.RememberHost(remote.Address, who.Port, _cfgPath);   // so that it can be found again without the call-out
                            await ch.Send(MsgList, ListPayload(), limit.Token);
                            if (ReadInfo(msg.Data) is { Name.Length: > 0 } caller)
                            {
                                // this side was reached: it counts as a comparison here too (the reminder after some days without one looks at it)
                                _cfg.LastTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); _cfg.LastPeer = caller.Name;
                                _cfg.Save(_cfgPath);
                                Contacted?.Invoke(caller.Name);
                            }
                            break;
                        case MsgGet:
                        {
                            var name = Encoding.UTF8.GetString(msg.Data);
                            if (!MarkLogic.IsValidVaultName(name) || !_vaults.Names().Contains(name, StringComparer.OrdinalIgnoreCase)) { await ch.Send(MsgNone, [], limit.Token); break; }
                            await ch.Send(MsgData, await Snapshot(name), limit.Token);
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
                                await MergeInto(name, file, asker);
                                if (asker.Length > 0) await Task.Run(() => SaveBase(asker, name), limit.Token);
                                await ch.Send(MsgOk, Encoding.UTF8.GetBytes(await Task.Run(() => _vaults.Fingerprint(name), limit.Token)), limit.Token);
                            }
                            finally { TryDelete(file); }
                            break;
                        }
                    }
                }
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
        return list;
    }

    /// <summary>One comparison with one device (it can be called for an address from a test too).</summary>
    public async Task<SyncReport> SyncWithAsync(SyncPeer peer)
    {
        SyncReport report;
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = limit.Token;
        int changed = 0, sent = 0;
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
                if (myFp == theirFp) { Remember(key, myFp, theirFp); continue; }
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
                    Remember(key, await Task.Run(() => _vaults.Fingerprint(nowMine), ct), theirNow);
                    if (peer.Id.Length > 0) await Task.Run(() => SaveBase(peer.Id, nowMine), ct);
                }
            }
            _cfg.LastTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); _cfg.LastPeer = peer.Name;
            _cfg.Save(_cfgPath);
            report = new SyncReport(peer.Name, changed, sent, null);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or OperationCanceledException or SocketException or InvalidDataException or ObjectDisposedException or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            report = new SyncReport(peer.Name, changed, sent, ex is CryptographicException ? "code" : ex.Message);
        }
        if (report.Error is null) _failed.TryRemove(peer.Id, out _); else _failed[peer.Id] = DateTime.UtcNow;
        Finished?.Invoke(report);
        return report;
    }

    // ---- small things ----------------------------------------------------------------------------------------------------

    void Remember(string key, string mine, string theirs)
    {
        lock (_cfg.Done) _cfg.Done[key] = mine + "|" + theirs;
    }

    /// <summary>The state of a vault after the last comparison with one device: the third copy that shows which side changed or deleted what.</summary>
    string BasePath(string peerId, string vault) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_cfgPath))!, "sync-base", string.Concat(peerId.Where(char.IsLetterOrDigit)), vault.ToLowerInvariant() + ".db");

    void SaveBase(string peerId, string vault)
    {
        var path = BasePath(peerId, vault);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var fresh = path + ".new";
        _vaults.Snapshot(vault, fresh);
        File.Move(fresh, path, true);
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
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    sealed record Info(string Id, string Name, int Port, Dictionary<string, string> Vaults);

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
        return new Info(Str("id"), Str("name"), port, d);
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
