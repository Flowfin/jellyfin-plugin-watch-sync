namespace Jellyfin.Plugin.WatchSync.Peer;

/// <summary>
/// What the bounded backoff answers about one pairing after a run of failed attempts, which is
/// #53.
///
/// Three answers and no fourth. Either nothing has failed, in which case there is no wait at all
/// and the caller is not backing off; or the wait is still growing; or the wait has reached the
/// ceiling and every further failure is answered with that same wait.
/// </summary>
public enum BackoffAnswer
{
    /// <summary>
    /// No attempt has failed since the last one that succeeded, so there is nothing to wait for.
    ///
    /// It is a separate answer rather than a wait of zero because the two are read differently by
    /// everything above: a caller looking at a peer with no failures behind it is looking at a
    /// peer that is working, and a caller looking at a wait of zero on a failing peer is looking
    /// at a retry loop with no interval in it. Collapsing them makes the second invisible.
    /// </summary>
    NothingHasFailed,

    /// <summary>
    /// Attempts have failed and the wait is still below the ceiling, so it is longer after this
    /// failure than it was after the last one.
    /// </summary>
    Growing,

    /// <summary>
    /// The wait has reached the ceiling and stays there for every further failure.
    ///
    /// This is the state a peer that is genuinely down settles into, and it is the state the
    /// ceiling exists for: the interval stops growing, so a peer that comes back is retried
    /// within one ceiling rather than in a day, and it stops shortening, so a peer that stays
    /// down is asked at a rate that spends almost none of what the layer below admits.
    /// </summary>
    AtTheCeiling,
}
