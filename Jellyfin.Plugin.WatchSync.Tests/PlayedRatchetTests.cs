using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Conflict;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// A completion held against a position that says the work was not finished, which is #31.
///
/// The facts are arranged so that one of them carries the property the issue says a later
/// change will quietly remove, and the others do not. Every fact but
/// <see cref="PlayedBeatsAPositionHoweverMuchNewerThePositionIs"/> gives the played side the
/// later of the two dates, so a resolver that had stopped ignoring the clock and settled the
/// pair by recency would still answer them correctly. That is deliberate rather than
/// incidental: the issue asks that removing the rule redden exactly one fact and that the
/// fact say what broke, and a file where every case happened to carry the margin would redden
/// seven and name nothing.
/// </summary>
public class PlayedRatchetTests
{
    private static readonly DateTime _watched =
        new DateTime(2026, 3, 1, 20, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime _stoppedEarlier =
        new DateTime(2026, 2, 1, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Twenty minutes of a film, which is the stale partial position the issue is about.
    /// </summary>
    private const long TwentyMinutes = 20 * 60 * TimeSpan.TicksPerSecond;

    private const long FiveMinutes = 5 * 60 * TimeSpan.TicksPerSecond;

    /// <summary>
    /// A completion beats a position offered against it, whichever server holds which.
    ///
    /// Both directions are here because the rule is one server's answer about a pair and not a
    /// preference for the local side. A resolver that held the ratchet only for a completion it
    /// found locally would leave the peer's finished film resumable here forever.
    /// </summary>
    [Fact]
    public void PlayedBeatsAPositionInBothDirections()
    {
        var playedHere = PlayedRatchet.Hold(
            Finished(_watched),
            StoppedAt(TwentyMinutes, _stoppedEarlier));

        Assert.Equal(RatchetAnswer.PlayedStands, playedHere.Answer);

        var playedAtThePeer = PlayedRatchet.Hold(
            StoppedAt(TwentyMinutes, _stoppedEarlier),
            Finished(_watched));

        Assert.Equal(RatchetAnswer.PlayedStands, playedAtThePeer.Answer);
    }

    /// <summary>
    /// The margin does not buy the position the win, at any size.
    ///
    /// This is the fact the issue asks for and the one a simplification reddens. The position
    /// is newer here by a minute, by a day, by a year and by a decade, which covers both the
    /// two servers whose clocks disagree and the position genuinely written afterwards, and
    /// the answer does not move for any of them. A resolver that settled the pair by the later
    /// last played date would hand every one of these to the position, and the person would
    /// watch the end of the film again after every run.
    /// </summary>
    [Fact]
    public void PlayedBeatsAPositionHoweverMuchNewerThePositionIs()
    {
        var margins = new List<TimeSpan>
        {
            TimeSpan.FromMinutes(1),
            TimeSpan.FromDays(1),
            TimeSpan.FromDays(365),
            TimeSpan.FromDays(3650),
        };

        Assert.All(
            margins,
            margin =>
            {
                var answer = PlayedRatchet.Hold(
                    Finished(_watched),
                    StoppedAt(TwentyMinutes, _watched + margin));

                Assert.Equal(RatchetAnswer.PlayedStands, answer.Answer);
            });
    }

    /// <summary>
    /// The position the completion discarded is carried out, named to the side that held it.
    ///
    /// The rule discards something a person did, and the whole of #36 is that such a moment is
    /// findable afterwards. A resolution that answered only "played" would leave an operator
    /// asking why an episode is marked watched with nothing to read.
    /// </summary>
    [Fact]
    public void TheDiscardedPositionIsCarriedOutAsTheLoser()
    {
        var answer = PlayedRatchet.Hold(
            Finished(_watched),
            StoppedAt(TwentyMinutes, _stoppedEarlier));

        Assert.True(answer.IsAResolvedConflict);
        Assert.Equal(TwentyMinutes, answer.PositionDiscardedAtThePeer);
        Assert.Null(answer.PositionDiscardedHere);
    }

    /// <summary>
    /// Two positions and no completion are not this rule's case.
    ///
    /// Answering them here would be answering #32, which settles a position by the later play
    /// bounded by the tolerated clock skew and has a tie rule of its own. A ratchet that
    /// returned a winner for this pair would be that rule written twice, in the file least
    /// likely to be read when the skew tolerance changes.
    /// </summary>
    [Fact]
    public void NeitherSidePlayedLeavesTheAnswerToTheRuleThatOwnsIt()
    {
        var answer = PlayedRatchet.Hold(
            StoppedAt(FiveMinutes, _stoppedEarlier),
            StoppedAt(TwentyMinutes, _watched));

        Assert.Equal(RatchetAnswer.NoCompletionToHold, answer.Answer);
        Assert.False(answer.IsAResolvedConflict);
        Assert.Null(answer.PositionDiscardedHere);
        Assert.Null(answer.PositionDiscardedAtThePeer);
    }

    /// <summary>
    /// A side that never started the work loses nothing to the completion.
    ///
    /// Played still stands, because the peer is the one that watched it, and there is no
    /// conflict to record: a position of zero is a work nobody began rather than a place
    /// somebody stopped. Recording it would put a row naming no loss into #36's record for
    /// every item a second server has not been asked about yet, which is most of a library on
    /// the first exchange.
    /// </summary>
    [Fact]
    public void APositionNobodyReachedIsNotADiscardedOne()
    {
        var answer = PlayedRatchet.Hold(
            Finished(_watched),
            StoppedAt(0, _stoppedEarlier));

        Assert.Equal(RatchetAnswer.PlayedStands, answer.Answer);
        Assert.False(answer.IsAResolvedConflict);
        Assert.Null(answer.PositionDiscardedAtThePeer);
    }

    /// <summary>
    /// Two sides that both finished the work discard nothing between them.
    ///
    /// Each one's position is where it stopped on a work it finished, so neither is a partial
    /// position being held against a completion, and neither is a loss. Which position a
    /// finished item carries afterwards is a question about positions and is #32's.
    /// </summary>
    [Fact]
    public void TwoCompletionsDiscardNothing()
    {
        var answer = PlayedRatchet.Hold(
            new SyncedState(true, 1, FiveMinutes, _watched),
            new SyncedState(true, 1, TwentyMinutes, _stoppedEarlier));

        Assert.Equal(RatchetAnswer.PlayedStands, answer.Answer);
        Assert.False(answer.IsAResolvedConflict);
    }

    /// <summary>
    /// The answer does not depend on which side is asked first.
    ///
    /// The two arguments are two servers, and every server in a pairing runs this rule over
    /// the same pair from its own side. A rule whose answer moved with the order would let two
    /// paired servers reach opposite conclusions about one item and hand each other back what
    /// the other just discarded.
    /// </summary>
    [Fact]
    public void TheAnswerIsTheSameWhicheverSideIsAskedFirst()
    {
        var finished = Finished(_watched);
        var stopped = StoppedAt(TwentyMinutes, _stoppedEarlier);

        var asked = PlayedRatchet.Hold(finished, stopped);
        var askedTheOtherWayRound = PlayedRatchet.Hold(stopped, finished);

        Assert.Equal(asked.Answer, askedTheOtherWayRound.Answer);
        Assert.Equal(asked.IsAResolvedConflict, askedTheOtherWayRound.IsAResolvedConflict);
        Assert.Equal(asked.PositionDiscardedAtThePeer, askedTheOtherWayRound.PositionDiscardedHere);
        Assert.Equal(asked.PositionDiscardedHere, askedTheOtherWayRound.PositionDiscardedAtThePeer);
    }

    /// <summary>
    /// A position below zero is refused rather than read.
    ///
    /// The server does not produce one, so it arrives out of an envelope, and #19 is what
    /// bounds what an envelope may carry. Read as an ordinary position it would be discarded
    /// as a place somebody stopped and written into #36's record as a loss that never
    /// happened.
    /// </summary>
    [Fact]
    public void APositionBelowZeroIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlayedRatchet.Hold(
                Finished(_watched),
                new SyncedState(false, 0, -1, _stoppedEarlier)));
    }

    /// <summary>
    /// A missing side is refused rather than read as an empty one.
    ///
    /// An absent peer state and a peer that holds nothing for the item are different
    /// statements, and treating the first as the second would mark a work played on a server
    /// that was never asked.
    /// </summary>
    [Fact]
    public void AMissingSideIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => PlayedRatchet.Hold(Finished(_watched), null!));
    }

    private static SyncedState Finished(DateTime lastPlayed) =>
        new SyncedState(true, 1, 0, lastPlayed);

    private static SyncedState StoppedAt(long ticks, DateTime lastPlayed) =>
        new SyncedState(false, 0, ticks, lastPlayed);
}
