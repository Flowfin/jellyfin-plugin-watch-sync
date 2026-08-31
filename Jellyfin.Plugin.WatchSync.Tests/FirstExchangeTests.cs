using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Conflict;
using Jellyfin.Plugin.WatchSync.Exchange;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Tests.Harness;
using Jellyfin.Plugin.WatchSync.Transfer;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What two servers that have never agreed anything do when they meet, which is #37.
///
/// The decision on that issue is that the first run merges by the conflict table, seeds neither
/// side, overwrites nothing, and records what it cannot decide. Every fact below is one of those
/// four sentences made checkable, and the two worth reading twice are the ones that assert
/// nothing happened: an item the table decides nothing about is left standing, and a pair that
/// has just finished a first exchange has nothing outstanding for the next one.
///
/// The whole file is written under the rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>. Every moment a rule reads is the
/// harness clock rather than the machine clock, and the two servers are the in-process harness
/// rather than two installations.
/// </summary>
public sealed class FirstExchangeTests
{
    private static readonly Guid _pairing = new Guid("11111111111111111111111111111111");

    private static readonly DateTimeOffset _agreedAt = TwoServers.Epoch.AddHours(2);

    private static readonly DateTime _now = TwoServers.Epoch.UtcDateTime.AddHours(2);

    /// <summary>
    /// The point the two sides confirmed is what says the whole set was exchanged, so a record
    /// carrying none is a pair still in its first exchange.
    ///
    /// This is the first condition of #37, the half that makes the mode distinguishable from an
    /// ordinary run without anybody being told which it is. Both directions are driven, because
    /// a reading that answered <c>First</c> for everything would satisfy the sentence this fact
    /// is about and would be wrong about every pair that has ever finished a run.
    /// </summary>
    [Fact]
    public void APairThatHasConfirmedNoPointIsStillInItsFirstExchange()
    {
        var records = AgreedRecords.NoneYet(_pairing, Someone());

        Assert.Equal(ExchangeMode.First, FirstExchange.ModeFor(records));

        var confirmed = Watermark.Confirmed("a-point-the-peer-named", _agreedAt).Mark;

        Assert.Equal(ExchangeMode.Ordinary, FirstExchange.ModeFor(records.At(confirmed!)));
    }

    /// <summary>
    /// A pair that has finished a first exchange may not be run through one again.
    ///
    /// This mode assumes there is nothing to have moved since a point, because there is no
    /// point. Letting it run over a pair that has one would be the ordinary work done under the
    /// first exchange name, which is exactly what #37 asks the mode to be distinguishable from,
    /// and it would do it silently.
    /// </summary>
    [Fact]
    public void ARunOverAPairThatHasAlreadyFinishedOneIsRefused()
    {
        var someone = Someone();
        var confirmed = Watermark.Confirmed("a-point-the-peer-named", _agreedAt).Mark;
        var records = AgreedRecords.NoneYet(_pairing, someone).At(confirmed!);

        Assert.Throws<ArgumentException>(() => FirstExchange.Over(
            records,
            new[] { Pair(someone, Guid.NewGuid(), Watched(1), NeverTouched()) },
            PositionRecency.DefaultToleratedSkew,
            _now,
            _agreedAt));
    }

