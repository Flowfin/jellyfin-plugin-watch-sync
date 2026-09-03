using System;
using Jellyfin.Plugin.WatchSync.Transfer;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// Whether a run was stopped by the cap, read from the plan the stop recorded.
///
/// This is the item #38's fifth condition asks the status to show prominently, and it is the
/// reason <see cref="SyncStatus.NeedsAttention"/> exists: a cap nobody sees is a sync that
/// stopped working silently.
/// </summary>
public sealed class StoppedRunStatus
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StoppedRunStatus"/> class.
    /// </summary>
    /// <param name="reading">What the read of the plan came back with.</param>
    /// <param name="answer">Which bound stopped the run, where there is a plan.</param>
    /// <param name="changes">How many changes the run was about to make, where there is a plan.</param>
    /// <param name="allowed">How many the crossed bound allowed, where there is a plan.</param>
    /// <param name="matched">How many items the person had matched, where there is a plan.</param>
    /// <param name="stoppedAt">When the run stopped, where there is a plan.</param>
    public StoppedRunStatus(
        RecordReading reading,
        RunCapAnswer? answer,
        int? changes,
        int? allowed,
        int? matched,
        DateTimeOffset? stoppedAt)
    {
        Reading = reading;
        Answer = answer;
        Changes = changes;
        Allowed = allowed;
        Matched = matched;
        StoppedAt = stoppedAt;
    }

    /// <summary>
    /// Gets what the read of the plan came back with.
    /// </summary>
    public RecordReading Reading { get; }

    /// <summary>
    /// Gets a value indicating whether a run is stopped and waiting for an operator.
    /// </summary>
    public bool IsStopped => Reading == RecordReading.Read;

    /// <summary>
    /// Gets which bound stopped the run, or null where no run is stopped.
    /// </summary>
    public RunCapAnswer? Answer { get; }

    /// <summary>
    /// Gets how many changes the run was about to make, or null where no run is stopped.
    /// </summary>
    public int? Changes { get; }

    /// <summary>
    /// Gets how many changes the crossed bound allowed, or null where no run is stopped.
    /// </summary>
    public int? Allowed { get; }

    /// <summary>
    /// Gets how many items the person had matched when the run was judged, or null.
    /// </summary>
    public int? Matched { get; }

    /// <summary>
    /// Gets when the run stopped, or null where no run is stopped.
    /// </summary>
    public DateTimeOffset? StoppedAt { get; }
}
