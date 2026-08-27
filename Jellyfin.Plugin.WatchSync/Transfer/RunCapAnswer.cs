namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// What the cap on one run answered.
///
/// Three values, and the two that stop a run are separate rather than one, because an operator
/// reading a stopped run has to be told which bound was crossed before they can decide whether
/// to approve it. The two bounds fail for different reasons and on different libraries: a count
/// is crossed by a run that is large in absolute terms, and a share is crossed by a run that is
/// large relative to what this person has, which is the same mistake on a library too small for
/// the count to notice.
/// </summary>
public enum RunCapAnswer
{
    /// <summary>
    /// The run is under both bounds and proceeds.
    /// </summary>
    Within,

    /// <summary>
    /// The run would change more items than the count allows, so it stops.
    /// </summary>
    ExceedsCount,

    /// <summary>
    /// The run would change a greater share of this person's matched items than the proportion
    /// allows, so it stops.
    /// </summary>
    ExceedsShare,
}