    /// <summary>
    /// Two populated servers, item by item, which is the second condition of #37.
    ///
    /// The two sides are the harness, each with a store, a record and a clock of its own, and
    /// each item is written into one side records and read back out of it through the same
    /// adapter the plugin uses. Six items rather than one, because the outcome the decision
    /// specifies is different per pair of states and a fixture holding one pair proves the
    /// answer for that pair alone.
    ///
    /// The undecidable pair sits in the middle rather than at the end. A run that stopped at the
    /// first item it could not answer would leave every item after it out of the record, and a
    /// fixture that put that item last would pass over the whole of that failure.
    /// </summary>
    [Fact]
    public void TwoPopulatedServersMergeByTheTableItemByItem()
    {
        using var servers = TwoServers.Create();

        var finishedHere = Both(servers, Finished(1, At(30)), Partway(900, At(10)));
        var finishedAtThePeer = Both(servers, Partway(700, At(10)), Finished(1, At(40)));
        var twoHistories = Both(servers, Finished(2, At(20)), Finished(3, At(50)));
        var oneSideNeverWatched = Both(servers, Finished(4, At(60)), new SyncedState(true, 0, 0, null));
        var twoPositions = Both(servers, Partway(1200, At(70)), Partway(400, At(5)));
        var untouched = Both(servers, NeverTouched(), NeverTouched());

        var exchange = Run(servers, finishedHere, finishedAtThePeer, twoHistories, oneSideNeverWatched, twoPositions, untouched);

        Assert.Equal(ExchangeMode.First, exchange.Mode);
        Assert.Equal(5, exchange.Decided.Count);

        Holds(exchange, finishedHere, Finished(1, At(30)));
        Holds(exchange, finishedAtThePeer, Finished(1, At(40)));
        Holds(exchange, oneSideNeverWatched, Finished(4, At(60)));
        Holds(exchange, twoPositions, Partway(1200, At(70)));
        Holds(exchange, untouched, NeverTouched());

        var standing = Assert.Single(exchange.Undecided);

        Assert.Equal(twoHistories, standing.Subject.ItemId);
        Assert.Equal(UndecidedReason.TwoHistoriesOfPlaysThatHaveNeverAgreed, standing.Reason);
    }

    /// <summary>
    /// A server holding nothing at all does not clear a completion on the server it meets,
    /// which is the second condition of #34.
    ///
    /// That condition is a property of this run and of no other. An unplayed state wins over a
    /// played one only where an agreement separates an intent from an old value, and a first
    /// exchange is by definition the run where there is no agreement to read, so what protects
    /// the established side here is the ratchet holding and nothing else.
    ///
    /// The shape is the one the issue leads with: a fresh, empty server meeting an established
    /// one. It is not a side holding the work unplayed at a position, which the pairs above
    /// drive. An untouched side carries no position, no count and no date, so every value the
    /// table could read from it is the empty one, and a rule that took any of those three for
    /// something a person did would clear the other side's history on first contact.
    ///
    /// Both directions are driven, because which of the two servers runs the exchange is not
    /// something either of them chooses.
    /// </summary>
    [Fact]
    public void AnUntouchedServerDoesNotClearACompletionOnTheOtherSide()
    {
        using var servers = TwoServers.Create();

        var finishedHere = Both(servers, Finished(4, At(60)), NeverTouched());
        var finishedAtThePeer = Both(servers, NeverTouched(), Finished(4, At(60)));

        var exchange = Run(servers, finishedHere, finishedAtThePeer);

        Assert.Equal(2, exchange.Decided.Count);

        Holds(exchange, finishedHere, Finished(4, At(60)));
        Holds(exchange, finishedAtThePeer, Finished(4, At(60)));

        Assert.False(For(exchange, finishedHere).ChangesHere);
        Assert.True(For(exchange, finishedHere).ChangesAtThePeer);
        Assert.True(For(exchange, finishedAtThePeer).ChangesHere);
        Assert.False(For(exchange, finishedAtThePeer).ChangesAtThePeer);
    }

