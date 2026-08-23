using System;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What one progress report is worth carrying, which is #17.
///
/// The facts are arranged around the order the three refusals are asked in, because that order
/// is the part of this rule a later change would tidy away without noticing. Three of them
/// drive a pair that two different orders answer differently, so a rule that asked the stop
/// before the finish, or the finish before the length, reddens a fact whose name says which
/// pair it got wrong rather than reddening everything at once.
///
/// The two boundaries are driven at the boundary and one tick either side of it, because a
/// comparison written with the wrong one of two operators is the mistake somebody actually
/// makes here and it is invisible everywhere except at that single value.
/// </summary>
public class PositionThresholdTests
{
    private static readonly TimeSpan _film = TimeSpan.FromHours(2);

    private static readonly TimeSpan _clip = TimeSpan.FromMinutes(3);

    private static long Minutes(double count) => TimeSpan.FromMinutes(count).Ticks;

    /// <summary>
    /// The property this rule exists for: the number of changes one playback produces follows
    /// the length of the work and not the number of reports the player sent.
    ///
    /// The same two hours are driven twice, once at a report every ten seconds and once at a
    /// report every second, so the second run feeds the rule ten times as many reports as the
    /// first. Both are held to the bound the move threshold sets, which is the runtime divided
    /// by the threshold, and the tenfold run is held to the other one within one change.
    ///
    /// The one change of slack is the rule rather than a tolerance. A finer report interval
    /// crosses each threshold a little earlier, so the carried positions sit a little closer
    /// together and the last one before the finish can fit where the coarser run had no room
    /// for it. What would not fit inside it is a rule that carried reports rather than moves,
    /// which produces seven thousand two hundred against seven hundred and twenty.
    /// </summary>
    [Fact]
    public void AWholePlaybackProducesAHandfulOfChangesHoweverManyReportsArrive()
    {
        var coarse = CarriedOverOnePlayback(TimeSpan.FromSeconds(10));
        var fine = CarriedOverOnePlayback(TimeSpan.FromSeconds(1));

        Assert.Equal(coarse.Reports * 10, fine.Reports);

        var bound = _film.Ticks / PositionThresholds.DefaultMove.Ticks;

        Assert.True(
            coarse.Carried <= bound,
            $"a report every ten seconds carried {coarse.Carried} positions out of {coarse.Reports} reports, and the move threshold bounds it at {bound}");

        Assert.True(
            fine.Carried <= bound,
            $"a report every second carried {fine.Carried} positions out of {fine.Reports} reports, and the move threshold bounds it at {bound}");

        Assert.True(
            fine.Carried <= coarse.Carried + 1,
            $"ten times as many reports carried {fine.Carried} positions against {coarse.Carried}, so the count is following the reports rather than the work");
    }

    /// <summary>
    /// A stop below the threshold still carries the position it stopped at.
    ///
    /// This is what lets the move threshold be as coarse as it is. Somebody who stops two
    /// minutes into a scene has a resume point that matters to them, and a rule that only
    /// carried moves would drop it for being small.
    /// </summary>
    [Fact]
    public void AStopBelowTheThresholdStillCarriesTheFinalPosition()
    {
        var answer = PositionThreshold.Judge(
            Minutes(22),
            Minutes(20),
            thePlaybackStopped: true,
            _film,
            PositionThresholds.Default);

        Assert.Equal(PositionThresholdAnswer.TheStopIsCarried, answer.Answer);
        Assert.Equal(Minutes(22), answer.Position);
        Assert.False(answer.CarriesPlayed);
    }

    /// <summary>
    /// A position past the finish distance is carried as watched and carries no position with
    /// it.
    ///
    /// Both halves are asserted. A resolution carrying the watched state and the number beside
    /// it would hand the receiving side the pair the ratchet in #31 exists to settle, invented
    /// on this side for no reason.
    /// </summary>
    [Fact]
    public void APositionPastTheFinishThresholdIsCarriedAsWatchedAndNotAsAPosition()
    {
        var answer = PositionThreshold.Judge(
            _film.Ticks - Minutes(1),
            Minutes(110),
            thePlaybackStopped: false,
            _film,
            PositionThresholds.Default);

        Assert.Equal(PositionThresholdAnswer.TheFinishIsCarriedAsPlayed, answer.Answer);
        Assert.True(answer.CarriesPlayed);
        Assert.Null(answer.Position);
    }

