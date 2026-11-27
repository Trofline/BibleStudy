using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using BibleStudy.Core.Collab;
using Xunit;

namespace BibleStudy.Tests;

/// <summary>Studying together across networks: rooms on a relay. The relay here is a small copy of relay/src/index.js in this process.</summary>
public class CollabTests
{
    [Fact]
    public async Task Two_people_meet_in_a_room_and_exchange_messages()
    {
        await using var relay = TestRelay.Start();
        using var host = RelayLink.Create(relay.Url);
        await host.StartAsync();
        using var guest = RelayLink.Join(relay.Url, host.Code.ToLowerInvariant());   // typed in small letters: still the same room
        var got = new ConcurrentQueue<string>();
        host.Received += got.Enqueue;
        await guest.StartAsync();
        await Task.WhenAll(host.WaitOpenAsync(TimeSpan.FromSeconds(10)), guest.WaitOpenAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(host.CheckCode, guest.CheckCode);
        Assert.Matches("^[0-9]{4}$", host.CheckCode);

        guest.Send("{\"t\":\"chat\",\"text\":\"Hallo\"}");
        guest.Send("{\"t\":\"chat\",\"text\":\"zweite\"}");
        await Until(() => got.Count == 2);
        Assert.Equal(["{\"t\":\"chat\",\"text\":\"Hallo\"}", "{\"t\":\"chat\",\"text\":\"zweite\"}"], got.ToArray());
    }

    [Fact]
    public async Task The_relay_sees_neither_the_code_nor_the_content()
    {
        await using var relay = TestRelay.Start();
        using var host = RelayLink.Create(relay.Url);
        await host.StartAsync();
        using var guest = RelayLink.Join(relay.Url, host.Code);
        await guest.StartAsync();
        await guest.WaitOpenAsync(TimeSpan.FromSeconds(10));
        var received = new TaskCompletionSource();
        host.Received += _ => received.TrySetResult();
        await host.WaitOpenAsync(TimeSpan.FromSeconds(10));
        guest.Send("{\"t\":\"chat\",\"text\":\"Geheimnis 4711\"}");
        await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var code = RelayLink.Normalize(host.Code)!;
        Assert.DoesNotContain(code, relay.Rooms);
        var all = relay.Seen.SelectMany(b => b).ToArray();
        Assert.Equal(-1, all.AsSpan().IndexOf("Geheimnis"u8));
        Assert.Equal(-1, all.AsSpan().IndexOf(Encoding.ASCII.GetBytes(code)));
    }

    [Fact]
    public async Task Five_people_share_one_room_and_a_sixth_is_turned_away()
    {
        await using var relay = TestRelay.Start();
        var people = new List<RelayLink> { RelayLink.Create(relay.Url) };
        await people[0].StartAsync();
        var inbox = new ConcurrentDictionary<int, ConcurrentQueue<string>>();
        void Listen(int i) { var q = inbox[i] = new(); people[i].Received += q.Enqueue; }
        Listen(0);
        for (var i = 1; i < 5; i++)
        {
            people.Add(RelayLink.Join(relay.Url, people[0].Code));
            Listen(i);
            await people[i].StartAsync();
        }
        try
        {
            await Until(() => people.All(p => p.Others == 4));
            Assert.Single(people.Select(p => p.CheckCode).Distinct());

            people[3].Send("{\"t\":\"chat\",\"text\":\"an alle\"}");
            await Until(() => Enumerable.Range(0, 5).Where(i => i != 3).All(i => inbox[i].Contains("{\"t\":\"chat\",\"text\":\"an alle\"}")));
            Assert.Empty(inbox[3]);

            using var sixth = RelayLink.Join(relay.Url, people[0].Code);
            await sixth.StartAsync();
            await Until(() => sixth.Status == "closed");
            Assert.Equal("full", sixth.CloseReason);
        }
        finally { foreach (var p in people) p.Dispose(); }
    }

    [Fact]
    public async Task Someone_who_vanishes_is_said_goodbye_to()
    {
        await using var relay = TestRelay.Start();
        using var a = RelayLink.Create(relay.Url);
        await a.StartAsync();
        using var b = RelayLink.Join(relay.Url, a.Code);
        using var c = RelayLink.Join(relay.Url, a.Code);
        await b.StartAsync(); await c.StartAsync();
        await Until(() => a.Others == 2 && b.Others == 2 && c.Others == 2);
        var got = new ConcurrentQueue<string>();
        a.Received += got.Enqueue;
        c.Send("{\"t\":\"hello\",\"name\":\"Carla\",\"fid\":\"C1\",\"fn\":\"Carla\"}");
        await Until(() => got.Count == 1);
        c.Dispose();
        await Until(() => got.Count == 2);
        Assert.Contains("\"t\":\"bye\"", got.Last());
        Assert.Contains("\"fid\":\"C1\"", got.Last());
        Assert.True(a.IsOpen, "the room stays while others are in it");
        b.Dispose();
        await Until(() => a.Status == "closed");
        Assert.Equal("left", a.CloseReason);
    }

    [Fact]
    public async Task A_long_message_arrives_whole()
    {
        await using var relay = TestRelay.Start();
        using var host = RelayLink.Create(relay.Url);
        await host.StartAsync();
        using var guest = RelayLink.Join(relay.Url, host.Code);
        var got = new TaskCompletionSource<string>();
        host.Received += s => got.TrySetResult(s);
        await guest.StartAsync();
        await guest.WaitOpenAsync(TimeSpan.FromSeconds(10));
        var big = "{\"t\":\"doc\",\"body\":\"" + new string('x', 900_000) + "\"}";
        guest.Send(big);
        Assert.Equal(big, await got.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.True(relay.Seen.Max(b => b.Length) <= 256 * 1024, "every piece fits the relay");
    }

    /// <summary>Against the real relay (relay/src/index.js), when BIBLESTUDY_RELAY_URL names one (for example ws://127.0.0.1:8787 from "npx wrangler dev"); skipped otherwise.</summary>
    [Fact]
    public async Task Three_people_meet_on_the_real_relay()
    {
        if (Environment.GetEnvironmentVariable("BIBLESTUDY_RELAY_URL") is not { Length: > 0 } url) return;
        using var a = RelayLink.Create(url);
        await a.StartAsync();
        using var b = RelayLink.Join(url, a.Code);
        using var c = RelayLink.Join(url, a.Code);
        var got = new ConcurrentQueue<string>();
        c.Received += got.Enqueue;
        await b.StartAsync(); await c.StartAsync();
        await Until(() => a.Others == 2 && b.Others == 2 && c.Others == 2, 20);
        Assert.Single(new[] { a.CheckCode, b.CheckCode, c.CheckCode }.Distinct());
        var big = "{\"t\":\"doc\",\"body\":\"" + new string('x', 500_000) + "\"}";
        a.Send("{\"t\":\"chat\",\"text\":\"Hallo\"}");
        b.Send(big);
        await Until(() => got.Count == 2, 20);
        Assert.Contains("{\"t\":\"chat\",\"text\":\"Hallo\"}", got);
        Assert.Contains(big, got);
    }

    [Fact]
    public async Task Camera_pictures_that_wait_are_replaced_by_the_newest()
    {
        await using var relay = TestRelay.Start();
        using var host = RelayLink.Create(relay.Url);
        await host.StartAsync();
        using var guest = RelayLink.Join(relay.Url, host.Code);
        var got = new ConcurrentQueue<string>();
        host.Received += got.Enqueue;
        await guest.StartAsync();
        await guest.WaitOpenAsync(TimeSpan.FromSeconds(10));
        await host.WaitOpenAsync(TimeSpan.FromSeconds(10));
        for (var i = 0; i < 20; i++) guest.Send($"{{\"t\":\"vf\",\"d\":\"{i}\",\"fid\":\"G\"}}");
        guest.Send("{\"t\":\"chat\",\"text\":\"danach\",\"fid\":\"G\"}");
        await Until(() => got.Any(m => m.Contains("danach")));
        var pictures = got.Where(m => m.Contains("\"vf\"")).ToList();
        Assert.True(pictures.Count < 20, $"{pictures.Count} pictures arrived");
        Assert.Contains("\"d\":\"19\"", pictures.Last());
        Assert.Contains(got, m => m.Contains("danach"));   // everything else arrives
    }

    [Theory]
    [InlineData("k7f2-qxm9", "K7F2QXM9")]
    [InlineData(" K7F2 QXM9 ", "K7F2QXM9")]
    [InlineData("K7F2-QXM", null)]          // too short
    [InlineData("K7F2-QXM0", null)]         // 0 is not in a code
    [InlineData("", null)]
    public void A_typed_code_is_read_kindly(string typed, string? expected) => Assert.Equal(expected, RelayLink.Normalize(typed));

    [Theory]
    [InlineData("k", 1, "K", 1)]
    [InlineData("k7f2", 4, "K7F2", 4)]                 // no dash yet: deleting backwards does not get stuck at it
    [InlineData("k7f2q", 5, "K7F2-Q", 6)]              // the fifth letter brings the dash
    [InlineData("K7F2-QXM9", 9, "K7F2-QXM9", 9)]
    [InlineData("K7F2-", 5, "K7F2", 4)]                // the dash goes with the letter after it
    [InlineData(" k7f2 qxm9 ", 11, "K7F2-QXM9", 9)]   // pasted with spaces
    [InlineData("K7F2-QXM9", 2, "K7F2-QXM9", 2)]       // the caret stays where it was
    [InlineData("K7F2x-QXM9", 5, "K7F2-XQXM9", 6)]     // typed in the middle
    public void A_code_is_formatted_while_typing(string typed, int caret, string expected, int expectedCaret)
    {
        Assert.Equal(expected, RelayLink.Typing(typed, ref caret));
        Assert.Equal(expectedCaret, caret);
    }

    [Fact]
    public void New_codes_are_readable_and_differ()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => RelayLink.NewCode()).ToList();
        Assert.All(codes, c => Assert.Equal(c, RelayLink.Normalize(c)));
        Assert.Equal(200, codes.Distinct().Count());
    }

