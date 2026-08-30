namespace Jellyfin.Plugin.WatchSync.Agreement;

/// <summary>
/// How an exchange ended, as far as the watermark is concerned.
///
/// Three arms rather than one per row of the table in <c>docs/transfer.md</c>. That table has
/// twelve rows and they collapse to three here, because the watermark asks one question of an
/// ending and not twelve: did the far side confirm a point. Nine of the rows say the watermark
/// is unmoved, two say it advances to the point the answer named, and the peer not recognising
/// the point this server offered is the one that is neither.
///
/// The rows are not restated here. What each ending leaves behind and what the next exchange
/// does about it is fixed in that document, and an enumeration that carried its own copy of the
/// table would be a second answer to drift against it.
/// </summary>
public enum ExchangeEnd
{
    /// <summary>
    /// The far side answered and named the point it answered to.
    ///
    /// This is the only ending that moves the watermark, and it covers both the run that
    /// finished and the run that stopped at its cap, because the cap advances to the last point
    /// both sides agreed rather than leaving the record behind what it already wrote.
    /// </summary>
    ConfirmedTo,

    /// <summary>
    /// Nothing came back that confirms a point.
    ///
    /// A refusal before anything was read, a peer that did not answer, an envelope refused for
    /// its version or for a bound, a run that stopped part way. One value for all of them,
    /// because they leave the same thing behind and the next exchange asks the same question.
    /// A send that was made is not a confirmation and does not appear here as one.
    /// </summary>
    NotConfirmed,

    /// <summary>
    /// The far side does not recognise the point this server offered.
    ///
    /// What a peer restored from a backup looks like from this end. It is not an error and it is
    /// not a refusal: the point is gone, so there is nothing to resume from, and the next
    /// exchange is the full reconciliation in #52.
    /// </summary>
    PointNotRecognised,
}
