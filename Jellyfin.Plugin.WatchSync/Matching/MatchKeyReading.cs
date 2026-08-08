namespace Jellyfin.Plugin.WatchSync.Matching;

/// <summary>
/// What a key derivation answered: a key, or the reason there is none.
///
/// The reason is carried rather than dropped, for the same reason
/// <see cref="IdentifierReading"/> carries one. An item that produced no key is recorded
/// with why, and a bare null leaves the caller to guess or to record nothing.
/// </summary>
public sealed class MatchKeyReading
{
    private MatchKeyReading(ProviderIdentifier? key, MatchKeyRefusal refusal)
    {
        Key = key;
        Refusal = refusal;
    }

    /// <summary>
    /// Gets the key, or null where the item produced none.
    ///
    /// The key is an identifier rather than a bare string, so the provider travels with the
    /// value and a TMDb identifier can never be read as the TVDb identifier that happens to
    /// be the same number.
    /// </summary>
    public ProviderIdentifier? Key { get; }

    /// <summary>
    /// Gets the reason the item produced no key, or <see cref="MatchKeyRefusal.None"/>.
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
    internal static MatchKeyReading Keyed(ProviderIdentifier key) =>
        new MatchKeyReading(key, MatchKeyRefusal.None);

    /// <summary>
    /// A reading that produced no key, with the reason.
    /// </summary>
    /// <param name="refusal">Why the item has no key.</param>
    /// <returns>The reading.</returns>
    internal static MatchKeyReading Unkeyed(MatchKeyRefusal refusal) =>
        new MatchKeyReading(null, refusal);
}
