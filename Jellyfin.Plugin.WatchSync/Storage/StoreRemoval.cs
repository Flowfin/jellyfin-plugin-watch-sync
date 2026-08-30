using System;
using System.IO;

namespace Jellyfin.Plugin.WatchSync.Storage;

/// <summary>
/// Removes everything this plugin holds, and nothing the server holds.
///
/// This is the action #73 puts in front of an uninstall rather than inside one. Uninstalling
/// removes the assembly; what is left is a decision, and the answer that issue takes is that the
/// watch state written into the server stays, because that is the server's data now and removing
/// a plugin is not an instruction to erase a household's history, while this plugin's own store
/// is about people and is useless without the plugin.
///
/// The offer is what makes the two separable. An operator removing this plugin because they no
/// longer want history moving between servers is asking for the records of it to go; an operator
/// removing and reinstalling to upgrade is not. So the removal is something they do first and on
/// purpose, and doing nothing leaves a store the next install resumes from.
///
/// What it removes is the one folder the store lives in, taken from the application paths the
/// server hands over, which the <c>store-path-from-the-server</c> invariant refuses a departure
/// from. Nothing here composes a path out of anything else, walks upwards, or removes a file it
/// found by name: a removal that took a pattern would take a neighbour the day somebody named
/// one badly, and the neighbours here are other plugins' data.
/// </summary>
public static class StoreRemoval
{
    /// <summary>
    /// Removes this plugin's store.
    /// </summary>
    /// <param name="folder">The store folder, which knows where the server keeps its data.</param>
    /// <returns>Whether there was a store to remove.</returns>
    /// <exception cref="ArgumentNullException">The folder is null.</exception>
    public static StoreRemovalAnswer Remove(StoreFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var path = folder.FullPath;

        if (!Directory.Exists(path))
        {
            return StoreRemovalAnswer.NothingToRemove;
        }

        Directory.Delete(path, true);

        return StoreRemovalAnswer.Removed;
    }
}
