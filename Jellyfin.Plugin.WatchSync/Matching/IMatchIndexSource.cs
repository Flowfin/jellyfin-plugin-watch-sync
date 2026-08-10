using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// Where the index reads the library's keyed items from.
///
/// One page at a time, because the whole point of the interface is that a build never holds
/// the library in memory. A method returning everything would make the bound impossible to
/// keep whatever the index did with the result, so the bound is in the shape of the call
/// rather than in the discipline of the caller.
/// </summary>
public interface IMatchIndexSource
{
    /// <summary>
    /// Reads one page of the items that produced a key.
    ///
    /// A page shorter than <paramref name="count"/> ends the walk, which is the ordinary
    /// convention and means the index needs no separate count. A count read first would be a
    /// second question to the library whose answer can already be stale by the time the last
    /// page is read, and a walk that trusted it would either stop early or ask for a page
    /// past the end.
    ///
    /// Items with no key are not returned. They have nothing to be indexed under, and what
    /// an operator is told about them is the unmatched record in #26.
    /// </summary>
    /// <param name="startIndex">How many items to skip.</param>
    /// <param name="count">The most items to return.</param>
    /// <returns>The page, which is empty or short at the end of the library.</returns>
    IReadOnlyList<KeyedItem> ReadPage(int startIndex, int count);
}
