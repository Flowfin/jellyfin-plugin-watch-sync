using System;

namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// Where the last run of the scheduled sweep is kept, so that something other than the task
/// can read it.
///
/// The server constructs a scheduled task for its own worker and hands the instance to nobody
/// else, so a record the task kept on itself would be a record only the task could read. This
/// is a registered singleton the task writes into and the status page in #62 reads out of, and
/// it holds one run: the last one that ended. A run still walking is not recorded here, because
/// a reader of a run that has not ended can conclude nothing from its counts, which
/// <see cref="SweepRun"/> says about itself, and a record that could hold one would hand that
/// conclusion to whoever forgot to check.
///
/// <para>
/// It is held in memory and a restart loses it. The server's own task history keeps when the
/// sweep last ran and whether it failed across a restart; what is lost is how many subjects it
/// was over, how many it examined and how many changes it made. That is stated rather than
/// left to be found, and a record the store keeps is the shape that would retire it.
/// </para>
/// </summary>
public sealed class SweepRuns
{
    private SweepRun? _last;

    /// <summary>
    /// Gets the last run that ended, or nothing where no sweep has ended since the server
    /// started.
    /// </summary>
    public SweepRun? Last => _last;

    /// <summary>
    /// Records a run that ended.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <exception cref="InvalidOperationException">
    /// The run has not ended. Its counts say nothing about coverage yet, and a reader of this
    /// record is owed a run whose counts do.
    /// </exception>
    public void Record(SweepRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Outcome == SweepRunOutcome.Running)
        {
            throw new InvalidOperationException(
                "The run has not ended, so nothing about its coverage can be concluded, and a record holding it would hand that conclusion to a reader who did not check.");
        }

        _last = run;
    }
}
