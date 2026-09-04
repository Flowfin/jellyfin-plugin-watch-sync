using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// The key one item of this server's library produces, read off the item the server holds.
///
/// It is one derivation and there is deliberately no second one. Two things ask this question
/// now - the walk that fills the index and the events that keep it current - and the failure a
/// second copy produces is the one nothing reports: a walk and an event stream that key the
/// same item differently leave the index answering one thing after a build and another after an
/// update, with both files reading correctly.
///
/// The derivation itself is the one <c>docs/matching.md</c> fixes and is not repeated here.
/// What this type adds is where the values come from: the item the server handed over, and the
/// series fetched through the manager rather than through the episode's own <c>Series</c>
/// property, which resolves off a static the server fills in at start. A type reading that
/// static answers correctly on a running server and throws in every test.
///
/// An item that produced no key is null rather than a reading carrying its reason. The index
/// has nothing to hold such an item under, and what an operator is told about it is the
/// unmatched record in #26.
/// </summary>
internal static class LibraryItemKey
{
    /// <summary>
    /// The key one library item produced, or null where it produced none.
    /// </summary>
    /// <param name="library">The server's own library manager, which the series is fetched through.</param>
    /// <param name="item">The library item.</param>
    /// <returns>The key, or null.</returns>
    internal static MatchKey? Of(ILibraryManager library, BaseItem item)
    {
        if (item is Episode episode)
        {
            var series = episode.SeriesId.Equals(default)
                ? null
                : library.GetItemById(episode.SeriesId) as Series;

            var derived = EpisodeMatchKey.Derive(
                episode.ProviderIds,
                series?.ProviderIds,
                series?.DisplayOrder,
                episode.ParentIndexNumber,
                episode.IndexNumber,
                episode.IndexNumberEnd);

            return derived.IsKeyed ? MatchKey.Of(derived.Key!) : null;
        }

        var film = MovieMatchKey.Derive(item.ProviderIds);

        return film.IsKeyed ? MatchKey.Of(film.Key!) : null;
    }
}
