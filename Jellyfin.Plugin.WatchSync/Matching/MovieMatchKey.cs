using System;
using System.Collections.Generic;
using System.Linq;

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
    /// </summary>
    /// <param name="providerIdentifiers">
    /// The identifiers the item carries, keyed by provider name as the server holds them.
    /// </param>
    /// <returns>The key, or the reason there is none.</returns>
    public static MatchKeyReading Derive(IReadOnlyDictionary<string, string>? providerIdentifiers)
    {
        var carriesSomething = providerIdentifiers is not null
            && providerIdentifiers.Any(pair => !string.IsNullOrWhiteSpace(pair.Value));

        if (!carriesSomething)
        {
            return MatchKeyReading.Unkeyed(MatchKeyRefusal.NoIdentifierAtAll);
        }

        var reachedAPreferredProvider = false;

        foreach (var provider in PreferenceOrder())
        {
            if (!TryRead(providerIdentifiers!, provider, out var stored))
            {
                continue;
            }

            reachedAPreferredProvider = true;

            var reading = ProviderIdentifier.Normalise(provider, stored);

            if (reading.IsUsable)
            {
                return MatchKeyReading.Keyed(reading.Identifier!);
            }
        }

        return MatchKeyReading.Unkeyed(reachedAPreferredProvider
            ? MatchKeyRefusal.EveryPreferredIdentifierWasRefused
            : MatchKeyRefusal.NoIdentifierFromAPreferredProvider);
    }

    /// <summary>
    /// The providers in the order the key prefers them.
    ///
    /// It is the declaration order of <see cref="IdentifierProvider"/> rather than a second
    /// list, because a second list is a thing that drifts against the first. A test reads
    /// the order out of <c>docs/matching.md</c> and refuses the document and this order
    /// disagreeing.
    /// </summary>
    /// <returns>The providers, most preferred first.</returns>
    public static IReadOnlyList<IdentifierProvider> PreferenceOrder() =>
        Enum.GetValues<IdentifierProvider>();

    /// <summary>
    /// Finds what the item stored under one provider's name.
    ///
    /// The comparison ignores case. The name is a string in a map that scrapers, imports and
    /// two server lines all write into, and a key that differs from this plugin's spelling
    /// only in case is an identifier the item genuinely carries. Reading it as absent would
    /// record a scraped film as unmatched.
    /// </summary>
    /// <param name="providerIdentifiers">The identifiers the item carries.</param>
    /// <param name="provider">The provider to look for.</param>
    /// <param name="stored">The value as stored.</param>
    /// <returns>Whether the item carries anything under that provider.</returns>
    private static bool TryRead(
        IReadOnlyDictionary<string, string> providerIdentifiers,
        IdentifierProvider provider,
        out string? stored)
    {
        var name = provider.ToString();

        if (providerIdentifiers.TryGetValue(name, out stored))
        {
            return true;
        }

        foreach (var pair in providerIdentifiers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                stored = pair.Value;

                return true;
            }
        }

        stored = null;

        return false;
    }
}
