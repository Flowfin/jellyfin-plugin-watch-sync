namespace Jellyfin.Plugin.WatchSync.Agreement;

/// <summary>
/// What the next exchange for one pairing and one mapped user asks the peer for.
/// </summary>
public enum NextExchange
{
    /// <summary>
    /// Everything, because there is no point to resume from.
    ///
    /// Two states reach it and they are deliberately one answer. A pairing and a user that have
    /// never confirmed anything have no point, and a pairing whose point the peer no longer
    /// recognises has none either. The second is the case #52 is about and the first is the
    /// first exchange in #37; what they have in common is the question this server can ask, and
    /// what separates them is which rules the answer is then applied under, which is not this
    /// type's to say.
    /// </summary>
    FullReconciliation,

    /// <summary>
    /// What has changed since the point the far side last confirmed.
    /// </summary>
    SinceTheWatermark,
}