    /// <summary>
    /// The second exchange after a first one moves nothing, which is the third condition of #37.
    ///
    /// It is asserted against the type that decides what an exchange has to offer rather than
    /// against a second run, because there is no ordinary exchange in this tree and a fact
    /// written against one invented here would be a fact about the invention.
    /// <c>OutstandingChanges</c> is what a later run reads: it compares what a side holds against
    /// the agreement, and an empty answer on both sides is nothing left to say.
    ///
    /// Both sides are asked. A first exchange whose agreement matched only the side that ran it
    /// would pass a fact that asked one of them, and the pair would go on exchanging the same
    /// item forever.
    /// </summary>
    [Fact]
    public void TheSecondExchangeAfterAFirstOneMovesNothing()
    {
        using var servers = TwoServers.Create();

        var finished = Both(servers, Finished(1, At(30)), Partway(900, At(10)));
        var inProgress = Both(servers, Partway(1200, At(70)), Partway(400, At(5)));
        var untouched = Both(servers, NeverTouched(), NeverTouched());
        var exchange = Run(servers, finished, inProgress, untouched);

        Assert.Equal(3, exchange.Decided.Count);

        foreach (var resolution in exchange.Decided)
        {
            Apply(servers, servers.Here, resolution);
            Apply(servers, servers.There, resolution);
        }

        foreach (var resolution in exchange.Decided)
        {
            Assert.Empty(Outstanding(servers, servers.Here, exchange, resolution));
            Assert.Empty(Outstanding(servers, servers.There, exchange, resolution));
        }
    }

    /// <summary>
    /// An interrupted first exchange resumes without redoing what it already agreed, which is
    /// the fourth condition of #37.
    ///
    /// The run that resumes is the same mode as the run it resumes, because neither confirmed a
    /// point and the mode is read out of that. What it does differently is skip the items the
    /// earlier run agreed, and the assertion is on the agreement rather than on the answer: an
    /// item agreed at one moment and agreed again at another is a record two servers reached
    /// twice, and every later exchange is decided against whichever of the two was written last.
    /// </summary>
    [Fact]
    public void AnInterruptedFirstExchangeResumesWithoutRedoingWhatItAlreadyAgreed()
    {
        using var servers = TwoServers.Create();

        var reached = Both(servers, Finished(1, At(30)), Partway(900, At(10)));
        var notReached = Both(servers, Partway(1200, At(70)), Partway(400, At(5)));
        var interrupted = Run(servers, reached);

        Assert.Single(interrupted.Decided);
        Assert.Equal(ExchangeMode.First, FirstExchange.ModeFor(interrupted.Agreed));

        var resumed = Resume(servers, interrupted.Agreed, _agreedAt.AddHours(5), reached, notReached);

        Assert.Equal(ExchangeMode.First, resumed.Mode);
        Assert.Equal(ResolutionAnswer.AlreadyAgreed, For(resumed, reached).Answer);
        Assert.Equal(ResolutionAnswer.Decided, For(resumed, notReached).Answer);
        Assert.Null(For(resumed, reached).Resolved);

        Assert.Equal(_agreedAt, resumed.Agreed.For(reached)!.AgreedAt);
        Assert.Equal(_agreedAt.AddHours(5), resumed.Agreed.For(notReached)!.AgreedAt);
        Assert.Equal(2, resumed.Agreed.Count);
    }

    /// <summary>
    /// Two counts that have never agreed and are both above zero are not told apart.
    ///
    /// Two sides holding two and three plays may be three watchings and may be five, and the two
    /// answers a reader reaches for are both a guess: the sum invents plays for a pair whose
    /// histories already met, and the greater throws away watchings the other side recorded. So
    /// the item stands, nothing is agreed for it, and neither side is asked to change.
    /// </summary>
    [Fact]
    public void TwoHistoriesOfPlaysThatNeverAgreedAreLeftStanding()
    {
        var someone = Someone();

        var exchange = FirstExchange.Over(
            AgreedRecords.NoneYet(_pairing, someone),
            new[] { Pair(someone, Guid.NewGuid(), Watched(2), Watched(3)) },
            PositionRecency.DefaultToleratedSkew,
            _now,
            _agreedAt);

        var standing = Assert.Single(exchange.Undecided);

        Assert.Equal(UndecidedReason.TwoHistoriesOfPlaysThatHaveNeverAgreed, standing.Reason);
        Assert.Null(standing.Resolved);
        Assert.False(standing.ChangesHere);
        Assert.False(standing.ChangesAtThePeer);
        Assert.Equal(0, exchange.Agreed.Count);
    }

