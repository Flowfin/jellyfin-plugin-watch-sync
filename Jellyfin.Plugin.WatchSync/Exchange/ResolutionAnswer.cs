namespace Jellyfin.Plugin.WatchSync.Exchange;

/// <summary>
/// What a first exchange answered for one item.
///
/// Three states rather than two, because a run that resumes an interrupted one meets items it
/// has already agreed and those are neither decided by this run nor left standing by it.
/// Collapsing them into decided would make a resumed run report work it did not do, and
/// collapsing them into undecided would send an operator at items nothing is wrong with.
/// </summary>
public enum ResolutionAnswer
{
    /// <summary>
    /// The table answered every field, so there is a state both sides agree on and an agreement
    /// to record for it.
    /// </summary>
    Decided,

    /// <summary>
    /// The table did not answer, so the item stands as it is on both sides and
    /// <see cref="FirstExchangeResolution.Reason"/> says what for. Nothing is written and no
    /// agreement is recorded, because an agreement over a state nobody decided is the thing
    /// every later exchange would then be decided against.
    /// </summary>
    Undecided,

    /// <summary>
    /// The record already carries an agreement for this item, so an earlier run of this same
    /// first exchange reached it before being interrupted. It is not decided again.
    /// </summary>
    AlreadyAgreed,
}
