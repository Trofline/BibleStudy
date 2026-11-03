using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SIPSorcery.Net;

namespace BibleStudy.Core.Collab;

/// <summary>
/// A direct, encrypted connection to one other person, without any server of ours: WebRTC data channel (DTLS-encrypted).
/// The two sides swap two short codes (invitation, answer) by any messenger. The codes contain the address data and the key fingerprint,
/// so whoever has both codes is the only one who can connect. A public STUN server is asked once for the own public address (no content goes through it).
/// </summary>
/// <summary>An optional relay server (TURN) of the user's own choice, used when no direct connection is possible. The data stays encrypted end to end.</summary>
public sealed record RelayServer(string Url, string User, string Password)
{
    /// <summary>Reads what a person typed: "turn:host:3478", "turns:host:5349" or just "host:3478" (then "turn:" is added). Null when it is empty or unusable.</summary>
    public static RelayServer? Parse(string? url, string? user, string? password)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0 || url.Any(char.IsWhiteSpace)) return null;
        if (url.StartsWith("stun:", StringComparison.OrdinalIgnoreCase)) return null;
        if (!url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("turns:", StringComparison.OrdinalIgnoreCase)) url = "turn:" + url;
        return new RelayServer(url, (user ?? "").Trim(), password ?? "");
    }
}

public sealed class CollabPeer : IDisposable
{
    readonly RelayServer? _relay;

    public CollabPeer(RelayServer? relay = null) => _relay = relay;

    public const string Prefix = "BS1-";
    static readonly string[] StunServers = ["stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302", "stun:stun.cloudflare.com:3478"];

    RTCPeerConnection? _pc;
    readonly List<string> _candidates = [];   // every address found, also those the library leaves out of the description
    RTCDataChannel? _channel;
    readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action? Connected;
    public event Action<string>? Closed;
    public event Action<string>? Received;

    public bool IsOpen => _channel?.readyState == RTCDataChannelState.open;

    /// <summary>Where the connection stands, for the status line while waiting (for example "connecting/checking").</summary>
    public string Status => _pc is null ? "" : $"{_pc.connectionState}/{_pc.iceConnectionState}";

    /// <summary>A short technical line for the failure message: the state and how many addresses of which kind each side offered
    /// (host = own network, srflx = seen from outside by the STUN server). Tells at once whether the other side's addresses were seen.</summary>
    public string Diagnose()
    {
        static string Count(string? sdp)
        {
            if (string.IsNullOrEmpty(sdp)) return "0";
            var kinds = System.Text.RegularExpressions.Regex.Matches(sdp, @"a=candidate:.* typ (\w+)").Select(m => m.Groups[1].Value)
                .GroupBy(k => k).Select(g => $"{g.Key} {g.Count()}").ToList();
            return kinds.Count == 0 ? "0" : string.Join(", ", kinds);
        }
        return $"{Status}; own addresses: {Count(_pc?.localDescription?.sdp?.ToString())}; other addresses: {Count(_pc?.remoteDescription?.sdp?.ToString())}";
    }

