using System;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Versions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Where a position from a peer lands when one work is held in several versions, which is #28.
///
/// The facts are arranged so that the two halves of the rule fail apart. A rule that stopped
/// comparing the runtimes and let every position across reddens the two drop cases and leaves
/// the landing ones green. A rule that stopped letting any position across reddens the landing
/// cases and leaves the drops green. And a rule that started treating a dropped position as a
/// dropped change reddens the cases below that read the three work fields after a drop, which
/// is the half this issue's fourth condition is about.
///
/// The four cases this issue names are covered by what the runtime pair is, because which
/// version's runtime this side hands in is the adapter's answer in #20 rather than this rule's.
/// One version is the pair where the runtime is the item's own. Several versions with close
/// runtimes and several with very different ones are the two pairs below that differ by seconds
/// and by half an hour. A peer whose runtime is unknown is the pair with none on that side.
/// </summary>
public class VersionLandingTests
{
    /// <summary>
    /// Ninety minutes, which is the runtime of the version this server would resume in every
    /// case below. The other side is placed against it rather than both being varied, so that a
    /// failure names the difference rather than the arithmetic.
    /// </summary>
    private const long NinetyMinutes = 90 * 60 * TimeSpan.TicksPerSecond;

    private const long FortyMinutes = 40 * 60 * TimeSpan.TicksPerSecond;

    private const long ThirtySeconds = 30 * TimeSpan.TicksPerSecond;

    private const long OneMinute = 60 * TimeSpan.TicksPerSecond;

    private const long HalfAnHour = 30 * 60 * TimeSpan.TicksPerSecond;