    /// <summary>
    /// Two sides holding the same count agree at it rather than at the sum of it.
    ///
    /// This is the reading the other direction of the same absence produces, and it is the one
    /// that costs somebody their count rather than a watching. Two servers that each hold a work
    /// watched twice are two readings of one history far more often than they are four watchings,
    /// and a run that added them would double every count on the day a pair first meets and add
    /// nothing to what either side knew.
    /// </summary>
    [Fact]
    public void TwoSidesHoldingTheSameCountAgreeAtItRatherThanAtTheSumOfIt()
    {
        var someone = Someone();

        var exchange = FirstExchange.Over(
            AgreedRecords.NoneYet(_pairing, someone),
            new[] { Pair(someone, Guid.NewGuid(), Watched(2), Watched(2)) },
            PositionRecency.DefaultToleratedSkew,
            _now,
            _agreedAt);

        var resolution = Assert.Single(exchange.Decided);

        Assert.Equal(2, resolution.Resolved!.PlayCount);
        Assert.False(resolution.ChangesHere);
        Assert.False(resolution.ChangesAtThePeer);
    }

    /// <summary>
    /// A side holding no plays contributes nothing, so the other side count stands.
    ///
    /// It is the case the pair above is not: a count of zero is not a history that disagrees
    /// with anything, so carrying the other side count invents no play and discards none. A rule
    /// that left this standing as well would leave every item a person has watched on one server
    /// and not on the other undecided, which is most of what a first exchange is for.
    /// </summary>
    [Fact]
    public void ASideWithNoPlaysContributesNothingAndTheOtherCountStands()
    {
        var someone = Someone();

        var exchange = FirstExchange.Over(
            AgreedRecords.NoneYet(_pairing, someone),
            new[] { Pair(someone, Guid.NewGuid(), Watched(0), Watched(3)) },
            PositionRecency.DefaultToleratedSkew,
            _now,
            _agreedAt);

        var resolution = Assert.Single(exchange.Decided);

        Assert.Equal(3, resolution.Resolved!.PlayCount);
        Assert.True(resolution.ChangesHere);
        Assert.False(resolution.ChangesAtThePeer);
    }

    /// <summary>
    /// Two completions holding different positions are left standing.
    ///
    /// The ratchet discards the position offered against a completion, and it names a loser only
    /// where one of the two sides is not played. Where both are, the table decides nothing
    /// between two leftover positions, and this run picking one would be inventing a rule rather
    /// than applying the table. The pair that agrees about the position is driven beside it,
    /// because a run that left every finished item standing would satisfy the first half of this
    /// fact and be useless.
    /// </summary>
    [Fact]
    public void TwoCompletionsHoldingDifferentPositionsAreLeftStanding()
    {
        var someone = Someone();

        var exchange = FirstExchange.Over(
            AgreedRecords.NoneYet(_pairing, someone),
            new[]
            {
                Pair(someone, Guid.NewGuid(), Held(1, 300, At(30)), Held(1, 700, At(40))),
                Pair(someone, Guid.NewGuid(), Held(1, 300, At(30)), Held(1, 300, At(40))),
            },
            PositionRecency.DefaultToleratedSkew,
            _now,
            _agreedAt);

        var standing = Assert.Single(exchange.Undecided);
        var decided = Assert.Single(exchange.Decided);

        Assert.Equal(UndecidedReason.TwoCompletionsHoldingDifferentPositions, standing.Reason);
        Assert.Equal(300, decided.Resolved!.PlaybackPositionTicks);
        Assert.True(decided.Resolved.Played);
    }

