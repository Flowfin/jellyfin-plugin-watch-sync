namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// What one sweep run came to, which is the half of #55's fifth condition that a reader of a
/// finished run needs before any of its numbers mean anything.
///
/// Three values rather than two. A run that has not ended yet is its own answer, because the
/// alternative is a record whose end is absent and whose coverage a caller decides by reading
/// that absence, and a caller that forgets to read it takes a run still walking for one that
/// finished. The two ended answers are separate for the reason the condition names: a run that
/// reached every subject it was over and a run that stopped part way are different statements
/// about what an operator may conclude from the same counts, and collapsing them is what lets a
/// partial pass be read as a complete one.
/// </summary>
public enum SweepRunOutcome
{
    /// <summary>
    /// The run has not ended. What it has examined so far is what it has examined, and nothing
    /// about coverage may be concluded from it.
    /// </summary>
    Running,

    /// <summary>
    /// The run ended having examined every subject it was over.
    /// </summary>
    Covered,

    /// <summary>
    /// The run ended having examined fewer subjects than it was over, which is what a
    /// cancellation, a shutdown or a refusal part way through leaves behind.
    /// </summary>
    StoppedShort,
}
