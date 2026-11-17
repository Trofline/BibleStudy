using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BibleStudy.Core.Collab;

/// <summary>
/// Several people studying together: every program can hold connections to several others, and passes on what it gets to its other
/// connections. So Anna–Ben–Carla are a group although Anna and Carla are not connected directly. Every message carries a random id
/// (seen once, never passed on twice: no loops) and the id and name of the person who wrote it.
/// A message without an id (from an older version, or sent to one person only) is not passed on.
/// </summary>
public sealed class CollabGroup
{
    readonly object _gate = new();
    readonly List<ICollabLink> _links = [];
    readonly HashSet<string> _seen = [];
    readonly Queue<string> _order = new();
    readonly Dictionary<ICollabLink, string> _fallback = [];
    readonly Dictionary<ICollabLink, Action<string>> _handlers = [];

    public CollabGroup(string myId) => MyId = myId;

    public string MyId { get; }
    public string MyName { get; set; } = "";

    /// <summary>A message arrived: the connection it came through, the id and name of who wrote it, and the message itself.</summary>
    public event Action<ICollabLink, string, string, string>? Message;

    public IReadOnlyList<ICollabLink> Links { get { lock (_gate) return _links.ToList(); } }
    public bool Any { get { lock (_gate) return _links.Count > 0; } }

    public void Add(ICollabLink link)
    {
        Action<string> handler = raw => OnReceive(link, raw);
        lock (_gate)
        {
            if (_links.Contains(link)) return;
            _links.Add(link);
            _fallback[link] = "L" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
            _handlers[link] = handler;
        }
        link.Received += handler;
    }

    /// <summary>The connection leaves the group: nothing of it is passed on any more (added again later, its messages do not arrive twice).</summary>
    public void Remove(ICollabLink link)
    {
        Action<string>? handler;
        lock (_gate) { _links.Remove(link); _fallback.Remove(link); _handlers.Remove(link, out handler); }
        if (handler is not null) link.Received -= handler;
    }

    /// <summary>The id the people behind this connection are known by when their messages carry none (older versions).</summary>
    public string FallbackId(ICollabLink link) { lock (_gate) return _fallback.TryGetValue(link, out var id) ? id : ""; }

    /// <summary>To everyone in the group. Returns the id of the message (for reactions to it).</summary>
    public string Send(object message)
    {
        var mid = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var node = JsonSerializer.SerializeToNode(message)!.AsObject();
        node["mid"] = mid; node["fid"] = MyId; node["fn"] = MyName;
        var raw = node.ToJsonString();
        Remember(mid);
        foreach (var l in Links) if (l.IsOpen) l.Send(raw);
        return mid;
    }

    /// <summary>To the person (or people) behind one connection only; not passed on.</summary>
    public void SendTo(ICollabLink link, object message)
    {
        var node = JsonSerializer.SerializeToNode(message)!.AsObject();
        node["fid"] = MyId; node["fn"] = MyName;
        if (link.IsOpen) link.Send(node.ToJsonString());
    }

    bool Remember(string mid)
    {
        lock (_gate)
        {
            if (!_seen.Add(mid)) return false;
            _order.Enqueue(mid);
            while (_order.Count > 5000) _seen.Remove(_order.Dequeue());
            return true;
        }
    }

    void OnReceive(ICollabLink from, string raw)
    {
        string fid = "", fn = "";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            string Str(string key, int max) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String && e.GetString() is { } s ? (s.Length > max ? s[..max] : s) : "";
            var mid = Str("mid", 32);
            if (mid.Length > 0)
            {
                if (!Remember(mid)) return;   // seen before: came round a second way
                foreach (var l in Links) if (l != from && l.IsOpen) l.Send(raw);
            }
            fid = Str("fid", 32); fn = Str("fn", 40).Trim();
        }
        catch (JsonException) { return; }
        if (fid.Length == 0) fid = FallbackId(from);
        if (fid == MyId) return;   // my own message came back
        Message?.Invoke(from, fid, fn, raw);
    }
}