    /// <summary>
    /// The length of the item is asked before the finish.
    ///
    /// The pair is a clip somebody watched to the end. A rule that asked the finish first
    /// answers it as watched, which reads as harmless and is not: the shortest-item rule is
    /// what keeps a library of trailers and clips out of the record of what two sides last
    /// agreed, and a rule that converts them into watched states instead has put every one of
    /// them into it under another name.
    /// </summary>
    [Fact]
    public void TheLengthOfTheItemIsAskedBeforeTheFinish()
    {
        var answer = PositionThreshold.Judge(
            _clip.Ticks,
            0,
            thePlaybackStopped: true,
            _clip,
            PositionThresholds.Default);

        Assert.Equal(PositionThresholdAnswer.TheItemIsTooShortToResume, answer.Answer);
        Assert.Null(answer.Position);
        Assert.False(answer.CarriesPlayed);
    }

    /// <summary>
    /// The finish is asked before the stop.
    ///
    /// The pair is the ordinary end of a film, which is a stop and a finish at once. A rule
    /// that asked the stop first carries the last tick of every work anybody finishes as a
    /// position, and the peer then offers to resume them a minute from the end of something
    /// they have watched.
    /// </summary>
    [Fact]
    public void TheFinishIsAskedBeforeTheStop()
    {
        var answer = PositionThreshold.Judge(
            _film.Ticks,
            Minutes(115),
            thePlaybackStopped: true,
            _film,
            PositionThresholds.Default);

        Assert.Equal(PositionThresholdAnswer.TheFinishIsCarriedAsPlayed, answer.Answer);
        Assert.Null(answer.Position);
    }

    /// <summary>
    /// A move of exactly the threshold is not yet a change, and one tick further is.
    ///
    /// The threshold is the largest move that is still too small to carry, so the boundary
    /// belongs to the refusal. Both sides are driven because a comparison written with the
    /// wrong one of two operators answers every other pair correctly.
    /// </summary>
    [Fact]
    public void AMoveOfExactlyTheThresholdIsNotYetAChangeAndOneTickFurtherIs()
    {
        var at = PositionThreshold.Judge(
            Minutes(20) + PositionThresholds.DefaultMove.Ticks,
            Minutes(20),
            thePlaybackStopped: false,
            _film,
            PositionThresholds.Default);

        Assert.Equal(PositionThresholdAnswer.TheMoveIsNotYetAChange, at.Answer);
        Assert.Null(at.Position);

        var past = PositionThreshold.Judge(
            Minutes(20) + PositionThresholds.DefaultMove.Ticks + 1,
            Minutes(20),
            thePlaybackStopped: false,
            _film,
            PositionThresholds.Default);

        Assert.Equal(PositionThresholdAnswer.TheMoveIsCarried, past.Answer);
        Assert.Equal(Minutes(20) + PositionThresholds.DefaultMove.Ticks + 1, past.Position);
    }

    /// <summary>
    /// A position exactly the finish distance from the end is a finish, and one tick further
    /// from the end is not.
    ///
    /// The distance is the widest gap that still counts as the end, so this boundary belongs
    /// to the conversion, which is the opposite direction from the move above. The two are
    /// driven together so that a later reader who tidies them into one comparison reddens
    /// both rather than neither.
    /// </summary>
    [Fact]
    public void APositionExactlyTheFinishDistanceFromTheEndIsAFinishAndOneTickFurtherIsNot()
    {
        var at = PositionThreshold.Judge(
            _film.Ticks - PositionThresholds.DefaultFinish.Ticks,
            0,
            thePlaybackStopped: false,
            _film,
            PositionThresholds.Default);

        Assert.Equal(PositionThresholdAnswer.TheFinishIsCarriedAsPlayed, at.Answer);

        var justShort = PositionThreshold.Judge(
            _film.Ticks - PositionThresholds.DefaultFinish.Ticks - 1,
            0,
            thePlaybackStopped: false,
            _film,
            PositionThresholds.Default);

        Assert.Equal(PositionThresholdAnswer.TheMoveIsCarried, justShort.Answer);
    }

