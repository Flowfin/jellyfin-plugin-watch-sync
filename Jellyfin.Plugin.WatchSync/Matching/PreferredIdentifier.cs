using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// Reads the one identifier a key is derived from out of everything an item carries.
///
/// A film keys on its own identifiers and an episode falls back to its series' identifiers,
/// and the walk is the same walk: take the providers in the order
/// <c>docs/matching.md</c> fixes, normalise the first value the item carries under one of
/// them, and answer with the reason where nothing usable is there. It lives here rather
/// than in either caller because two copies of a preference order are two things that
/// drift, and the drift would show up as two servers disagreeing about a work they had
/// both identified correctly.
/// </summary>
internal static class PreferredIdentifier
{
    /// <summary>
    /// The providers in the order a key prefers them.
    ///
    /// It is the declaration order of <see cref="IdentifierProvider"/> rather than a second
    /// list, because a second list is a thing that drifts against the first. A test reads
    /// the order out of <c>docs/matching.md</c> and refuses the document and this order
    /// disagreeing.
    /// </summary>
    /// <returns>The providers, most preferred first.</returns>
    internal static IReadOnlyList<IdentifierProvider> Order() =>
        Enum.GetValues<IdentifierProvider>();

    /// <summary>
    /// Finds the most preferred usable identifier, or says why there is none.
    ///
    /// The whole input is the provider identifier map. Nothing about where the item is
    /// stored is a parameter, and neither is the item's own database identifier: the server
    /// puts that in its own user data key list, where it is meaningful, and it names nothing
    /// on the peer.
    /// </summary>
    /// <param name="providerIdentifiers">
    /// The identifiers the item carries, keyed by provider name as the server holds them.
    /// </param>
    /// <returns>The identifier, or the reason there is none.</returns>
    internal static MatchKeyReading Of(IReadOnlyDictionary<string, string>? providerIdentifiers)
    {
        var carriesSomething = providerIdentifiers is not null
            && providerIdentifiers.Any(pair => !string.IsNullOrWhiteSpace(pair.Value));

        if (!carriesSomething)
        {
            return MatchKeyReading.Unkeyed(MatchKeyRefusal.NoIdentifierAtAll);
        }

        var reachedAPreferredProvider = false;

        foreach (var provider in Order())
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
