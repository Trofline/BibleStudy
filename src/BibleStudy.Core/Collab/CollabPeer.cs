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
public sealed class CollabPeer : IDisposable
{
    public const string Prefix = "BS1-";
    static readonly string[] StunServers = ["stun:stun.l.google.com:19302", "stun:stun.cloudflare.com:3478"];

    RTCPeerConnection? _pc;
    RTCDataChannel? _channel;
    readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action? Connected;
    public event Action<string>? Closed;
    public event Action<string>? Received;

    public bool IsOpen => _channel?.readyState == RTCDataChannelState.open;

    RTCPeerConnection Create()
    {
        var pc = new RTCPeerConnection(new RTCConfiguration { iceServers = StunServers.Select(u => new RTCIceServer { urls = u }).ToList() });
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

    static async Task<string> EncodeAsync(RTCPeerConnection pc, string kind)
    {
        var gathered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pc.onicegatheringstatechange += s => { if (s == RTCIceGatheringState.complete) gathered.TrySetResult(); };
        if (pc.iceGatheringState == RTCIceGatheringState.complete) gathered.TrySetResult();
        await Task.WhenAny(gathered.Task, Task.Delay(8000));   // without internet the local addresses are all there is
        var sdp = pc.localDescription!.sdp.ToString();
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
        using var doc = JsonDocument.Parse(z);
        if (doc.RootElement.GetProperty("k").GetString() != kind) throw new FormatException("kind");
        return doc.RootElement.GetProperty("s").GetString()!;
    }

    public void Dispose() { try { _channel?.close(); _pc?.close(); } catch { } }
}