    /// <summary>
    /// Seeking backwards further than the threshold is a change.
    ///
    /// The move is a distance and not a difference, because somebody who jumps back half an
    /// hour has moved their resume point by half an hour. A rule that only measured forward
    /// leaves the peer offering a position the person deliberately left, and it does so on the
    /// one action that says most plainly where they want to be.
    /// </summary>
    [Fact]
    public void ASeekBackwardsFurtherThanTheThresholdIsAChange()
    {
        var answer = PositionThreshold.Judge(
            Minutes(20),
            Minutes(50),
            thePlaybackStopped: false,
            _film,
            PositionThresholds.Default);

        Assert.Equal(PositionThresholdAnswer.TheMoveIsCarried, answer.Answer);
        Assert.Equal(Minutes(20), answer.Position);
    }

    /// <summary>
    /// An item this server has not analysed is judged by the move and the stop alone, and the
    /// answer says the length was never known.
    ///
    /// The position driven here sits where the finish rule would have converted it on an item
    /// of the same length with a runtime, so the fact is about the rule not having run rather
    /// than about a value it happened to agree on. Carrying that out is what lets #62 show an
    /// operator why a work near its end is offering a resume point, and it is the residual
    /// this rule leaves rather than one it closes.
    /// </summary>
    [Fact]
    public void AnItemWithNoRuntimeIsJudgedByTheMoveAndTheStopAndSaysTheLengthWasNotKnown()
    {
        var answer = PositionThreshold.Judge(
            _film.Ticks - Minutes(1),
            Minutes(110),
            thePlaybackStopped: false,
            null,
            PositionThresholds.Default);

        Assert.Equal(PositionThresholdAnswer.TheMoveIsCarried, answer.Answer);
        Assert.Equal(_film.Ticks - Minutes(1), answer.Position);
        Assert.True(answer.TheRuntimeWasNotKnown);
    }

    /// <summary>
    /// An item whose length is known never reports that it was not.
    ///
    /// Without this the flag could be true everywhere and the fact above would still pass,
    /// which would make it a fact about a constant rather than about the runtime.
    /// </summary>
    [Fact]
    public void AnItemWhoseLengthIsKnownDoesNotReportThatItWasNot()
    {
        var answer = PositionThreshold.Judge(
            Minutes(30),
            Minutes(20),
            thePlaybackStopped: false,
            _film,
            PositionThresholds.Default);

        Assert.Equal(PositionThresholdAnswer.TheMoveIsCarried, answer.Answer);
        Assert.False(answer.TheRuntimeWasNotKnown);
    }