    /// <summary>
    /// A peer whose clock is further ahead than the tolerance allows leaves the item standing.
    ///
    /// The position rule refuses to compare the two readings and says so with a refusal of its
    /// own, and that refusal is carried through here as its own reason rather than folded into
    /// the others. A clock failure that reads as anything else costs an evening, which is what
    /// <c>docs/conflicts.md</c> says of the same refusal one layer down.
    /// </summary>
    [Fact]
    public void APeerClockOutsideTheToleranceLeavesTheItemStanding()
    {
        var someone = Someone();

        var exchange = FirstExchange.Over(
            AgreedRecords.NoneYet(_pairing, someone),
            new[]
            {
                Pair(
                    someone,
                    Guid.NewGuid(),
                    Partway(100, _now.AddMinutes(-5)),
                    Partway(200, _now.AddHours(3))),
            },
            PositionRecency.DefaultToleratedSkew,
            _now,
            _agreedAt);

        var standing = Assert.Single(exchange.Undecided);

        Assert.Equal(UndecidedReason.ThePeersClockIsOutsideTheTolerance, standing.Reason);
        Assert.Equal(0, exchange.Agreed.Count);
    }

    /// <summary>
    /// An item the run cannot decide does not stop the items after it.
    ///
    /// A run that treated an undecidable pair as an error would leave every item behind it
    /// unagreed, and the pair would meet the same wall on the next run and every one after it.
    /// The undecidable item is neither first nor last, so a walk that stopped at it and a walk
    /// that skipped the remainder are both refused.
    /// </summary>
    [Fact]
    public void AnItemTheRunCannotDecideDoesNotStopTheOnesAfterIt()
    {
        var someone = Someone();
        var before = Guid.NewGuid();
        var standing = Guid.NewGuid();
        var after = Guid.NewGuid();

        var exchange = FirstExchange.Over(
            AgreedRecords.NoneYet(_pairing, someone),
            new[]
            {
                Pair(someone, before, Watched(1), Watched(1)),
                Pair(someone, standing, Watched(2), Watched(3)),
                Pair(someone, after, Watched(4), Watched(0)),
            },
            PositionRecency.DefaultToleratedSkew,
            _now,
            _agreedAt);

        Assert.Equal(2, exchange.Decided.Count);
        Assert.Equal(3, exchange.Resolutions.Count);
        Assert.NotNull(exchange.Agreed.For(before));
        Assert.Null(exchange.Agreed.For(standing));
        Assert.NotNull(exchange.Agreed.For(after));
    }

    /// <summary>
    /// An item about another mapped user is refused rather than agreed.
    ///
    /// A record of what two sides agreed is one user record, and an entry filed under it for
    /// somebody else is one person watch history answered against another on every later
    /// exchange.
    /// </summary>
    [Fact]
    public void AnItemAboutAnotherMappedUserIsRefused()
    {
        Assert.Throws<ArgumentException>(() => FirstExchange.Over(
            AgreedRecords.NoneYet(_pairing, Someone()),
            new[] { Pair(Guid.NewGuid(), Guid.NewGuid(), Watched(1), Watched(1)) },
            PositionRecency.DefaultToleratedSkew,
            _now,
            _agreedAt));
    }

    private static Guid Someone() => new Guid("22222222222222222222222222222222");

    private static SyncedState NeverTouched() => new SyncedState(false, 0, 0, null);

    private static SyncedState Finished(int playCount, DateTime lastPlayed) =>
        new SyncedState(true, playCount, 0, lastPlayed);

    private static SyncedState Held(int playCount, long positionTicks, DateTime lastPlayed) =>
        new SyncedState(true, playCount, positionTicks, lastPlayed);

    private static SyncedState Partway(long positionTicks, DateTime lastPlayed) =>
        new SyncedState(false, 0, positionTicks, lastPlayed);

    private static SyncedState Watched(int playCount) =>
        playCount > 0 ? Finished(playCount, At(30)) : NeverTouched();

    private static DateTime At(int minutes) => TwoServers.Epoch.UtcDateTime.AddMinutes(minutes);

