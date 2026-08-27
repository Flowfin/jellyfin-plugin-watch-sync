using System;
using System.Text.Json.Nodes;
using System.Threading;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Tests.Harness;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What the two-server harness is, which is #77's second condition and the four surfaces it is
/// about.
///
/// Every case here mutates one side and reads the other. That is the shape the condition asks
/// for and it is the only shape that catches the failure: two sides sharing a store, a record, a
/// library or a clock make the cases written on top of this harness pass for a reason that is
/// nowhere in their bodies, and the first thing anybody hears of it is a rule that was never
/// exercised shipping broken.
/// </summary>
public sealed class TwoServersTests
{
    private const string DocumentName = "agreed";

    /// <summary>
    /// The stores are two directories rather than one, so a document one side wrote is not a
    /// document the other side finds.
    /// </summary>
    [Fact]
    public void ADocumentWrittenOnOneSideIsNotInTheOtherSidesStore()
    {
        using var servers = TwoServers.Create();

        servers.Here.Store.Write(DocumentName, _ => Document("here"));

        Assert.NotNull(servers.Here.Store.Read(DocumentName));
        Assert.Null(servers.There.Store.Read(DocumentName));
        Assert.NotEqual(servers.Here.DataPath, servers.There.DataPath);
    }

    /// <summary>
    /// The user data records are two dictionaries rather than one, so a write this plugin makes
    /// through one side's adapter reaches that side and no other.
    /// </summary>
    [Fact]
    public void ARecordWrittenOnOneSideIsNotHeldByTheOther()
    {
        using var servers = TwoServers.Create();
        var work = UserDataFixtures.Work(Guid.NewGuid(), 90 * TimeSpan.TicksPerMinute);

        servers.Here.UserData.Write(
            servers.Someone,
            work,
            new SyncedState(true, 1, 0, null),
            UserDataSaveReason.UpdateUserData,
            CancellationToken.None);

        Assert.True(servers.Here.HeldFor(servers.Someone, work)!.Played);
        Assert.Null(servers.There.HeldFor(servers.Someone, work));
    }

    /// <summary>
    /// The libraries are two lists rather than one. An item the peer holds and this server does
    /// not is the ordinary case rather than a fault, and a harness whose sides shared a library
    /// could not produce it at all.
    /// </summary>
    [Fact]
    public void AnItemGivenToOneSidesLibraryIsNotInTheOthers()
    {
        using var servers = TwoServers.Create();

        servers.Here.Library.Add(UserDataFixtures.Work(Guid.NewGuid(), null));

        Assert.Single(servers.Here.Library);
        Assert.Empty(servers.There.Library);
    }

    /// <summary>
    /// The clocks are two clocks rather than one, which is what makes skew something a case can
    /// set. Both sides start at the same moment so a case that is not about skew says nothing
    /// about it.
    /// </summary>
    [Fact]
    public void AdvancingOneSidesClockLeavesTheOtherWhereItWas()
    {
        using var servers = TwoServers.Create();

        servers.Here.Clock.Advance(TimeSpan.FromHours(3));

        Assert.Equal(TwoServers.Epoch.AddHours(3), servers.Here.Clock.Now);
        Assert.Equal(TwoServers.Epoch, servers.There.Clock.Now);
    }

    /// <summary>
    /// A clock that could be wound back would let a case build a pair of moments no run of this
    /// plugin observes on one side, and every rule driven with that pair would then be about a
    /// state that cannot happen.
    /// </summary>
    [Fact]
    public void ASideSClockCannotBeWoundBack()
    {
        using var servers = TwoServers.Create();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => servers.Here.Clock.Advance(TimeSpan.FromSeconds(-1)));
    }

    /// <summary>
    /// The ordinary case for the link, and the one every fault below is a departure from: a body
    /// handed over reaches the far side on the next delivery and reaches nowhere else.
    /// </summary>
    [Fact]
    public void ABodyHandedToTheLinkReachesTheFarSideAndNotTheSender()
    {
        using var servers = TwoServers.Create();

        servers.Link.Send(servers.Here, "one");
        servers.Link.Deliver();

        Assert.Equal(new[] { "one" }, servers.There.Inbox);
        Assert.Empty(servers.Here.Inbox);
    }

    /// <summary>
    /// The link carries in both directions, and a case about an exchange needs it to: the answer
    /// to a pull goes back the way the request came.
    /// </summary>
    [Fact]
    public void TheLinkCarriesInBothDirections()
    {
        using var servers = TwoServers.Create();

        servers.Link.Send(servers.There, "answer");
        servers.Link.Deliver();

        Assert.Equal(new[] { "answer" }, servers.Here.Inbox);
        Assert.Empty(servers.There.Inbox);
    }

    /// <summary>
    /// A body sits in flight until a delivery is asked for, which is what makes a delay something
    /// a case can observe: with delivery on the send there is no round to hold a body over into.
    /// </summary>
    [Fact]
    public void NothingArrivesUntilADeliveryIsAskedFor()
    {
        using var servers = TwoServers.Create();

        servers.Link.Send(servers.Here, "one");

        Assert.Empty(servers.There.Inbox);
    }

    private static StoredDocument Document(string who)
    {
        var fields = new JsonObject
        {
            ["version"] = JsonValue.Create(DocumentVersions.Current),
            ["who"] = JsonValue.Create(who),
        };

        return StoredDocument.Read(fields.ToJsonString(), DocumentVersions.Current).Document!;
    }
}
