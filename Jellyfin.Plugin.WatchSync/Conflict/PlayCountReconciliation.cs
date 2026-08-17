using System;

namespace Jellyfin.Plugin.WatchSync.Conflict;

/// <summary>
/// How often the person watched the work, reckoned against what the two sides last agreed.
///
/// Both of the rules a reader reaches for first are wrong, and they are wrong in opposite
/// directions. Adding the two counts re-counts every play that already moved, so a pair of
/// servers that exchange nothing new still climbs by the whole history on every run. Taking
/// the newer side's count overwrites a rewatch the other side recorded, and the field carries
/// no per-play timestamp for anything to notice that with.
///
/// What both rules are missing is that a count is a running total rather than a statement
/// about the person now. Two readings of it are two partial histories, and the only thing
/// they share is the total the two sides last agreed. So each side's plays since that
/// agreement are what it holds above it, and the answer is the agreement plus both of those.
/// A pair that agreed at four, where one side has since watched the work twice and the other
/// once, holds seven, and it holds seven however many times it is reconciled afterwards.
///
/// <c>docs/sync-model.md</c> fixes the fields that move, the unit one transfer is about and
/// the record of what two sides last agreed. This type points at that document rather than
/// restating it, and #33 is the rule.
/// </summary>
public sealed class PlayCountReconciliation
{
    private PlayCountReconciliation(
        PlayCountAnswer answer,
        long? count,
        int shortfallHere,
        int shortfallAtThePeer)
    {
        Answer = answer;
        Count = count;
        ShortfallHere = shortfallHere;
        ShortfallAtThePeer = shortfallAtThePeer;
    }

    /// <summary>
    /// Gets what the reconciliation answered.
    /// </summary>
    public PlayCountAnswer Answer { get; }

    /// <summary>
    /// Gets the reconciled count, or null where <see cref="Answer"/> is
    /// <see cref="PlayCountAnswer.NoAgreement"/> and there is none.
    ///
    /// It is wider than the field it came out of on purpose. Two histories that have never met
    /// sum to more than either of them, and the server holds a count in a 32 bit field, so a
    /// total that does not fit in one is arithmetic rather than corruption. Whether such a
    /// total is written narrowed, refused or recorded is a decision about writing and belongs
    /// to the apply path in #50; a rule that narrowed it here would take that decision quietly
    /// and lose the evidence for it in the same expression.
    /// </summary>
    public long? Count { get; }

    /// <summary>
    /// Gets how far this server's count falls below the agreed one, or zero.
    ///
    /// It is a distance rather than a flag because the two causes look different in it. A store
    /// restored from a backup taken before the agreement falls short by the plays recorded in
    /// between, and an entry whose record was replaced falls short by the whole agreement.
    /// </summary>
    public int ShortfallHere { get; }

    /// <summary>
    /// Gets how far the peer's count falls below the agreed one, or zero.
    /// </summary>
    public int ShortfallAtThePeer { get; }

    /// <summary>
    /// Gets a value indicating whether a side fell below the agreement, which is the conflict
    /// #36 records with its loser.
    /// </summary>
    public bool IsBelowTheAgreement => Answer == PlayCountAnswer.BelowTheAgreement;

    /// <summary>
    /// Reckons the two counts against the count the two sides last agreed.
    ///
    /// A shortfall is never a negative contribution. A count that has fallen is not a record of
    /// plays being undone, because nothing undoes a play: it is a store restored from before
    /// the agreement or a record the server no longer holds the same way. Subtracting it would
    /// take the other side's genuine plays away for something that happened on this one, which
    /// is the failure the fourth condition of #33 names, so the shortfall is carried out to be
    /// recorded and contributes nothing to the total.
    /// </summary>
    /// <param name="agreed">
    /// The count the two sides last agreed for this mapped user and this item, or null where
    /// they never have. The record it is read out of is #14.
    /// </param>
    /// <param name="here">The count this server holds.</param>
    /// <param name="atThePeer">The count the peer holds.</param>
    /// <returns>The reconciliation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A count below zero, which the server does not produce. It reaches this rule only out of
    /// an envelope, where #19 is what bounds and refuses what one may carry, and treating it as
    /// an ordinary reading here would let it be read as a shortfall it is not.
    /// </exception>
    public static PlayCountReconciliation Reconcile(int? agreed, int here, int atThePeer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(here);
        ArgumentOutOfRangeException.ThrowIfNegative(atThePeer);

        if (agreed is not int settled)
        {
            return new PlayCountReconciliation(PlayCountAnswer.NoAgreement, null, 0, 0);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(settled, nameof(agreed));

        var shortfallHere = Math.Max(0, settled - here);
        var shortfallAtThePeer = Math.Max(0, settled - atThePeer);

        var sinceHere = (long)Math.Max(0, here - settled);
        var sinceAtThePeer = (long)Math.Max(0, atThePeer - settled);

        var answer = shortfallHere > 0 || shortfallAtThePeer > 0
            ? PlayCountAnswer.BelowTheAgreement
            : PlayCountAnswer.Reconciled;

        return new PlayCountReconciliation(
            answer,
            settled + sinceHere + sinceAtThePeer,
            shortfallHere,
            shortfallAtThePeer);
    }
}
