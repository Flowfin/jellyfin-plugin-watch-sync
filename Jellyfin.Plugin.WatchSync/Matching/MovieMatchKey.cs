using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// The key one movie is matched by across two servers.
///
/// The two servers are not required to hold the same file. One may hold a remux where the
/// other holds a web release, in another language, at another resolution, under another
/// name, and both are the same work. So the key is the metadata identity a scraper wrote
/// and never anything about the file, which <c>docs/matching.md</c> argues and #25 refuses
/// by name.
/// </summary>
public static class MovieMatchKey
{
    /// <summary>
    /// Derives the key from the provider identifiers an item carries.
    ///
    /// The whole input is the provider identifier map. Nothing about where the item is
    /// stored is a parameter, and neither is the item's own database identifier: the server
    /// puts that in its own user data key list, where it is meaningful, and it names nothing
    /// on the peer. A key that carried it would match nothing there and would look like an
    /// unscraped library rather than like a mistake.
    ///
    /// Where an item carries several, the first in the order <c>docs/matching.md</c> fixes
    /// wins. That order is fixed rather than left to whichever value is read first, because
    /// otherwise one item carrying two identifiers would produce a different key depending
    /// on the shape of a dictionary, and two servers would disagree about a work they had
    /// both identified correctly.
    ///
    /// The walk itself is <see cref="PreferredIdentifier"/>, which #23 shares for the series
    /// an episode falls back to. One walk rather than two, because two copies of a
    /// preference order drift and the drift is invisible until two servers disagree.
    /// </summary>
    /// <param name="providerIdentifiers">
    /// The identifiers the item carries, keyed by provider name as the server holds them.
    /// </param>
    /// <returns>The key, or the reason there is none.</returns>
    public static MatchKeyReading Derive(IReadOnlyDictionary<string, string>? providerIdentifiers) =>
        PreferredIdentifier.Of(providerIdentifiers);

    /// <summary>
    /// The providers in the order the key prefers them.
    /// </summary>
    /// <returns>The providers, most preferred first.</returns>
    public static IReadOnlyList<IdentifierProvider> PreferenceOrder() =>
        PreferredIdentifier.Order();
}
