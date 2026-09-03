namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// Why an approval left one item of a stopped run unwritten.
///
/// Every value here is the third condition of #38 being kept: an approved plan applies exactly
/// what it recorded, and nothing that changed in the meantime is written without being noticed.
/// An item set aside is noticed rather than recomputed, and it is offered again by the next run,
/// which judges it afresh against what is there then.
/// </summary>
public enum SetAsideReason
{
    /// <summary>
    /// What this server holds for the item is not what the plan recorded it held, so something
    /// moved it between the stop and the approval. A person watched it, a metadata refresh
    /// touched it, or another pairing wrote it; whichever it was, the operator approved a plan
    /// about a different value.
    /// </summary>
    HeldMovedSinceTheRunStopped,

    /// <summary>
    /// The plan recorded no baseline for the item, because reading what this server held was
    /// refused at the moment the run stopped, so there is nothing to compare against.
    /// </summary>
    HeldWasNotReadWhenTheRunStopped,

    /// <summary>
    /// What this server holds could not be read at the approval, so whether it moved cannot be
    /// decided and the item is not written on a guess.
    /// </summary>
    HeldCouldNotBeReadAtTheApproval,

    /// <summary>
    /// The library no longer holds the item, so there is nothing to write against.
    /// </summary>
    ItemGoneFromTheLibrary,
}
