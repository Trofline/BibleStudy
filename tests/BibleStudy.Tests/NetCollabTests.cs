using System.Net;
using BibleStudy.Core.Collab;
using Xunit;

namespace BibleStudy.Tests;

public class NetCollabTests
{
    [Fact]
    public async Task Two_people_in_the_same_network_connect_without_codes_and_talk()
    {
        using var anna = new NetCollab { Name = "Anna" };
        using var ben = new NetCollab { Name = "Ben" };
        string? asked = null, annaCheck = null;
        anna.Approve = a => { asked = a.Name; annaCheck = a.CheckCode; return Task.FromResult(true); };
        var incoming = new TaskCompletionSource<NetLink>();
        anna.Incoming += l => incoming.TrySetResult(l);
        anna.Start(0, 47923); ben.Start(0, 47923);

        using var mine = await ben.ConnectAsync(IPAddress.Loopback, anna.Port, TimeSpan.FromSeconds(10));
        using var theirs = await incoming.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Ben", asked);
        Assert.Equal("Anna", mine.PeerName);
        Assert.Equal(annaCheck, mine.CheckCode);

        var gotByAnna = new TaskCompletionSource<string>();
        var gotByBen = new List<string>();
        var benDone = new TaskCompletionSource();
        theirs.Received += m => gotByAnna.TrySetResult(m);
        mine.Received += m => { lock (gotByBen) { gotByBen.Add(m); if (gotByBen.Count == 50) benDone.TrySetResult(); } };
        theirs.Start(); mine.Start();
        Assert.True(mine.IsOpen && theirs.IsOpen);

        mine.Send("hallo");
        Assert.Equal("hallo", await gotByAnna.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        for (var i = 0; i < 50; i++) theirs.Send("n" + i);
        await benDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(Enumerable.Range(0, 50).Select(i => "n" + i), gotByBen);

        var closed = new TaskCompletionSource();
        theirs.Closed += _ => closed.TrySetResult();
        mine.Dispose();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_friend_is_recognised_by_id_and_token()
    {
        using var anna = new NetCollab { Name = "Anna", Id = "ANNA1" };
        using var ben = new NetCollab { Name = "Ben", Id = "BEN1" };
        CollabAsk? got = null;
        anna.Approve = a => { got = a; return Task.FromResult(true); };
        anna.Start(0, 47927); ben.Start(0, 47927);
        using var link = await ben.ConnectAsync(IPAddress.Loopback, anna.Port, TimeSpan.FromSeconds(10), "secret-token");
        Assert.Equal("BEN1", got!.Id);
        Assert.Equal("secret-token", got.Token);
        Assert.Equal("ANNA1", link.PeerId);
        Assert.Equal(IPAddress.Loopback, link.RemoteAddress);
    }

    [Fact]
    public async Task A_person_who_says_no_is_not_connected()
    {
        using var anna = new NetCollab { Name = "Anna", Approve = _ => Task.FromResult(false) };
        using var ben = new NetCollab { Name = "Ben" };
        anna.Start(0, 47924); ben.Start(0, 47924);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ben.ConnectAsync(IPAddress.Loopback, anna.Port, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Someone_with_another_program_on_the_port_is_turned_away()
    {
        using var anna = new NetCollab { Name = "Anna", Approve = _ => Task.FromResult(true) };
        anna.Start(0, 47925);
        using var stranger = new System.Net.Sockets.TcpClient();
        await stranger.ConnectAsync(IPAddress.Loopback, anna.Port);
        var s = stranger.GetStream();
        await s.WriteAsync("GET / HTTP/1.1\r\n\r\n"u8.ToArray());
        var buffer = new byte[64];
        // the app answers with its own greeting and then closes: nothing of the stranger is taken
        var total = 0;
        try { for (int n; (n = await s.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10))) > 0;) total += n; } catch (IOException) { }
        Assert.True(total <= 4);
    }

    [Fact]
    public async Task People_see_each_other_by_name()
    {
        using var anna = new NetCollab { Name = "Anna" };
        using var ben = new NetCollab { Name = "Ben" };
        anna.Start(0, 47926);
        ben.Start(0, 47926);
        // both share one call-out port in this test: whoever holds it hears the other; asked by address the other answers too
        ben.AddAddress(IPAddress.Loopback); anna.AddAddress(IPAddress.Loopback);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !(anna.Neighbors.Any(n => n.Name == "Ben") || ben.Neighbors.Any(n => n.Name == "Anna")))
            await Task.Delay(200);
        Assert.True(anna.Neighbors.Any(n => n.Name == "Ben") || ben.Neighbors.Any(n => n.Name == "Anna"));
    }
}
