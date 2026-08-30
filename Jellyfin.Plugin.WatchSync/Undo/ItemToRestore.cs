using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Undo;

/// <summary>
/// One item an undo puts values back into, as the state to assign and the fields it is putting
/// back.
///
/// It carries a whole moved set rather than the fields alone because a write assigns all four,
/// which is the invariant <c>applied-change-is-assigned</c> keeps for the apply path and holds
/// here for the same reason. So the state is what the person holds now with the restored fields
/// replaced, and the three fields nobody is putting back arrive at the server as the values that
/// are already there.
///
/// <see cref="Fields"/> is what stops that being unreadable. A caller shown a state alone cannot
/// tell which of the four the undo decided about, and an operator asking what a revocation did to
/// one item wants exactly that list.
/// </summary>
public sealed class ItemToRestore
{
    private readonly List<SyncedField> _fields;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemToRestore"/> class.
    /// </summary>
    /// <param name="itemId">The local item to write.</param>
    /// <param name="restored">The moved set to assign, which is what stands now with the restored fields replaced.</param>
    /// <param name="fields">The fields this undo is putting back.</param>
    /// <exception cref="ArgumentNullException">The state or the field list is null.</exception>
    /// <exception cref="ArgumentException">
    /// The field list is empty. An item nothing is being put back into is not an item to write,
    /// and a caller handed one would write a person's own values back over themselves under the
    /// reason a revocation carries.
    /// </exception>
    public ItemToRestore(Guid itemId, SyncedState restored, IReadOnlyList<SyncedField> fields)
    {
        ArgumentNullException.ThrowIfNull(restored);
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Count == 0)
        {
            throw new ArgumentException(
                "An item with no field to put back is not an item to write, and writing one would assign a person's own values back over themselves under the reason a revocation carries.",
                nameof(fields));
        }

        ItemId = itemId;
        Restored = restored;
        _fields = new List<SyncedField>(fields);
    }

    /// <summary>
    /// Gets the local item to write.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the moved set to assign.
    /// </summary>
    public SyncedState Restored { get; }

    /// <summary>
    /// Gets the fields this undo is putting back.
    /// </summary>
    public IReadOnlyList<SyncedField> Fields => _fields;
}
