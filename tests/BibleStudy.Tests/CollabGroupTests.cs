using BibleStudy.Core.Collab;
using Xunit;

namespace BibleStudy.Tests;

public class CollabGroupTests
{
    /// <summary>Two ends of one connection, in memory.</summary>
    sealed class Pipe : ICollabLink
    {
        public Pipe? Other;
        public event Action? Connected;
        public event Action<string>? Closed;
        public event Action<string>? Received;
        public bool IsOpen => true;
        public void Send(string text) => Other!.Received?.Invoke(text);
        public void Dispose() { Closed?.Invoke("left"); _ = Connected; }
        public static (Pipe, Pipe) Make() { var a = new Pipe(); var b = new Pipe { Other = a }; a.Other = b; return (a, b); }
    }

    static (CollabGroup Group, List<(string From, string Name, string Raw)> Got) Person(string id, string name)
    {
        var g = new CollabGroup(id) { MyName = name };
        var got = new List<(string, string, string)>();
        g.Message += (_, fid, fn, raw) => got.Add((fid, fn, raw));
        return (g, got);
    }

    [Fact]
    public void A_message_reaches_everyone_in_a_chain_once_with_its_writer()
    {
        var (anna, annaGot) = Person("A", "Anna");
        var (ben, benGot) = Person("B", "Ben");
        var (carla, carlaGot) = Person("C", "Carla");
        var (ab1, ab2) = Pipe.Make(); anna.Add(ab1); ben.Add(ab2);
        var (bc1, bc2) = Pipe.Make(); ben.Add(bc1); carla.Add(bc2);

        anna.Send(new { t = "chat", text = "hallo" });

        Assert.Single(benGot);
        Assert.Single(carlaGot);
        Assert.Equal(("A", "Anna"), (carlaGot[0].From, carlaGot[0].Name));
        Assert.Contains("hallo", carlaGot[0].Raw);
        Assert.Empty(annaGot);   // the own message does not come back
    }

    [Fact]
    public void In_a_ring_nothing_goes_round_forever()
    {
        var (a, aGot) = Person("A", "Anna");
        var (b, bGot) = Person("B", "Ben");
        var (c, cGot) = Person("C", "Carla");
        var (ab1, ab2) = Pipe.Make(); a.Add(ab1); b.Add(ab2);
        var (bc1, bc2) = Pipe.Make(); b.Add(bc1); c.Add(bc2);
        var (ca1, ca2) = Pipe.Make(); c.Add(ca1); a.Add(ca2);

        b.Send(new { t = "chat", text = "x" });

        Assert.Single(aGot);
        Assert.Single(cGot);
        Assert.Empty(bGot);
    }

    [Fact]
    public void A_message_to_one_person_is_not_passed_on()
    {
        var (anna, _) = Person("A", "Anna");
        var (ben, benGot) = Person("B", "Ben");
        var (carla, carlaGot) = Person("C", "Carla");
        var (ab1, ab2) = Pipe.Make(); anna.Add(ab1); ben.Add(ab2);
        var (bc1, bc2) = Pipe.Make(); ben.Add(bc1); carla.Add(bc2);

        anna.SendTo(ab1, new { t = "friend" });

        Assert.Single(benGot);
        Assert.Empty(carlaGot);
    }

    [Fact]
    public void An_older_version_without_ids_is_known_by_its_connection()
    {
        var (anna, annaGot) = Person("A", "Anna");
        var (mine, theirs) = Pipe.Make(); anna.Add(mine);
        theirs.Send("{\"t\":\"chat\",\"text\":\"alt\"}");
        Assert.Equal(anna.FallbackId(mine), annaGot.Single().From);
    }
}
