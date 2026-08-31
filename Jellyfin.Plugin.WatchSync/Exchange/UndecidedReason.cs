namespace Jellyfin.Plugin.WatchSync.Exchange;

/// <summary>
/// Why a first exchange left one item standing rather than answering it.
///
/// The decision on #37 says an item a first exchange cannot decide stays undecided rather than
/// being resolved by a weaker rule, and this is what it stayed undecided for. It is a reason
/// rather than a flag for the same argument <c>Records/UnmatchedRecord</c> makes about an
/// unmatched item: an operator meeting a standing item can act on the reason and can do nothing
/// at all with the fact that something was skipped.
///
/// There is no member for an item that was decided. A resolution carries this as an absence
/// instead, so a decided item has no reason to show rather than a reason meaning nothing, and
/// the state where the two are told apart is the type system rather than a guard somebody has
/// to remember to write.
/// </summary>
public enum UndecidedReason
{
    /// <summary>
    /// Both sides hold plays and the two sides have never agreed a count.
    ///
    /// This is the case <c>PlayCountAnswer.NoAgreement</c> sends here rather than guessing at.
    /// Two sides each holding one play may be one play that already moved and may be two
    /// watchings that never met, and no reading of the two numbers separates them. Either
    /// answer invents something: the sum invents a play for a pair whose history already met,
    /// and the greater of the two throws away a watching the other side recorded.
    /// </summary>
    TwoHistoriesOfPlaysThatHaveNeverAgreed,

    /// <summary>
    /// Both sides hold the work played and each holds a different position in it.
    ///
    /// A completion discards the position offered against it, which is the ratchet's own
    /// answer, and that answer names a loser only where one of the two sides is not played.
    /// Where both are, the table decides nothing between two leftover positions, and picking
    /// one is a rule this run would be inventing rather than applying.
    /// </summary>
    TwoCompletionsHoldingDifferentPositions,

    /// <summary>
    /// The peer's last played date is further ahead of this server's clock than the tolerated
    /// skew allows, so the position rule refused to compare the two.
    ///
    /// It is carried through as its own reason rather than folded into the others because a
    /// clock failure that reads as anything else costs an evening, which is what
    /// <c>docs/conflicts.md</c> says of the same refusal one layer down.
    /// </summary>
    ThePeersClockIsOutsideTheTolerance,
}
