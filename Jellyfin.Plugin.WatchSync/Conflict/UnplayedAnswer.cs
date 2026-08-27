namespace Jellyfin.Plugin.WatchSync.Conflict;

/// <summary>
/// What holding a deliberate unmark against a completion answered.
///
/// Four answers rather than two, because the two that hand the pair on to the ratchet hand it
/// on for different reasons and #36 records why a value lost rather than only that it did. One
/// of them says the case is not in front of the rule at all; the other says it was and the
/// unmark lost, which is the answer an operator asking why an episode is watched again needs to
/// be able to find.
/// </summary>
public enum UnplayedAnswer
{
    /// <summary>
    /// This server turned an agreed completion off and the peer has held that completion
    /// unchanged since the agreement, so the unmark is the intent and it carries.
    ///
    /// The peer's played state is the loser and it is a resolved conflict, because the peer
    /// is holding a value that was true when the two sides agreed and has not been touched by
    /// anybody since.
    /// </summary>
    UnplayedCarriesFromHere,

    /// <summary>
    /// The peer turned an agreed completion off and this server has held that completion
    /// unchanged since the agreement, so the peer's unmark carries. The mirror of the answer
    /// above, and the rule answers the same whichever way round the two sides are passed.
    /// </summary>
    UnplayedCarriesFromThePeer,

    /// <summary>
    /// There is no unmark of an agreed completion in front of the rule.
    ///
    /// Four states reach it: no agreement at all, which is a first exchange; an agreement that
    /// was not a completion, so there was nothing to turn off; both sides still holding the
    /// work played; and both sides having turned it off, which the two sides already agree
    /// about. Nothing is decided here and the ratchet is what answers the pair.
    /// </summary>
    NoUnmarkToCarry,

    /// <summary>
    /// One side turned the agreed completion off and the other has watched the work again
    /// since that agreement, so what it holds is a newer intent rather than an old value.
    ///
    /// The unmark loses and the ratchet's answer stands. That is a decision rather than a
    /// consequence, and the reason is in <see cref="DeliberateUnplayed"/>: the server stores
    /// no moment for an unmark, so the two intents cannot be ordered, and the direction that
    /// keeps a play somebody actually made is the one this plan takes everywhere.
    /// </summary>
    TheCompletionMovedSinceTheAgreement,
}
