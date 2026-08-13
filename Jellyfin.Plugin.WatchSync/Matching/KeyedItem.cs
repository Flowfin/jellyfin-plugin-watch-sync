using System;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// One local item that produced a key, as the index holds it.
///
/// It is the item's own database identifier and its key, and nothing else. The index exists
/// to answer which local item carries a key, so anything more would be state the index kept
/// about an item, and the index is a cache. The item's title, its library and where its
/// file is are all absent for that reason, and the last of the three is also refused
/// outright by <c>docs/matching.md</c>.
///
/// An item that produced no key is not one of these. It has nothing to be indexed under,
/// and what an operator is told about it is the unmatched record in #26.
/// </summary>
public sealed class KeyedItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedItem"/> class.
    /// </summary>
    /// <param name="itemId">The item's identifier on this server.</param>
    /// <param name="key">The key the item produced.</param>
    public KeyedItem(Guid itemId, MatchKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        ItemId = itemId;
        Key = key;
    }

    /// <summary>
    /// Gets the item's identifier on this server.
    ///
    /// It addresses the item here and names nothing on the peer, which is why it is what the
    /// index resolves to rather than anything that travels in an envelope.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the key the item produced.
    /// </summary>
    public MatchKey Key { get; }
}
