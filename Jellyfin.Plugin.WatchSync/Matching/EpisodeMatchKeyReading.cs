namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// What an episode key derivation answered: a key, or the reason there is none.
///
/// It is its own reading rather than <see cref="MatchKeyReading"/> because an episode key is
/// not a bare identifier. The refusal vocabulary is shared, so an operator reading the
/// unmatched record meets one set of reasons rather than one per item kind.
/// </summary>
public sealed class EpisodeMatchKeyReading
{
    private EpisodeMatchKeyReading(EpisodeMatchKey? key, MatchKeyRefusal refusal)
    {
        Key = key;
        Refusal = refusal;
    }

    /// <summary>
    /// Gets the key, or null where the episode produced none.
    /// </summary>
    public EpisodeMatchKey? Key { get; }

    /// <summary>
    /// Gets the reason the episode produced no key, or <see cref="MatchKeyRefusal.None"/>.
    /// </summary>
    public MatchKeyRefusal Refusal { get; }

    /// <summary>
    /// Gets a value indicating whether there is a key to compare.
    /// </summary>
    public bool IsKeyed => Refusal == MatchKeyRefusal.None;

    /// <summary>
    /// A reading that produced a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The reading.</returns>
    internal static EpisodeMatchKeyReading Keyed(EpisodeMatchKey key) =>
        new EpisodeMatchKeyReading(key, MatchKeyRefusal.None);

    /// <summary>
    /// A reading that produced no key, with the reason.
    /// </summary>
    /// <param name="refusal">Why the episode has no key.</param>
    /// <returns>The reading.</returns>
    internal static EpisodeMatchKeyReading Unkeyed(MatchKeyRefusal refusal) =>
        new EpisodeMatchKeyReading(null, refusal);
}
