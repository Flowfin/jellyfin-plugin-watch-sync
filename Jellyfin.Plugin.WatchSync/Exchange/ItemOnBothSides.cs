using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Exchange;

/// <summary>
/// One mapped user's state for one leaf item, as each of the two servers holds it.
///
/// The unit a transfer is about is one mapped user and one leaf item, which
/// <c>docs/sync-model.md</c> fixes and this type points at rather than restating. What it adds
/// is the pairing of the two readings: every rule in the table takes both sides at once, so a
/// run that carried them separately would have to put them back together at every row and
/// could put the wrong pair together at one of them.
/// </summary>
public sealed class ItemOnBothSides
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ItemOnBothSides"/> class.
    /// </summary>
    /// <param name="subject">The mapped user and the leaf item both readings are about.</param>
    /// <param name="here">What this server holds.</param>
    /// <param name="atThePeer">What the peer offered.</param>
    /// <exception cref="ArgumentNullException">Any of the three is absent.</exception>
    public ItemOnBothSides(TransferSubject subject, SyncedState here, SyncedState atThePeer)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(here);
        ArgumentNullException.ThrowIfNull(atThePeer);

        Subject = subject;
        Here = here;
        AtThePeer = atThePeer;
    }

    /// <summary>
    /// Gets the mapped user and the leaf item.
    /// </summary>
    public TransferSubject Subject { get; }

    /// <summary>
    /// Gets what this server holds.
    /// </summary>
    public SyncedState Here { get; }

    /// <summary>
    /// Gets what the peer offered.
    /// </summary>
    public SyncedState AtThePeer { get; }
}
