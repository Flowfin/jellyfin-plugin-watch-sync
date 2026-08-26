using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchSync.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.WatchSync.UserData;

/// <summary>
/// The one interface this plugin reads and writes a user's record through, which is #20.
///
/// The server's own interface is not the same on the two supported lines: the newer one adds a
/// batch read and a pair of members naming which version drives a resume point, and the older
/// one has neither. Every difference between the two lines that this plugin has to answer for
/// sits behind this interface, so a caller asks one question and is answered the same way on
/// both.
///
/// Three members and no more. Read one is what an event about a single item needs, read many is
/// what a pass over a page of a library needs, and write is the only way anything this plugin
/// decides reaches a person's record. Nothing here creates, deletes or enumerates items, because
/// the server owns the library and this plugin owns nothing in it.
///
/// What this interface does not decide is which reason a write carries. The server takes that
/// from the caller on both lines, so the same reason a metadata scan produces can be produced by
/// anything holding an access token, and choosing it is the apply path's decision in #54 rather
/// than this adapter's. It is a parameter here for that reason and not for flexibility.
/// </summary>
public interface IUserDataGateway
{
    /// <summary>
    /// Reads what this server holds for one mapped user and one leaf item.
    /// </summary>
    /// <param name="user">The local user the mapping names.</param>
    /// <param name="item">The leaf item.</param>
    /// <returns>The moved set and the runtime of the version this server would resume.</returns>
    UserDataReading Read(User user, BaseItem item);

    /// <summary>
    /// Reads what this server holds for one mapped user and a page of leaf items.
    ///
    /// One call rather than a loop the caller writes, because the newer line answers it in one
    /// read and the older one cannot, and a caller looping for itself would pay the older line's
    /// cost on both. The line that offers a batch uses it; the line that does not says so in its
    /// own implementation rather than pretending otherwise.
    /// </summary>
    /// <param name="user">The local user the mapping names.</param>
    /// <param name="items">The page of leaf items.</param>
    /// <returns>
    /// A reading per item, keyed on the item's identifier. An item the server holds nothing for
    /// is still answered, because a caller comparing two sides needs to know that this side holds
    /// nothing rather than finding the item absent from the answer.
    /// </returns>
    IReadOnlyDictionary<Guid, UserDataReading> ReadMany(User user, IReadOnlyList<BaseItem> items);

    /// <summary>
    /// Writes a moved set into one mapped user's record for one leaf item.
    ///
    /// The write assigns every moved field rather than adding to any of them, which is the
    /// invariant #50 refuses the other spelling of: an apply that adds is not idempotent, and
    /// whether a send that timed out is ever repeated is not this plugin's to decide.
    /// </summary>
    /// <param name="user">The local user the mapping names.</param>
    /// <param name="item">The leaf item.</param>
    /// <param name="state">The moved set to assign.</param>
    /// <param name="reason">The reason the server records against the save.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    void Write(
        User user,
        BaseItem item,
        SyncedState state,
        UserDataSaveReason reason,
        CancellationToken cancellationToken);
}