    RTCPeerConnection Create()
    {
        var servers = StunServers.Select(u => new RTCIceServer { urls = u }).ToList();
        if (_relay is not null) servers.Add(new RTCIceServer { urls = _relay.Url, username = _relay.User, credential = _relay.Password, credentialType = RTCIceCredentialType.password });
        var pc = new RTCPeerConnection(new RTCConfiguration { iceServers = servers });
        pc.onicecandidate += c =>
        {
            if (c?.candidate is { Length: > 0 } line) lock (_candidates) _candidates.Add(line.StartsWith("candidate:") ? line : "candidate:" + line);
        };
        pc.onconnectionstatechange += state =>
        {
            if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed or RTCPeerConnectionState.disconnected) Closed?.Invoke(state.ToString());
        };
        return pc;
    }

    void Hook(RTCDataChannel channel)
    {
        _channel = channel;
        channel.onopen += () => { _open.TrySetResult(); Connected?.Invoke(); };
        channel.onclose += () => Closed?.Invoke("closed");
        channel.onmessage += (_, _, data) => Received?.Invoke(Encoding.UTF8.GetString(data));
        if (channel.readyState == RTCDataChannelState.open) { _open.TrySetResult(); Connected?.Invoke(); }
    }

    /// <summary>Step 1 (host): creates the invitation code.</summary>
    public async Task<string> CreateInvitationAsync()
    {
        _pc = Create();
        Hook(await _pc.createDataChannel("study"));
        var offer = _pc.createOffer();
        await _pc.setLocalDescription(offer);
        return await EncodeAsync(_pc, "offer");
    }

    /// <summary>Step 2 (guest): reads the invitation and creates the answer code.</summary>
    public async Task<string> AcceptInvitationAsync(string invitation)
    {
        var offer = Decode(invitation, "offer");
        _pc = Create();
        _pc.ondatachannel += Hook;
        var result = _pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = offer });
        if (result != SetDescriptionResultEnum.OK) throw new InvalidOperationException(result.ToString());
        var answer = _pc.createAnswer();
        await _pc.setLocalDescription(answer);
        return await EncodeAsync(_pc, "answer");
    }

    /// <summary>Step 3 (host): reads the answer code; the connection comes up by itself.</summary>
    public void CompleteWithAnswer(string answerCode)
    {
        var sdp = Decode(answerCode, "answer");
        var result = _pc!.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp });
        if (result != SetDescriptionResultEnum.OK) throw new InvalidOperationException(result.ToString());
    }

    public Task WaitOpenAsync(TimeSpan timeout) => _open.Task.WaitAsync(timeout);

    public void Send(string text) { if (IsOpen) _channel!.send(text); }

    // ---- codes: the description once all addresses are known, packed small ------------------------------------------------

    async Task<string> EncodeAsync(RTCPeerConnection pc, string kind)
    {
        var gathered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pc.onicegatheringstatechange += s => { if (s == RTCIceGatheringState.complete) gathered.TrySetResult(); };
        if (pc.iceGatheringState == RTCIceGatheringState.complete) gathered.TrySetResult();
        await Task.WhenAny(gathered.Task, Task.Delay(8000));   // without internet the local addresses are all there is
        var sdp = pc.localDescription!.sdp.ToString();
        // the library puts only the address of the own network into the description; the address seen from outside (found with the STUN server)
        // arrives separately and must be added, else the other side could never reach us from another network
        string[] found; lock (_candidates) found = _candidates.ToArray();
        var missing = found.Where(c => !sdp.Contains(c, StringComparison.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            if (!sdp.EndsWith('\n')) sdp += "\r\n";
            sdp += string.Concat(missing.Select(c => "a=" + c + "\r\n"));
        }
        var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { k = kind, s = sdp }));
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize, true)) z.Write(raw);
        return Prefix + Convert.ToBase64String(ms.ToArray()).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    static string Decode(string code, string kind)
    {
        code = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!code.StartsWith(Prefix, StringComparison.Ordinal)) throw new FormatException("code");
        var b64 = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        using var input = new MemoryStream(Convert.FromBase64String(b64));
        using var z = new DeflateStream(input, CompressionMode.Decompress);
        // a real code unpacks to a few kilobytes; a made-up one must not fill the memory
        using var unpacked = new MemoryStream();
        var buffer = new byte[16 * 1024];
        for (int n; (n = z.Read(buffer)) > 0;)
        {
            unpacked.Write(buffer, 0, n);
            if (unpacked.Length > 1024 * 1024) throw new FormatException("size");
        }
        unpacked.Position = 0;
        using var doc = JsonDocument.Parse(unpacked);
        if (doc.RootElement.GetProperty("k").GetString() != kind) throw new FormatException("kind");
        return doc.RootElement.GetProperty("s").GetString()!;
    }

    public void Dispose() { try { _channel?.close(); _pc?.close(); } catch { } }
}
