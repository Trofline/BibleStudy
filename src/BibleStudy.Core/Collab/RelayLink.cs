using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BibleStudy.Core.Collab;

/// <summary>
/// Studying together across networks: up to five programs connect to a small relay server (an ordinary secure web connection on port 443,
/// which passes nearly every router, mobile network and firewall) and meet in a "room" named by a short code. The relay only passes bytes on:
/// everything is sealed end to end with keys only the programs know. For the group the whole room is one connection (see <see cref="CollabGroup"/>).
///
/// The keys: every program makes a key pair (ECDH) and a random "sender key". With each other person it agrees a pair key (ECDH secret mixed
/// with the room code) and hands over its sender key sealed with it. What a program sends to the room is sealed once with its sender key, so a
/// message goes to the relay only once, however many people are there. Whoever does not know the code (the relay, someone guessing rooms)
/// cannot open the sealed sender key. The relay sees only a hash of the code. A four-digit check number, the same for everyone in the room
/// when nobody sits in between, is made from all keys of the room.
///
/// Messages are gathered for a moment (<see cref="Gather"/>) and sent together: a voice sends a piece every 40 ms, and the relay counts messages.
/// The relay protocol is described in relay/src/index.js in the repository.
/// </summary>
public sealed class RelayLink : ICollabLink
{
    /// <summary>The letters of a room code: no 0/O and 1/I/L, so that it can be read out and typed without mistakes. 31 letters: 8 of them are almost 40 bits.</summary>
    public const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    public const int CodeLength = 8;
    public const int MaxPeople = 5;
    const int Chunk = 200 * 1024;                  // the relay takes messages up to 256 KB
    const int MaxMessage = 16 * 1024 * 1024;       // a whole batch, put together from its pieces
    static readonly TimeSpan Gather = TimeSpan.FromMilliseconds(100);
    const byte KeyFrame = 0x01, SenderKeyFrame = 0x02, DataFrame = 0x10;
    const byte More = 1, First = 2;
    static readonly byte[] SenderKeyAad = [SenderKeyFrame];

    sealed class Member
    {
        public byte[]? Public;
        public AesGcm? Pair, Recv;
        public bool KeySent, SenderKeySent;
        public ulong Last;
        public MemoryStream? Parts;
        public readonly Dictionary<string, string> People = [];   // who wrote through this place (id → name): they may be several, passed on from their network
    }

    readonly Uri _server;
    readonly string _code;
    readonly byte[] _codeKey;
    readonly ClientWebSocket _ws = new();
    readonly CancellationTokenSource _cts = new();
    readonly SemaphoreSlim _write = new(1, 1);
    readonly SemaphoreSlim _signal = new(0);
    readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly object _gate = new();
    readonly Dictionary<byte, Member> _members = [];
    readonly List<(byte[] Data, string? Picture)> _outbox = [];
    readonly ECDiffieHellman _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    readonly byte[] _mine, _senderKey = RandomNumberGenerator.GetBytes(32);
    readonly AesGcm _send;
    ulong _counter;
    int _closed;
    bool _started;

    public event Action? Connected;
    public event Action<string>? Closed;
    public event Action<string>? Received;
    /// <summary>One more person came into the room after the connection stood (their key is agreed): time to say hello to them.</summary>
    public event Action? PeerJoined;

    /// <summary>The room code, as shown to people (for example "K7F2-QXM9").</summary>
    public string Code => Pretty(_code);
    /// <summary>Four digits made from the keys of everybody in the room: the same for all of them when nobody sits in between.</summary>
    public string CheckCode { get; private set; } = "";
    public bool IsOpen => _started && _closed == 0;
    /// <summary>How many others are connected right now (their key is agreed).</summary>
    public int Others { get { lock (_gate) return _members.Values.Count(m => m.Recv is not null); } }
    /// <summary>Where it stands, for the status line while waiting: "connecting", "waiting" (in the room, alone), "keys", "open", "closed".</summary>
    public string Status { get; private set; } = "connecting";
    /// <summary>Why it ended: "full" (five people there already), "wrong code" (the keys did not match), "left" (everybody went), or a technical message.</summary>
    public string CloseReason { get; private set; } = "";

    RelayLink(Uri server, string code)
    {
        _server = server; _code = code;
        _codeKey = SHA256.HashData(Encoding.UTF8.GetBytes("BibleStudy relay key v1|" + code));
        _mine = _ecdh.PublicKey.ExportSubjectPublicKeyInfo();
        _send = new AesGcm(_senderKey, 16);
    }

    /// <summary>A new room with a random code (the person who invites).</summary>
    public static RelayLink Create(string server) => new(ServerUri(server), NewCode());

