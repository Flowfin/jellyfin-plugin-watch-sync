using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Undo;

/// <summary>
/// One value this plugin wrote that the undo leaves standing, and why.
///
/// #44's body says the case is recorded rather than forced, so a skip is an entry an operator can
/// be shown and not an absence they have to notice. A revocation that reverted most of what a
/// pairing wrote and silently left the rest would tell somebody their data was undone, which is a
/// specific claim and would be false for exactly the items the person had touched since.
///
/// It names the item and the field rather than carrying values. The values are in the record of
/// provenance and in the person's own account, both of which an operator already has, and this
/// record is a candidate for the same page and the same log as its siblings, where a title and a
/// number about what somebody watched may not go.
/// </summary>
public sealed class SkippedValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkippedValue"/> class.
    /// </summary>
    /// <param name="itemId">The local item the value was written against.</param>
    /// <param name="field">The moved field that is left standing.</param>
    /// <param name="reason">Why it is left standing.</param>
    public SkippedValue(Guid itemId, SyncedField field, SkipReason reason)
    {
        ItemId = itemId;
        Field = field;
        Reason = reason;
    }

    /// <summary>
    /// Gets the local item the value was written against.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the moved field that is left standing.
    /// </summary>
    public SyncedField Field { get; }

    /// <summary>
    /// Gets why it is left standing.
    /// </summary>
    public SkipReason Reason { get; }
}
