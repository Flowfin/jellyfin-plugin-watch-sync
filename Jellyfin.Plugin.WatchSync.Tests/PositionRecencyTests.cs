using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Conflict;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Two positions settled by the later play inside a bounded tolerance, which is #32.
///
/// The facts are arranged so that the two halves of the rule fail apart. A resolver that
/// stopped bounding the comparison, and settled every pair by the later date however close the
/// two dates were, still answers the recency facts correctly and reddens the tie ones. A
/// resolver that stopped comparing at all, and answered every pair with the greater position,
/// reddens the recency facts and leaves the tie ones green. Neither half can be removed without
/// something here saying which one went.
///
/// Every fact gives the tie cases a smaller position on the later side, so a rule that had
/// quietly become the greater position everywhere cannot pass the recency cases by accident.
/// </summary>
public class PositionRecencyTests
{
    /// <summary>
    /// The present moment on this server. Every date below is placed against it rather than
    /// against a clock, which is what lets the peer's clock be put in the future deliberately.
    /// </summary>
    private static readonly DateTime _now =
        new DateTime(2026, 3, 1, 20, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan _tolerance = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Twenty minutes into a film and five minutes into it. The pair is the one the rule is
    /// about: two positions a person could plausibly have reached on two evenings.
    /// </summary>
    private const long TwentyMinutes = 20 * 60 * TimeSpan.TicksPerSecond;

    private const long FiveMinutes = 5 * 60 * TimeSpan.TicksPerSecond;

    /// <summary>
    /// The later play wins, and the smaller position on the later side is what proves it is the
    /// date being read rather than the number.
    ///
    /// Both directions, because the rule is one server's answer about a pair and not a
    /// preference for the local side. A rule that answered by side would leave the two servers
    /// resolving one pair two different ways forever, each one convinced it had settled it.
    /// </summary>
    [Fact]
    public void TheLaterPlayWinsInBothDirections()
    {
        var laterHere = PositionRecency.Settle(
            StoppedAt(FiveMinutes, _now - TimeSpan.FromHours(1)),
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromDays(7)),
            _tolerance,
            _now);

        Assert.Equal(PositionAnswer.LaterPlayStands, laterHere.Answer);
        Assert.Equal(FiveMinutes, laterHere.Position);

        var laterAtThePeer = PositionRecency.Settle(
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromDays(7)),
            StoppedAt(FiveMinutes, _now - TimeSpan.FromHours(1)),
            _tolerance,
            _now);

