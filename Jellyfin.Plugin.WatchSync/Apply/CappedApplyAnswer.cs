using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Transfer;

namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// What a capped apply did: either the walk it made, or the plan it recorded instead.
///
/// Exactly one of the two is present, and which one is the cap's verdict. A run within the cap
/// walked and has no plan, because nothing stopped it. A run the cap stopped has a plan and no
/// walk, because it wrote nothing. A caller is meant to read <see cref="Verdict"/> first and then
/// the one the verdict names, and the two members are separate rather than one nullable thing so
/// that a caller reading the wrong one meets a null rather than an empty walk that looks like a
/// run of nothing.
/// </summary>
public sealed class CappedApplyAnswer
{
    private CappedApplyAnswer(RunCap verdict, ApplyAnswer? walk, StoppedRun? stopped)
    {
        Verdict = verdict;
        Walk = walk;
        Stopped = stopped;
    }

    /// <summary>
    /// Gets what the cap answered about this run.
    /// </summary>
    public RunCap Verdict { get; }

    /// <summary>
    /// Gets the walk, where the run was within the cap, or null where it was stopped.
    /// </summary>
    public ApplyAnswer? Walk { get; }

    /// <summary>
    /// Gets the plan, where the cap stopped the run, or null where it proceeded.
    /// </summary>
    public StoppedRun? Stopped { get; }

    /// <summary>
    /// Gets a value indicating whether the cap stopped the run.
    /// </summary>
    public bool IsStopped => Verdict.Answer != RunCapAnswer.Within;

    internal static CappedApplyAnswer Walked(RunCap verdict, ApplyAnswer walk) =>
        new CappedApplyAnswer(verdict, walk, null);

    internal static CappedApplyAnswer StoppedWith(RunCap verdict, StoppedRun stopped) =>
        new CappedApplyAnswer(verdict, null, stopped);
}
