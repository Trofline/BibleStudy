using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BibleStudy.Core.Live;

/// <summary>A question a participant sent from the phone.</summary>
public sealed record LiveQuestion(int Id, string Text, string Name, DateTime At, bool Answered = false);

/// <summary>A poll: a question with choices; every phone has one vote and may change it.</summary>
public sealed record LivePoll(int Id, string Question, IReadOnlyList<string> Options, bool ShowResults = false);

/// <summary>
/// The Bible study on the participants' phones, without an app: the program serves a small web page in the local network
/// (http://192.168.…:port/code). The page shows what the projector shows, the handout, lets one send a question and vote in a poll.
/// Only who knows the random code of the room (from the QR code) gets in; nothing leaves the local network and nothing is kept
/// after the room is closed, except what the leader keeps in the archive.
/// A plain TCP listener, so that no administrator rights are needed (an HttpListener on all addresses would need them on Windows).
/// </summary>
public sealed class LiveRoom : IDisposable
{
    const int MaxBody = 4096, MaxQuestions = 300, MaxQuestionLength = 500, MaxNameLength = 40;
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    readonly object _gate = new();
    readonly Func<string> _page;
    TcpListener? _listener;
    CancellationTokenSource _cts = new();

    string _title = "", _text = "";
    string? _handout;
    long _version = 1;
    LivePoll? _poll;
    readonly Dictionary<string, int> _votes = [];   // voter → option of the current poll
    readonly List<LiveQuestion> _questions = [];
    readonly Dictionary<string, Queue<DateTime>> _askedBy = [];   // address → times of the last questions (at most 5 a minute)
    readonly HashSet<string> _phones = [];

    /// <summary>The secret part of the address; letters and digits that cannot be mixed up (no 0/O, 1/l/I).</summary>
    public string Code { get; } = MakeCode();
    public int Port { get; private set; }
    public bool IsRunning => _listener is not null;

    /// <summary>A question came in, or a vote; raised on a worker thread.</summary>
    public event Action? Changed;

    /// <param name="page">The web page for the phones (with its texts in the language of the program).</param>
    public LiveRoom(Func<string> page) => _page = page;

    static string MakeCode()
    {
        const string letters = "abcdefghjkmnpqrstuvwxyz23456789";
        return string.Concat(RandomNumberGenerator.GetBytes(6).Select(b => letters[b % letters.Length]));
    }

    // ---- starting -------------------------------------------------------------------------------------------------------

