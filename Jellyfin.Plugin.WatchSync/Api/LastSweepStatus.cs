using System;
using Jellyfin.Plugin.WatchSync.Transfer;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// What the last run of the scheduled sweep did, read from the run record the sweep keeps.
///
/// This is the item of #62 that asks what the last run did, and it is the server's rather than
/// the pairing's. The sweep walks the records the store holds rather than pairs today, so one
/// run is over every pairing and every person at once and the same run is answered on every
/// status. When the exchange arrives it takes its place in the same walk under the same record,
/// which is where a run becomes one pairing's.
///
/// <para>
/// The record is held in memory and a restart loses it, which <see cref="SweepRuns"/> says of
/// itself. So the absence here is not a document that is missing: it is that no sweep has ended
/// since the server started, and the status says that rather than showing zeros a reader would
/// take for a run that examined nothing. The server's own task list keeps when the sweep last
/// ran and whether it failed across a restart.
/// </para>
///
/// <para>
/// A run that stopped short is the one a status exists to show. Its counts look like a run that
/// finished, and what it did not reach was not trimmed, so the surface says which it was rather
/// than leaving the reader to compare two numbers. Every number is the run's own.
/// </para>
/// </summary>
public sealed class LastSweepStatus
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LastSweepStatus"/> class.
    /// </summary>
    /// <param name="isRecorded">Whether a sweep has ended since the server started.</param>
    /// <param name="startedAt">When the run started, where there is one.</param>
    /// <param name="endedAt">When the run ended, where there is one.</param>
    /// <param name="outcome">Whether the run covered its subjects or stopped short, where there is one.</param>
    /// <param name="subjects">How many records the run set out over, where there is one.</param>
    /// <param name="examined">How many of those it examined, where there is one.</param>
    /// <param name="changed">How many entries it removed, where there is one.</param>
    public LastSweepStatus(
        bool isRecorded,
        DateTimeOffset? startedAt,
        DateTimeOffset? endedAt,
        SweepRunOutcome? outcome,
        int? subjects,
        int? examined,
        int? changed)
    {
        IsRecorded = isRecorded;
        StartedAt = startedAt;
        EndedAt = endedAt;
        Outcome = outcome;
        Subjects = subjects;
        Examined = examined;
        Changed = changed;
    }

    /// <summary>
    /// Gets the answer where no sweep has ended since the server started.
    /// </summary>
    public static LastSweepStatus NoneSinceTheServerStarted { get; } =
        new LastSweepStatus(false, null, null, null, null, null, null);

    /// <summary>
    /// Gets a value indicating whether a sweep has ended since the server started. False is a
    /// server on which the task has not run to its end yet, or one restarted since it did.
    /// </summary>
    public bool IsRecorded { get; }

    /// <summary>
    /// Gets a value indicating whether the last run ended having examined fewer records than it
    /// was over, which is what a cancellation, a shutdown or a refused write leaves behind.
    /// </summary>
    public bool StoppedShort => Outcome == SweepRunOutcome.StoppedShort;

    /// <summary>
    /// Gets when the run started, or null where none is recorded.
    /// </summary>
    public DateTimeOffset? StartedAt { get; }

    /// <summary>
    /// Gets when the run ended, or null where none is recorded.
    /// </summary>
    public DateTimeOffset? EndedAt { get; }

    /// <summary>
    /// Gets whether the run covered its subjects or stopped short, or null where none is recorded.
    /// </summary>
    public SweepRunOutcome? Outcome { get; }

    /// <summary>
    /// Gets how many records the run set out over, or null where none is recorded.
    /// </summary>
    public int? Subjects { get; }

    /// <summary>
    /// Gets how many of those records the run examined, or null where none is recorded.
    /// </summary>
    public int? Examined { get; }

    /// <summary>
    /// Gets how many entries the run removed across the records it examined, or null where none
    /// is recorded.
    /// </summary>
    public int? Changed { get; }

    /// <summary>
    /// The status of one ended run, with every number the run's own.
    /// </summary>
    /// <param name="run">The run, which has ended because <see cref="SweepRuns"/> holds no other.</param>
    /// <returns>The status.</returns>
    public static LastSweepStatus Of(SweepRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new LastSweepStatus(
            true,
            run.StartedAt,
            run.EndedAt,
            run.Outcome,
            run.Subjects,
            run.Examined,
            run.Changed);
    }
}