        Assert.Equal(PositionAnswer.LaterPlayStands, laterAtThePeer.Answer);
        Assert.Equal(FiveMinutes, laterAtThePeer.Position);
    }

    /// <summary>
    /// A margin smaller than the tolerance is not a comparison, so the greater position is the
    /// answer even though the smaller one was written later.
    ///
    /// This is the fact a resolver that dropped the bound reddens. It hands this pair to the
    /// five minute position, and the person is put back fifteen minutes for a difference of two
    /// seconds between two clocks.
    /// </summary>
    [Fact]
    public void AMarginInsideTheToleranceIsNotAComparison()
    {
        var settled = PositionRecency.Settle(
            StoppedAt(FiveMinutes, _now - TimeSpan.FromSeconds(8)),
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromSeconds(10)),
            _tolerance,
            _now);

        Assert.Equal(PositionAnswer.TheGreaterPositionStands, settled.Answer);
        Assert.Equal(TwentyMinutes, settled.Position);
    }

    /// <summary>
    /// The tie rule answers the same pair the same way whichever way round it is asked.
    ///
    /// This is the fourth condition of #32 in the words it uses. It is the cheap fact and the
    /// one most likely to be skipped, and a resolver that asked whichever side it happened to
    /// hold first passes every other fact in this file.
    /// </summary>
    [Fact]
    public void TheTieRuleAnswersThePairTheSameWayInBothDirections()
    {
        var one = StoppedAt(FiveMinutes, _now - TimeSpan.FromSeconds(8));
        var other = StoppedAt(TwentyMinutes, _now - TimeSpan.FromSeconds(10));

        var asked = PositionRecency.Settle(one, other, _tolerance, _now);
        var askedTheOtherWay = PositionRecency.Settle(other, one, _tolerance, _now);

        Assert.Equal(asked.Answer, askedTheOtherWay.Answer);
        Assert.Equal(asked.Position, askedTheOtherWay.Position);
        Assert.Equal(asked.PositionDiscardedHere, askedTheOtherWay.PositionDiscardedAtThePeer);
        Assert.Equal(asked.PositionDiscardedAtThePeer, askedTheOtherWay.PositionDiscardedHere);
    }

    /// <summary>
    /// A peer whose last play is further ahead of this server's present moment than the
    /// tolerance allows names the clock and settles nothing.
    ///
    /// The margins run from a few minutes to a year, because the shapes behind them are
    /// different and the refusal is the same: a clock set by hand into next week, a server
    /// whose battery lost the date and came up in a different year, and one that is simply not
    /// synchronised. A play cannot have happened after the present moment, so this is the one
    /// reading this rule knows is false rather than merely old.
    /// </summary>
    [Fact]
    public void APeerPlayingAfterThePresentMomentNamesTheClock()
    {
        var margins = new List<TimeSpan>
        {
            TimeSpan.FromMinutes(5),
            TimeSpan.FromDays(1),
            TimeSpan.FromDays(365),
        };

        Assert.All(
            margins,
            margin =>
            {
                var settled = PositionRecency.Settle(
                    StoppedAt(TwentyMinutes, _now - TimeSpan.FromHours(1)),
                    StoppedAt(FiveMinutes, _now + margin),
                    _tolerance,
                    _now);

                Assert.Equal(PositionAnswer.PeerClockOutsideTolerance, settled.Answer);
                Assert.Null(settled.Position);
                Assert.False(settled.IsAResolvedConflict);
            });
    }

    /// <summary>
    /// The two boundaries, held apart.
    ///
    /// A difference of exactly the tolerance is a comparison, and a peer date exactly the
    /// tolerance ahead of the present moment is inside it. They are the two questions the one
    /// number answers, and a change that made either boundary agree with the other would pass
    /// every other fact here.
    /// </summary>
    [Fact]
    public void TheTwoBoundariesAreWhereTheRuleSaysTheyAre()
    {
        var exactlyApart = PositionRecency.Settle(
            StoppedAt(FiveMinutes, _now),
            StoppedAt(TwentyMinutes, _now - _tolerance),
            _tolerance,
            _now);

        Assert.Equal(PositionAnswer.LaterPlayStands, exactlyApart.Answer);
        Assert.Equal(FiveMinutes, exactlyApart.Position);

        var exactlyAhead = PositionRecency.Settle(
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromHours(1)),
            StoppedAt(FiveMinutes, _now + _tolerance),
            _tolerance,
            _now);

        Assert.Equal(PositionAnswer.LaterPlayStands, exactlyAhead.Answer);
        Assert.Equal(FiveMinutes, exactlyAhead.Position);
    }

    /// <summary>
    /// A side with no last played date is not a comparison either, so the tie rule answers it.
    ///
    /// A position with no date behind it is what a restored store and a record the server
    /// rebuilt both produce. There is nothing to compare, and the alternative to the tie rule is
    /// treating an absent date as the oldest possible moment, which hands every such pair to the
    /// other side however far into the work the person actually was.
    /// </summary>
    [Fact]
    public void ASideWithNoDateIsNotAComparison()
    {
        var noDateHere = PositionRecency.Settle(
            StoppedAt(TwentyMinutes, null),
            StoppedAt(FiveMinutes, _now - TimeSpan.FromMinutes(30)),
            _tolerance,
            _now);

        Assert.Equal(PositionAnswer.TheGreaterPositionStands, noDateHere.Answer);
        Assert.Equal(TwentyMinutes, noDateHere.Position);

        var noDateOnEitherSide = PositionRecency.Settle(
            StoppedAt(FiveMinutes, null),
            StoppedAt(TwentyMinutes, null),
            _tolerance,
            _now);

        Assert.Equal(PositionAnswer.TheGreaterPositionStands, noDateOnEitherSide.Answer);
        Assert.Equal(TwentyMinutes, noDateOnEitherSide.Position);
    }

    /// <summary>
    /// A completion is the ratchet's pair and not this rule's, whichever side holds it.
    ///
    /// The two rules are kept apart, so this one says the pair is not its own rather than
    /// answering it a second way. A resolver where both rules answer a played pair is one where
    /// which answer the caller gets depends on the order the two are asked in.
    /// </summary>
    [Fact]
    public void ACompletionIsNotThisRulesPair()
    {
        var playedHere = PositionRecency.Settle(
            Finished(_now - TimeSpan.FromDays(7)),
            StoppedAt(FiveMinutes, _now - TimeSpan.FromMinutes(30)),
            _tolerance,
            _now);

        Assert.Equal(PositionAnswer.ACompletionIsHeld, playedHere.Answer);
        Assert.Null(playedHere.Position);
        Assert.False(playedHere.IsAResolvedConflict);

        var playedAtThePeer = PositionRecency.Settle(
            StoppedAt(FiveMinutes, _now - TimeSpan.FromMinutes(30)),
            Finished(_now - TimeSpan.FromDays(7)),
            _tolerance,
            _now);

        Assert.Equal(PositionAnswer.ACompletionIsHeld, playedAtThePeer.Answer);
        Assert.Null(playedAtThePeer.Position);
    }

    /// <summary>
    /// The losing position is carried out on the side it was on, so the loss can be recorded.
    ///
    /// #36 records a resolved conflict with its loser, and it records which server the discarded
    /// value was on. A rule that answered with the winning number alone would leave that to be
    /// worked out a second time out of the two states, by whoever writes the record.
    /// </summary>
    [Fact]
    public void TheDiscardedPositionIsCarriedOutOnItsOwnSide()
    {
        var lostAtThePeer = PositionRecency.Settle(
            StoppedAt(FiveMinutes, _now - TimeSpan.FromHours(1)),
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromDays(7)),
            _tolerance,
            _now);

        Assert.True(lostAtThePeer.IsAResolvedConflict);
        Assert.Null(lostAtThePeer.PositionDiscardedHere);
        Assert.Equal(TwentyMinutes, lostAtThePeer.PositionDiscardedAtThePeer);

        var lostHere = PositionRecency.Settle(
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromDays(7)),
            StoppedAt(FiveMinutes, _now - TimeSpan.FromHours(1)),
            _tolerance,
            _now);

        Assert.True(lostHere.IsAResolvedConflict);
        Assert.Equal(TwentyMinutes, lostHere.PositionDiscardedHere);
        Assert.Null(lostHere.PositionDiscardedAtThePeer);
    }

    /// <summary>
    /// Nothing was lost where the losing side had not started, and nothing was lost where the
    /// two sides were already at the same position.
    ///
    /// Both are answers rather than conflicts. A zero carried out as a discarded value, and a
    /// pair recorded as a conflict whose loser is the same number as its winner, would fill the
    /// record in #36 with rows naming no loss, and the rows that do name one are what an
    /// operator opens that record to find.
    /// </summary>
    [Fact]
    public void APositionNobodyReachedAndAPairThatAgreesAreNotConflicts()
    {
        var nobodyStarted = PositionRecency.Settle(
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromHours(1)),
            StoppedAt(0, _now - TimeSpan.FromDays(7)),
            _tolerance,
            _now);

        Assert.Equal(TwentyMinutes, nobodyStarted.Position);
        Assert.False(nobodyStarted.IsAResolvedConflict);

        var alreadyAgreed = PositionRecency.Settle(
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromHours(1)),
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromDays(7)),
            _tolerance,
            _now);

        Assert.Equal(TwentyMinutes, alreadyAgreed.Position);
        Assert.False(alreadyAgreed.IsAResolvedConflict);
    }

    /// <summary>
    /// A tolerance wider than the documented maximum is refused rather than used.
    ///
    /// The maximum is a bound on the rule and not advice to whoever sets the setting. Above it
    /// the tie rule swallows genuine plays: a person reaches a materially different position
    /// inside a quarter of an hour, and a tolerance of a day would stop two evenings being
    /// compared at all while every fact about a clock in this file still passed.
    /// </summary>
    [Fact]
    public void AToleranceOutsideTheDocumentedBoundsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PositionRecency.Settle(
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromHours(1)),
            StoppedAt(FiveMinutes, _now - TimeSpan.FromDays(7)),
            PositionRecency.MaximumToleratedSkew + TimeSpan.FromSeconds(1),
            _now));

        Assert.Throws<ArgumentOutOfRangeException>(() => PositionRecency.Settle(
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromHours(1)),
            StoppedAt(FiveMinutes, _now - TimeSpan.FromDays(7)),
            TimeSpan.FromSeconds(-1),
            _now));

        var atTheMaximum = PositionRecency.Settle(
            StoppedAt(TwentyMinutes, _now - TimeSpan.FromHours(1)),
            StoppedAt(FiveMinutes, _now - TimeSpan.FromDays(7)),
            PositionRecency.MaximumToleratedSkew,
            _now);

        Assert.Equal(PositionAnswer.LaterPlayStands, atTheMaximum.Answer);
        Assert.True(PositionRecency.DefaultToleratedSkew < PositionRecency.MaximumToleratedSkew);
    }

    /// <summary>
    /// A present moment that is not UTC is refused.
    ///
    /// This is the failure the tolerance itself cannot see. Two servers agreeing on the instant
    /// to the second disagree by an hour once one of them has been through a local zone, and the
    /// rule would then name the peer's clock for a mistake made on this side, which is the right
    /// refusal for the wrong reason and sends an operator to the wrong machine.
    /// </summary>
    [Fact]
    public void APresentMomentThatIsNotUtcIsRefused()
    {
        Assert.All(
            new List<DateTimeKind> { DateTimeKind.Local, DateTimeKind.Unspecified },
            kind => Assert.Throws<ArgumentOutOfRangeException>(() => PositionRecency.Settle(
                StoppedAt(TwentyMinutes, _now - TimeSpan.FromHours(1)),
                StoppedAt(FiveMinutes, _now - TimeSpan.FromDays(7)),
                _tolerance,
                DateTime.SpecifyKind(_now, kind))));
    }

    /// <summary>
    /// A position below zero is refused rather than compared.
    ///
    /// The server does not produce one. It reaches this rule out of an envelope, where #19 is
    /// what bounds and refuses what one may carry, and a negative treated as an ordinary reading
    /// here would be answered as a position somebody reached and could be recorded as a loss.
    /// </summary>
    [Fact]
    public void APositionBelowZeroIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PositionRecency.Settle(
            StoppedAt(-1, _now - TimeSpan.FromHours(1)),
            StoppedAt(FiveMinutes, _now - TimeSpan.FromDays(7)),
            _tolerance,
            _now));

        Assert.Throws<ArgumentOutOfRangeException>(() => PositionRecency.Settle(
            StoppedAt(FiveMinutes, _now - TimeSpan.FromHours(1)),
            StoppedAt(-1, _now - TimeSpan.FromDays(7)),
            _tolerance,
            _now));
    }

    /// <summary>
    /// A side that stopped part of the way through.
    /// </summary>
    /// <param name="ticks">Where the person stopped.</param>
    /// <param name="lastPlayed">When they last watched it, or null.</param>
    /// <returns>The state one server holds.</returns>
    private static SyncedState StoppedAt(long ticks, DateTime? lastPlayed) =>
        new SyncedState(false, 0, ticks, lastPlayed);

    /// <summary>
    /// A side that finished the work, which is the ratchet's pair rather than this rule's.
    /// </summary>
    /// <param name="lastPlayed">When they watched it.</param>
    /// <returns>The state one server holds.</returns>
    private static SyncedState Finished(DateTime lastPlayed) =>
        new SyncedState(true, 1, 0, lastPlayed);
}