    /// <summary>Listens on all addresses of the computer, on the first free port from 49400.</summary>
    public bool Start()
    {
        for (var port = 49400; port < 49500 && _listener is null; port++)
        {
            try
            {
                var l = new TcpListener(IPAddress.Any, port);
                l.Start();
                _listener = l; Port = port;
            }
            catch (SocketException) { /* taken, try the next */ }
        }
        if (_listener is null) return false;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptAsync(_listener, _cts.Token));
        return true;
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch (SocketException) { }
        _listener = null;
    }

    public void Dispose() => Stop();

    /// <summary>The addresses of this computer in the local network (Wi-Fi and cable first), so that a phone in the same network reaches it.</summary>
    public static List<(string Address, string Network)> LocalAddresses()
    {
        var found = new List<(string, string, int)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = nic.GetIPProperties();
                var gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var a in props.UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a.Address)) continue;
                    var b = a.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue;   // no address from a router
                    var isPrivate = b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
                    var virtualNic = nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || nic.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase)
                        || nic.Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase);
                    var rank = (gateway ? 0 : 4) + (isPrivate ? 0 : 2) + (virtualNic ? 8 : 0) + (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1);
                    found.Add((a.Address.ToString(), nic.Name, rank));
                }
            }
        }
        catch (NetworkInformationException) { }
        return found.OrderBy(f => f.Item3).Select(f => (f.Item1, f.Item2)).Distinct().ToList();
    }

    public string UrlFor(string address) => $"http://{address}:{Port}/{Code}";

    // ---- what the leader changes ------------------------------------------------------------------------------------------

    /// <summary>What the phones show now (the projector's verse or piece of the materials).</summary>
    public void Show(string title, string text)
    {
        lock (_gate)
        {
            if (_title == title && _text == text) return;
            _title = title; _text = text; _version++;
        }
    }

    /// <summary>The handout as a whole web page, or null for none.</summary>
    public void SetHandout(string? html) { lock (_gate) { _handout = html; _version++; } }
    public bool HasHandout { get { lock (_gate) return _handout is not null; } }

    public LivePoll StartPoll(string question, IEnumerable<string> options)
    {
        lock (_gate)
        {
            var opts = options.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().Take(8).ToList();
            _poll = new LivePoll((_poll?.Id ?? 0) + 1, question.Trim(), opts);
            _votes.Clear(); _version++;
            return _poll;
        }
    }

    public void ShowPollResults(bool show) { lock (_gate) { if (_poll is { } p) { _poll = p with { ShowResults = show }; _version++; } } }
    public void EndPoll() { lock (_gate) { _poll = null; _votes.Clear(); _version++; } }

    public LivePoll? Poll { get { lock (_gate) return _poll; } }

    /// <summary>The votes for each choice of the current poll.</summary>
    public int[] Results()
    {
        lock (_gate)
        {
            if (_poll is not { } p) return [];
            var r = new int[p.Options.Count];
            foreach (var v in _votes.Values) if (v >= 0 && v < r.Length) r[v]++;
            return r;
        }
    }

    public List<LiveQuestion> Questions { get { lock (_gate) return [.. _questions]; } }
    public void MarkAnswered(int id, bool answered = true) { lock (_gate) { var i = _questions.FindIndex(q => q.Id == id); if (i >= 0) _questions[i] = _questions[i] with { Answered = answered }; } }
    public void RemoveQuestion(int id) { lock (_gate) _questions.RemoveAll(q => q.Id == id); }

    /// <summary>How many phones asked for the page in this room (each phone counted once).</summary>
    public int Phones { get { lock (_gate) return _phones.Count; } }

    // ---- what the phones ask for ------------------------------------------------------------------------------------------

    /// <summary>The state for a phone: what is shown, the poll (with the results once the leader shows them) and whether there is a handout.</summary>
    public string StateJson(string? voter)
    {
        lock (_gate)
        {
            object? poll = _poll is { } p ? new
            {
                id = p.Id, question = p.Question, options = p.Options,
                results = p.ShowResults ? Results() : null,
                voted = voter is not null && _votes.TryGetValue(voter, out var v) ? v : (int?)null,
            } : null;
            return JsonSerializer.Serialize(new { v = _version, title = _title, text = _text, handout = _handout is not null, poll }, Json);
        }
    }

    /// <summary>A question from a phone; false when it is empty, too many came from that phone or the room is full.</summary>
    public bool Ask(string text, string name, string from)
    {
        text = Clean(text, MaxQuestionLength); name = Clean(name, MaxNameLength);
        if (text.Length == 0) return false;
        lock (_gate)
        {
            if (_questions.Count >= MaxQuestions) return false;
            if (!_askedBy.TryGetValue(from, out var times)) _askedBy[from] = times = new();
            while (times.Count > 0 && DateTime.UtcNow - times.Peek() > TimeSpan.FromMinutes(1)) times.Dequeue();
            if (times.Count >= 5) return false;
            times.Enqueue(DateTime.UtcNow);
            _questions.Add(new LiveQuestion((_questions.Count == 0 ? 0 : _questions.Max(q => q.Id)) + 1, text, name, DateTime.Now));
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>A vote from a phone for the poll it saw; a later vote of the same phone replaces the earlier one.</summary>
    public bool Vote(int poll, int option, string voter)
    {
        voter = Clean(voter, 64);
        if (voter.Length == 0) return false;
        lock (_gate)
        {
            if (_poll is not { } p || p.Id != poll || option < 0 || option >= p.Options.Count) return false;
            _votes[voter] = option;
            if (p.ShowResults) _version++;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>One line, no control characters, at most so long.</summary>
    static string Clean(string? s, int max)
    {
        var t = new string((s ?? "").Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
        return t.Length > max ? t[..max] : t;
    }

    // ---- HTTP ---------------------------------------------------------------------------------------------------------------

    async Task AcceptAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or InvalidOperationException) { break; }
            _ = Task.Run(() => ServeAsync(client, ct));
        }
    }

    async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = client.SendTimeout = 10000;
            var stream = client.GetStream();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var request = await ReadRequestAsync(stream, timeout.Token);
            if (request is null) return;
            var from = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
            var (status, type, body) = Handle(request.Value.Method, request.Value.Target, request.Value.Body, from);
            await WriteAsync(stream, status, type, body, timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
    }

    /// <summary>The answer to one request: status, content type and body. Separate from the network, so that it can be tested.</summary>
    public (int Status, string Type, byte[] Body) Handle(string method, string target, string body, string from)
    {
        var path = target.Split('?', 2)[0].TrimEnd('/');
        var query = target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "";
        var prefix = "/" + Code;
        if (!path.Equals(prefix, StringComparison.Ordinal) && !path.StartsWith(prefix + "/", StringComparison.Ordinal)) return NotFound();
        var rest = path[prefix.Length..];
        switch (method, rest)
        {
            case ("GET", ""):
                lock (_gate) _phones.Add(from);
                return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(_page()));
            case ("GET", "/state"):
                return (200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(StateJson(QueryValue(query, "voter"))));
            case ("GET", "/handout"):
                string? html;
                lock (_gate) html = _handout;
                return html is null ? NotFound() : (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
            case ("POST", "/ask"):
            {
                var o = ParseJson(body);
                var ok = o is { } j && Ask(Str(j, "text"), Str(j, "name"), from);
                return Ok(ok);
            }
            case ("POST", "/vote"):
            {
                var o = ParseJson(body);
                var ok = o is { } j && j.TryGetProperty("poll", out var p) && p.TryGetInt32(out var pid) && j.TryGetProperty("option", out var op) && op.TryGetInt32(out var oid)
                    && Vote(pid, oid, Str(j, "voter"));
                return Ok(ok);
            }
            default: return NotFound();
        }

        static (int, string, byte[]) NotFound() => (404, "text/plain; charset=utf-8", "Not found"u8.ToArray());
        static (int, string, byte[]) Ok(bool ok) => (ok ? 200 : 400, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(ok ? "{\"ok\":true}" : "{\"ok\":false}"));
        static string Str(JsonElement j, string name) => j.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        static JsonElement? ParseJson(string s) { try { using var d = JsonDocument.Parse(s); return d.RootElement.Clone(); } catch (JsonException) { return null; } }
    }

    static string? QueryValue(string query, string key)
    {
        foreach (var part in query.Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == key) return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    static async Task<(string Method, string Target, string Body)?> ReadRequestAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxBody + 8192];
        var read = 0;
        int headEnd;
        while ((headEnd = IndexOf(buffer, read, "\r\n\r\n"u8)) < 0)
        {
            if (read == buffer.Length) return null;
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) return null;
            read += n;
        }
        var head = Encoding.ASCII.GetString(buffer, 0, headEnd);
        var lines = head.Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2) return null;
        var length = 0;
        foreach (var l in lines.Skip(1))
            if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) && int.TryParse(l[15..].Trim(), out var len)) length = len;
        if (length < 0 || length > MaxBody) return null;
        var bodyStart = headEnd + 4;
        while (read - bodyStart < length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) return null;
            read += n;
        }
        return (first[0], first[1], Encoding.UTF8.GetString(buffer, bodyStart, length));
    }

    static int IndexOf(byte[] data, int count, ReadOnlySpan<byte> what) => data.AsSpan(0, count).IndexOf(what);

    static async Task WriteAsync(Stream stream, int status, string type, byte[] body, CancellationToken ct)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", _ => "Not Found" };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\n" +
                   "X-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.WriteAsync(body, ct);
    }
}