    /// <summary>The room of a code someone got. Throws <see cref="FormatException"/> when it is not a room code.</summary>
    public static RelayLink Join(string server, string code) => new(ServerUri(server), Normalize(code) ?? throw new FormatException("code"));

    public static string NewCode() => new(RandomNumberGenerator.GetItems(CodeAlphabet.AsSpan(), CodeLength));

    /// <summary>A typed code as the program uses it: upper case, without dashes and spaces. Null when it cannot be a code.</summary>
    public static string? Normalize(string? typed)
    {
        var sb = new StringBuilder();
        foreach (var raw in typed ?? "")
        {
            if (char.IsWhiteSpace(raw) || raw is '-' or '_' or '.') continue;
            var c = char.ToUpperInvariant(raw);
            if (!CodeAlphabet.Contains(c)) return null;
            sb.Append(c);
        }
        return sb.Length == CodeLength ? sb.ToString() : null;
    }

    public static string Pretty(string code) => code.Length == CodeLength ? code[..4] + "-" + code[4..] : code;

    /// <summary>The name of the room on the relay: a hash of the code, so the relay does not learn the code itself.</summary>
    public static string RoomOf(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("BibleStudy room v1|" + code))).ToLowerInvariant();

    static Uri ServerUri(string server)
    {
        server = (server ?? "").Trim().TrimEnd('/');
        if (server.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) server = "wss://" + server[8..];
        else if (server.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) server = "ws://" + server[7..];
        else if (!server.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) && !server.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)) server = "wss://" + server;
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Host.Length == 0) throw new FormatException("server");
        return uri;
    }

    /// <summary>Connects to the relay and enters the room. Returns when the program is in the room; <see cref="Connected"/> comes once someone else is there too.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        using (var reach = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token))
        {
            reach.CancelAfter(TimeSpan.FromSeconds(15));
            await _ws.ConnectAsync(new Uri(_server, _server.AbsolutePath.TrimEnd('/') + "/r/" + RoomOf(_code)), reach.Token);
        }
        Status = "waiting";
        _ = ReadLoop(_cts.Token);
        _ = PingLoop(_cts.Token);
        _ = SendLoop(_cts.Token);
    }

    public Task WaitOpenAsync(TimeSpan timeout) => _open.Task.WaitAsync(timeout);

    // ---- the keys ---------------------------------------------------------------------------------------------------------

    Member MemberOf(byte id) { lock (_gate) return _members.TryGetValue(id, out var m) ? m : _members[id] = new Member(); }

    async Task SendKeyAsync(byte id, Member m)
    {
        if (m.KeySent) return;
        m.KeySent = true;
        if (!_started) Status = "keys";
        await SendFrameAsync([id, KeyFrame, .. _mine]);
    }

    async Task OnKeyAsync(byte id, byte[] theirs)
    {
        var m = MemberOf(id);
        if (m.Public is not null || theirs.AsSpan().SequenceEqual(_mine)) return;
        using var other = ECDiffieHellman.Create();
        other.ImportSubjectPublicKeyInfo(theirs, out _);
        var shared = _ecdh.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256);
        var (a, b) = _mine.AsSpan().SequenceCompareTo(theirs) < 0 ? (_mine, theirs) : (theirs, _mine);
        byte[] both = [.. a, .. b];
        var transcript = SHA256.HashData(both);
        m.Public = theirs;
        byte[] secret = [.. shared, .. transcript];
        m.Pair = new AesGcm(HMACSHA256.HashData(_codeKey, secret), 16);
        await SendKeyAsync(id, m);
        if (m.SenderKeySent) return;
        m.SenderKeySent = true;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var sealedKey = new byte[_senderKey.Length]; var tag = new byte[16];
        m.Pair.Encrypt(nonce, _senderKey, sealedKey, tag, SenderKeyAad);
        await SendFrameAsync([id, SenderKeyFrame, .. nonce, .. sealedKey, .. tag]);
    }

    void OnSenderKey(byte id, ReadOnlySpan<byte> data)
    {
        var m = MemberOf(id);
        if (m.Pair is null || m.Recv is not null || data.Length != 12 + 32 + 16) return;
        var key = new byte[32];
        try { m.Pair.Decrypt(data[..12], data[12..44], data[44..], key, SenderKeyAad); }
        catch (CryptographicException)
        {
            // whoever does not know the code cannot seal this: in an empty room that means a wrong code (or someone guessing), else it is ignored
            if (!_started) End("wrong code");
            return;
        }
        m.Recv = new AesGcm(key, 16);
        UpdateCheckCode();
        if (_started) { PeerJoined?.Invoke(); return; }
        _started = true;
        Status = "open";
        _open.TrySetResult();
        Connected?.Invoke();
    }

    void UpdateCheckCode()
    {
        List<byte[]> keys;
        lock (_gate) keys = [_mine, .. _members.Values.Where(m => m.Recv is not null && m.Public is not null).Select(m => m.Public!)];
        keys.Sort((x, y) => x.AsSpan().SequenceCompareTo(y));
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var k in keys) h.AppendData(k);
        CheckCode = (BitConverter.ToUInt32(h.GetHashAndReset(), 0) % 10000).ToString("0000");
    }

    // ---- reading ----------------------------------------------------------------------------------------------------------

    async Task ReadLoop(CancellationToken ct)
    {
        var why = "closed";
        var buffer = new byte[64 * 1024];
        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                using var frame = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(buffer, ct);
                    if (r.MessageType == WebSocketMessageType.Close) { End(_ws.CloseStatusDescription is { Length: > 0 } d ? d : "closed"); return; }
                    frame.Write(buffer, 0, r.Count);
                    if (frame.Length > Chunk + 4096) throw new InvalidDataException("frame");
                } while (!r.EndOfMessage);
                var data = frame.ToArray();
                if (r.MessageType == WebSocketMessageType.Text) await OnRelayAsync(Encoding.UTF8.GetString(data));
                else if (data.Length >= 2) await OnFrameAsync(data[0], data[1], data[2..]);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { why = ex.Message; }
        End(why);
    }

    async Task OnRelayAsync(string text)
    {
        var p = text.Split(' ');
        switch (p[0])
        {
            case "hello":   // in the room: say hello to everybody already there
                if (p.Length > 2)
                    foreach (var s in p[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
                        if (byte.TryParse(s, out var id)) await SendKeyAsync(id, MemberOf(id));
                break;
            case "join" when p.Length > 1 && byte.TryParse(p[1], out var joined):
                await SendKeyAsync(joined, MemberOf(joined));
                break;
            case "left" when p.Length > 1 && byte.TryParse(p[1], out var left):
                Member? m;
                bool anyLeft;
                lock (_gate) { _members.Remove(left, out m); anyLeft = _members.Values.Any(x => x.Recv is not null); }
                if (m?.Recv is null) break;
                m.Pair?.Dispose(); m.Recv.Dispose();
                UpdateCheckCode();
                // whoever was reached through this place and not through someone still here is gone: tell the group, as if they had said goodbye
                HashSet<string> stillHere;
                lock (_gate) stillHere = [.. _members.Values.SelectMany(x => x.People.Keys)];
                foreach (var (fid, fn) in m.People.Where(x => !stillHere.Contains(x.Key)).ToList())
                    Received?.Invoke(JsonSerializer.Serialize(new { t = "bye", fid, fn }));
                if (!anyLeft) End("left");
                break;
            case "full": End("full"); break;
        }
    }

    async Task OnFrameAsync(byte from, byte kind, byte[] data)
    {
        switch (kind)
        {
            case KeyFrame: await OnKeyAsync(from, data); return;
            case SenderKeyFrame: OnSenderKey(from, data); return;
            case DataFrame: OnData(from, data); return;
        }
    }

    void OnData(byte from, byte[] data)
    {
        Member? m;
        lock (_gate) _members.TryGetValue(from, out m);
        if (m?.Recv is null || data.Length < 1 + 8 + 16) return;   // sent before we had their key: not for us
        var flags = data[0];
        var counter = BitConverter.ToUInt64(data, 1);
        if (counter <= m.Last) return;                             // played back a second time
        var cipher = data.AsSpan(9, data.Length - 9 - 16);
        var plain = new byte[cipher.Length];
        try { m.Recv.Decrypt(Nonce(counter), cipher, data.AsSpan(data.Length - 16), plain, new[] { flags }); }
        catch (CryptographicException) { return; }
        m.Last = counter;
        if ((flags & First) != 0) m.Parts = null;
        if ((flags & More) != 0 || m.Parts is not null)
        {
            if (m.Parts is null && (flags & First) == 0) return;    // the start of this batch came before we were there
            m.Parts ??= new MemoryStream();
            m.Parts.Write(plain);
            if (m.Parts.Length > MaxMessage) { m.Parts = null; return; }
            if ((flags & More) != 0) return;
            plain = m.Parts.ToArray();
            m.Parts = null;
        }
        else if ((flags & First) == 0) return;                      // the end of a batch whose start we missed
        // a batch: [length, 4 bytes][text] …
        for (var at = 0; at + 4 <= plain.Length;)
        {
            var n = BitConverter.ToInt32(plain, at);
            if (n < 0 || at + 4 + n > plain.Length) return;
            var text = Encoding.UTF8.GetString(plain, at + 4, n);
            at += 4 + n;
            Remember(m, text);
            Received?.Invoke(text);
        }
    }

    /// <summary>Who writes through this place: to say goodbye for them when it vanishes. Only the start of a message is read (where the group puts "fid" for the small ones it would not pay to look at all).</summary>
    void Remember(Member m, string text)
    {
        // "hello", "bye" and "nav" carry the name; the voice and the pointer come often and say nothing new
        if (text.Length > 2000 || !(text.Contains("\"t\":\"hello\"", StringComparison.Ordinal) || m.People.Count == 0)) return;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("fid", out var f) || f.ValueKind != JsonValueKind.String || f.GetString() is not { Length: > 0 and <= 32 } fid) return;
            var fn = root.TryGetProperty("fn", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
            lock (_gate) if (m.People.Count < 50) m.People[fid] = fn.Length > 40 ? fn[..40] : fn;
        }
        catch (JsonException) { }
    }

    // ---- writing ----------------------------------------------------------------------------------------------------------

    static byte[] Nonce(ulong n) { var b = new byte[12]; BitConverter.GetBytes(n).CopyTo(b, 4); return b; }

    async Task SendFrameAsync(byte[] frame, bool text = false)
    {
        await _write.WaitAsync(_cts.Token);
        try { await _ws.SendAsync(frame, text ? WebSocketMessageType.Text : WebSocketMessageType.Binary, true, _cts.Token); }
        finally { _write.Release(); }
    }

    public void Send(string text)
    {
        if (!IsOpen) return;
        // a camera picture still waiting is worth nothing once a newer one of the same person is there: with a slow upload the pictures
        // are thinned out instead of falling further and further behind
        var picture = text.StartsWith("{\"t\":\"vf\"", StringComparison.Ordinal) ? PictureOwner(text) : null;
        lock (_outbox)
        {
            if (picture is not null) _outbox.RemoveAll(o => o.Picture == picture);
            _outbox.Add((Encoding.UTF8.GetBytes(text), picture));
        }
        if (_signal.CurrentCount == 0) _signal.Release();
    }

    /// <summary>Whose camera picture this is: the group writes the id at the end ("fid"), the picture itself is base64 and holds no quotes.</summary>
    static string PictureOwner(string text)
    {
        var at = text.LastIndexOf("\"fid\":\"", StringComparison.Ordinal);
        if (at < 0) return "";
        var end = text.IndexOf('"', at + 7);
        return end < 0 ? "" : text[(at + 7)..end];
    }

    async Task SendLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _signal.WaitAsync(ct);
                await Task.Delay(Gather, ct);   // what comes in meanwhile goes in the same batch
                byte[][] items;
                lock (_outbox) { items = [.. _outbox.Select(o => o.Data)]; _outbox.Clear(); }
                if (items.Length == 0) continue;
                using var batch = new MemoryStream();
                foreach (var item in items)
                {
                    if (4 + item.Length > MaxMessage) continue;
                    if (batch.Length + 4 + item.Length > MaxMessage) { await SendBatchAsync(batch.ToArray()); batch.SetLength(0); }
                    batch.Write(BitConverter.GetBytes(item.Length)); batch.Write(item);
                }
                if (batch.Length > 0) await SendBatchAsync(batch.ToArray());
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { End(ex.Message); }
    }

    async Task SendBatchAsync(byte[] plain)
    {
        for (var at = 0; at < plain.Length; at += Chunk)
        {
            var n = Math.Min(Chunk, plain.Length - at);
            var flags = (byte)((at == 0 ? First : 0) | (at + n < plain.Length ? More : 0));
            var counter = ++_counter;
            var frame = new byte[2 + 1 + 8 + n + 16];
            frame[0] = 0; frame[1] = DataFrame; frame[2] = flags;
            BitConverter.GetBytes(counter).CopyTo(frame, 3);
            _send.Encrypt(Nonce(counter), plain.AsSpan(at, n), frame.AsSpan(11, n), frame.AsSpan(11 + n, 16), new[] { flags });
            await SendFrameAsync(frame);
        }
    }

    async Task PingLoop(CancellationToken ct)
    {
        // keeps the connection alive through routers that drop quiet ones, and tells the relay we are still there; it answers without waking up
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(20), ct); await SendFrameAsync("ping"u8.ToArray(), text: true); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { End(ex.Message); return; }
        }
    }

    void End(string why)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        Status = "closed";
        CloseReason = why;
        _open.TrySetException(new IOException(why));
        _ = _open.Task.Exception;   // nobody has to wait for it
        _cts.Cancel();
        _ = Task.Run(async () =>
        {
            try { if (_ws.State == WebSocketState.Open) await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            _ws.Dispose();
        });
        Closed?.Invoke(why);
    }

    public void Dispose() => End("left");
}
