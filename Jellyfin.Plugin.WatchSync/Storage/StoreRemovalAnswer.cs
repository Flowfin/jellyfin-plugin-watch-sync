namespace Jellyfin.Plugin.WatchSync.Storage;

/// <summary>
/// What removing this plugin's store came back with.
/// </summary>
public enum StoreRemovalAnswer
{
    /// <summary>
    /// The store was there and is gone.
    /// </summary>
    Removed,

    /// <summary>
    /// There was no store to remove.
    ///
    /// A separate answer rather than the same one, and it is not an error either. An operator who
    /// presses the action twice, or presses it on a server that never synced, has asked for a
    /// state that already holds, and the honest report of that is a different word rather than a
    /// failure or a claim that something was deleted. #64 asks every manual action to be safe to
    /// repeat, and this is what safe to repeat looks like from the inside.
    /// </summary>
    NothingToRemove,
}
