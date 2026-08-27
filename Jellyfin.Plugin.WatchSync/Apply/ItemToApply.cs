using System;
using Jellyfin.Plugin.WatchSync.Model;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// One item an exchange has already decided about, and the state it decided on.
///
/// The apply path writes what it is handed and decides nothing. Which value wins is the conflict
/// table, which item a change is about is the matcher, and which local user it belongs to is the
/// mapping the pairing plugin hands over. Carrying all three as one already-decided item is what
/// makes the order rule in #54 a property of the walk rather than of whatever the resolver
/// happened to return.
///
/// It carries the item itself beside the subject rather than an identifier alone. The one
/// interface this plugin writes a record through takes the server's own item, so a walk holding
/// identifiers would have to ask the library again per item, and a second read of the library is
/// a second answer that can disagree with the one the change was matched against.
///
/// That second read is refused rather than avoided by care: the item and the subject have to name
/// the same item, and a pair that does not is refused here rather than at the write. A decided
/// state written against a different item is the failure this plugin exists to refuse, and it
/// leaves an agreed record saying two servers settled on a value for an item neither of them was
/// talking about.
/// </summary>
public sealed class ItemToApply
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ItemToApply"/> class.
    /// </summary>
    /// <param name="subject">The mapped user and the leaf item the change is about.</param>
    /// <param name="item">The item as this server holds it, which the write is made against.</param>
    /// <param name="decided">The state the conflict table decided on.</param>
    /// <exception cref="ArgumentNullException">The subject, the item or the state is null.</exception>
    /// <exception cref="ArgumentException">
    /// The item is not the item the subject names. Nothing later in the walk could notice it: the
    /// write would land on the item, the agreed record would be written for the subject, and both
    /// would look ordinary.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The decided state carries a position or a play count below zero. Neither server produces
    /// one, and what arrives from a peer is bounded and refused one layer earlier, which is #19.
    /// </exception>
    public ItemToApply(TransferSubject subject, BaseItem item, SyncedState decided)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(decided);

        if (item.Id != subject.ItemId)
        {
            throw new ArgumentException(
                "The item to write against is not the item the subject names, so the write and the agreed record would be about two different items.",
                nameof(item));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(decided.PlaybackPositionTicks, nameof(decided));
        ArgumentOutOfRangeException.ThrowIfNegative(decided.PlayCount, nameof(decided));

        Subject = subject;
        Item = item;
        Decided = decided;
    }

    /// <summary>
    /// Gets the mapped user and the leaf item this change is about.
    /// </summary>
    public TransferSubject Subject { get; }

    /// <summary>
    /// Gets the item as this server holds it.
    /// </summary>
    public BaseItem Item { get; }

    /// <summary>
    /// Gets the state the conflict table decided on, which is assigned rather than added to.
    ///
    /// Assigned is what makes a second delivery of one envelope indistinguishable from the first,
    /// which is #50, and the <c>applied-change-is-assigned</c> invariant refuses the spellings
    /// that add.
    /// </summary>
    public SyncedState Decided { get; }
}
