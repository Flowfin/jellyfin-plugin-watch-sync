using System;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// What a removal did, which is the count #74 asks be answered rather than assumed.
///
/// The count is of documents that were removed and not of documents that were found, which is
/// <see cref="Jellyfin.Plugin.WatchSync.Storage.HeldAboutOnePerson.Remove"/>'s own distinction
/// and its own bound on how far that is proven.
/// </summary>
public sealed class RecordsRemoved
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecordsRemoved"/> class.
    /// </summary>
    /// <param name="mappedUserId">The person the removal was about.</param>
    /// <param name="removed">How many documents were removed.</param>
    public RecordsRemoved(Guid mappedUserId, int removed)
    {
        MappedUserId = mappedUserId;
        Removed = removed;
    }

    /// <summary>
    /// Gets the person the removal was about, as this server names them.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets how many documents were removed.
    /// </summary>
    public int Removed { get; }
}
