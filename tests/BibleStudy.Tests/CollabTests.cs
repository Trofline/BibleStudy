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

    [Fact]
    public async Task A_wrong_code_is_refused()
    {
        using var guest = new CollabPeer();
        await Assert.ThrowsAnyAsync<Exception>(() => guest.AcceptInvitationAsync("kein code"));
    }
}
