using System;
using Jellyfin.Plugin.WatchSync.Transfer;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The record of one sweep run, which is #55's fifth condition: the run records its start, its
/// end, what it examined and what it changed, so that a run which covered less than everything
/// cannot be read as one that covered it all.
///
/// The condition names two properties that fail in opposite directions, and a set written against
/// only one of them passes while the other is broken. A record that never says a run was complete
/// satisfies the second half and is useless, and a record that says every ended run was complete
/// satisfies nothing while every count on it reads correctly. Both directions are asserted here,
/// and the pair of facts that does it is the first two below.
///
/// Nothing here reads a clock and nothing waits. Both moments are parameters, which is the
/// headless rule in <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c> and is also the shape
/// the rule itself takes: a run of any length is driven by handing it two instants.
/// </summary>
public class SweepRunTests
{
    private static readonly DateTimeOffset _started = new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _ended = new DateTimeOffset(2026, 9, 1, 3, 4, 0, TimeSpan.Zero);

    /// <summary>
    /// A run that ended having examined fewer subjects than it was over is not covered.
    ///
    /// This is the condition's own assertion and the failure it names: a sweep cancelled from the
    /// dashboard, or cut off by a shutdown, leaves a record whose counts are all true and whose
    /// meaning is the opposite of what an operator would take from them.
    /// </summary>
    [Fact]
    public void ARunThatStoppedPartWayIsNotReadableAsOneThatCoveredEverything()
    {
        var run = SweepRun.Over(_started, 10)
            .HavingExamined(1)
            .HavingExamined(0)
            .HavingExamined(2)
            .Ended(_ended);

        Assert.Equal(SweepRunOutcome.StoppedShort, run.Outcome);
        Assert.False(run.IsCovered);
        Assert.Equal(3, run.Examined);
        Assert.Equal(10, run.Subjects);
    }

    /// <summary>
    /// A run that reached every subject it was over is covered.
    ///
    /// It is the other direction of the same rule, and without it a record that answers
    /// <c>StoppedShort</c> to everything passes the fact above while telling an operator that no
    /// sweep has ever finished.
    /// </summary>
    [Fact]
    public void ARunThatReachedEverySubjectIsCovered()
    {
        var run = SweepRun.Over(_started, 3)
            .HavingExamined(0)
            .HavingExamined(0)
            .HavingExamined(0)
            .Ended(_ended);

        Assert.Equal(SweepRunOutcome.Covered, run.Outcome);
        Assert.True(run.IsCovered);
    }

    /// <summary>
    /// The run carries all four of the things the condition asks for.
    ///
    /// The start, the end, what it examined and what it changed, with the changes summed across
    /// the subjects rather than replaced by the last one, which is the arithmetic mistake that
    /// leaves every other fact here green.
    /// </summary>
    [Fact]
    public void TheRunCarriesItsStartItsEndWhatItExaminedAndWhatItChanged()
    {
        var run = SweepRun.Over(_started, 2)
            .HavingExamined(4)
            .HavingExamined(7)
            .Ended(_ended);

        Assert.Equal(_started, run.StartedAt);
        Assert.Equal(_ended, run.EndedAt);
        Assert.Equal(2, run.Examined);
        Assert.Equal(11, run.Changed);
    }

    /// <summary>
    /// A run that has not ended is neither covered nor stopped short, and carries no end.
    ///
    /// A record with two ended answers and no third one makes the caller decide what an absent
    /// end means, and a caller that forgets reads a walk still in progress as a finished one. The
    /// answer is on the record instead.
    /// </summary>
    [Fact]
    public void ARunStillWalkingIsNeitherCoveredNorStoppedShort()
    {
        var run = SweepRun.Over(_started, 2).HavingExamined(1);

        Assert.Equal(SweepRunOutcome.Running, run.Outcome);
        Assert.False(run.IsCovered);
        Assert.Null(run.EndedAt);
    }