    static async Task Until(Func<bool> condition, int seconds = 10)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed.TotalSeconds > seconds) Assert.Fail("waited too long");
            await Task.Delay(20);
        }
    }

    /// <summary>The relay protocol as relay/src/index.js has it, enough for the tests: rooms of up to five, numbers, "hello/join/left/full", passing on.</summary>
    sealed class TestRelay : IAsyncDisposable
    {
        readonly HttpListener _http = new();
        readonly CancellationTokenSource _cts = new();
        readonly Dictionary<string, Dictionary<byte, WebSocket>> _rooms = [];
        readonly SemaphoreSlim _gate = new(1, 1);
        public string Url { get; private init; } = "";
        public ConcurrentBag<byte[]> Seen { get; } = [];
        public List<string> Rooms { get { lock (_rooms) return [.. _rooms.Keys]; } }

        public static TestRelay Start()
        {
            var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
            var r = new TestRelay { Url = $"ws://127.0.0.1:{port}" };
            r._http.Prefixes.Add($"http://127.0.0.1:{port}/");
            r._http.Start();
            _ = r.AcceptLoop();
            return r;
        }

        async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _http.GetContextAsync(); } catch { return; }
                _ = Task.Run(() => Serve(ctx));
            }
        }

        async Task Send(WebSocket ws, string text) { try { await ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, default); } catch { } }

        async Task Serve(HttpListenerContext ctx)
        {
            var room = ctx.Request.Url!.AbsolutePath.Split('/').Last();
            var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
            byte me;
            await _gate.WaitAsync();
            try
            {
                Dictionary<byte, WebSocket> people;
                lock (_rooms) people = _rooms.TryGetValue(room, out var p) ? p : _rooms[room] = [];
                if (people.Count >= 5) { await Send(ws, "full"); await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "full", default); return; }
                me = 1; while (people.ContainsKey(me)) me++;
                await Send(ws, $"hello {me} {string.Join(",", people.Keys.Order())}");
                foreach (var o in people.Values) await Send(o, "join " + me);
                people[me] = ws;
            }
            finally { _gate.Release(); }
            var buffer = new byte[300 * 1024];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    using var frame = new MemoryStream();
                    WebSocketReceiveResult r;
                    do { r = await ws.ReceiveAsync(buffer, _cts.Token); frame.Write(buffer, 0, r.Count); } while (!r.EndOfMessage && r.MessageType != WebSocketMessageType.Close);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    if (r.MessageType == WebSocketMessageType.Text) { if (Encoding.UTF8.GetString(frame.ToArray()) == "ping") await Send(ws, "pong"); continue; }
                    var data = frame.ToArray();
                    Seen.Add(data);
                    var target = data[0]; data[0] = me;
                    await _gate.WaitAsync();
                    try
                    {
                        foreach (var (id, o) in _rooms[room])
                            if (id != me && (target == 0 || target == id)) try { await o.SendAsync(data, WebSocketMessageType.Binary, true, default); } catch { }
                    }
                    finally { _gate.Release(); }
                }
            }
            catch { }
            await _gate.WaitAsync();
            try
            {
                _rooms[room].Remove(me);
                foreach (var o in _rooms[room].Values) await Send(o, "left " + me);
            }
            finally { _gate.Release(); }
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", default); } catch { }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _http.Stop(); } catch { }
            await Task.Delay(50);
        }
    }
}
