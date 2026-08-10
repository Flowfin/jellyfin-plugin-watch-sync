namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// Which derivation produced a key.
///
/// It travels inside the key rather than beside it because the two derivations write into
/// one space of values. An episode that carries its own provider identifier keys on that
/// identifier, and so does a film that carries the same one, which happens whenever a
/// scraper has written a series' identifier onto an episode. Without this, the index would
/// resolve one key to a film and an episode together and call it an ambiguity, or worse,
/// resolve a film's key to an episode and move one person's watch state onto the wrong
/// work.
///
/// The kinds are the two <c>docs/matching.md</c> gives a key rule to. A kind added to that
/// table is a member added here, and the derivation for it is what decides the member's
/// name.
/// </summary>
public enum MatchKeyKind
{
    /// <summary>
    /// A key <see cref="MovieMatchKey"/> derived, which is a provider identifier for the
    /// film itself.
    /// </summary>
    Movie,

    /// <summary>
    /// A key <see cref="EpisodeMatchKey"/> derived, which is the series' identifier with the
    /// ordering and the position, or the episode's own identifier.
    /// </summary>
    Episode,
}