    /// <summary>
    /// A runtime of nothing is refused rather than read as an item of no length.
    ///
    /// An item the server has not analysed carries no runtime, and a caller that spelled that
    /// absence as zero would have every item answered as too short to resume, which is a
    /// plugin that silently stops carrying positions at all. The two are told apart here so
    /// that the mistake is an exception rather than a behaviour.
    /// </summary>
    [Fact]
    public void ARuntimeOfNothingIsRefusedBecauseAbsentIsNull()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PositionThreshold.Judge(
            0,
            0,
            thePlaybackStopped: false,
            TimeSpan.Zero,
            PositionThresholds.Default));
    }

    /// <summary>
    /// A position below zero is refused.
    ///
    /// The server does not produce one, so it reaches this rule only from a caller that
    /// computed it, and a negative position would make the distance comparison answer about a
    /// place nobody was.
    /// </summary>
    [Fact]
    public void APositionBelowZeroIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PositionThreshold.Judge(
            -1,
            0,
            thePlaybackStopped: true,
            _film,
            PositionThresholds.Default));

        Assert.Throws<ArgumentOutOfRangeException>(() => PositionThreshold.Judge(
            0,
            -1,
            thePlaybackStopped: true,
            _film,
            PositionThresholds.Default));
    }

    /// <summary>
    /// A finish distance at or above the shortest item length is refused.
    ///
    /// The two numbers are not independent. A finish distance that reaches the shortest item
    /// this plugin carries a position for makes every position on such an item a finish, so
    /// one of the three rules quietly stops existing while all three settings still read as
    /// though they are there.
    /// </summary>
    [Fact]
    public void AFinishDistanceAtOrAboveTheShortestItemIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PositionThresholds(
            PositionThresholds.DefaultMove,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5)));
    }

    /// <summary>
    /// Each of the three is refused above its own maximum and below zero.
    ///
    /// The maximums are bounds on the rule rather than advice to whoever sets the setting, so
    /// they are refused here rather than only described in the document. A number a document
    /// declares and no code refuses is one a later caller passes straight through.
    /// </summary>
    [Fact]
    public void EachThresholdIsRefusedOutsideItsOwnBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PositionThresholds(
            PositionThresholds.MaximumMove + TimeSpan.FromTicks(1),
            PositionThresholds.DefaultFinish,
            PositionThresholds.DefaultShortestItem));

        Assert.Throws<ArgumentOutOfRangeException>(() => new PositionThresholds(
            PositionThresholds.DefaultMove,
            PositionThresholds.MaximumFinish + TimeSpan.FromTicks(1),
            PositionThresholds.MaximumShortestItem));

        Assert.Throws<ArgumentOutOfRangeException>(() => new PositionThresholds(
            PositionThresholds.DefaultMove,
            PositionThresholds.DefaultFinish,
            PositionThresholds.MaximumShortestItem + TimeSpan.FromTicks(1)));

        Assert.Throws<ArgumentOutOfRangeException>(() => new PositionThresholds(
            TimeSpan.FromTicks(-1),
            PositionThresholds.DefaultFinish,
            PositionThresholds.DefaultShortestItem));
    }

    /// <summary>
    /// The defaults satisfy the bounds the same type declares.
    ///
    /// A default outside its own maximum, or a default finish distance at or above the default
    /// shortest item, would be a rule that refuses the value it uses when nobody has chosen
    /// one, which is the failure that only appears on the first machine that runs it.
    /// </summary>
    [Fact]
    public void TheDefaultsSatisfyTheBoundsTheSameTypeDeclares()
    {
        var thresholds = PositionThresholds.Default;

        Assert.Equal(PositionThresholds.DefaultMove, thresholds.Move);
        Assert.Equal(PositionThresholds.DefaultFinish, thresholds.Finish);
        Assert.Equal(PositionThresholds.DefaultShortestItem, thresholds.ShortestItem);
    }

    /// <summary>
    /// Drives one whole playback of the film at a given report interval and counts what the
    /// rule carried.
    ///
    /// The position last carried is advanced only when the rule carried one, which is what the
    /// handler in #15 does against the record of what two sides last agreed. A driver that
    /// advanced it on every report would measure a different rule and would measure it as
    /// carrying nothing.
    /// </summary>
    /// <param name="interval">How often the player reports.</param>
    /// <returns>How many reports were fed and how many positions were carried.</returns>
    private static (int Reports, int Carried) CarriedOverOnePlayback(TimeSpan interval)
    {
        var carriedPosition = 0L;
        var reports = 0;
        var carried = 0;

        for (var at = interval.Ticks; at <= _film.Ticks; at += interval.Ticks)
        {
            reports++;

            var answer = PositionThreshold.Judge(
                at,
                carriedPosition,
                thePlaybackStopped: false,
                _film,
                PositionThresholds.Default);

            if (answer.Position is long position)
            {
                carried++;
                carriedPosition = position;
            }
        }

        return (reports, carried);
    }
}
