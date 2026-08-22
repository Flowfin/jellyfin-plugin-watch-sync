namespace Jellyfin.Plugin.WatchSync.Versions;

/// <summary>
/// What deciding where a position from a peer lands answered, which is #28.
///
/// Three values, and two of them are refusals of the position alone. Nothing here refuses the
/// change: played, the play count and the last played date are properties of the work and are
/// applied whatever this answers, which is why the type carrying the answer has no way to
/// express dropping them.
///
/// The two refusals are separate values although both end with no position, because an operator
/// reads them differently and can act on only one. Two runtimes that are far apart is a library
/// holding two different cuts, which is a fact about the two libraries. A peer that sent no
/// runtime is a peer that has not analysed the file yet, which repairs itself on the other
/// server's next scan.
/// </summary>
public enum VersionLandingAnswer
{
    /// <summary>
    /// The two runtimes are close enough that the tick names the same moment on both, so the
    /// position is applied along with the rest of the change.
    /// </summary>
    ThePositionLands,

    /// <summary>
    /// The two runtimes differ by more than the tolerance, so the position is dropped and the
    /// drop is recorded with both runtimes.
    ///
    /// The displacement a position can carry is the difference between the two runtimes, and a
    /// difference this large is an edit or a speed conversion rather than packaging. Both move
    /// the whole timeline, so the tick lands in a scene the person had not reached.
    /// </summary>
    TheRuntimesAreTooFarApart,

    /// <summary>
    /// One of the two runtimes is absent, so there is no comparison to make and the position is
    /// dropped.
    ///
    /// It is one value for either side being absent, because the situation is the same from
    /// this rule's position: without both numbers the displacement cannot be bounded, and a
    /// position applied on the strength of a missing number is applied on nothing.
    /// </summary>
    ARuntimeIsMissing,
}
