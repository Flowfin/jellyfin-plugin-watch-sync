namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// What the position thresholds answered about one progress report.
///
/// Five values, and three of them carry nothing onward. That asymmetry is the point of the
/// rule rather than an accident of it: a playback produces one report every few seconds for as
/// long as it runs, and all but a handful of them say something the next one contradicts.
///
/// The two that carry a position are separate values although both name the same number,
/// because they are reached for opposite reasons and an operator reads them differently. One
/// is a move large enough to be worth carrying while something is still playing. The other is
/// the report the playback stopped on, carried whatever the distance, which is what lets the
/// first one be as coarse as it is without losing where somebody got to.
/// </summary>
public enum PositionThresholdAnswer
{
    /// <summary>
    /// The move is smaller than the threshold and the playback is still running, so there is
    /// nothing to carry yet.
    ///
    /// This is the ordinary answer. It is counted rather than ignored, because a count of the
    /// reports a threshold dropped is how an operator reads that the threshold is doing what
    /// it was set to do, and #62 is the surface it is read from.
    /// </summary>
    TheMoveIsNotYetAChange,

    /// <summary>
    /// The position moved further than the threshold while the playback was still running, so
    /// it is carried.
    /// </summary>
    TheMoveIsCarried,

    /// <summary>
    /// The playback stopped, so the position it stopped at is carried whatever the distance.
    ///
    /// A stop below the threshold is the case this value exists for. Somebody who stops two
    /// minutes into a scene has a resume point that matters to them, and a rule that only
    /// carried moves would drop it for being small.
    /// </summary>
    TheStopIsCarried,

    /// <summary>
    /// The position is within the finish distance of the end of the item, so the work is
    /// carried as watched rather than as a place to resume from.
    ///
    /// The reason is that a tick is a number about one file and the peer holds its own. Two
    /// servers with different runtimes for one work disagree about where the end is, so a
    /// position near it carried as a position is a resume point a few minutes from the end of
    /// something the person has finished. Carried as played it is the same statement on both
    /// sides whatever either runtime says.
    /// </summary>
    TheFinishIsCarriedAsPlayed,

    /// <summary>
    /// The item is shorter than the length below which this plugin carries no position at all,
    /// so no position is carried for it.
    ///
    /// It is about the position and about nothing else. Whether the person watched the item,
    /// how often and when are carried under the ordinary treatment of the reason the server
    /// saved under, so a short work still syncs as watched; what is refused is a resume point
    /// on something nobody resumes.
    /// </summary>
    TheItemIsTooShortToResume,
}
