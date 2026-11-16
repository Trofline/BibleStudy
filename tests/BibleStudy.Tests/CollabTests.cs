using BibleStudy.Core.Collab;
using Xunit;

namespace BibleStudy.Tests;

public class CollabTests
{
    [Fact]
    public async Task Two_peers_connect_with_the_two_codes_and_exchange_messages()
    {
        using var host = new CollabPeer();
        using var guest = new CollabPeer();
        var got = new TaskCompletionSource<string>();
        guest.Received += m => got.TrySetResult(m);
        var back = new TaskCompletionSource<string>();
        host.Received += m => back.TrySetResult(m);

        var invitation = await host.CreateInvitationAsync();
        Assert.StartsWith(CollabPeer.Prefix, invitation);
        var answer = await guest.AcceptInvitationAsync(invitation);
        host.CompleteWithAnswer(answer);

        await host.WaitOpenAsync(TimeSpan.FromSeconds(30));
        await guest.WaitOpenAsync(TimeSpan.FromSeconds(30));
        host.Send("hallo");
        Assert.Equal("hallo", await got.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        guest.Send("zurück");
        Assert.Equal("zurück", await back.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData("turn:relay.example.org:3478", "turn:relay.example.org:3478")]
    [InlineData("relay.example.org:3478", "turn:relay.example.org:3478")]
    [InlineData("  turns:relay.example.org:5349  ", "turns:relay.example.org:5349")]
    public void A_typed_relay_address_is_completed(string typed, string expected) => Assert.Equal(expected, RelayServer.Parse(typed, "u", "p")!.Url);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("stun:stun.example.org:3478")]
    [InlineData("two words")]
    public void An_unusable_relay_address_is_ignored(string typed) => Assert.Null(RelayServer.Parse(typed, "u", "p"));

    [Fact]
    public async Task A_wrong_code_is_refused()
    {
        using var guest = new CollabPeer();
        await Assert.ThrowsAnyAsync<Exception>(() => guest.AcceptInvitationAsync("kein code"));
    }

    /// <summary>A ZeroTier or Tailscale network is a network card of its own: its address must be in the invitation, not only the one with the internet route.</summary>
    [Fact]
    public async Task The_invitation_offers_the_address_of_every_network_of_this_device()
    {
        var own = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(a => a.ToString()).Distinct().ToList();
        if (own.Count < 2) return;   // one network only: nothing to show here
        using var host = new CollabPeer();
        var code = await host.CreateInvitationAsync();
        var b64 = code[CollabPeer.Prefix.Length..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        using var z = new System.IO.Compression.DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), System.IO.Compression.CompressionMode.Decompress);
        var sdp = new StreamReader(z).ReadToEnd();
        var missing = own.Where(a => !sdp.Contains(" " + a + " ", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "not offered: " + string.Join(", ", missing));
    }

    /// <summary>People swap the codes by messenger: the answer often comes back much later. The guest must still be reachable then.</summary>
    [Fact]
    public async Task The_connection_comes_up_even_when_the_answer_is_pasted_much_later()
    {
        using var host = new CollabPeer();
        using var guest = new CollabPeer();
        var invitation = await host.CreateInvitationAsync();
        var answer = await guest.AcceptInvitationAsync(invitation);
        await Task.Delay(TimeSpan.FromSeconds(45));
        host.CompleteWithAnswer(answer);
        await host.WaitOpenAsync(TimeSpan.FromSeconds(30));
        await guest.WaitOpenAsync(TimeSpan.FromSeconds(30));
        Assert.True(host.IsOpen && guest.IsOpen);
    }

    /// <summary>A second click on "Connect" after a failed try: it says that a new invitation is needed, not that the code is wrong.</summary>
    [Fact]
    public async Task An_invitation_takes_one_answer_only()
    {
        using var host = new CollabPeer();
        using var guest = new CollabPeer();
        var answer = await guest.AcceptInvitationAsync(await host.CreateInvitationAsync());
        host.CompleteWithAnswer(answer);
        var ex = Assert.Throws<InvalidOperationException>(() => host.CompleteWithAnswer(answer));
        Assert.Equal("used", ex.Message);
    }
}