    private static ItemOnBothSides Pair(
        Guid mappedUserId,
        Guid itemId,
        SyncedState here,
        SyncedState atThePeer) =>
        new ItemOnBothSides(
            TransferSubject.From(mappedUserId, itemId, BaseItemKind.Movie).Value!,
            here,
            atThePeer);

    private static FirstExchangeResolution For(FirstExchange exchange, Guid itemId) =>
        exchange.Resolutions.Single(each => each.Subject.ItemId == itemId);

    private static void Holds(FirstExchange exchange, Guid itemId, SyncedState expected)
    {
        var resolved = For(exchange, itemId).Resolved;

        Assert.NotNull(resolved);
        Assert.Equal(expected.Played, resolved!.Played);
        Assert.Equal(expected.PlayCount, resolved.PlayCount);
        Assert.Equal(expected.PlaybackPositionTicks, resolved.PlaybackPositionTicks);
        Assert.Equal(expected.LastPlayedDate, resolved.LastPlayedDate);
    }

    /// <summary>
    /// Gives one item to both sides, each side holding what it is handed.
    ///
    /// It is a helper rather than lines inside a case because the bound in
    /// <c>Harness/case-bound.txt</c> is what says a two-server property has to read as an
    /// ordinary case, and six items written out side by side is the setup that bound refuses.
    /// </summary>
    /// <param name="servers">The two sides.</param>
    /// <param name="here">What this server is to hold.</param>
    /// <param name="atThePeer">What the peer is to hold.</param>
    /// <returns>The identifier of the item both sides were given.</returns>
    private static Guid Both(TwoServers servers, SyncedState here, SyncedState atThePeer)
    {
        var itemId = Guid.NewGuid();

        Write(servers, servers.Here, itemId, here);
        Write(servers, servers.There, itemId, atThePeer);

        return itemId;
    }

    private static void Write(TwoServers servers, HarnessSide side, Guid itemId, SyncedState state) =>
        side.UserData.Write(
            servers.Someone,
            Work(side, itemId),
            state,
            UserDataSaveReason.UpdateUserData,
            CancellationToken.None);

    private static SyncedState Reading(TwoServers servers, HarnessSide side, Guid itemId) =>
        side.UserData.Read(servers.Someone, Work(side, itemId)).State!;

    private static void Apply(
        TwoServers servers,
        HarnessSide side,
        FirstExchangeResolution resolution) =>
        Write(servers, side, resolution.Subject.ItemId, resolution.Resolved!);

    private static IReadOnlyList<RecordedChange> Outstanding(
        TwoServers servers,
        HarnessSide side,
        FirstExchange exchange,
        FirstExchangeResolution resolution) =>
        OutstandingChanges.Since(
            _pairing,
            exchange.Agreed.For(resolution.Subject.ItemId),
            resolution.Subject,
            Reading(servers, side, resolution.Subject.ItemId),
            _agreedAt);

    private static BaseItem Work(HarnessSide side, Guid itemId)
    {
        var held = side.Library.FirstOrDefault(item => item.Id == itemId);

        if (held is not null)
        {
            return held;
        }

        var work = UserDataFixtures.Work(itemId, 120 * TimeSpan.TicksPerMinute);
        side.Library.Add(work);

        return work;
    }

    private static ItemOnBothSides PairFrom(TwoServers servers, Guid itemId) =>
        Pair(
            servers.Someone.Id,
            itemId,
            Reading(servers, servers.Here, itemId),
            Reading(servers, servers.There, itemId));

    private static FirstExchange Run(TwoServers servers, params Guid[] items) =>
        Resume(servers, AgreedRecords.NoneYet(_pairing, servers.Someone.Id), _agreedAt, items);

    private static FirstExchange Resume(
        TwoServers servers,
        AgreedRecords records,
        DateTimeOffset agreedAt,
        params Guid[] items) =>
        FirstExchange.Over(
            records,
            items.Select(id => PairFrom(servers, id)).ToList(),
            PositionRecency.DefaultToleratedSkew,
            _now,
            agreedAt);
}
