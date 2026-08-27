using System;
using Jellyfin.Plugin.WatchSync.Tests.Harness;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What the link can be told to do to a body, which is #77's third condition.
///
/// The four faults are told rather than drawn. A case that has to be run several times before it
/// meets the state it is about is the case that gets deleted the first time somebody is in a
/// hurry, and a harness that produced faults at random would make every case written on it one
/// of those.
///
/// Each case here asserts what the fault does and, where two faults could be confused, what it
/// does not do. A delay and a reorder are the pair worth separating: one moves when a body
/// arrives and the other moves what it arrives after, and a link that answered both the same way
/// would let a case about ordering pass on a link that only ever ran late.
/// </summary>
public sealed class LinkFaultTests
{
    /// <summary>
    /// A dropped body is gone rather than late, so no later delivery produces it either. The
    /// count on the link is what separates a body nobody sent from one that was sent and lost.
    /// </summary>
    [Fact]
    public void ADroppedBodyNeverArrivesOnAnyDelivery()
    {
        using var servers = TwoServers.Create();

        servers.Link.Send(servers.Here, "one", LinkFault.Drop);
        servers.Link.Deliver();
        servers.Link.Deliver();

        Assert.Empty(servers.There.Inbox);
        Assert.Equal(1, servers.Link.Dropped);
    }

    /// <summary>
    /// A delayed body arrives on the delivery after next, which is late rather than lost.
    /// </summary>
    [Fact]
    public void ADelayedBodyArrivesOnTheDeliveryAfterNext()
    {
        using var servers = TwoServers.Create();

        servers.Link.Send(servers.Here, "one", LinkFault.Delay);
        servers.Link.Deliver();

        Assert.Empty(servers.There.Inbox);

        servers.Link.Deliver();

        Assert.Equal(new[] { "one" }, servers.There.Inbox);
    }

    /// <summary>
    /// A delay moves when a body arrives and never what it arrives after. This is the half that
    /// separates it from a reorder, and it is asserted rather than described because a link that
    /// confused the two would still pass the case above.
    /// </summary>
    [Fact]
    public void ADelayLeavesTheOrderOfWhatIsLeftAlone()
    {
        using var servers = TwoServers.Create();

        servers.Link.Send(servers.Here, "one", LinkFault.Delay);
        servers.Link.Send(servers.Here, "two");
        servers.Link.Deliver();
        servers.Link.Deliver();

        Assert.Equal(new[] { "two", "one" }, servers.There.Inbox);
    }

    /// <summary>
    /// A duplicated body arrives twice on one delivery, which is what a retry after an answer
    /// that was lost rather than never sent looks like to the receiver.
    /// </summary>
    [Fact]
    public void ADuplicatedBodyArrivesTwiceOnOneDelivery()
    {
        using var servers = TwoServers.Create();

        servers.Link.Send(servers.Here, "one", LinkFault.Duplicate);
        servers.Link.Deliver();

        Assert.Equal(new[] { "one", "one" }, servers.There.Inbox);
    }

    /// <summary>
    /// A reordered body arrives after the one sent behind it, on the same delivery. Both arrive,
    /// which is what separates this from a drop, and both arrive now, which is what separates it
    /// from a delay.
    /// </summary>
    [Fact]
    public void AReorderedBodyArrivesAfterTheOneSentBehindIt()
    {
        using var servers = TwoServers.Create();

        servers.Link.Send(servers.Here, "one", LinkFault.Reorder);
        servers.Link.Send(servers.Here, "two");
        servers.Link.Deliver();

        Assert.Equal(new[] { "two", "one" }, servers.There.Inbox);
    }

    /// <summary>
    /// A body with nothing behind it has nothing to yield to, so it is carried in place rather
    /// than held back. Holding it would make a reorder a delay whenever a case reordered the last
    /// body of a round, which is the case somebody writes without noticing.
    /// </summary>
    [Fact]
    public void AReorderedBodyWithNothingBehindItIsCarriedInPlace()
    {
        using var servers = TwoServers.Create();

        servers.Link.Send(servers.Here, "one", LinkFault.Reorder);
        servers.Link.Deliver();

        Assert.Equal(new[] { "one" }, servers.There.Inbox);
    }

    /// <summary>
    /// One yielding body swaps with the body behind it and nothing else moves, so a case
    /// reordering one of several bodies is about that one.
    /// </summary>
    [Fact]
    public void AReorderMovesOnePairAndLeavesTheRestWhereTheyWere()
    {
        using var servers = TwoServers.Create();

        servers.Link.Send(servers.Here, "one");
        servers.Link.Send(servers.Here, "two", LinkFault.Reorder);
        servers.Link.Send(servers.Here, "three");
        servers.Link.Send(servers.Here, "four");
        servers.Link.Deliver();

        Assert.Equal(new[] { "one", "three", "two", "four" }, servers.There.Inbox);
    }

    /// <summary>
    /// A link joins two sides. A third has no far side to be carried to, and a link that guessed
    /// one would deliver a body to whichever side it happened to hold first.
    /// </summary>
    [Fact]
    public void ASideThisLinkDoesNotJoinIsRefused()
    {
        using var servers = TwoServers.Create();
        using var third = HarnessSide.Create("third", TwoServers.Epoch);

        Assert.Throws<ArgumentException>(() => servers.Link.Send(third, "one"));
    }
}