    /// <summary>
    /// A run over no subjects at all is covered when it ends, rather than stopped short.
    ///
    /// A server with no pairing sweeps nothing, and that run reached everything there was. Read
    /// the other way it would report a failed convergence on every pass of an ordinary
    /// installation that has not been paired yet, which is the alert nobody would keep reading.
    /// </summary>
    [Fact]
    public void ARunOverNoSubjectsIsCoveredRatherThanStoppedShort()
    {
        var run = SweepRun.Over(_started, 0).Ended(_ended);

        Assert.Equal(SweepRunOutcome.Covered, run.Outcome);
        Assert.Equal(0, run.Examined);
        Assert.Equal(0, run.Changed);
    }

    /// <summary>
    /// Examining one more subject than the run was over is refused.
    ///
    /// It is the route by which a walk would talk itself into coverage: reach the declared set,
    /// then keep going, and the two numbers agree again at some later point. The declared set is
    /// what coverage is measured against, so a walk that found more subjects than it set out over
    /// is a walk over a different set and is refused rather than counted.
    /// </summary>
    [Fact]
    public void ExaminingMoreSubjectsThanTheRunWasOverIsRefused()
    {
        var run = SweepRun.Over(_started, 1).HavingExamined(0);

        Assert.Throws<InvalidOperationException>(() => run.HavingExamined(0));
    }

    /// <summary>
    /// A subject examined after the run ended is refused.
    ///
    /// An ended run is a statement about a walk that is over. A record that accepted more work
    /// afterwards would let a run report itself stopped short, carry on, and end again as
    /// covered, with one walk arriving as two.
    /// </summary>
    [Fact]
    public void ASubjectExaminedAfterTheRunEndedIsRefused()
    {
        var run = SweepRun.Over(_started, 2).HavingExamined(0).Ended(_ended);

        Assert.Throws<InvalidOperationException>(() => run.HavingExamined(0));
    }

    /// <summary>
    /// Ending a run twice is refused.
    ///
    /// The second end would move a moment the record already carries, and it is the cheapest way
    /// to turn a run that stopped short into one that covered everything: end it, examine
    /// nothing, end it again with the counts adjusted.
    /// </summary>
    [Fact]
    public void EndingARunTwiceIsRefused()
    {
        var run = SweepRun.Over(_started, 1).HavingExamined(0).Ended(_ended);

        Assert.Throws<InvalidOperationException>(() => run.Ended(_ended));
    }

    /// <summary>
    /// An end before the start is refused rather than recorded.
    ///
    /// Both moments come from one clock in one process, so the pair arriving out of order is a
    /// caller mixing two sources. Recorded, it is a run whose duration is negative and whose
    /// record an operator reads as evidence of something that did not happen.
    /// </summary>
    [Fact]
    public void AnEndBeforeTheStartIsRefused()
    {
        var run = SweepRun.Over(_ended, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => run.Ended(_started));
    }

    /// <summary>
    /// A run that ends at the moment it started is accepted.
    ///
    /// The bound above is on the order of the two moments and not on the length of the run. A
    /// sweep over no subjects, or one cancelled before its first subject, takes less time than
    /// the clock it is measured by can report, and refusing that would refuse the case the rule
    /// is most likely to meet.
    /// </summary>
    [Fact]
    public void ARunThatEndedAtTheMomentItStartedIsAccepted()
    {
        var run = SweepRun.Over(_started, 0).Ended(_started);

        Assert.Equal(_started, run.EndedAt);
        Assert.Equal(SweepRunOutcome.Covered, run.Outcome);
    }

    /// <summary>
    /// A count of changes below zero is refused.
    ///
    /// Nothing in a walk produces one, which is the point: it arrives out of a subtraction
    /// somewhere upstream, and summed into the run it hides changes that were made rather than
    /// reporting a number an operator would question.
    /// </summary>
    [Fact]
    public void AChangeCountBelowZeroIsRefused()
    {
        var run = SweepRun.Over(_started, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => run.HavingExamined(-1));
    }

    /// <summary>
    /// A run over fewer than no subjects is refused.
    ///
    /// The denominator coverage is measured against cannot be negative, and one that is makes
    /// every run over it read as covered before it has examined anything.
    /// </summary>
    [Fact]
    public void ARunOverFewerThanNoSubjectsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SweepRun.Over(_started, -1));
    }
}
