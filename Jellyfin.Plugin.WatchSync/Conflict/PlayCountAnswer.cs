namespace Jellyfin.Plugin.WatchSync.Conflict;

/// <summary>
/// What a reconciliation of one play count answered.
///
/// A count is the one moved field where the two sides cannot be compared against each other
/// at all. Every other field holds a value that is true or false about the person now, so the
/// newer of two readings is the answer. A count is a running total, so two readings of it are
/// two partial histories, and what they have in common is the total both sides last agreed.
/// These three values are what is left once that is taken seriously: the agreement holds, one
/// side has fallen below it, or there is no agreement to reckon against.
/// </summary>
public enum PlayCountAnswer
{
    /// <summary>
    /// Both sides hold at least the agreed count, so each one's plays since the agreement are
    /// what it holds above it. The reconciled count is the agreement plus both contributions,
    /// and it is the answer for both sides.
    /// </summary>
    Reconciled,

    /// <summary>
    /// One side or both hold fewer plays than the two sides last agreed.
    ///
    /// A count does not fall on its own. What produces this is a store restored from a backup
    /// taken before the agreement, a library entry replaced, or a person the server no longer
    /// has the same record for. None of them is a play that was undone, so the shortfall is
    /// not a negative contribution and is not subtracted from anything.
    ///
    /// The reconciled count is carried, and it is the one that does not lower the other side.
    /// The shortfall is carried beside it because an operator is the only one who can tell a
    /// restore from a defect, and #36 is the record it is written into.
    /// </summary>
    BelowTheAgreement,

    /// <summary>
    /// The two sides have never agreed a count for this item and this mapped user.
    ///
    /// There is no reconciled count, and this type does not invent one. Without an agreement
    /// there is nothing to measure a contribution against: two sides each holding one play may
    /// be one play that already moved or two people-watchings that never met, and no reading of
    /// the two numbers separates those. #37 fixes what the first exchange does with a pair like
    /// that, and it decides this case rather than this type guessing at it.
    /// </summary>
    NoAgreement,
}
