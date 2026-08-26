using System;
using Jellyfin.Plugin.WatchSync.Conflict;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The later of two last played dates, which is the <c>LastPlayedDate</c> row of
/// <c>docs/conflicts.md</c>.
///
/// The row this file is about is the one the document declared and the sources decided
/// nowhere, and it is the first condition of #81: every row of that table has at least one
/// fact driving it, so a row an operator's history is answered by is never a row nothing ever
/// executes.
///
/// The states are arranged against the rule rather than with it. The side holding the later
/// date is the side that is not played, holds no plays and carries the greater position, so a
/// resolver that had reached for the played state, the count or the position instead of the
/// two dates answers these wrongly rather than happening to agree.
/// </summary>
public class LastPlayedMaximumTests
{
    private static readonly DateTime _earlier =
        new DateTime(2026, 2, 1, 20, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime _later =
        new DateTime(2026, 3, 1, 20, 0, 0, DateTimeKind.Utc);

    private const long TwentyMinutes = 20 * 60 * TimeSpan.TicksPerSecond;

    /// <summary>
    /// The later date is the answer, whichever server holds it.
    ///
    /// Both directions, because the failure the row names is directional. A rule that answered
    /// with whichever side it happened to be handed second moves somebody's last played date
    /// backwards in one of the two runs and looks correct in the other, and a fact written in
    /// one direction only is green for exactly half of that.
    /// </summary>
    [Fact]
    public void TheLaterDateIsTheAnswerInBothDirections()
    {
        var laterHere = LastPlayedMaximum.Take(Played(_later), Played(_earlier));
        var laterAtThePeer = LastPlayedMaximum.Take(Played(_earlier), Played(_later));

        Assert.Equal(LastPlayedAnswer.TheLaterDateStands, laterHere.Answer);
        Assert.Equal(LastPlayedAnswer.TheLaterDateStands, laterAtThePeer.Answer);
        Assert.Equal(_later, laterHere.LastPlayedDate);
        Assert.Equal(_later, laterAtThePeer.LastPlayedDate);
    }

    /// <summary>
    /// The date wins against everything else the two sides hold.
    ///
    /// This is the fact that says the evidence column is a restriction. The side with the later
    /// date has not played the work, has no plays recorded and is twenty minutes in; the side
    /// with the earlier date is finished and has watched it three times. A rule reaching for
    /// any of those three answers with the earlier date, which is the backwards move this row
    /// exists to refuse.
    /// </summary>
    [Fact]
    public void TheDateDecidesItAndNotThePlayedStateTheCountOrThePosition()
    {
        var finishedAndEarlier = new SyncedState(true, 3, 0, _earlier);
        var partwayAndLater = new SyncedState(false, 0, TwentyMinutes, _later);

        var answered = LastPlayedMaximum.Take(finishedAndEarlier, partwayAndLater);

        Assert.Equal(LastPlayedAnswer.TheLaterDateStands, answered.Answer);
        Assert.Equal(_later, answered.LastPlayedDate);
    }

    /// <summary>
    /// A side that never played the work is not a competitor, and the one date there is stands.
    ///
    /// Both directions again. The mistake this is about is a rule that treats the absent date as
    /// a value to compare, which in this runtime is the earliest moment there is on one spelling
    /// and an exception on another, and neither of those is what never having watched something
    /// means.
    /// </summary>
    [Fact]
    public void ASideThatNeverPlayedTheWorkLosesNothingAndTheOtherDateStands()
    {
        var onlyHere = LastPlayedMaximum.Take(Played(_earlier), Never());
        var onlyAtThePeer = LastPlayedMaximum.Take(Never(), Played(_earlier));

        Assert.Equal(LastPlayedAnswer.TheLaterDateStands, onlyHere.Answer);
        Assert.Equal(LastPlayedAnswer.TheLaterDateStands, onlyAtThePeer.Answer);
        Assert.Equal(_earlier, onlyHere.LastPlayedDate);
        Assert.Equal(_earlier, onlyAtThePeer.LastPlayedDate);
    }

    /// <summary>
    /// Neither side having played the work is its own answer and carries no date.
    ///
    /// It is asserted as an answer rather than as a null, because the caller that would be
    /// wrong here is the one reading the date out of a resolution that says a date stands.
    /// </summary>
    [Fact]
    public void NeitherSideHavingPlayedIsItsOwnAnswerAndInventsNoDate()
    {
        var answered = LastPlayedMaximum.Take(Never(), Never());

        Assert.Equal(LastPlayedAnswer.NeitherSideHasPlayed, answered.Answer);
        Assert.Null(answered.LastPlayedDate);
    }

    /// <summary>
    /// Two sides holding the same moment answer with it and lose nothing.
    ///
    /// The tie is the ordinary case of this row rather than an edge of it: two servers that
    /// already agree when the person last watched the work reach #36 as nothing rather than as
    /// a conflict whose loser is the same moment as its winner.
    /// </summary>
    [Fact]
    public void TwoSidesHoldingOneMomentAnswerWithItAndLoseNothing()
    {
        var answered = LastPlayedMaximum.Take(Played(_later), Played(_later));

        Assert.Equal(LastPlayedAnswer.TheLaterDateStands, answered.Answer);
        Assert.Equal(_later, answered.LastPlayedDate);
    }

    /// <summary>
    /// A date that is not the instant the server stores is refused rather than compared.
    ///
    /// Both sides, because a rule guarding one of the two arguments is a rule that refuses half
    /// of the mistake. What arrives is the local zone read somewhere upstream: two dates an hour
    /// apart as instants can be equal as wall clock numbers, and a comparison made on those is
    /// wrong by the offset and says nothing about it.
    /// </summary>
    [Fact]
    public void ADateThatIsNotUtcIsRefusedOnEitherSide()
    {
        var local = new SyncedState(true, 1, 0, DateTime.SpecifyKind(_later, DateTimeKind.Local));

        Assert.Throws<ArgumentOutOfRangeException>(() => LastPlayedMaximum.Take(local, Played(_earlier)));
        Assert.Throws<ArgumentOutOfRangeException>(() => LastPlayedMaximum.Take(Played(_earlier), local));
    }

    /// <summary>
    /// A side that is not there at all is a defect one step earlier than anything a pair can be.
    /// </summary>
    [Fact]
    public void ASideThatIsNotThereIsADefectRatherThanAnAnswer()
    {
        Assert.Throws<ArgumentNullException>(() => LastPlayedMaximum.Take(null!, Played(_later)));
        Assert.Throws<ArgumentNullException>(() => LastPlayedMaximum.Take(Played(_later), null!));
    }

    private static SyncedState Played(DateTime at) => new SyncedState(true, 1, 0, at);

    private static SyncedState Never() => new SyncedState(false, 0, 0, null);
}
