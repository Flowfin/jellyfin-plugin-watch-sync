using System;
using Jellyfin.Plugin.WatchSync.Transfer;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Where the last sweep run is kept for a reader other than the task, which holds one ended run
/// and refuses one still walking.
/// </summary>
public class SweepRunsTests
{
    private static readonly DateTimeOffset _started = new(2026, 9, 3, 3, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Nothing has ended until something has, and the last run to end is the one held.
    /// </summary>
    [Fact]
    public void TheLastRunThatEndedIsTheOneHeld()
    {
        var runs = new SweepRuns();

        Assert.Null(runs.Last);

        var first = SweepRun.Over(_started, 1).HavingExamined(0).Ended(_started.AddMinutes(1));
        var second = SweepRun.Over(_started.AddHours(1), 2).HavingExamined(3).Ended(_started.AddHours(1).AddMinutes(1));

        runs.Record(first);
        runs.Record(second);

        Assert.Same(second, runs.Last);
    }

    /// <summary>
    /// A run still walking is refused, because its counts say nothing about coverage yet and a
    /// record holding it would hand that conclusion to a reader who did not check.
    /// </summary>
    [Fact]
    public void ARunStillWalkingIsRefused()
    {
        var runs = new SweepRuns();

        Assert.Throws<InvalidOperationException>(() => runs.Record(SweepRun.Over(_started, 1)));
        Assert.Throws<ArgumentNullException>(() => runs.Record(null!));
        Assert.Null(runs.Last);
    }
}