    private static readonly DateTime _watched =
        new DateTime(2026, 3, 1, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// One version, and the runtime the peer sent is the same number. This is the ordinary case
    /// the whole rule exists to leave alone: two servers holding one encode of one work, where
    /// the tick names the same moment on both.
    /// </summary>
    [Fact]
    public void OneVersionOfOneLengthTakesThePosition()
    {
        var landing = VersionLanding.Decide(
            StoppedAt(FortyMinutes),
            NinetyMinutes,
            NinetyMinutes);

        Assert.Equal(VersionLandingAnswer.ThePositionLands, landing.Answer);
        Assert.Equal(FortyMinutes, landing.PositionToApply);
        Assert.False(landing.ThePositionWasDropped);
    }

    /// <summary>
    /// Several versions with close runtimes. Thirty seconds apart is packaging: a distributor
    /// logo, a few seconds of black, a container that padded the end. The tick lands in the same
    /// scene and the person resumes a little early or a little late, which is what they do by
    /// hand anyway.
    ///
    /// Both directions, because the rule is about how far apart two lengths are and not about
    /// which side is longer. A rule that subtracted one way round would answer this pair
    /// correctly in one direction and let a negative difference through as small in the other.
    /// </summary>
    [Fact]
    public void RuntimesThirtySecondsApartTakeThePositionInBothDirections()
    {
        var peerIsLonger = VersionLanding.Decide(
            StoppedAt(FortyMinutes),
            NinetyMinutes,
            NinetyMinutes + ThirtySeconds);

        var peerIsShorter = VersionLanding.Decide(
            StoppedAt(FortyMinutes),
            NinetyMinutes,
            NinetyMinutes - ThirtySeconds);

        Assert.Equal(VersionLandingAnswer.ThePositionLands, peerIsLonger.Answer);
        Assert.Equal(VersionLandingAnswer.ThePositionLands, peerIsShorter.Answer);
        Assert.Equal(FortyMinutes, peerIsLonger.PositionToApply);
        Assert.Equal(FortyMinutes, peerIsShorter.PositionToApply);
    }

    /// <summary>
    /// The boundary, stated as a fact rather than left to a reader of the comparison. A
    /// difference of exactly the tolerance lets the position across, because the tolerance is
    /// the largest difference that still counts as packaging, and one tick more does not.
    ///
    /// This is the case a one-character mistake moves. A comparison written with the wrong
    /// inequality answers every other fact here identically and only this pair differently.
    /// </summary>
    [Fact]
    public void ExactlyTheToleranceLandsAndOneTickMoreDoesNot()
    {
        Assert.Equal(OneMinute, VersionLanding.WidestRuntimeDifference.Ticks);

        var atTheBoundary = VersionLanding.Decide(
            StoppedAt(FortyMinutes),
            NinetyMinutes,
            NinetyMinutes + OneMinute);

        var oneTickPast = VersionLanding.Decide(
            StoppedAt(FortyMinutes),
            NinetyMinutes,
            NinetyMinutes + OneMinute + 1);

        Assert.Equal(VersionLandingAnswer.ThePositionLands, atTheBoundary.Answer);
        Assert.Equal(VersionLandingAnswer.TheRuntimesAreTooFarApart, oneTickPast.Answer);
    }

    /// <summary>
    /// Several versions with very different runtimes. Half an hour apart is a theatrical cut
    /// against an extended one, and both move the whole timeline, so the tick lands in a scene
    /// the person had not reached.
    ///
    /// The two runtimes come out on the answer, because the drop is recorded against the item
    /// with both of them and an operator reading that record has to be able to see which two
    /// lengths produced it.
    /// </summary>
    [Fact]
    public void RuntimesHalfAnHourApartDropThePositionAndCarryBothRuntimes()
    {
        var landing = VersionLanding.Decide(
            StoppedAt(FortyMinutes),
            NinetyMinutes,
            NinetyMinutes + HalfAnHour);

        Assert.Equal(VersionLandingAnswer.TheRuntimesAreTooFarApart, landing.Answer);
        Assert.Null(landing.PositionToApply);
        Assert.True(landing.ThePositionWasDropped);
        Assert.Equal(NinetyMinutes, landing.RuntimeHereTicks);
        Assert.Equal(NinetyMinutes + HalfAnHour, landing.RuntimeAtThePeerTicks);
    }

    /// <summary>
    /// A peer whose runtime is unknown. There is no comparison to make, so the position is
    /// dropped rather than applied on the strength of a missing number.
    ///
    /// It is a separate answer from the runtimes being far apart, because an operator can act on
    /// only one of the two. This one repairs itself on the other server's next scan; the other
    /// is a fact about the two libraries.
    /// </summary>
    [Fact]
    public void APeerThatSentNoRuntimeDropsThePosition()
    {
        var landing = VersionLanding.Decide(StoppedAt(FortyMinutes), NinetyMinutes, null);

        Assert.Equal(VersionLandingAnswer.ARuntimeIsMissing, landing.Answer);
        Assert.Null(landing.PositionToApply);
        Assert.Null(landing.RuntimeAtThePeerTicks);
        Assert.Equal(NinetyMinutes, landing.RuntimeHereTicks);
    }

    /// <summary>
    /// The same absence read from this side. An item this server has not analysed yet carries no
    /// runtime either, and the situation is the same one: without both numbers the displacement
    /// cannot be bounded.
    /// </summary>
    [Fact]
    public void AnItemHereWithNoRuntimeDropsThePosition()
    {
        var landing = VersionLanding.Decide(StoppedAt(FortyMinutes), null, NinetyMinutes);

        Assert.Equal(VersionLandingAnswer.ARuntimeIsMissing, landing.Answer);
        Assert.Null(landing.PositionToApply);
        Assert.Null(landing.RuntimeHereTicks);
    }

    /// <summary>
    /// The fourth condition of #28, over every answer the rule has. Dropping a position never
    /// drops the played state that came with it, nor the count, nor the date.
    ///
    /// This is asserted over all three answers rather than over the two drops, because what
    /// makes it hold is that the three fields are carried the same way whatever the position
    /// did. A rule that carried them only on the answers where nothing was dropped would pass a
    /// test written against the drops alone by treating them as a special case, which is exactly
    /// the shape this condition is against: the position and the played state arrive together,
    /// and refusing the pair is how a watched film comes out unwatched on the other server.
    /// </summary>
    [Theory]
    [InlineData(null, VersionLandingAnswer.ARuntimeIsMissing)]
    [InlineData(NinetyMinutes, VersionLandingAnswer.ThePositionLands)]
    [InlineData(NinetyMinutes + HalfAnHour, VersionLandingAnswer.TheRuntimesAreTooFarApart)]
    public void TheWorkFieldsAreAppliedWhateverThePositionDid(
        long? runtimeAtThePeerTicks,
        VersionLandingAnswer expected)
    {
        var watchedTwice = new SyncedState(true, 2, FortyMinutes, _watched);

        var landing = VersionLanding.Decide(
            watchedTwice,
            NinetyMinutes,
            runtimeAtThePeerTicks);

        Assert.Equal(expected, landing.Answer);
        Assert.True(landing.PlayedToApply);
        Assert.Equal(2, landing.PlayCountToApply);
        Assert.Equal(_watched, landing.LastPlayedDateToApply);
    }

    /// <summary>
    /// A dropped position leaves the local one where it is rather than writing anything. The
    /// answer says so by carrying no position at all, so a caller has nothing to write and
    /// cannot write a zero by reading the field as one.
    /// </summary>
    [Fact]
    public void ADroppedPositionIsAbsentRatherThanZero()
    {
        var landing = VersionLanding.Decide(
            StoppedAt(FortyMinutes),
            NinetyMinutes,
            NinetyMinutes + HalfAnHour);

        Assert.Null(landing.PositionToApply);
        Assert.NotEqual(0L, landing.PositionToApply.GetValueOrDefault(-1));
    }

    /// <summary>
    /// The values the server cannot produce, which reach this rule only out of an envelope. #19
    /// bounds what one may carry and this refuses what gets past that, because a negative
    /// runtime makes every difference wrong and a negative position is not a moment.
    /// </summary>
    [Fact]
    public void ANegativePositionOrRuntimeIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => VersionLanding.Decide(null!, NinetyMinutes, NinetyMinutes));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => VersionLanding.Decide(StoppedAt(-1), NinetyMinutes, NinetyMinutes));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => VersionLanding.Decide(StoppedAt(FortyMinutes), -1, NinetyMinutes));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => VersionLanding.Decide(StoppedAt(FortyMinutes), NinetyMinutes, -1));
    }

    /// <summary>
    /// One state, part way through and not played, which is the pair a position question is
    /// asked about at all.
    /// </summary>
    /// <param name="positionTicks">Where the person stopped.</param>
    /// <returns>The state.</returns>
    private static SyncedState StoppedAt(long positionTicks) =>
        new SyncedState(false, 0, positionTicks, _watched);
}
